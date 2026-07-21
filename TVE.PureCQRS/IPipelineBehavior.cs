namespace TVE.PureCQRS;

/// <summary>
/// Delegate for the next step in the pipeline.
/// The <paramref name="cancellationToken"/> is optional so existing behaviors that
/// call <c>next()</c> keep compiling; prefer forwarding the token: <c>next(cancellationToken)</c>.
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);

/// <summary>
/// Pipeline behavior for cross-cutting concerns
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Runs custom logic around the rest of the pipeline.</summary>
    /// <param name="request">The request being handled.</param>
    /// <param name="next">Delegate invoking the next step; forward the token via <c>next(cancellationToken)</c>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The response, possibly transformed by this behavior.</returns>
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}