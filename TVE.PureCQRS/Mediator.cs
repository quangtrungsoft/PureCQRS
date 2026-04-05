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

    public Mediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

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

    public Task Send(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();

        var wrapper = (RequestHandlerWrapperVoid)_requestHandlers.GetOrAdd(
            requestType,
            static t =>
            {
                var wrapperType = typeof(RequestHandlerWrapperVoidImpl<>).MakeGenericType(t);
                return (RequestHandlerBase)Activator.CreateInstance(wrapperType)!;
            });

        return wrapper.Handle(request, _serviceProvider, cancellationToken);
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var responseType = GetResponseType(requestType);

        if (responseType != null)
        {
            var wrapper = _requestHandlers.GetOrAdd(
                requestType,
                CreateWrapper,
                responseType);

            return wrapper.Handle(request, _serviceProvider, cancellationToken);
        }

        if (typeof(IRequest).IsAssignableFrom(requestType))
        {
            var wrapper = _requestHandlers.GetOrAdd(
                requestType,
                static t =>
                {
                    var wrapperType = typeof(RequestHandlerWrapperVoidImpl<>).MakeGenericType(t);
                    return (RequestHandlerBase)Activator.CreateInstance(wrapperType)!;
                });

            return wrapper.Handle(request, _serviceProvider, cancellationToken);
        }

        throw new InvalidRequestException(requestType);
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