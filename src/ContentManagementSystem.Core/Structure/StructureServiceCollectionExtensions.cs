using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// Registration helpers for the structure services behind the content-model admin.
/// </summary>
public static class StructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the template, zone, and block-type services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Scoped, because they hold the request's <see cref="Data.Models.ApplicationDbContext"/>. Each
    /// one also resolves an <see cref="Shared.Contracts.Security.ICmsAuthorization"/>, which the
    /// hosting layer supplies — <c>Core</c> has no way to see who is calling, and a default that
    /// permitted everything would make the service-layer checks decorative.
    /// </remarks>
    public static IServiceCollection AddCmsStructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ITemplateService, TemplateService>();
        services.TryAddScoped<IZoneService, ZoneService>();
        services.TryAddScoped<IBlockTypeService, BlockTypeService>();
        services.TryAddScoped<ICompositionService, CompositionService>();

        // Singleton, unlike the rest: it describes the field type registry, which is itself a
        // singleton whose contents cannot change without a restart. Building a dozen JSON Schema
        // documents per request to describe something constant would be waste on the one screen a
        // developer refreshes repeatedly.
        services.TryAddSingleton<IFieldTypeCatalog, FieldTypeCatalog>();

        return services;
    }

    /// <summary>
    /// Registers the startup reconciliation and schema sync (tasks P1-25 and P1-26).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">
    /// The assemblies declaring <c>[CmsTemplate]</c> and <c>[CmsBlockType]</c>. Named explicitly —
    /// see <see cref="CmsStructureAssemblies"/> for why the scan is not left to discover them.
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Separate from <see cref="AddCmsStructure"/> because the two are wanted independently: the
    /// admin API is useful in a host that reconciles nothing, and a migration or tooling host may
    /// want the reconciler without mapping an endpoint.
    /// </remarks>
    public static IServiceCollection AddCmsStructureReconciliation(
        this IServiceCollection services,
        params System.Reflection.Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCmsComponentScanning(assemblies);
        services.TryAddScoped<ITemplateReconciler, TemplateReconciler>();
        services.TryAddScoped<ISchemaSyncService, SchemaSyncService>();

        return services;
    }

    /// <summary>
    /// Names the assemblies declaring <c>[CmsTemplate]</c> and <c>[CmsBlockType]</c> components and
    /// registers the scanner that reads them.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Its own method because two unrelated features consume the same answer: reconciliation, which
    /// creates and orphans rows from it, and rendering, which turns a stored <c>templateKey</c> back
    /// into a component. A host wanting only one of the two still names its assemblies exactly once.
    /// </remarks>
    public static IServiceCollection AddCmsComponentScanning(
        this IServiceCollection services,
        params System.Reflection.Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(new CmsStructureAssemblies(assemblies));
        services.TryAddSingleton<CmsComponentScanner>();

        return services;
    }
}
