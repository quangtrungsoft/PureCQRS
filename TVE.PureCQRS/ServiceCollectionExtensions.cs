using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        foreach (var assembly in config.AssembliesToRegister)
        {
            RegisterHandlersFromAssembly(services, assembly, config.HandlerLifetime);
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

    private static void RegisterHandlersFromAssembly(
        IServiceCollection services,
        Assembly assembly,
        ServiceLifetime lifetime)
    {
        var types = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericType: false });

        foreach (var type in types)
        {
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
        }
    }

    private static void RegisterImplementations(
        IServiceCollection services,
        Type implementationType,
        Type openGenericInterface,
        ServiceLifetime lifetime,
        bool addMultiple)
    {
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
        }
    }
}