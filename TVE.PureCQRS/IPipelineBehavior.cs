namespace TVE.PureCQRS;

/// <summary>
/// Delegate for next handler in pipeline
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Pipeline behavior for cross-cutting concerns
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}