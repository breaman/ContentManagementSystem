using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Rendering;

using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace ContentManagementSystem.Server.Delivery;

/// <summary>
/// The one endpoint that serves content URLs (spec section 15.1, task P3-13).
/// </summary>
/// <remarks>
/// Registered after every other route, so <c>/admin</c>, <c>/api</c>, <c>/account</c>, the health
/// endpoints, and the Blazor framework paths keep priority over it. A catch-all registered early
/// would shadow the entire application, which is risk R6 and is asserted against by the route
/// ordering tests in <c>P3-15</c>.
/// <para>
/// The shape of the handler is four answers, in this order: a redirect, a canonical-form correction,
/// a page, or a 404. The ordering is not this class's to decide — <c>IRouteResolver</c> already owns
/// it, including the rule that a live page outranks a redirect at the same URL (spec section 10.5) —
/// and re-deriving any of it here would be a second copy that eventually disagrees.
/// </para>
/// </remarks>
public static class DeliveryEndpoint
{
    /// <summary>
    /// The key the accumulated cache tags are left under for the output cache policy to read.
    /// </summary>
    /// <remarks>
    /// Output caching itself is Phase 8. The tags are published here anyway, because they are only
    /// obtainable at the moment the render finishes and the policy that will consume them runs on
    /// the way out of a request that has already been served. A key on <c>HttpContext.Items</c> is
    /// the seam; nothing reads it yet, and everything needed to write it exists now.
    /// </remarks>
    public const string CacheTagsItemKey = "cms.delivery.cacheTags";

    /// <summary>Cache-Control for a page response (spec section 16.1).</summary>
    /// <remarks>
    /// No private caching, five minutes of shared caching, and revalidation on every use. The
    /// browser must not hold a copy: a visitor who reloads after an editor publishes has to see the
    /// new page, and the only cache the CMS can evict on publish is the shared one.
    /// </remarks>
    private const string PageCacheControl = "max-age=0, s-maxage=300, must-revalidate";

    /// <summary>Cache-Control for a 404, which must never be cached anywhere.</summary>
    /// <remarks>
    /// A URL that 404s today is very often one somebody is about to create a page or a redirect for,
    /// and a cached 404 makes that fix invisible for as long as the entry lives.
    /// </remarks>
    private const string ErrorCacheControl = "no-cache, no-store, must-revalidate";

    /// <summary>
    /// The document served when a URL resolves to nothing and no CMS 404 page is configured.
    /// </summary>
    /// <remarks>
    /// A constant rather than a component. This is the response for a site whose settings are
    /// incomplete or whose 404 page has itself been unpublished, so it must not depend on anything
    /// that could be the reason it is being served.
    /// </remarks>
    private const string BuiltInNotFoundHtml =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8" /><title>Page not found</title>
        <meta name="robots" content="noindex, nofollow" /></head>
        <body><h1>Page not found</h1><p>The page you asked for does not exist.</p></body>
        </html>
        """;

    /// <summary>
    /// Serves a content URL.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="routes">Turns the request URL into a page, a redirect, or nothing.</param>
    /// <param name="published">Loads the published version of a page.</param>
    /// <param name="renderer">Renders that version to a document.</param>
    /// <param name="redirects">Counts a followed redirect.</param>
    /// <param name="notFound">Records an unresolved URL.</param>
    /// <param name="context">Read only for the configured 404 page.</param>
    /// <param name="metrics">Counts route misses (task P3-28).</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    public static async Task HandleAsync(
        HttpContext http,
        IRouteResolver routes,
        IPublishedContentService published,
        CmsPageRenderer renderer,
        IRedirectService redirects,
        INotFoundLogService notFound,
        ApplicationDbContext context,
        CmsMetrics metrics,
        CancellationToken cancellationToken)
    {
        // The request path, not the route value. A catch-all parameter arrives without its leading
        // slash and already percent-decoded in places the resolver's own normalization expects to
        // do itself; the path is what the visitor actually asked for.
        var path = http.Request.Path.Value;

        if (IsReserved(path))
        {
            // A path under a prefix the application owns — /api, /admin, /account, /_framework —
            // that no endpoint matched. It reaches here because the catch-all matches everything,
            // and the answer must be a bare 404 rather than the site's 404 page: an API client
            // reporting "the server returned HTML" is a far worse diagnosis than a 404, and content
            // must never appear to be served from a reserved prefix, which no page can be published
            // at anyway (spec sections 10.3 and 15.1).
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            http.Response.Headers.CacheControl = ErrorCacheControl;

            return;
        }

        var resolution = await routes.ResolveAsync(path, cancellationToken);

        switch (resolution.Kind)
        {
            case RouteResolutionKind.Redirect when resolution.TargetUrl is { Length: > 0 } target:
                await redirects.RecordHitAsync(resolution.RedirectId, cancellationToken);
                Redirect(http, target, resolution.StatusCode);

                return;

            case RouteResolutionKind.Page when resolution.CanonicalUrl is { Length: > 0 } canonical:
                // The right page reached by the wrong spelling. Sent on rather than served, so the
                // canonical tag, the sitemap, and the analytics report all describe one address
                // (spec section 10.3).
                Redirect(http, canonical, StatusCodes.Status301MovedPermanently);

                return;

            case RouteResolutionKind.Page:
                if (await published.GetAsync(resolution.PageId, cancellationToken) is { } content)
                {
                    await WritePageAsync(http, renderer, content, StatusCodes.Status200OK, cancellationToken);

                    return;
                }

                // A published route pointing at a page with no published version. Rare, and always a
                // symptom of something else, but the visitor's answer is the same 404 as any other
                // dead URL.
                break;
        }

        metrics.RecordRouteMiss();

        await notFound.RecordAsync(path, http.Request.Headers.Referer.ToString(), cancellationToken);
        await WriteNotFoundAsync(http, published, renderer, context, cancellationToken);
    }

    /// <summary>
    /// Whether a path's first segment is one the application owns rather than one content may use.
    /// </summary>
    /// <remarks>
    /// The same list <c>Slugs.Validate</c> refuses at the root of the site, read rather than
    /// restated. Two copies of "which prefixes are reserved" would drift, and the copy that drifted
    /// would be the one that let a page be created at a URL delivery then declined to serve — or
    /// worse, the one that let delivery answer for a prefix an endpoint was supposed to own.
    /// </remarks>
    private static bool IsReserved(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        var trimmed = path.AsSpan().TrimStart('/');
        var end = trimmed.IndexOf('/');
        var first = end < 0 ? trimmed : trimmed[..end];

        return !first.IsEmpty && Slugs.Reserved.Contains(first.ToString());
    }

    private static void Redirect(HttpContext http, string target, int statusCode)
    {
        http.Response.StatusCode = statusCode is StatusCodes.Status301MovedPermanently or
            StatusCodes.Status302Found or
            StatusCodes.Status307TemporaryRedirect or
            StatusCodes.Status308PermanentRedirect
            ? statusCode
            : StatusCodes.Status301MovedPermanently;

        http.Response.Headers.Location = target;

        // Never cached. A redirect an editor deletes has to stop being followed, and a 301 a browser
        // has cached is famously close to permanent.
        http.Response.Headers.CacheControl = ErrorCacheControl;
    }

    /// <summary>Renders the configured 404 page, or the built-in one when there is not one.</summary>
    private static async Task WriteNotFoundAsync(
        HttpContext http,
        IPublishedContentService published,
        CmsPageRenderer renderer,
        ApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        // The 404 page is itself a CMS page (spec section 10.6), loaded by id rather than by URL so
        // that a misconfigured route cannot make the 404 handler recurse into itself.
        var notFoundPageId = await context.SiteSettings
            .AsNoTracking()
            .Select(settings => settings.NotFoundPageId)
            .FirstOrDefaultAsync(cancellationToken);

        if (notFoundPageId is > 0 &&
            await published.GetAsync(notFoundPageId.Value, cancellationToken) is { } content)
        {
            await WritePageAsync(http, renderer, content, StatusCodes.Status404NotFound, cancellationToken);

            return;
        }

        http.Response.StatusCode = StatusCodes.Status404NotFound;
        http.Response.ContentType = "text/html; charset=utf-8";
        http.Response.Headers.CacheControl = ErrorCacheControl;

        await http.Response.WriteAsync(BuiltInNotFoundHtml, cancellationToken);
    }

    /// <summary>
    /// Renders a version, then sets the headers, then writes — in that order.
    /// </summary>
    /// <remarks>
    /// The ordering is the requirement, not an implementation detail. <c>ETag</c> is computed over
    /// the finished document and the cache tags are read off a render that has completed; both are
    /// unavailable to a response that has already started (S2 spike, consequence 3).
    /// </remarks>
    private static async Task WritePageAsync(
        HttpContext http,
        CmsPageRenderer renderer,
        PublishedContent content,
        int statusCode,
        CancellationToken cancellationToken)
    {
        var rendered = await renderer.RenderAsync(content, CmsRenderMode.Live, cancellationToken);
        var etag = ETagOf(rendered.Html);

        // Left for the Phase 8 output cache policy to read on the way out. Nothing consumes it yet.
        http.Items[CacheTagsItemKey] = rendered.CacheTags;

        // Revalidation only applies to a page that is actually there. Answering 304 to a request for
        // the 404 document would tell the client its cached copy of a page that does not exist is
        // still good.
        if (statusCode is StatusCodes.Status200OK && IsUnchanged(http, etag))
        {
            http.Response.StatusCode = StatusCodes.Status304NotModified;
            http.Response.Headers.ETag = etag;
            http.Response.Headers.CacheControl = PageCacheControl;

            return;
        }

        http.Response.StatusCode = statusCode;
        http.Response.ContentType = "text/html; charset=utf-8";
        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl =
            statusCode is StatusCodes.Status200OK ? PageCacheControl : ErrorCacheControl;

        // The publish timestamp, per spec section 15.4, falling back to the last edit for a version
        // that has one and no publish date. Formatted from the UTC instant: "R" only emits GMT, so a
        // value carrying a non-zero offset would be off by that offset rather than converted.
        if ((content.PublishedOn ?? content.ModifiedOn) is { } modified)
        {
            http.Response.Headers.LastModified =
                modified.UtcDateTime.ToString("R", CultureInfo.InvariantCulture);
        }

        await http.Response.WriteAsync(rendered.Html, cancellationToken);
    }

    /// <summary>
    /// The entity tag for a rendered document.
    /// </summary>
    /// <remarks>
    /// Computed over the finished markup rather than derived from the version id, and strong rather
    /// than weak. A version id says nothing about the many things that legitimately change a page
    /// without changing its version — a link target moving, a reusable item being republished, a
    /// media rendition changing — and every one of those has to break the tag or a visitor keeps a
    /// stale page (spec section 16.4).
    /// </remarks>
    private static string ETagOf(string html) =>
        $"\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(html)))}\"";

    private static bool IsUnchanged(HttpContext http, string etag)
    {
        var candidates = http.Request.Headers[HeaderNames.IfNoneMatch];

        foreach (var header in candidates)
        {
            if (header is null) continue;

            foreach (var candidate in header.Split(','))
            {
                var trimmed = candidate.Trim();

                if (trimmed == "*" || string.Equals(trimmed, etag, StringComparison.Ordinal)) return true;
            }
        }

        return false;
    }
}
