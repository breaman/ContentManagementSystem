using System.Reflection;

using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Fields;

/// <summary>
/// Registration helpers for the field type framework.
/// </summary>
public static class FieldTypeServiceCollectionExtensions
{
    /// <summary>
    /// Registers a single field type and the registry that will expose it.
    /// </summary>
    /// <typeparam name="TFieldType">The field type implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// This is the documented extension point (spec section 7.3, step 4). Field types are
    /// singletons, so implementations must be stateless and thread-safe.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddCmsFieldType&lt;MarkdownTableFieldType&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddCmsFieldType<TFieldType>(this IServiceCollection services)
        where TFieldType : class, IFieldType
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFieldTypeRegistry();
        // TryAddEnumerable keeps a double registration — the assembly scan plus an explicit call
        // for the same type — from becoming a duplicate-key startup failure.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFieldType, TFieldType>());

        return services;
    }

    /// <summary>
    /// Registers every concrete <see cref="IFieldType"/> declared in an assembly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assembly">Assembly to scan.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Used to pick up the built-in field types without listing eleven registrations by hand.
    /// Applications adding their own should prefer the explicit
    /// <see cref="AddCmsFieldType{TFieldType}"/>, so a field type never appears in a deployment
    /// merely because it was compiled into an assembly that happened to be scanned.
    /// <para>
    /// Only public types are discovered. A field type is part of a deployment's content contract —
    /// its key ends up in stored payloads — so an internal or nested implementation is a helper or
    /// a test double, not something to register behind the author's back.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCmsFieldTypesFrom(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        services.AddFieldTypeRegistry();

        foreach (var type in assembly.GetExportedTypes())
        {
            if (type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false } &&
                typeof(IFieldType).IsAssignableFrom(type))
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IFieldType), type));
            }
        }

        return services;
    }

    /// <summary>
    /// Registers the built-in field types shipped in this assembly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// The <c>richText</c> and <c>html</c> field types take an
    /// <see cref="Shared.Contracts.Security.IContentSanitizer"/>, so a container registering these
    /// without one fails to resolve the registry. That is deliberate: the alternative to failing at
    /// startup is a deployment that quietly stores unsanitized markup.
    /// </remarks>
    public static IServiceCollection AddCmsFieldTypes(this IServiceCollection services) =>
        services.AddCmsFieldTypesFrom(CoreAssemblyMarker.Assembly);

    private static void AddFieldTypeRegistry(this IServiceCollection services) =>
        services.TryAddSingleton<IFieldTypeRegistry, FieldTypeRegistry>();
}
