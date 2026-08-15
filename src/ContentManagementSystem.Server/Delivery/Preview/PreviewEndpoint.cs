using System.Net;

using ContentManagementSystem.Core.Preview;
using ContentManagementSystem.Rendering;

namespace ContentManagementSystem.Server.Delivery.Preview;

/// <summary>
/// The preview endpoints: an editor's own, and the shareable link (tasks P3-16 and P3-18,
/// spec sections 12.1 and 12.2).
/// </summary>
/// <remarks>
/// Four routes, two entry points, one renderer. <c>/preview/{pageId}</c> and
/// <c>/preview/s/{token}</c> differ in exactly one thing — how the caller proved they may see an
/// unpublished page — and after that both produce the same chrome around the same delivery document.
/// The split into a chrome request and a content request is what gives spec section 12.3's device
/// widths a real viewport to constrain; see <see cref="PreviewChrome"/>.
/// <para>
/// <strong>Every response here is uncacheable and unindexable.</strong> <c>X-Robots-Tag</c> and
/// <c>Cache-Control: no-store</c> are applied before anything is written, on the success path and on
/// every refusal, because the failure that matters is not one preview being cached — it is an
/// unpublished page sitting in a shared cache or a search index, where nothing the CMS does can
/// evict it.
/// </para>
/// <para>
/// Spec section 12.2 also asks that shared previews be excluded from <c>sitemap.xml</c>. Nothing
/// here does that, and nothing needs to: the sitemap of Phase 8 is built from published
/// <c>PageRoute</c> rows, and a preview URL is not a route — it is a token or a page id under a
/// reserved prefix. The exclusion is structural, which is the only kind that survives somebody
/// writing the sitemap generator without having read this file.
/// </para>
/// </remarks>
public static class PreviewEndpoint
{
    /// <summary>Path the editor-facing preview is served under.</summary>
    public const string BasePath = "/preview";

    /// <summary>Path segment that carries a shared token.</summary>
    public const string SharedSegment = "/s";

    /// <summary>Name of the rate-limiting policy the shared-link routes require.</summary>
    public const string SharedRateLimitPolicy = "cms-preview-shared";

    /// <summary>
    /// Robots directive on every preview response (spec sections 12.1 and 12.2).
    /// </summary>
    /// <remarks>
    /// <c>nofollow</c> as well as <c>noindex</c>, on the editor path too. A preview of an unreleased
    /// section links to other unreleased pages by their draft URLs, and a crawler that indexed
    /// nothing but followed everything would still find them.
    /// </remarks>
    private const string RobotsDirective = "noindex, nofollow";

    /// <summary>Cache-Control on every preview response.</summary>
    /// <remarks>
    /// <c>no-store</c>, not merely <c>no-cache</c>: the second permits a copy to be held as long as
    /// it is revalidated, and a copy of an unpublished page held anywhere is the thing being
    /// prevented. Output caching (Phase 8) is refused separately, by metadata on the endpoints.
    /// </remarks>
    private const string CacheControl = "no-store, no-cache, must-revalidate, private";

    /// <summary>
    /// Serves the toolbar and device frame for an editor previewing one of their own pages.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="pageId">Page being previewed.</param>
    /// <param name="preview">Reads any version of a page.</param>
    /// <param name="renderer">Renders the chrome document.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <param name="version">The exact version to show, or absent for the page's draft.</param>
    /// <param name="device">Which width to constrain the frame to.</param>
    public static async Task EditorChromeAsync(
        HttpContext http,
        int pageId,
        IPreviewContentService preview,
        CmsPreviewRenderer renderer,
        CancellationToken cancellationToken,
        int? version = null,
        string? device = null)
    {
        ApplyPreviewHeaders(http);

        if (await preview.DescribeAsync(pageId, version, cancellationToken) is not { } described)
        {
            await WriteNoticeAsync(
                http,
                HttpStatusCode.NotFound,
                "Nothing to preview",
                "That page or version does not exist. It may have been deleted since the link was made.",
                cancellationToken);

            return;
        }

        var chrome = new PreviewChrome(
            $"{BasePath}/{pageId}",
            described,
            PreviewDevices.Parse(device),
            version,

            // Back to the page the editor came from, rather than to the site. Preview is entered
            // from the editor and the way out of it is the way back in.
            ExitUrl: $"/admin/pages/{pageId}");

        await WriteHtmlAsync(http, HttpStatusCode.OK, await renderer.RenderAsync(chrome), cancellationToken);
    }

    /// <summary>
    /// Serves the page itself, for the frame in an editor's preview.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="pageId">Page being previewed.</param>
    /// <param name="preview">Reads any version of a page.</param>
    /// <param name="renderer">Renders the page through the delivery pipeline.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <param name="version">The exact version to show, or absent for the page's draft.</param>
    public static Task EditorContentAsync(
        HttpContext http,
        int pageId,
        IPreviewContentService preview,
        CmsPageRenderer renderer,
        CancellationToken cancellationToken,
        int? version = null) =>
        WriteContentAsync(http, pageId, version, preview, renderer, cancellationToken);

    /// <summary>
    /// Serves the toolbar and device frame for a shared link, to a caller with no account.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="token">The base64url secret from the URL.</param>
    /// <param name="tokens">Validates the token.</param>
    /// <param name="preview">Reads any version of a page.</param>
    /// <param name="renderer">Renders the chrome document.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <param name="device">Which width to constrain the frame to.</param>
    /// <remarks>
    /// Checked rather than redeemed: a use is a view of the content, so it is spent by the request
    /// the frame makes and not by the wrapper around it. A <c>MaxUses = 1</c> link that spent its
    /// one view on the toolbar would never show anybody a page.
    /// </remarks>
    public static async Task SharedChromeAsync(
        HttpContext http,
        string token,
        IPreviewTokenService tokens,
        IPreviewContentService preview,
        CmsPreviewRenderer renderer,
        CancellationToken cancellationToken,
        string? device = null)
    {
        ApplyPreviewHeaders(http);

        var redemption = await tokens.CheckAsync(token, cancellationToken);

        if (!redemption.IsValid)
        {
            await WriteRefusalAsync(http, redemption.Outcome, cancellationToken);

            return;
        }

        if (await preview.DescribeAsync(
                redemption.PageId, redemption.PageVersionId, cancellationToken) is not { } described)
        {
            await WriteRefusalAsync(http, PreviewRedemptionOutcome.PageUnavailable, cancellationToken);

            return;
        }

        var chrome = new PreviewChrome(
            $"{BasePath}{SharedSegment}/{token}",
            described,
            PreviewDevices.Parse(device),

            // The version is carried by the token, not by the query string, so nothing in the URL a
            // reviewer holds can be edited to reach a different one. That is what "serves exactly
            // one page version" means (spec section 12.2).
            VersionId: null,
            ExitUrl: null,
            ExpiresOn: redemption.ExpiresOn);

        await WriteHtmlAsync(http, HttpStatusCode.OK, await renderer.RenderAsync(chrome), cancellationToken);
    }

    /// <summary>
    /// Serves the page itself for a shared link, recording the use.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="token">The base64url secret from the URL.</param>
    /// <param name="tokens">Validates the token and records the view.</param>
    /// <param name="preview">Reads any version of a page.</param>
    /// <param name="renderer">Renders the page through the delivery pipeline.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    public static async Task SharedContentAsync(
        HttpContext http,
        string token,
        IPreviewTokenService tokens,
        IPreviewContentService preview,
        CmsPageRenderer renderer,
        CancellationToken cancellationToken)
    {
        ApplyPreviewHeaders(http);

        var redemption = await tokens.RedeemAsync(token, cancellationToken);

        if (!redemption.IsValid)
        {
            await WriteRefusalAsync(http, redemption.Outcome, cancellationToken);

            return;
        }

        await WriteContentAsync(
            http,
            redemption.PageId,
            redemption.PageVersionId,
            preview,
            renderer,
            cancellationToken);
    }

    /// <summary>
    /// Renders one version through the delivery pipeline and writes it.
    /// </summary>
    /// <remarks>
    /// <c>CmsRenderMode.Preview</c> is the only thing that distinguishes this from a public page
    /// render, and it changes exactly one behaviour: an internal link to an unpublished page
    /// resolves to that page's draft URL and is badged, so a reviewer can walk an unreleased section
    /// (task P3-20, spec section 12.3).
    /// </remarks>
    private static async Task WriteContentAsync(
        HttpContext http,
        int pageId,
        int? versionId,
        IPreviewContentService preview,
        CmsPageRenderer renderer,
        CancellationToken cancellationToken)
    {
        ApplyPreviewHeaders(http);

        if (await preview.GetAsync(pageId, versionId, cancellationToken) is not { } content)
        {
            await WriteNoticeAsync(
                http,
                HttpStatusCode.NotFound,
                "Nothing to preview",
                "That page or version does not exist. It may have been deleted since the link was made.",
                cancellationToken);

            return;
        }

        var rendered = await renderer.RenderAsync(content, CmsRenderMode.Preview);

        // The accumulated cache tags are deliberately not published on HttpContext.Items the way the
        // delivery endpoint publishes them. Nothing may cache this response, so a tag set inviting
        // something to is at best noise and at worst an invitation somebody accepts.
        await WriteHtmlAsync(http, HttpStatusCode.OK, rendered.Html, cancellationToken);
    }

    /// <summary>Turns a refused token into the page its holder sees.</summary>
    /// <remarks>
    /// Four outcomes, three of which name something the reader can act on. The status codes matter
    /// as much as the words: <c>410 Gone</c> for a link that worked and has stopped tells an
    /// intermediary the URL is dead for good, whereas <c>404</c> for one that never worked says
    /// nothing about whether the string was ever a token — which is the point (spec section 12.2).
    /// </remarks>
    private static Task WriteRefusalAsync(
        HttpContext http,
        PreviewRedemptionOutcome outcome,
        CancellationToken cancellationToken) => outcome switch
    {
        PreviewRedemptionOutcome.Expired => WriteNoticeAsync(
            http,
            HttpStatusCode.Gone,
            "This preview link has expired",
            "Preview links last a limited time. Ask whoever shared it with you for a new one.",
            cancellationToken),

        PreviewRedemptionOutcome.Exhausted => WriteNoticeAsync(
            http,
            HttpStatusCode.Gone,
            "This preview link has been used up",
            "It was issued for a limited number of views. Ask whoever shared it with you for a new one.",
            cancellationToken),

        PreviewRedemptionOutcome.PageUnavailable => WriteNoticeAsync(
            http,
            HttpStatusCode.NotFound,
            "This preview is no longer available",
            "The page it pointed at has been deleted. Ask the person who sent the link to restore it.",
            cancellationToken),

        _ => WriteNoticeAsync(
            http,
            HttpStatusCode.NotFound,
            "This preview link is not valid",
            "It may have been revoked, or copied incompletely. Ask whoever shared it with you for a new one.",
            cancellationToken),
    };

    /// <summary>
    /// Applies the two headers every preview response carries, before anything is written.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <remarks>
    /// Called on entry to each handler rather than at each write, so a path that returns early —
    /// including every refusal — cannot be the one that forgets. The cost of setting them twice on
    /// the paths that delegate is nothing; the cost of missing one is an unpublished page in an
    /// index.
    /// </remarks>
    private static void ApplyPreviewHeaders(HttpContext http)
    {
        http.Response.Headers["X-Robots-Tag"] = RobotsDirective;
        http.Response.Headers.CacheControl = CacheControl;
        http.Response.Headers.Pragma = "no-cache";
    }

    private static async Task WriteHtmlAsync(
        HttpContext http,
        HttpStatusCode status,
        string html,
        CancellationToken cancellationToken)
    {
        http.Response.StatusCode = (int)status;
        http.Response.ContentType = "text/html; charset=utf-8";

        await http.Response.WriteAsync(html, cancellationToken);
    }

    /// <summary>
    /// Writes a plain document explaining why there is no preview.
    /// </summary>
    /// <remarks>
    /// A hand-written constant rather than a CMS page or a component. This is the response for a
    /// deleted page, a revoked link, and a database that answered nothing, so it must not depend on
    /// any of the things that could be the reason it is being served — the same reasoning as the
    /// built-in 404 on the delivery endpoint.
    /// </remarks>
    private static async Task WriteNoticeAsync(
        HttpContext http,
        HttpStatusCode status,
        string heading,
        string detail,
        CancellationToken cancellationToken)
    {
        var html =
            $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8" /><title>{WebUtility.HtmlEncode(heading)}</title>
            <meta name="robots" content="{RobotsDirective}" />
            <link rel="stylesheet" href="/css/preview.css" /></head>
            <body class="cms-preview cms-preview--notice">
            <main class="cms-preview-notice">
            <h1>{WebUtility.HtmlEncode(heading)}</h1><p>{WebUtility.HtmlEncode(detail)}</p>
            </main>
            </body>
            </html>
            """;

        await WriteHtmlAsync(http, status, html, cancellationToken);
    }
}
