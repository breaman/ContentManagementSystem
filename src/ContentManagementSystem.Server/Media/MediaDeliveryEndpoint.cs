using System.Net;

using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Core.Media.Renditions;
using ContentManagementSystem.Core.Media.Stores;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace ContentManagementSystem.Server.Media;

/// <summary>
/// Serves signed image renditions and stored originals (tasks P5-14 to P5-17,
/// spec sections 13.5 and 20.7).
/// </summary>
/// <remarks>
/// Every byte of media the site emits goes through here rather than through static file middleware,
/// which is what makes four properties hold everywhere rather than on the paths somebody remembered:
/// the content type is pinned to what the bytes were sniffed as, <c>nosniff</c> is set, the encode
/// surface is bounded by a signature, and a soft-deleted item stops being served
/// (spec section 13.2).
/// </remarks>
public static class MediaDeliveryEndpoint
{
    /// <summary>Cache-Control for a rendition (spec section 13.5).</summary>
    /// <remarks>
    /// A year, and immutable. Safe precisely because of what is in the URL: the item id, every
    /// parameter that affects the bytes, and the item's edits version. A library edit bumps that
    /// version, so the URL changes and browser and CDN copies of the old one are simply never asked
    /// for again — no purge, no stale image (ADR 0007).
    /// </remarks>
    public const string RenditionCacheControl = "public, max-age=31536000, immutable";

    /// <summary>Cache-Control for a refusal, which must not be cached at all.</summary>
    /// <remarks>
    /// A signature that failed today may be a key mid-rotation or an item that was not yet
    /// published; caching the refusal would keep an image broken long after the cause was fixed.
    /// </remarks>
    private const string RefusalCacheControl = "no-cache, no-store, must-revalidate";

    /// <summary>
    /// Serves one rendition.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="id">Media item id from the route.</param>
    /// <param name="size">The <c>{width}x{height}</c> route segment.</param>
    /// <param name="mode">The mode route segment.</param>
    /// <param name="name">The file name route segment, whose extension names the format.</param>
    /// <param name="signer">Validates the signature.</param>
    /// <param name="renditions">Produces or fetches the rendition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public static async Task HandleRenditionAsync(
        HttpContext http,
        int id,
        string size,
        string mode,
        string name,
        IMediaUrlSigner signer,
        IRenditionService renditions,
        CancellationToken cancellationToken)
    {
        var query = http.Request.Query;

        var parsed = RenditionRequestParser.TryParse(new RenditionRequest(
            id,
            size,
            mode,
            name,
            query["v"],
            query["q"],
            query["f"],
            query["c"]));

        // Parsing refuses AVIF, unlisted widths, unknown modes, and malformed geometry — all before
        // the signature is even considered, because none of them can be made valid by signing them
        // (spec section 13.9.1).
        if (!parsed.IsSuccess)
        {
            await RefuseAsync(
                http, HttpStatusCode.BadRequest, parsed.FailureCode!, parsed.FailureMessage!);

            return;
        }

        var spec = parsed.Spec!;
        var issuedOn = ReadIssuedOn(query[MediaUrlSigner.IssuedParameter]);

        if (!signer.Validate(spec, query[MediaUrlSigner.SignatureParameter], issuedOn))
        {
            // 403 rather than 404: the request was understood and refused. Without this check the
            // endpoint would be an unbounded encode farm — /media/1/1x1 through /media/1/9999x9999
            // is a denial of service anyone with a browser can run (ADR 0007).
            await RefuseAsync(
                http,
                HttpStatusCode.Forbidden,
                MediaCodes.SignatureInvalid,
                "This image URL is not signed by this site.");

            return;
        }

        // Format negotiation (task P5-15). The signature covers the format that was asked for, so a
        // client that cannot display WebP is served the same rendition in the original format — a
        // spec derived here, from an already-validated one, rather than one the client supplied.
        // AVIF is never produced whatever the client says it accepts.
        if (spec.Format is ImageOutputFormat.Webp && !AcceptsWebp(http.Request.Headers.Accept))
        {
            spec = spec with { Format = ImageOutputFormat.Jpeg };
        }

        var (content, failure) = await renditions.GetAsync(spec, cancellationToken);

        if (content is null)
        {
            await RefuseAsync(http, StatusForFailure(failure), MediaCodes.NotFound, DescribeFailure(failure));

            return;
        }

        await using (content.Content)
        {
            // Vary is set whether or not this response was negotiated: a shared cache that stored the
            // WebP without it would go on to serve those bytes to a client that cannot read them.
            http.Response.Headers.Vary = HeaderNames.Accept;
            http.Response.Headers.CacheControl = RenditionCacheControl;
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.ContentDisposition = "inline";
            http.Response.ContentType = content.ContentType;
            http.Response.ContentLength = content.SizeBytes;

            await content.Content.CopyToAsync(http.Response.Body, cancellationToken);
        }
    }

    /// <summary>
    /// Serves a stored original — the download path for documents and for "download original".
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="id">Media item id from the route.</param>
    /// <param name="signer">Validates the signature.</param>
    /// <param name="context">The application database context.</param>
    /// <param name="store">Where the bytes live.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the response is written.</returns>
    /// <remarks>
    /// Signed like a rendition, though nothing here is generated. The signature is what keeps the
    /// stored originals of a whole library from being enumerable by id — an editor's uploaded
    /// contract or a photograph attached to an unpublished page is not public merely because it was
    /// uploaded.
    /// </remarks>
    public static async Task HandleOriginalAsync(
        HttpContext http,
        int id,
        IMediaUrlSigner signer,
        ApplicationDbContext context,
        IMediaStore store,
        CancellationToken cancellationToken)
    {
        var item = await context.MediaItems
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (item is null)
        {
            await RefuseAsync(http, HttpStatusCode.NotFound, MediaCodes.NotFound, "No such media item.");

            return;
        }

        if (!signer.ValidateOriginal(item.Id, item.EditsVersion, http.Request.Query[MediaUrlSigner.SignatureParameter]))
        {
            await RefuseAsync(
                http,
                HttpStatusCode.Forbidden,
                MediaCodes.SignatureInvalid,
                "This file URL is not signed by this site.");

            return;
        }

        await using var content = await store.GetAsync(item.StorageKey, cancellationToken);

        if (content is null)
        {
            await RefuseAsync(http, HttpStatusCode.NotFound, MediaCodes.NotFound, "The stored file is missing.");

            return;
        }

        http.Response.Headers.CacheControl = RenditionCacheControl;
        http.Response.Headers.XContentTypeOptions = "nosniff";

        // The sniffed type from the row, never a client-supplied one, and never re-sniffed here
        // (spec section 20.7).
        http.Response.ContentType = item.ContentType;
        http.Response.ContentLength = item.SizeBytes;

        // Images display; everything else downloads. An inline PDF or Office document is rendered by
        // a plugin inside the site's origin, and inline is how an uploaded document becomes a
        // phishing page on the site's own domain.
        http.Response.Headers.ContentDisposition = item.MediaKind is MediaKind.Image
            ? "inline"
            : ContentDispositionHeader(item.OriginalFileName);

        await content.CopyToAsync(http.Response.Body, cancellationToken);
    }

    /// <summary>
    /// Reports whether the client said it can display WebP.
    /// </summary>
    /// <param name="accept">The <c>Accept</c> header.</param>
    /// <returns><see langword="true"/> when WebP may be served.</returns>
    /// <remarks>
    /// A wildcard counts. Every browser released in the last several years accepts WebP, and many
    /// non-browser clients send <c>*/*</c> and handle whatever arrives — treating those as unable to
    /// read WebP would serve the larger format to almost everything that is not a browser.
    /// </remarks>
    private static bool AcceptsWebp(StringValues accept)
    {
        if (accept.Count is 0) return false;

        foreach (var value in accept)
        {
            if (value is null) continue;

            if (value.Contains("image/webp", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("*/*", StringComparison.Ordinal) ||
                value.Contains("image/*", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static DateTimeOffset? ReadIssuedOn(StringValues value) =>
        long.TryParse(value.ToString(), out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    private static HttpStatusCode StatusForFailure(RenditionFailure? failure) => failure switch
    {
        RenditionFailure.NotAnImage => HttpStatusCode.BadRequest,

        // 410 rather than 404: the rendition existed at that address and deliberately does not any
        // more, which is what a client re-fetching the page needs to be told apart from a typo.
        RenditionFailure.Stale => HttpStatusCode.Gone,

        // A missing original or a failed encode is the server's problem, not the caller's, and it
        // must not be reported as "no such image" — that would hide a lost storage container behind
        // a plausible-looking 404.
        RenditionFailure.OriginalMissing or RenditionFailure.ProcessingFailed =>
            HttpStatusCode.InternalServerError,

        _ => HttpStatusCode.NotFound,
    };

    private static string DescribeFailure(RenditionFailure? failure) => failure switch
    {
        RenditionFailure.NotAnImage => "That media item is not an image.",
        RenditionFailure.Stale => "This image has been edited since that link was made.",
        RenditionFailure.OriginalMissing => "The stored original is missing.",
        RenditionFailure.ProcessingFailed => "The image could not be processed.",
        _ => "No such media item.",
    };

    /// <summary>
    /// Builds a <c>Content-Disposition</c> that downloads under a safe file name.
    /// </summary>
    /// <param name="fileName">The original, client-supplied name.</param>
    /// <returns>The header value.</returns>
    /// <remarks>
    /// The name is quoted and stripped of anything that could close the quoting or split the header.
    /// It is uploader-controlled text going into a response header, which is a header-injection
    /// vector and, if it reaches a shell or a file manager unescaped, worse.
    /// </remarks>
    private static string ContentDispositionHeader(string fileName)
    {
        var safe = new string(fileName
            .Where(character => !char.IsControl(character) && character is not '"' and not '\\' and not ';')
            .ToArray());

        if (safe.Length is 0) safe = "download";

        return $"attachment; filename=\"{safe}\"";
    }

    /// <summary>
    /// Writes a refusal in the API's problem shape.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="status">Status to return.</param>
    /// <param name="code">A <see cref="MediaCodes"/> value.</param>
    /// <param name="message">What to tell the caller.</param>
    /// <returns>A task that completes when the response is written.</returns>
    /// <remarks>
    /// Through <see cref="CmsProblems"/> rather than a bespoke body, so a client that already
    /// understands this API's failures does not need a second parser for the one endpoint that
    /// happens to serve bytes on success.
    /// </remarks>
    private static Task RefuseAsync(HttpContext http, HttpStatusCode status, string code, string message)
    {
        http.Response.Headers.CacheControl = RefusalCacheControl;
        http.Response.Headers.XContentTypeOptions = "nosniff";

        return CmsProblems
            .Problem(status, "media", "Media request refused", ValidationResult.Error(code, message))
            .ExecuteAsync(http);
    }
}
