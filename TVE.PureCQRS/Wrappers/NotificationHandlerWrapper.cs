using Microsoft.Extensions.DependencyInjection;

namespace TVE.PureCQRS.Wrappers;

/// <summary>
/// Base wrapper for notifications
/// </summary>
internal abstract class NotificationHandlerWrapper
{
    public abstract Task Handle(
        INotification notification,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);
}

/// <summary>
/// High-performance notification wrapper
/// </summary>
internal sealed class NotificationHandlerWrapperImpl<TNotification> : NotificationHandlerWrapper
    where TNotification : INotification
{
    public override Task Handle(
        INotification notification,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        return HandleCore((TNotification)notification, serviceProvider, cancellationToken);
    }

    private static Task HandleCore(
        TNotification notification,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        // Materialize handlers ngay
        var handlers = serviceProvider
            .GetServices<INotificationHandler<TNotification>>()
            .ToArray();

        // Fast path - không có handler
        if (handlers.Length == 0)
        {
            return Task.CompletedTask;
        }

        // Single handler - direct call
        if (handlers.Length == 1)
        {
            return handlers[0].Handle(notification, cancellationToken);
        }

        // Multiple handlers - parallel
        return ExecuteAll(notification, handlers, cancellationToken);
    }

    private static async Task ExecuteAll(
        TNotification notification,
        INotificationHandler<TNotification>[] handlers,
        CancellationToken cancellationToken)
    {
        // Pre-allocate task array
        var tasks = new Task[handlers.Length];
        
        for (var i = 0; i < handlers.Length; i++)
        {
            tasks[i] = handlers[i].Handle(notification, cancellationToken);
        }

        await Task.WhenAll(tasks);
    }
}