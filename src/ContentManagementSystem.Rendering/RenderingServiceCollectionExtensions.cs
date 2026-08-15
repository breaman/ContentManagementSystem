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
    /// Registers the component catalog the render path resolves template and block keys through.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Call <c>AddCmsComponentScanning(...)</c> — directly, or through
    /// <c>AddCmsStructureReconciliation(...)</c> — to name the assemblies that declare the
    /// components. Without it the catalog fails to resolve at startup rather than serving a site on
    /// which every page reports an unknown template.
    /// <para>
    /// A singleton, and the scan runs once when it is first resolved: the set of deployed components
    /// cannot change without a restart, and scanning per request would put reflection on the
    /// delivery path for an answer that never changes.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCmsRendering(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<CmsComponentScanner>();
        services.TryAddSingleton<ICmsComponentCatalog, CmsComponentCatalog>();

        return services;
    }
}
