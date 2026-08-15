using ContentManagementSystem.Core.Structure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Registration helpers for the rendering pipeline.
/// </summary>
public static class RenderingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the catalogs the render path resolves template, block, and field type keys through.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Call <c>AddCmsComponentScanning(...)</c> — directly, or through
    /// <c>AddCmsStructureReconciliation(...)</c> — to name the assemblies that declare the
    /// components, and <c>AddCmsFieldTypes()</c> for the field type registry the renderer catalog is
    /// built from. Without either, the catalogs fail to resolve at startup rather than serving a
    /// site on which every page reports an unknown template.
    /// <para>
    /// Both are singletons whose contents are computed once when they are first resolved: neither
    /// the set of deployed components nor the set of registered field types can change without a
    /// restart, and recomputing per request would put reflection on the delivery path for an answer
    /// that never changes.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCmsRendering(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<CmsComponentScanner>();
        services.TryAddSingleton<ICmsComponentCatalog, CmsComponentCatalog>();
        services.TryAddSingleton<IFieldRendererCatalog, FieldRendererCatalog>();

        return services;
    }
}
