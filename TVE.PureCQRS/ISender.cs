namespace TVE.PureCQRS;

/// <summary>
/// Send requests to single handler
/// </summary>
public interface ISender
{
    /// <summary>
    /// Send request with response
    /// </summary>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send request without response
    /// </summary>
    Task Send(IRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send request as object (dynamic)
    /// </summary>
    Task<object?> Send(object request, CancellationToken cancellationToken = default);
}