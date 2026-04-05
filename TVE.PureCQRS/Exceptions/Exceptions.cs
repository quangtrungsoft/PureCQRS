namespace TVE.PureCQRS;

/// <summary>
/// No handler found for request
/// </summary>
public sealed class HandlerNotFoundException : InvalidOperationException
{
    public HandlerNotFoundException(Type requestType)
        : base($"No handler registered for '{requestType.Name}'") { }
}

/// <summary>
/// Invalid request type
/// </summary>
public sealed class InvalidRequestException : ArgumentException
{
    public InvalidRequestException(Type requestType)
        : base($"'{requestType.Name}' does not implement IRequest or IRequest<TResponse>") { }
}