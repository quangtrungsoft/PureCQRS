namespace TVE.PureCQRS;

/// <summary>
/// Publish notifications to multiple handlers
/// </summary>
public interface IPublisher
{
    /// <summary>Publishes a notification (typed as <see cref="object"/>) to all matching handlers.</summary>
    /// <param name="notification">The notification instance; must implement <see cref="INotification"/>.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task Publish(object notification, CancellationToken cancellationToken = default);

    /// <summary>Publishes a notification to all matching handlers.</summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification instance.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}