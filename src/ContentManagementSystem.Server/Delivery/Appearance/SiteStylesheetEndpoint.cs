using System.Globalization;
using System.Text;

using ContentManagementSystem.Core.Appearance;
using ContentManagementSystem.Core.Caching;

using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ContentManagementSystem.Server.Delivery.Appearance;

/// <summary>
/// Serves the administrator-authored site stylesheet (task P10-05, spec section 30.4).
/// </summary>
/// <remarks>
/// An endpoint rather than a file on disk, because the bytes live in the database — and the last
/// thing a scaled-out deployment needs is a file every instance has to be told to rewrite before it
/// can serve the same CSS as its neighbours.
/// <para>
/// <strong>The URL is stable and revalidated, not fingerprinted.</strong> A content hash in the path
/// would serve this response more cheaply, and it appears in the <c>&lt;head&gt;</c> of every page —
/// so every stylesheet publish would evict the entire site. One saved revalidation per visit, paid
/// for with a full re-render of every page each time somebody adjusts a margin (D27).
/// </para>
/// </remarks>
public static class SiteStylesheetEndpoint
{
    /// <summary>Path the public document links.</summary>
    public const string Path = "/css/site-custom.css";

    /// <summary>Path preview links, which serves the draft. Relative to the preview group.</summary>
    public const string PreviewPath = "/site-custom.css";

    /// <summary>The draft stylesheet's absolute address, as the preview document links it.</summary>
    public const string PreviewHref = Preview.PreviewEndpoint.BasePath + PreviewPath;

    /// <summary>Route name of the published stylesheet.</summary>
    public const string RouteName = "cms-site-stylesheet";

    /// <summary>Route name of the draft, served under the preview group.</summary>
    public const string PreviewRouteName = "cms-site-stylesheet-preview";

    /// <summary>
    /// Pinned, never negotiated, never sniffed. A stylesheet served as anything else is either
    /// ignored by the browser or — with <c>nosniff</c> absent — treated as whatever it looks like.
    /// </summary>
    private const string ContentType = "text/css; charset=utf-8";

    /// <summary>
    /// Serves what is published, or <c>404</c> when nothing is.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="reader">Reads the published copy. It cannot see the draft.</param>
    /// <param name="options">The configured shared-cache window.</param>
    /// <param name="cancellationToken">Token observed while querying and writing.</param>
    /// <remarks>
    /// A <c>404</c> while nothing is published is the honest answer rather than an empty <c>200</c>:
    /// the document does not link this file in that state, so a request for it is a stale cache or a
    /// crawler working from an old page, and saying "there is nothing here" is what both should act
    /// on.
    /// </remarks>
    public static async Task HandleAsync(
        HttpContext http,
        IPublishedStylesheetReader reader,
        IOptions<SiteStylesheetOptions> options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);

        var published = await reader.GetPublishedAsync(cancellationToken);

        if (published is null)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        // Evicted by the publish rather than waited out, on every instance, through the outbox.
        http.Items[DeliveryEndpoint.CacheTagsItemKey] = new[] { CacheTags.SiteStylesheet };

        http.Response.ContentType = ContentType;
        http.Response.Headers.ETag = published.ETag;
        http.Response.Headers.LastModified =
            published.PublishedOn.UtcDateTime.ToString("R", CultureInfo.InvariantCulture);

        // The page policy of spec section 16.1, for the same reason: a shared cache may hold it, a
        // browser must ask. A publish evicts this server's copy immediately, so the shared window
        // bounds only what a CDN in front of the site may serve without revalidating (Q6).
        http.Response.Headers.CacheControl = string.Create(
            CultureInfo.InvariantCulture,
            $"public, max-age=0, s-maxage={Math.Max(0, options.Value.SharedMaxAgeSeconds)}, must-revalidate");

        if (NotModified(http, published.ETag))
        {
            http.Response.StatusCode = StatusCodes.Status304NotModified;

            return;
        }

        await http.Response.WriteAsync(published.Css, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Serves the stylesheet a preview frame should wear: the <strong>draft</strong> to somebody who
    /// may edit it, and the published copy to everybody else.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="stylesheet">The editing service, which enforces <c>Appearance.Edit</c> itself.</param>
    /// <param name="reader">The published copy, for a caller who may not see the draft.</param>
    /// <param name="cancellationToken">Token observed while querying and writing.</param>
    /// <remarks>
    /// The fallback is what makes a <em>shared</em> preview link honest (spec section 12.2). Those
    /// are opened by approvers and clients with no account: they are meant to see unpublished
    /// <em>content</em>, and refusing them the stylesheet would show them the site with none of its
    /// styling — a page that looks nothing like the one they are approving. What they must not see is
    /// the unpublished <em>design</em>, which is why the draft is gated and the published copy is not.
    /// <para>
    /// <c>no-store</c> either way. A draft stylesheet in any shared cache is a redesign nobody
    /// approved being served to the public, and that is the one failure this endpoint could cause.
    /// </para>
    /// </remarks>
    public static async Task PreviewAsync(
        HttpContext http,
        ISiteStylesheetService stylesheet,
        IPublishedStylesheetReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(stylesheet);
        ArgumentNullException.ThrowIfNull(reader);

        // The service decides, rather than a check here reading the same claim a second time and
        // eventually reading it differently.
        var draft = await stylesheet.GetAsync(cancellationToken);

        var css = draft is { IsSuccess: true, Value: not null }
            ? draft.Value.DraftCss
            : (await reader.GetPublishedAsync(cancellationToken))?.Css;

        if (string.IsNullOrEmpty(css))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        http.Response.ContentType = ContentType;
        http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

        await http.Response.WriteAsync(css, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Whether the caller already holds this exact stylesheet.
    /// </summary>
    /// <remarks>
    /// A strong comparison, and <c>*</c> counts as a match: the header means "any representation",
    /// and the caller is asking whether anything has changed rather than naming a version.
    /// </remarks>
    private static bool NotModified(HttpContext http, string etag)
    {
        var header = http.Request.Headers.IfNoneMatch;

        if (header.Count == 0) return false;

        foreach (var value in header)
        {
            if (string.IsNullOrEmpty(value)) continue;

            foreach (var candidate in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (candidate is "*") return true;

                if (string.Equals(candidate, etag, StringComparison.Ordinal)) return true;
            }
        }

        return false;
    }

    /// <summary>Names the header the conditional request arrives in, for the tests to spell alike.</summary>
    internal static string IfNoneMatchHeader => HeaderNames.IfNoneMatch;
}
