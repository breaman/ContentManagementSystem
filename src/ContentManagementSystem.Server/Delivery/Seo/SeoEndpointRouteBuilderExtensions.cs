using ContentManagementSystem.Server.Caching;

namespace ContentManagementSystem.Server.Delivery.Seo;

/// <summary>
/// Maps the two files a search engine looks for before it looks at any page (tasks P8-04, P8-05).
/// </summary>
public static class SeoEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <c>/sitemap.xml</c>, its numbered files, and <c>/robots.txt</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// Mapped before the content catch-all, and safe from being shadowed by a page in any case:
    /// both names are in <c>Slugs.Reserved</c>, so no page can be created at either URL.
    /// </remarks>
    public static IEndpointRouteBuilder MapCmsSeo(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Cached under the same policy as a page, and tagged `content`: any publish changes what
        // belongs in a sitemap, and there is no narrower dependency to take (spec section 18.3).
        endpoints.MapGet("/sitemap.xml", SitemapEndpoint.IndexAsync)
            .AllowAnonymous()
            .CacheOutput(CachingServiceCollectionExtensions.PagePolicyName)
            .WithName(SitemapEndpoint.RouteName);

        endpoints.MapGet("/sitemap-{page:int:min(1)}.xml", SitemapEndpoint.PageAsync)
            .AllowAnonymous()
            .CacheOutput(CachingServiceCollectionExtensions.PagePolicyName)
            .WithName(SitemapEndpoint.PageRouteName);

        // Not cached, unlike the sitemap. The body depends on the environment as well as on the
        // settings row, and a copy of the production body cached in front of staging would undo the
        // one rule here that must not be reachable by editing anything (spec section 18.4).
        endpoints.MapGet("/robots.txt", RobotsEndpoint.HandleAsync)
            .AllowAnonymous()
            .WithName(RobotsEndpoint.RouteName);

        return endpoints;
    }
}
