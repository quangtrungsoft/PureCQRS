using Microsoft.Extensions.DependencyInjection;
using TVE.PureCQRS;

namespace TVE.PureCQRS.Tests;

/// <summary>
/// Per-test event/state collector, registered as a singleton and injected into handlers
/// so each test's <see cref="IServiceProvider"/> keeps its own isolated state
/// (safe under xUnit's parallel test execution).
/// </summary>
public sealed class Recorder
{
    private readonly object _gate = new();
    private readonly List<string> _events = [];

    public bool? LastTokenCancelled;

    public void Add(string @event)
    {
        lock (_gate)
        {
            _events.Add(@event);
        }
    }

    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    public int Count(string @event) => Events.Count(e => e == @event);
}

/// <summary>Builds an isolated mediator for a test, scanning this assembly for handlers.</summary>
internal static class TestHost
{
    public static (IMediator Mediator, Recorder Recorder) Build(
        Action<PureCQRSServiceConfiguration>? configure = null)
    {
        var recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddPureCQRS(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Recorder>();
            configure?.Invoke(cfg);
        });

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMediator>(), recorder);
    }
}

// ---------------- Shared test messages & handlers ----------------

// Cancellation
public sealed record Ping : IRequest<string>;

public sealed class PingHandler(Recorder recorder) : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken)
    {
        recorder.LastTokenCancelled = cancellationToken.IsCancellationRequested;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("pong");
    }
}

public sealed class TokenProbeBehavior<TRequest, TResponse>(Recorder recorder)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        recorder.Add($"behavior:{typeof(TRequest).Name}");
        return next(ct); // forward the token downstream
    }
}

// Covariant notifications
public abstract record OrderEvent(int Id) : INotification;
public sealed record OrderShipped(int OrderId) : OrderEvent(OrderId);

public sealed class ShippedHandler(Recorder recorder) : INotificationHandler<OrderShipped>
{
    public Task Handle(OrderShipped n, CancellationToken ct) { recorder.Add($"shipped:{n.OrderId}"); return Task.CompletedTask; }
}

public sealed class BaseOrderHandler(Recorder recorder) : INotificationHandler<OrderEvent>
{
    public Task Handle(OrderEvent n, CancellationToken ct) { recorder.Add($"base:{n.Id}"); return Task.CompletedTask; }
}

public sealed class CatchAllNotificationHandler(Recorder recorder) : INotificationHandler<INotification>
{
    public Task Handle(INotification n, CancellationToken ct) { recorder.Add("all"); return Task.CompletedTask; }
}

// Exception handling (value-returning)
public sealed record RiskyQuery(bool ShouldThrow) : IRequest<string>;

public sealed class RiskyHandler : IRequestHandler<RiskyQuery, string>
{
    public Task<string> Handle(RiskyQuery request, CancellationToken ct)
        => request.ShouldThrow ? throw new InvalidOperationException("boom") : Task.FromResult("ok");
}

public sealed class RiskyExceptionHandler(Recorder recorder)
    : IRequestExceptionHandler<RiskyQuery, string, InvalidOperationException>
{
    public Task Handle(RiskyQuery request, InvalidOperationException ex,
        RequestExceptionHandlerState<string> state, CancellationToken ct)
    {
        recorder.Add("recovered");
        state.SetHandled("fallback");
        return Task.CompletedTask;
    }
}

public sealed class RiskyExceptionAction(Recorder recorder) : IRequestExceptionAction<RiskyQuery, Exception>
{
    public Task Execute(RiskyQuery request, Exception ex, CancellationToken ct)
    {
        recorder.Add("action");
        return Task.CompletedTask;
    }
}

// A request whose handler throws an exception nobody recovers.
public sealed record UnrecoverableQuery : IRequest<string>;

public sealed class UnrecoverableHandler : IRequestHandler<UnrecoverableQuery, string>
{
    public Task<string> Handle(UnrecoverableQuery request, CancellationToken ct)
        => throw new InvalidOperationException("unrecoverable");
}

// A request type that has no handler at all.
public sealed record OrphanQuery : IRequest<string>;

// Void command (routed through IRequest<Unit>)
public sealed record DoWork(bool Fail) : IRequest;

public sealed class DoWorkHandler(Recorder recorder) : IRequestHandler<DoWork>
{
    public Task Handle(DoWork request, CancellationToken ct)
    {
        recorder.Add("work");
        if (request.Fail) throw new InvalidOperationException("void boom");
        return Task.CompletedTask;
    }
}

public sealed class DoWorkExceptionHandler(Recorder recorder)
    : IRequestExceptionHandler<DoWork, Unit, InvalidOperationException>
{
    public Task Handle(DoWork request, InvalidOperationException ex,
        RequestExceptionHandlerState<Unit> state, CancellationToken ct)
    {
        recorder.Add("void-recovered");
        state.SetHandled(Unit.Value);
        return Task.CompletedTask;
    }
}

// Constrained open-generic request handler (arity mismatch: 1 vs 2)
public sealed class Box<T> { public T Value { get; init; } = default!; }
public sealed record WrapQuery<T>(T Input) : IRequest<Box<T>>;

public sealed class WrapHandler<T> : IRequestHandler<WrapQuery<T>, Box<T>>
{
    public Task<Box<T>> Handle(WrapQuery<T> request, CancellationToken ct)
        => Task.FromResult(new Box<T> { Value = request.Input });
}

// Constrained open-generic handler that only applies to reference types with new().
public sealed record MakeQuery<T>() : IRequest<T> where T : new();

public sealed class MakeHandler<T> : IRequestHandler<MakeQuery<T>, T>
    where T : class, new()
{
    public Task<T> Handle(MakeQuery<T> request, CancellationToken ct) => Task.FromResult(new T());
}

public sealed class Widget { public string Name { get; set; } = "widget"; }
