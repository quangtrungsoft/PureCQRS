using System.Collections.Concurrent;
using TVE.PureCQRS.Wrappers;

namespace TVE.PureCQRS;

/// <summary>
/// High-performance mediator implementation
/// </summary>
public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;

    private static readonly ConcurrentDictionary<Type, RequestHandlerBase> _requestHandlers = new();
    private static readonly ConcurrentDictionary<Type, NotificationHandlerWrapper> _notificationHandlers = new();

    /// <summary>Creates the mediator over the given service provider used to resolve handlers.</summary>
    /// <param name="serviceProvider">The application service provider.</param>
    public Mediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var responseType = typeof(TResponse);

        var wrapper = (RequestHandlerWrapper<TResponse>)_requestHandlers.GetOrAdd(
            requestType,
            CreateWrapper,
            responseType);

        return wrapper.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public Task Send(IRequest request, CancellationToken cancellationToken = default)
    {
        // A void command is an IRequest<Unit>, so it flows through the same pipeline
        // (behaviors + exception handling) as any value-returning request.
        return Send<Unit>(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var responseType = GetResponseType(requestType);

        // Void commands surface as IRequest<Unit>, so responseType is Unit here.
        if (responseType is null)
        {
            throw new InvalidRequestException(requestType);
        }

        var wrapper = _requestHandlers.GetOrAdd(
            requestType,
            CreateWrapper,
            responseType);

        return wrapper.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <summary>
    /// Publish notification - SỬ DỤNG RUNTIME TYPE
    /// </summary>
    public Task Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        // ✅ KEY FIX: Dùng GetType() để lấy RUNTIME type
        // Không dùng typeof(TNotification) vì đó là compile-time type
        var notificationType = notification.GetType();

        var wrapper = _notificationHandlers.GetOrAdd(
            notificationType,
            static t =>
            {
                var wrapperType = typeof(NotificationHandlerWrapperImpl<>).MakeGenericType(t);
                return (NotificationHandlerWrapper)Activator.CreateInstance(wrapperType)!;
            });

        return wrapper.Handle(notification, _serviceProvider, cancellationToken);
    }

    /// <summary>
    /// Publish notification as object - RUNTIME TYPE
    /// </summary>
    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification is not INotification notif)
        {
            throw new ArgumentException(
                $"'{notification.GetType().Name}' does not implement INotification",
                nameof(notification));
        }

        var notificationType = notification.GetType();

        var wrapper = _notificationHandlers.GetOrAdd(
            notificationType,
            static t =>
            {
                var wrapperType = typeof(NotificationHandlerWrapperImpl<>).MakeGenericType(t);
                return (NotificationHandlerWrapper)Activator.CreateInstance(wrapperType)!;
            });

        return wrapper.Handle(notif, _serviceProvider, cancellationToken);
    }

    private static RequestHandlerBase CreateWrapper(Type requestType, Type responseType)
    {
        var wrapperType = typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType);
        return (RequestHandlerBase)Activator.CreateInstance(wrapperType)!;
    }

    private static Type? GetResponseType(Type requestType)
    {
        foreach (var @interface in requestType.GetInterfaces())
        {
            if (@interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IRequest<>))
            {
                return @interface.GetGenericArguments()[0];
            }
        }
        return null;
    }
}