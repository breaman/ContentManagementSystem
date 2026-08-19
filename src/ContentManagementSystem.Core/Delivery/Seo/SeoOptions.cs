namespace ContentManagementSystem.Core.Delivery.Seo;

/// <summary>
/// Deployment-level settings the search-engine output needs and content cannot supply
/// (spec sections 18.3 and 18.4).
/// </summary>
/// <remarks>
/// Everything an editor can decide lives on the page or in <c>SiteSettings</c>. What is left here is
/// the handful of facts only the deployment knows: the address the site is publicly reachable at,
/// and the sitemap's shape.
/// </remarks>
public sealed class SeoOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Cms:Seo";

    /// <summary>
    /// Absolute base address the site is published at, such as <c>https://www.example.com</c>.
    /// </summary>
    /// <remarks>
    /// Null falls back to the address of the request being served, which is right for a single-host
    /// deployment and wrong behind a proxy that terminates TLS or rewrites the host: a canonical
    /// link naming <c>http://10.0.0.4/</c> tells a crawler the page lives on an internal address.
    /// It is also the only source available to work that runs without a request, such as a sitemap
    /// warmed by a background job, so a deployment that scales out should configure it.
    /// </remarks>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Sitemap <c>changefreq</c> for pages that state none.</summary>
    public string DefaultChangeFrequency { get; set; } = "weekly";

    /// <summary>Sitemap <c>priority</c> for pages that state none.</summary>
    public decimal DefaultPriority { get; set; } = 0.5m;

    /// <summary>
    /// URLs per sitemap file, above which the sitemap becomes an index of several
    /// (spec section 18.3).
    /// </summary>
    /// <remarks>
    /// 40,000 rather than the protocol's own 50,000 limit, leaving headroom so that a site sitting
    /// just under the cap does not start emitting an invalid sitemap on the day somebody publishes
    /// one more page.
    /// </remarks>
    public int SitemapPageSize { get; set; } = 40_000;
}
