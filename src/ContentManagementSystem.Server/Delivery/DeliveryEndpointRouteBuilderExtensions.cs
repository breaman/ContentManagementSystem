using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Core.Delivery.Seo;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Server.Caching;
using ContentManagementSystem.Server.Delivery.Seo;
using ContentManagementSystem.Server.Security;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Server.Delivery;

/// <summary>
/// Registration and mapping for the public delivery path (task P3-13).
/// </summary>
public static class DeliveryEndpointRouteBuilderExtensions
{
    /// <summary>Route name of the catch-all, so a test can assert it is the endpoint that matched.</summary>
    public const string DeliveryRouteName = "cms-delivery";

    /// <summary>
    /// Registers the services the delivery endpoint resolves.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCmsDeliveryEndpoint(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCmsDelivery();

        // The head, the sitemap, and robots.txt all need the site's absolute address, and only the
        // host knows it — from configuration when a deployment sets one, and from the request
        // otherwise (task P8-01). Singleton because it holds no state of its own: the request it
        // reads comes from the accessor, per call.
        services.AddHttpContextAccessor();
        services.TryAddSingleton<ISiteAddress, HttpSiteAddress>();

        // Scoped, because it is constructed with the request's service provider and hands that to
        // the component renderer — a singleton here would give every request the root provider and
        // with it one shared database context.
        services.TryAddScoped<CmsPageRenderer>();

        // Also registered by AddCmsPages; TryAdd so the two cannot produce different meters.
        services.TryAddSingleton<CmsMetrics>();

        return services;
    }

    /// <summary>
    /// Maps the catch-all that serves content URLs.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// <strong>Call this last.</strong> Routing picks the most specific match rather than the first
    /// registered one, so a literal route such as <c>/health</c> beats <c>/{**slug}</c> whatever the
    /// order — but middleware-terminated paths such as the Blazor framework files are matched by
    /// order, and "registered last" is a rule that is cheap to keep and expensive to reason about
    /// once broken (risk R6). The <c>P3-15</c> tests assert the outcome rather than the ordering, so
    /// a future reshuffle fails on the behaviour that matters.
    /// <para>
    /// <c>CacheOutput</c> applies the CMS page policy (task P8-06): anonymous requests only, tagged
    /// with what the render actually depended on, and expiring within the hour even if no
    /// invalidation ever arrives.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapCmsDelivery(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/{**slug}", DeliveryEndpoint.HandleAsync)
            .AllowAnonymous()
            .CacheOutput(CachingServiceCollectionExtensions.PagePolicyName)
            // Six hundred a minute per address (spec section 20.6, task P9-03). Deliberately far
            // above anything a reader does: a cached page costs almost nothing to serve, so the limit
            // is here for the crawler that has stopped honouring crawl-delay rather than for people.
            // It sits on this route alone, so the framework assets and the health probe a page load
            // also fetches are not counted against a visitor's budget.
            .RequireRateLimiting(CmsRateLimits.PublicPages)
            .WithName(DeliveryRouteName);

        return endpoints;
    }
}
