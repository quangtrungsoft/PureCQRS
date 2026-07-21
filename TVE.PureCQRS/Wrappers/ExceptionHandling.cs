using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace TVE.PureCQRS.Wrappers;

/// <summary>
/// Caches the exception type hierarchy (most-derived first) so exception matching
/// never re-walks base types at throw time.
/// </summary>
internal static class ExceptionTypeCache
{
    private static readonly ConcurrentDictionary<Type, Type[]> Cache = new();

    public static Type[] GetHierarchy(Type exceptionType) => Cache.GetOrAdd(
        exceptionType,
        static et =>
        {
            var list = new List<Type>();
            for (var current = et; current is not null && current != typeof(object); current = current.BaseType)
            {
                if (typeof(Exception).IsAssignableFrom(current))
                {
                    list.Add(current);
                }
            }

            return list.ToArray();
        });
}

// ---- Recoverable exception handlers (IRequestExceptionHandler) ----

internal abstract class ExceptionHandlerInvoker<TResponse>
{
    /// <summary>Runs matching handlers; returns true once the state is marked handled.</summary>
    public abstract Task<bool> Invoke(
        object request,
        Exception exception,
        RequestExceptionHandlerState<TResponse> state,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

internal sealed class ExceptionHandlerInvokerImpl<TRequest, TResponse, TException> : ExceptionHandlerInvoker<TResponse>
    where TRequest : notnull
    where TException : Exception
{
    public override async Task<bool> Invoke(
        object request,
        Exception exception,
        RequestExceptionHandlerState<TResponse> state,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var handlers = serviceProvider.GetServices<IRequestExceptionHandler<TRequest, TResponse, TException>>();

        foreach (var handler in handlers)
        {
            await handler.Handle((TRequest)request, (TException)exception, state, cancellationToken)
                .ConfigureAwait(false);

            if (state.Handled)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Caches recoverable-handler invokers per exception type (TRequest/TResponse fixed by the closed generic).</summary>
internal static class ExceptionHandlerInvokerFactory<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ConcurrentDictionary<Type, ExceptionHandlerInvoker<TResponse>> Cache = new();

    public static ExceptionHandlerInvoker<TResponse> Get(Type exceptionType) => Cache.GetOrAdd(
        exceptionType,
        static et =>
        {
            var closed = typeof(ExceptionHandlerInvokerImpl<,,>)
                .MakeGenericType(typeof(TRequest), typeof(TResponse), et);
            return (ExceptionHandlerInvoker<TResponse>)Activator.CreateInstance(closed)!;
        });
}

// ---- Side-effect exception actions (IRequestExceptionAction) ----

internal abstract class ExceptionActionInvoker
{
    public abstract Task Invoke(
        object request,
        Exception exception,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

internal sealed class ExceptionActionInvokerImpl<TRequest, TException> : ExceptionActionInvoker
    where TRequest : notnull
    where TException : Exception
{
    public override async Task Invoke(
        object request,
        Exception exception,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var actions = serviceProvider.GetServices<IRequestExceptionAction<TRequest, TException>>();

        foreach (var action in actions)
        {
            await action.Execute((TRequest)request, (TException)exception, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

/// <summary>Caches action invokers per exception type (TRequest fixed by the closed generic).</summary>
internal static class ExceptionActionInvokerFactory<TRequest>
    where TRequest : notnull
{
    private static readonly ConcurrentDictionary<Type, ExceptionActionInvoker> Cache = new();

    public static ExceptionActionInvoker Get(Type exceptionType) => Cache.GetOrAdd(
        exceptionType,
        static et =>
        {
            var closed = typeof(ExceptionActionInvokerImpl<,>).MakeGenericType(typeof(TRequest), et);
            return (ExceptionActionInvoker)Activator.CreateInstance(closed)!;
        });
}

// ---- Pipeline behaviors ----

/// <summary>
/// Outermost exception behavior: catches any exception from inner behaviors/handler and
/// offers it to matching <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>
/// instances. If one recovers, its response is returned instead of the exception propagating.
/// Registered only when exception handlers exist, so it costs nothing otherwise.
/// </summary>
internal sealed class RequestExceptionProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IServiceProvider _serviceProvider;

    public RequestExceptionProcessorBehavior(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var state = new RequestExceptionHandlerState<TResponse>();

            foreach (var exceptionType in ExceptionTypeCache.GetHierarchy(exception.GetType()))
            {
                var invoker = ExceptionHandlerInvokerFactory<TRequest, TResponse>.Get(exceptionType);
                if (await invoker.Invoke(request, exception, state, _serviceProvider, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }

            if (!state.Handled)
            {
                throw;
            }

            return state.Response!;
        }
    }
}

/// <summary>
/// Inner exception behavior: runs every matching
/// <see cref="IRequestExceptionAction{TRequest,TException}"/> for observation/side-effects,
/// then always re-throws so the outer processor (or the caller) still sees the exception.
/// </summary>
internal sealed class RequestExceptionActionProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IServiceProvider _serviceProvider;

    public RequestExceptionActionProcessorBehavior(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            foreach (var exceptionType in ExceptionTypeCache.GetHierarchy(exception.GetType()))
            {
                var invoker = ExceptionActionInvokerFactory<TRequest>.Get(exceptionType);
                await invoker.Invoke(request, exception, _serviceProvider, cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
    }
}
