using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace TVE.PureCQRS;

/// <summary>
/// Configuration for PureCQRS registration
/// </summary>
public sealed class PureCQRSServiceConfiguration
{
    internal List<Assembly> AssembliesToRegister { get; } = [];
    internal List<Type> OpenBehaviors { get; } = [];
    internal ServiceLifetime HandlerLifetime { get; private set; } = ServiceLifetime.Transient;
    internal Type MediatorImplementationType { get; private set; } = typeof(Mediator);

    /// <summary>
    /// Register handlers from assembly
    /// </summary>
    public PureCQRSServiceConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        AssembliesToRegister.Add(assembly);
        return this;
    }

    /// <summary>
    /// Register handlers from assemblies
    /// </summary>
    public PureCQRSServiceConfiguration RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        AssembliesToRegister.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Register handlers from assembly containing type
    /// </summary>
    public PureCQRSServiceConfiguration RegisterServicesFromAssemblyContaining<T>()
    {
        return RegisterServicesFromAssembly(typeof(T).Assembly);
    }

    /// <summary>
    /// Register handlers from assembly containing type
    /// </summary>
    public PureCQRSServiceConfiguration RegisterServicesFromAssemblyContaining(Type type)
    {
        return RegisterServicesFromAssembly(type.Assembly);
    }

    /// <summary>
    /// Add open generic pipeline behavior
    /// </summary>
    /// <param name="openBehaviorType">Open generic type, e.g., typeof(LoggingBehavior&lt;,&gt;)</param>
    public PureCQRSServiceConfiguration AddOpenBehavior(Type openBehaviorType)
    {
        if (!openBehaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"'{openBehaviorType.Name}' must be an open generic type definition",
                nameof(openBehaviorType));
        }

        OpenBehaviors.Add(openBehaviorType);
        return this;
    }

    /// <summary>
    /// Add open generic pipeline behavior
    /// </summary>
    public PureCQRSServiceConfiguration AddOpenBehavior<TBehavior>()
    {
        return AddOpenBehavior(typeof(TBehavior));
    }

    /// <summary>
    /// Set handler lifetime (default: Transient)
    /// </summary>
    public PureCQRSServiceConfiguration WithHandlerLifetime(ServiceLifetime lifetime)
    {
        HandlerLifetime = lifetime;
        return this;
    }

    /// <summary>
    /// Use custom mediator implementation
    /// </summary>
    public PureCQRSServiceConfiguration Using<TMediator>() where TMediator : IMediator
    {
        MediatorImplementationType = typeof(TMediator);
        return this;
    }
}