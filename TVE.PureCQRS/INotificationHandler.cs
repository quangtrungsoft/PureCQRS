namespace TVE.PureCQRS;

/// <summary>
/// Handler for notification (multiple handlers allowed)
/// </summary>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>Handles the published notification.</summary>
    /// <param name="notification">The notification instance.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}