namespace TVE.PureCQRS;

/// <summary>
/// Handler for request with response
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Handles the request and produces a response.</summary>
    /// <param name="request">The request instance.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The response.</returns>
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Handler for request without response (void command).
/// Bridges to <see cref="IRequestHandler{TRequest,TResponse}"/> with <see cref="Unit"/>
/// via a default interface implementation, so void commands run through the full pipeline
/// without callers ever seeing <see cref="Unit"/>.
/// </summary>
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>
{
    /// <summary>Handle the command.</summary>
    new Task Handle(TRequest request, CancellationToken cancellationToken);

    /// <summary>Adapts the void handler to the <see cref="Unit"/>-returning pipeline.</summary>
    async Task<Unit> IRequestHandler<TRequest, Unit>.Handle(TRequest request, CancellationToken cancellationToken)
    {
        await Handle(request, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}