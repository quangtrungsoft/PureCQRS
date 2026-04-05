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
        // 1. Resolve handler - O(1) từ DI cache
        var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? throw new HandlerNotFoundException(typeof(TRequest));

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

    private static Task<TResponse> ExecutePipeline(
        TRequest request,
        IRequestHandler<TRequest, TResponse> handler,
        IPipelineBehavior<TRequest, TResponse>[] behaviors,
        CancellationToken cancellationToken)
    {
        // Handler delegate
        RequestHandlerDelegate<TResponse> next = () => handler.Handle(request, cancellationToken);

        // Build pipeline từ cuối về đầu - KHÔNG cần Reverse()
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var currentNext = next;
            next = () => behavior.Handle(request, currentNext, cancellationToken);
        }

        return next();
    }
}

/// <summary>
/// Base wrapper for void requests
/// </summary>
internal abstract class RequestHandlerWrapperVoid : RequestHandlerBase
{
    public abstract Task Handle(
        IRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

/// <summary>
/// High-performance void wrapper
/// </summary>
internal sealed class RequestHandlerWrapperVoidImpl<TRequest> : RequestHandlerWrapperVoid
    where TRequest : IRequest
{
    public override async Task<object?> Handle(
        object request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        await Handle((IRequest)request, serviceProvider, cancellationToken);
        return null;
    }

    public override Task Handle(
        IRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        return HandleCore((TRequest)request, serviceProvider, cancellationToken);
    }

    private static Task HandleCore(
        TRequest request,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var handler = serviceProvider.GetService<IRequestHandler<TRequest>>()
            ?? throw new HandlerNotFoundException(typeof(TRequest));

        return handler.Handle(request, cancellationToken);
    }
}