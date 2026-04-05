namespace TVE.PureCQRS;

/// <summary>
/// Handler for notification (multiple handlers allowed)
/// </summary>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}