using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TVE.PureCQRS.Wrappers;

namespace TVE.PureCQRS;

/// <summary>
/// Extension methods for registering PureCQRS
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add PureCQRS services with configuration
    /// </summary>
    public static IServiceCollection AddPureCQRS(
        this IServiceCollection services,
        Action<PureCQRSServiceConfiguration> configure)
    {
        var config = new PureCQRSServiceConfiguration();
        configure(config);

        // Register Mediator
        services.TryAdd(new ServiceDescriptor(typeof(IMediator), config.MediatorImplementationType, ServiceLifetime.Transient));
        services.TryAdd(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<IMediator>(), ServiceLifetime.Transient));
        services.TryAdd(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<IMediator>(), ServiceLifetime.Transient));

        // Register handlers from assemblies
        var hasExceptionHandling = false;
        var openGenericHandlers = new List<Type>();
        foreach (var assembly in config.AssembliesToRegister)
        {
            hasExceptionHandling |= RegisterHandlersFromAssembly(services, assembly, config.HandlerLifetime, openGenericHandlers);
        }

        // Constrained/open-generic request handlers are closed at runtime for the concrete
        // request/response pair (something the DI container cannot do when arities differ).
        // Registered only when such handlers exist, so the normal path never pays for it.
        if (openGenericHandlers.Count > 0)
        {
            services.TryAddSingleton(new GenericRequestHandlerRegistry(openGenericHandlers));
        }

        // OPTIMIZATION: the exception pipeline behaviors are added ONLY when the
        // scanned assemblies actually contain exception handlers/actions. Apps that don't use
        // exception handling keep the zero-behavior fast path with no try/catch overhead.
        // Registered before user behaviors so the processor is the OUTERMOST step and can
        // observe exceptions thrown by user behaviors too.
        if (hasExceptionHandling)
        {
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequestExceptionProcessorBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequestExceptionActionProcessorBehavior<,>));
        }

        // Register open behaviors (in order)
        foreach (var behaviorType in config.OpenBehaviors)
        {
            services.AddTransient(typeof(IPipelineBehavior<,>), behaviorType);
        }

        return services;
    }

    /// <summary>
    /// Add PureCQRS with assemblies (simple overload)
    /// </summary>
    public static IServiceCollection AddPureCQRS(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        return services.AddPureCQRS(config =>
        {
            config.RegisterServicesFromAssemblies(assemblies);
        });
    }

    /// <summary>
    /// Registers all handlers found in the assembly.
    /// Returns <c>true</c> if any exception handlers or actions were registered, signalling
    /// that the exception pipeline behaviors should be wired up.
    /// </summary>
    private static bool RegisterHandlersFromAssembly(
        IServiceCollection services,
        Assembly assembly,
        ServiceLifetime lifetime,
        List<Type> openGenericHandlers)
    {
        var allTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false });

        var hasExceptionHandling = false;

        foreach (var type in allTypes)
        {
            // Open-generic handler definitions are closed on demand at runtime (see
            // GenericRequestHandlerRegistry) rather than registered as closed services.
            if (type.IsGenericTypeDefinition)
            {
                if (type.GetInterfaces().Any(i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                {
                    openGenericHandlers.Add(type);
                }

                continue;
            }

            // IRequestHandler<TRequest, TResponse>
            RegisterImplementations(
                services,
                type,
                typeof(IRequestHandler<,>),
                lifetime,
                addMultiple: false);

            // IRequestHandler<TRequest> (void)
            RegisterImplementations(
                services,
                type,
                typeof(IRequestHandler<>),
                lifetime,
                addMultiple: false);

            // INotificationHandler<TNotification>
            RegisterImplementations(
                services,
                type,
                typeof(INotificationHandler<>),
                lifetime,
                addMultiple: true);

            // IRequestExceptionHandler<TRequest, TResponse, TException>
            hasExceptionHandling |= RegisterImplementations(
                services,
                type,
                typeof(IRequestExceptionHandler<,,>),
                lifetime,
                addMultiple: true);

            // IRequestExceptionAction<TRequest, TException>
            hasExceptionHandling |= RegisterImplementations(
                services,
                type,
                typeof(IRequestExceptionAction<,>),
                lifetime,
                addMultiple: true);
        }

        return hasExceptionHandling;
    }

    /// <summary>Returns <c>true</c> if at least one matching interface was registered.</summary>
    private static bool RegisterImplementations(
        IServiceCollection services,
        Type implementationType,
        Type openGenericInterface,
        ServiceLifetime lifetime,
        bool addMultiple)
    {
        var registered = false;

        var interfaces = implementationType
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface);

        foreach (var @interface in interfaces)
        {
            var descriptor = new ServiceDescriptor(@interface, implementationType, lifetime);

            if (addMultiple)
            {
                services.Add(descriptor);
            }
            else
            {
                services.TryAdd(descriptor);
            }

            registered = true;
        }

        return registered;
    }
}