namespace TVE.PureCQRS;

/// <summary>
/// Carries the outcome of exception handling for a request. A handler may mark the
/// exception as handled and supply a fallback response, short-circuiting the failure.
/// </summary>
public sealed class RequestExceptionHandlerState<TResponse>
{
    /// <summary>Whether a handler has taken ownership of the exception.</summary>
    public bool Handled { get; private set; }

    /// <summary>The fallback response supplied by the handler (valid only when <see cref="Handled"/> is true).</summary>
    public TResponse? Response { get; private set; }

    /// <summary>
    /// Marks the exception as handled and provides the response returned to the caller
    /// instead of the exception propagating.
    /// </summary>
    public void SetHandled(TResponse response)
    {
        Handled = true;
        Response = response;
    }
}

/// <summary>
/// Handles an exception thrown while processing a request and may recover from it by
/// calling <see cref="RequestExceptionHandlerState{TResponse}.SetHandled"/>.
/// Matched by exception type, most-derived first; the first handler to set the state wins.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <typeparam name="TException">The exception type this handler reacts to.</typeparam>
public interface IRequestExceptionHandler<in TRequest, TResponse, in TException>
    where TRequest : notnull
    where TException : Exception
{
    /// <summary>Handle the exception; call <c>state.SetHandled(...)</c> to recover.</summary>
    Task Handle(
        TRequest request,
        TException exception,
        RequestExceptionHandlerState<TResponse> state,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reacts to an exception thrown while processing a request WITHOUT recovering from it
/// (e.g. logging, metrics, compensating side-effects). The exception always propagates
/// afterwards. All matching actions run.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TException">The exception type this action reacts to.</typeparam>
public interface IRequestExceptionAction<in TRequest, in TException>
    where TRequest : notnull
    where TException : Exception
{
    /// <summary>Execute a side-effect in response to the exception.</summary>
    Task Execute(TRequest request, TException exception, CancellationToken cancellationToken);
}
