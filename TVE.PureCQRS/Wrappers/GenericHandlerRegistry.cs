using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace TVE.PureCQRS.Wrappers;

/// <summary>
/// Resolves open-generic request handlers by closing them at runtime for the concrete
/// request/response pair. This covers the case the built-in DI container cannot — where the
/// handler's arity differs from the closed interface, e.g.
/// <c>GetByIdHandler&lt;TEntity&gt; : IRequestHandler&lt;GetByIdQuery&lt;TEntity&gt;, TEntity&gt;</c>.
/// Generic constraints are validated before closing, and the compiled
/// <see cref="ObjectFactory"/> is cached per request/response pair.
/// </summary>
internal sealed class GenericRequestHandlerRegistry
{
    private readonly Type[] _openHandlers;
    private readonly ConcurrentDictionary<(Type Request, Type Response), ObjectFactory?> _factories = new();

    public GenericRequestHandlerRegistry(IEnumerable<Type> openHandlers)
        => _openHandlers = openHandlers.ToArray();

    public bool HasCandidates => _openHandlers.Length > 0;

    /// <summary>
    /// Creates a handler instance for the given request/response types, or returns
    /// <c>null</c> if no open-generic handler can be closed to satisfy them.
    /// </summary>
    public object? CreateHandler(IServiceProvider serviceProvider, Type requestType, Type responseType)
    {
        var factory = _factories.GetOrAdd((requestType, responseType), key =>
        {
            var closed = TryClose(key.Request, key.Response);
            return closed is null ? null : ActivatorUtilities.CreateFactory(closed, Type.EmptyTypes);
        });

        return factory?.Invoke(serviceProvider, null);
    }

    private Type? TryClose(Type requestType, Type responseType)
    {
        foreach (var openHandler in _openHandlers)
        {
            var typeParameters = openHandler.GetGenericArguments();

            foreach (var @interface in openHandler.GetInterfaces())
            {
                if (!@interface.IsGenericType ||
                    @interface.GetGenericTypeDefinition() != typeof(IRequestHandler<,>))
                {
                    continue;
                }

                var patternArgs = @interface.GetGenericArguments();
                var bindings = new Dictionary<Type, Type>();

                if (!TryUnify(patternArgs[0], requestType, bindings) ||
                    !TryUnify(patternArgs[1], responseType, bindings))
                {
                    continue;
                }

                var typeArguments = new Type[typeParameters.Length];
                var allBound = true;
                for (var i = 0; i < typeParameters.Length; i++)
                {
                    if (!bindings.TryGetValue(typeParameters[i], out var bound))
                    {
                        allBound = false;
                        break;
                    }

                    typeArguments[i] = bound;
                }

                if (!allBound || !SatisfiesConstraints(typeParameters, typeArguments))
                {
                    continue;
                }

                Type closed;
                try
                {
                    closed = openHandler.MakeGenericType(typeArguments);
                }
                catch (ArgumentException)
                {
                    // A constraint the reflection checks missed; skip this candidate.
                    continue;
                }

                var target = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);
                if (target.IsAssignableFrom(closed))
                {
                    return closed;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Structurally unifies an open pattern type (which may contain generic parameters)
    /// against a concrete type, accumulating parameter bindings.
    /// </summary>
    private static bool TryUnify(Type pattern, Type concrete, Dictionary<Type, Type> bindings)
    {
        if (pattern.IsGenericParameter)
        {
            if (bindings.TryGetValue(pattern, out var existing))
            {
                return existing == concrete;
            }

            bindings[pattern] = concrete;
            return true;
        }

        if (pattern.IsGenericType)
        {
            if (!concrete.IsGenericType ||
                pattern.GetGenericTypeDefinition() != concrete.GetGenericTypeDefinition())
            {
                return false;
            }

            var patternArgs = pattern.GetGenericArguments();
            var concreteArgs = concrete.GetGenericArguments();
            if (patternArgs.Length != concreteArgs.Length)
            {
                return false;
            }

            for (var i = 0; i < patternArgs.Length; i++)
            {
                if (!TryUnify(patternArgs[i], concreteArgs[i], bindings))
                {
                    return false;
                }
            }

            return true;
        }

        return pattern == concrete;
    }

    private static bool SatisfiesConstraints(Type[] typeParameters, Type[] typeArguments)
    {
        for (var i = 0; i < typeParameters.Length; i++)
        {
            var parameter = typeParameters[i];
            var argument = typeArguments[i];
            var special = parameter.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask;

            if ((special & GenericParameterAttributes.ReferenceTypeConstraint) != 0 && argument.IsValueType)
            {
                return false;
            }

            if ((special & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 &&
                (!argument.IsValueType ||
                 (argument.IsGenericType && argument.GetGenericTypeDefinition() == typeof(Nullable<>))))
            {
                return false;
            }

            if ((special & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
                !argument.IsValueType && argument.GetConstructor(Type.EmptyTypes) is null)
            {
                return false;
            }

            foreach (var constraint in parameter.GetGenericParameterConstraints())
            {
                var resolved = constraint.ContainsGenericParameters
                    ? Substitute(constraint, typeParameters, typeArguments)
                    : constraint;

                if (resolved is null || !resolved.IsAssignableFrom(argument))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Replaces generic parameters in a constraint type with the inferred arguments.</summary>
    private static Type? Substitute(Type type, Type[] typeParameters, Type[] typeArguments)
    {
        if (type.IsGenericParameter)
        {
            var index = Array.IndexOf(typeParameters, type);
            return index >= 0 ? typeArguments[index] : null;
        }

        if (type.IsGenericType)
        {
            var args = type.GetGenericArguments();
            var mapped = new Type[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                var m = Substitute(args[i], typeParameters, typeArguments);
                if (m is null)
                {
                    return null;
                }

                mapped[i] = m;
            }

            return type.GetGenericTypeDefinition().MakeGenericType(mapped);
        }

        return type;
    }
}
