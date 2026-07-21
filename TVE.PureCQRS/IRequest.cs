namespace TVE.PureCQRS;

/// <summary>
/// Marker interface for requests
/// </summary>
public interface IBaseRequest;

/// <summary>
/// Request with response
/// </summary>
public interface IRequest<out TResponse> : IBaseRequest;

/// <summary>
/// Request without response (void). Modelled as <c>IRequest&lt;Unit&gt;</c> so commands
/// share the same pipeline (behaviors, exception handling) as value-returning requests.
/// </summary>
public interface IRequest : IRequest<Unit>;