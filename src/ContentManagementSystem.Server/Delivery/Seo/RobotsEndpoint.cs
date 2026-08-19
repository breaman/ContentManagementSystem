using System.Text;

using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Delivery.Seo;
using ContentManagementSystem.Data.Models;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Delivery.Seo;

/// <summary>
/// <c>robots.txt</c>, editable in site settings and overridden outside production
/// (task P8-05, spec section 18.4).
/// </summary>
/// <remarks>
/// <strong>A non-production environment serves <c>Disallow: /</c> whatever is configured, and that
/// is not a setting.</strong> An indexed staging site competes with the real one for its own
/// search results, serves half-finished copy to the public, and is usually discovered weeks later
/// through a support ticket. Making it unconditional means the mistake cannot be made by editing a
/// text box, and the environment name is the one fact a copied production database cannot carry
/// with it.
/// </remarks>
public static class RobotsEndpoint
{
    /// <summary>Route name, so a link to it can be generated rather than typed.</summary>
    public const string RouteName = "cms-robots";

    /// <summary>The body every non-production environment serves.</summary>
    public const string DisallowAll = "User-agent: *\nDisallow: /\n";

    private const string ContentType = "text/plain; charset=utf-8";

    /// <summary>
    /// Serves <c>/robots.txt</c>.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="environment">Which environment this is, which outranks the configured body.</param>
    /// <param name="context">The application database context, for the editable body.</param>
    /// <param name="site">The absolute address the <c>Sitemap</c> line points at.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    public static async Task HandleAsync(
        HttpContext http,
        IHostEnvironment environment,
        ApplicationDbContext context,
        ISiteAddress site,
        CancellationToken cancellationToken)
    {
        http.Response.ContentType = ContentType;

        // Never cached anywhere shared. The staging override is decided per environment rather than
        // per request, and a copy of the production body cached by a proxy in front of staging would
        // undo it (spec section 18.4).
        http.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";

        if (!environment.IsProduction())
        {
            await http.Response.WriteAsync(DisallowAll, cancellationToken);

            return;
        }

        var configured = await context.SiteSettings
            .AsNoTracking()
            .Select(settings => settings.RobotsTxt)
            .FirstOrDefaultAsync(cancellationToken);

        var sitemap = site.Absolute("/sitemap.xml");

        // Tagged so a settings edit that publishes takes effect with the next generation rather than
        // at the end of a TTL.
        http.Items[DeliveryEndpoint.CacheTagsItemKey] = new[] { CacheTags.All };

        await http.Response.WriteAsync(Body(configured, sitemap), cancellationToken);
    }

    /// <summary>
    /// The body to serve: the editor's, with a <c>Sitemap</c> line added when it has none, or the
    /// generated default.
    /// </summary>
    /// <param name="configured">The stored body, or null when nobody has written one.</param>
    /// <param name="sitemapUrl">Absolute URL of the sitemap.</param>
    /// <returns>The response body.</returns>
    /// <remarks>
    /// The sitemap line is appended to a hand-written body rather than assumed to be in it. It is
    /// the one line whose absence is silent — a crawler simply never learns the sitemap exists — and
    /// an editor rewriting the disallow rules has no reason to think about it.
    /// </remarks>
    public static string Body(string? configured, string sitemapUrl)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return $"""
                User-agent: *
                Disallow: /admin
                Disallow: /api
                Disallow: /preview
                Sitemap: {sitemapUrl}

                """.ReplaceLineEndings("\n");
        }

        var body = configured.Trim().ReplaceLineEndings("\n");

        if (body.Contains("Sitemap:", StringComparison.OrdinalIgnoreCase)) return body + "\n";

        var builder = new StringBuilder(body.Length + sitemapUrl.Length + 16);

        builder.Append(body).Append("\n\n").Append("Sitemap: ").Append(sitemapUrl).Append('\n');

        return builder.ToString();
    }
}
