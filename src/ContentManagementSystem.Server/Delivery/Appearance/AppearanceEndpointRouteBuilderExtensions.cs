using ContentManagementSystem.Server.Caching;

namespace ContentManagementSystem.Server.Delivery.Appearance;

/// <summary>
/// Maps the public site stylesheet (task P10-05).
/// </summary>
public static class AppearanceEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <c>/css/site-custom.css</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// Mapped before the content catch-all. <c>/css</c> is served by the static-file middleware for
    /// the files the front-end build produces; this path is not one of them, so the request falls
    /// through to routing and reaches here. Living under <c>/css</c> anyway is deliberate: the two
    /// stylesheets are neighbours in the document, and a reader looking at the page source should
    /// not have to learn a second convention to find the second one.
    /// <para>
    /// Cached under the page policy, which caches anonymous requests only and attaches the tags the
    /// handler published — <c>sitecss</c>, evicted by a publish on every instance.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapCmsSiteStylesheet(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Deliberately not rate-limited. `CmsRateLimits.PublicPages` is a *page* budget, and every
        // page load fetches this file alongside the page — counting it would halve the budget a
        // visitor actually has. It is the same reason the framework assets and `site.css` are not
        // limited either: an asset a page requires does not spend the allowance for the page
        // (task P9-03, and `RateLimitTests` pins the list).
        endpoints.MapGet(SiteStylesheetEndpoint.Path, SiteStylesheetEndpoint.HandleAsync)
            .AllowAnonymous()
            .CacheOutput(CachingServiceCollectionExtensions.PagePolicyName)
            .WithName(SiteStylesheetEndpoint.RouteName);

        return endpoints;
    }
}
