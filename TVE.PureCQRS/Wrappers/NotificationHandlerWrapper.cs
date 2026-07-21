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
/// Invokes a resolved handler object against a notification, typed for a specific
/// notification type in the hierarchy. Enables covariant (polymorphic) dispatch
/// without depending on the DI container's variance support.
/// </summary>
internal abstract class NotificationHandlerInvoker
{
    public abstract Task Invoke(object handler, INotification notification, CancellationToken cancellationToken);
}

internal sealed class NotificationHandlerInvoker<TTarget> : NotificationHandlerInvoker
    where TTarget : INotification
{
    public override Task Invoke(object handler, INotification notification, CancellationToken cancellationToken)
        => ((INotificationHandler<TTarget>)handler).Handle((TTarget)notification, cancellationToken);
}

/// <summary>
/// High-performance notification wrapper with covariant (polymorphic) dispatch.
/// A notification published as its runtime type is delivered to handlers registered
/// for that type AND for any base type / notification interface it derives from.
/// </summary>
internal sealed class NotificationHandlerWrapperImpl<TNotification> : NotificationHandlerWrapper
    where TNotification : INotification
{
    /// <summary>
    /// Precomputed once per notification type: the closed
    /// <c>INotificationHandler&lt;T&gt;</c> service type for every T in this
    /// notification's type hierarchy, paired with a typed invoker.
    /// </summary>
    private static readonly (Type ServiceType, NotificationHandlerInvoker Invoker)[] Targets = BuildTargets();

    private static (Type, NotificationHandlerInvoker)[] BuildTargets()
    {
        var result = new List<(Type, NotificationHandlerInvoker)>();

        foreach (var target in GetNotificationTypes(typeof(TNotification)))
        {
            var serviceType = typeof(INotificationHandler<>).MakeGenericType(target);
            var invokerType = typeof(NotificationHandlerInvoker<>).MakeGenericType(target);
            var invoker = (NotificationHandlerInvoker)Activator.CreateInstance(invokerType)!;
            result.Add((serviceType, invoker));
        }

        return result.ToArray();
    }

    /// <summary>
    /// Every type in the notification's hierarchy that itself is an
    /// <see cref="INotification"/>: the concrete type, its base classes, and the
    /// notification interfaces it implements (including <see cref="INotification"/>).
    /// </summary>
    private static IEnumerable<Type> GetNotificationTypes(Type notificationType)
    {
        // Base classes (most-derived first), including the concrete type itself.
        for (var current = notificationType; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (typeof(INotification).IsAssignableFrom(current))
            {
                yield return current;
            }
        }

        // Notification interfaces (INotification and any interface deriving from it).
        foreach (var @interface in notificationType.GetInterfaces())
        {
            if (typeof(INotification).IsAssignableFrom(@interface))
            {
                yield return @interface;
            }
        }
    }

    public override Task Handle(
        INotification notification,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        return HandleCore(notification, serviceProvider, cancellationToken);
    }

    private static Task HandleCore(
        INotification notification,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        // Fast path: notification with no base/interface targets beyond itself.
        if (Targets.Length == 1)
        {
            var handlers = serviceProvider.GetServices(Targets[0].ServiceType);
            return InvokeHandlers(Targets[0].Invoker, handlers, notification, cancellationToken);
        }

        // Covariant path: collect handlers across the hierarchy, dedupe by instance
        // so a handler registered for both a base and derived type fires only once.
        List<(NotificationHandlerInvoker Invoker, object Handler)>? matched = null;
        HashSet<object>? seen = null;

        foreach (var (serviceType, invoker) in Targets)
        {
            foreach (var handler in serviceProvider.GetServices(serviceType))
            {
                if (handler is null)
                {
                    continue;
                }

                seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
                if (!seen.Add(handler))
                {
                    continue;
                }

                matched ??= [];
                matched.Add((invoker, handler));
            }
        }

        if (matched is null)
        {
            return Task.CompletedTask;
        }

        if (matched.Count == 1)
        {
            return matched[0].Invoker.Invoke(matched[0].Handler, notification, cancellationToken);
        }

        return ExecuteAll(matched, notification, cancellationToken);
    }

    private static Task InvokeHandlers(
        NotificationHandlerInvoker invoker,
        IEnumerable<object?> handlers,
        INotification notification,
        CancellationToken cancellationToken)
    {
        var materialized = handlers as IReadOnlyList<object?> ?? handlers.ToArray();

        if (materialized.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (materialized.Count == 1)
        {
            return invoker.Invoke(materialized[0]!, notification, cancellationToken);
        }

        var tasks = new Task[materialized.Count];
        for (var i = 0; i < materialized.Count; i++)
        {
            tasks[i] = invoker.Invoke(materialized[i]!, notification, cancellationToken);
        }

        return Task.WhenAll(tasks);
    }

    private static async Task ExecuteAll(
        List<(NotificationHandlerInvoker Invoker, object Handler)> matched,
        INotification notification,
        CancellationToken cancellationToken)
    {
        var tasks = new Task[matched.Count];
        for (var i = 0; i < matched.Count; i++)
        {
            tasks[i] = matched[i].Invoker.Invoke(matched[i].Handler, notification, cancellationToken);
        }

        await Task.WhenAll(tasks);
    }
}
