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

        endpoints.MapGet("/sitemap.xml", SitemapEndpoint.IndexAsync)
            .AllowAnonymous()
            .WithName(SitemapEndpoint.RouteName);

        endpoints.MapGet("/sitemap-{page:int:min(1)}.xml", SitemapEndpoint.PageAsync)
            .AllowAnonymous()
            .WithName(SitemapEndpoint.PageRouteName);

        endpoints.MapGet("/robots.txt", RobotsEndpoint.HandleAsync)
            .AllowAnonymous()
            .WithName(RobotsEndpoint.RouteName);

        return endpoints;
    }
}
