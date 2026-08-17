using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Registration helpers for the page services.
/// </summary>
/// <remarks>
/// Separate from <c>AddCmsContent()</c>, which registers the payload engine. These services hold a
/// database context and are therefore scoped, while the payload engine's stateless parts are
/// singleton — mixing the two lifetimes behind one call is how a singleton ends up capturing a
/// request's context.
/// </remarks>
public static class PageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services that own pages, their versions, and their publishing lifecycle.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Call alongside <c>AddCmsContent()</c> and <c>AddCmsFieldTypes()</c>: the draft and publishing
    /// services validate payloads and project reference rows, and both of those come from there.
    /// </remarks>
    public static IServiceCollection AddCmsPages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IPageTreeService, PageTreeService>();
        services.TryAddScoped<IPageService, PageService>();
        services.TryAddScoped<IDraftService, DraftService>();
        services.TryAddScoped<IRecycleBinService, RecycleBinService>();
        services.TryAddScoped<IDuplicationService, DuplicationService>();
        services.TryAddScoped<IEditLockService, EditLockService>();
        services.TryAddScoped<IPublishingService, PublishingService>();
        services.TryAddScoped<IVersionService, VersionService>();
        services.TryAddScoped<IContentDiffService, ContentDiffService>();
        services.TryAddScoped<IContentReferenceProjector, ContentReferenceProjector>();

        // Bulk operations (task P6-29). The service is scoped like the rest, because it resolves the
        // selection against a request's context; the job registry is a singleton, because a batch that
        // outlives its request has to be pollable from the next one. The scope factory is the
        // identity-free implementation, which a web host replaces — see HttpBulkOperationScopeFactory.
        services.TryAddScoped<IBulkOperationService, BulkOperationService>();
        services.TryAddSingleton<BulkOperationJobs>();
        services.TryAddScoped<IBulkOperationScopeFactory, ServiceScopeBulkOperationScopeFactory>();

        // Reusable content shares the pages' registration rather than getting a call of its own,
        // because it shares their dependencies exactly: the same validator, the same projector, the
        // same version numbering. A separate AddCmsReusableContent() would be a second door into the
        // same room, and the failure it invites is a host that calls one and not the other.
        services.TryAddScoped<IReferenceQueryService, ReferenceQueryService>();
        services.TryAddScoped<IReusableContentService, ReusableContentService>();

        // The clock is a dependency rather than a static call so edit-lock expiry and the retention
        // window can be tested without waiting two minutes or ninety days.
        services.TryAddSingleton(TimeProvider.System);

        // The publish instruments of spec section 24.1. AddMetrics is what supplies IMeterFactory,
        // and calling it here rather than relying on the host means a console tool or a test that
        // builds this graph by hand gets a working PublishingService rather than a resolution error.
        services.AddMetrics();
        services.TryAddSingleton<CmsMetrics>();

        return services;
    }
}
