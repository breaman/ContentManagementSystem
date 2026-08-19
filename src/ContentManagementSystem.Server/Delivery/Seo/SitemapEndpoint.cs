using System.Globalization;
using System.Text;
using System.Xml;

using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Delivery.Seo;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Delivery.Seo;

/// <summary>
/// <c>sitemap.xml</c>, and the numbered files it becomes above the split threshold
/// (task P8-04, spec section 18.3).
/// </summary>
/// <remarks>
/// Built from the same facts delivery serves from — a published route and a published version — so a
/// URL can only appear here if a visitor following it gets a page. The three exclusions are stated
/// in the query rather than filtered afterwards: an unpublished page has no published route, a
/// <c>noindex</c> page is excluded by its own version's flag, and the configured 404 page is
/// excluded by id, because a sitemap that advertises the not-found page teaches a crawler that the
/// site's error document is content.
/// <para>
/// Above <see cref="SeoOptions.SitemapPageSize"/> URLs the response becomes a sitemap index naming
/// the numbered files, each of which is a page of the same ordered query. The order is by URL and
/// therefore stable: a crawler that fetches file 3 an hour after file 2 must not be handed a
/// shuffled set that skips pages.
/// </para>
/// </remarks>
public static class SitemapEndpoint
{
    /// <summary>The sitemap namespace.</summary>
    public const string Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <summary>Route name of the index, so a link to it can be generated rather than typed.</summary>
    public const string RouteName = "cms-sitemap";

    /// <summary>Route name of one numbered file.</summary>
    public const string PageRouteName = "cms-sitemap-page";

    /// <summary>The XML declaration every sitemap response opens with.</summary>
    private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

    /// <summary>Media type every sitemap response carries.</summary>
    private const string ContentType = "application/xml; charset=utf-8";

    /// <summary>
    /// How long a sitemap may be cached for. Short: it is regenerated on publish through the
    /// <c>content</c> tag, and this is only the backstop for a missed eviction.
    /// </summary>
    private const string SitemapCacheControl = "max-age=0, s-maxage=300, must-revalidate";

    /// <summary>Serves <c>/sitemap.xml</c>: either the whole set, or an index of the files.</summary>
    /// <param name="http">The request.</param>
    /// <param name="context">The application database context.</param>
    /// <param name="site">The absolute address entries are written against.</param>
    /// <param name="options">The split threshold and the per-page defaults.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    public static async Task IndexAsync(
        HttpContext http,
        ApplicationDbContext context,
        ISiteAddress site,
        IOptions<SeoOptions> options,
        CancellationToken cancellationToken)
    {
        var pageSize = PageSize(options.Value);
        var total = await Indexable(context).CountAsync(cancellationToken);

        if (total <= pageSize)
        {
            await WriteUrlSetAsync(http, context, site, options.Value, page: 1, pageSize, cancellationToken);

            return;
        }

        var files = (total + pageSize - 1) / pageSize;
        var builder = new StringBuilder(256 + (files * 96));

        using (var writer = Writer(builder))
        {
            writer.WriteStartElement("sitemapindex", Namespace);

            for (var file = 1; file <= files; file++)
            {
                writer.WriteStartElement("sitemap", Namespace);
                writer.WriteElementString(
                    "loc",
                    Namespace,
                    site.Absolute(string.Create(CultureInfo.InvariantCulture, $"/sitemap-{file}.xml")));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        await WriteAsync(http, builder.ToString(), cancellationToken);
    }

    /// <summary>Serves one numbered file of a split sitemap.</summary>
    /// <param name="http">The request.</param>
    /// <param name="page">The 1-based file number from the URL.</param>
    /// <param name="context">The application database context.</param>
    /// <param name="site">The absolute address entries are written against.</param>
    /// <param name="options">The split threshold and the per-page defaults.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <remarks>
    /// A file number past the end is a 404 rather than an empty sitemap. An empty
    /// <c>&lt;urlset&gt;</c> is a valid document saying the site has no pages, which is exactly the
    /// wrong answer to give a crawler that guessed at a URL.
    /// </remarks>
    public static async Task PageAsync(
        HttpContext http,
        int page,
        ApplicationDbContext context,
        ISiteAddress site,
        IOptions<SeoOptions> options,
        CancellationToken cancellationToken)
    {
        var pageSize = PageSize(options.Value);

        if (page < 1 || (page - 1) * (long)pageSize >= await Indexable(context).CountAsync(cancellationToken))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        await WriteUrlSetAsync(http, context, site, options.Value, page, pageSize, cancellationToken);
    }

    private static async Task WriteUrlSetAsync(
        HttpContext http,
        ApplicationDbContext context,
        ISiteAddress site,
        SeoOptions options,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Ordered and paged over the route rows, then projected. Ordering after the projection
        // would be an order over a constructed record, which EF cannot translate — and doing it
        // client-side would mean paging a table.
        var entries = await Indexable(context)
            .OrderBy(route => route.Url)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(route => new SitemapEntry(
                route.Url,
                route.Page.PublishedVersion!.PublishedOn,
                route.Page.PublishedVersion.ModifiedOn,
                route.Page.PublishedVersion.ChangeFreq,
                route.Page.PublishedVersion.Priority))
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder(256 + (entries.Count * 160));

        using (var writer = Writer(builder))
        {
            writer.WriteStartElement("urlset", Namespace);

            foreach (var entry in entries)
            {
                writer.WriteStartElement("url", Namespace);
                writer.WriteElementString("loc", Namespace, site.Absolute(entry.Url));

                if ((entry.PublishedOn ?? entry.ModifiedOn) is { } lastModified)
                {
                    writer.WriteElementString(
                        "lastmod",
                        Namespace,
                        lastModified.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }

                writer.WriteElementString(
                    "changefreq",
                    Namespace,
                    Trimmed(entry.ChangeFreq) ?? options.DefaultChangeFrequency);

                writer.WriteElementString(
                    "priority",
                    Namespace,
                    Math.Clamp(entry.Priority ?? options.DefaultPriority, 0m, 1m)
                        .ToString("0.0", CultureInfo.InvariantCulture));

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        await WriteAsync(http, builder.ToString(), cancellationToken);
    }

    /// <summary>
    /// The pages a sitemap may name: published, indexable, routed, and not the 404 page.
    /// </summary>
    /// <remarks>
    /// A correlated subquery for the settings row rather than a second round trip, so the count and
    /// the page share one definition of what is indexable. Two queries that disagreed by one row
    /// would produce a sitemap index whose last file is empty.
    /// </remarks>
    private static IQueryable<PageRoute> Indexable(ApplicationDbContext context) =>
        context.PageRoutes
            .AsNoTracking()
            .Where(route =>
                route.IsPublished &&
                route.IsPrimary &&
                route.Page.PublishedVersionId != null &&
                route.Page.PublishedVersion!.RobotsIndex &&
                !context.SiteSettings.Any(settings => settings.NotFoundPageId == route.PageId));

    private static int PageSize(SeoOptions options) => Math.Clamp(options.SitemapPageSize, 1, 50_000);

    /// <summary>
    /// A writer over a buffer, with the XML declaration written by hand rather than by the writer.
    /// </summary>
    /// <remarks>
    /// A writer built over a <see cref="StringBuilder"/> declares the encoding of the buffer it
    /// writes into, which is UTF-16 — a declaration that would be a lie about a response served as
    /// UTF-8, and the kind of lie a strict parser refuses outright.
    /// </remarks>
    private static XmlWriter Writer(StringBuilder builder)
    {
        builder.Append(Declaration);

        return XmlWriter.Create(builder, new XmlWriterSettings
        {
            Indent = false,
            OmitXmlDeclaration = true,
        });
    }

    private static async Task WriteAsync(HttpContext http, string xml, CancellationToken cancellationToken)
    {
        http.Response.ContentType = ContentType;
        http.Response.Headers.CacheControl = SitemapCacheControl;

        // Tagged for the output cache the way a page is, with the site-wide tag: any publish changes
        // what belongs in the sitemap, and there is no narrower dependency to take
        // (spec section 18.3).
        http.Items[DeliveryEndpoint.CacheTagsItemKey] = new[] { CacheTags.All };

        await http.Response.WriteAsync(xml, cancellationToken);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>One row of the sitemap query.</summary>
    private sealed record SitemapEntry(
        string Url,
        DateTimeOffset? PublishedOn,
        DateTimeOffset? ModifiedOn,
        string? ChangeFreq,
        decimal? Priority);
}
