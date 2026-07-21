using Microsoft.Extensions.DependencyInjection;

namespace TVE.PureCQRS.Wrappers;

/// <summary>
/// Base wrapper for dynamic dispatch
/// </summary>
internal abstract class RequestHandlerBase
{
    public abstract Task<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

/// <summary>
/// Typed wrapper for requests WITH response
/// </summary>
internal abstract class RequestHandlerWrapper<TResponse> : RequestHandlerBase
{
    public abstract Task<TResponse> Handle(
        IRequest<TResponse> request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

/// <summary>
/// High-performance wrapper implementation
/// </summary>
internal sealed class RequestHandlerWrapperImpl<TRequest, TResponse> : RequestHandlerWrapper<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override async Task<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        return await Handle((IRequest<TResponse>)request, serviceProvider, cancellationToken);
    }

    public override Task<TResponse> Handle(
        IRequest<TResponse> request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        return HandleCore((TRequest)request, serviceProvider, cancellationToken);
    }

    private static Task<TResponse> HandleCore(
        TRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        // 1. Resolve handler - O(1) từ DI cache; fallback đóng open-generic handler khi cần
        var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? ResolveGenericHandler(serviceProvider);

        // 2. Resolve behaviors và materialize ngay - KHÔNG lazy enumerate
        var behaviors = serviceProvider
            .GetServices<IPipelineBehavior<TRequest, TResponse>>()
            .ToArray();

        // 3. Fast path - Không có behavior
        if (behaviors.Length == 0)
        {
            return handler.Handle(request, cancellationToken);
        }

        // 4. Build pipeline
        return ExecutePipeline(request, handler, behaviors, cancellationToken);
    }

    /// <summary>
    /// Closes an open-generic handler at runtime for this exact request/response pair when no
    /// closed handler is registered. Throws <see cref="HandlerNotFoundException"/> if none matches.
    /// </summary>
    private static IRequestHandler<TRequest, TResponse> ResolveGenericHandler(IServiceProvider serviceProvider)
    {
        var registry = serviceProvider.GetService<GenericRequestHandlerRegistry>();

        if (registry is { HasCandidates: true } &&
            registry.CreateHandler(serviceProvider, typeof(TRequest), typeof(TResponse))
                is IRequestHandler<TRequest, TResponse> handler)
        {
            return handler;
        }

        throw new HandlerNotFoundException(typeof(TRequest));
    }

    private static Task<TResponse> ExecutePipeline(
        TRequest request,
        IRequestHandler<TRequest, TResponse> handler,
        IPipelineBehavior<TRequest, TResponse>[] behaviors,
        CancellationToken cancellationToken)
    {
        // Handler delegate - honor the token flowing down the pipeline
        RequestHandlerDelegate<TResponse> next = ct => handler.Handle(request, ct);

        // Build pipeline từ cuối về đầu - KHÔNG cần Reverse()
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var currentNext = next;
            next = ct => behavior.Handle(request, currentNext, ct);
        }

        return next(cancellationToken);
    }
}