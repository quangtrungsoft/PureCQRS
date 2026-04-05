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
/// Request without response (void)
/// </summary>
public interface IRequest : IBaseRequest;