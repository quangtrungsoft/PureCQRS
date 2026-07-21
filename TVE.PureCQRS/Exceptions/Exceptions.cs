namespace TVE.PureCQRS;

/// <summary>
/// No handler found for request
/// </summary>
public sealed class HandlerNotFoundException : InvalidOperationException
{
    /// <summary>Creates the exception for the request type that has no registered handler.</summary>
    /// <param name="requestType">The request type that could not be handled.</param>
    public HandlerNotFoundException(Type requestType)
        : base($"No handler registered for '{requestType.Name}'") { }
}

/// <summary>
/// Invalid request type
/// </summary>
public sealed class InvalidRequestException : ArgumentException
{
    /// <summary>Creates the exception for an object that is not a valid request.</summary>
    /// <param name="requestType">The type that does not implement a request interface.</param>
    public InvalidRequestException(Type requestType)
        : base($"'{requestType.Name}' does not implement IRequest or IRequest<TResponse>") { }
}