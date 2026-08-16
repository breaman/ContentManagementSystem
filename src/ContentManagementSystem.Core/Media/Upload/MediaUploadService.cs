using System.Security.Cryptography;
using System.Text;

using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Core.Media.Stores;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Media.Upload;

/// <inheritdoc cref="IMediaUploadService" />
/// <param name="context">The application database context.</param>
/// <param name="store">Where the accepted bytes are written.</param>
/// <param name="processor">Reads image headers, bakes orientation, and strips metadata.</param>
/// <param name="scanner">Optional malware scan, applied before anything is stored.</param>
/// <param name="options">The limits and policies this deployment enforces.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="logger">Log for every rejection — the security-relevant half of this class.</param>
public sealed class MediaUploadService(
    ApplicationDbContext context,
    IMediaStore store,
    IImageProcessor processor,
    IMalwareScanner scanner,
    MediaUploadOptions options,
    ICmsAuthorization authorization,
    ILogger<MediaUploadService> logger) : IMediaUploadService
{
    /// <inheritdoc />
    public async Task<CmsResult<MediaUploadResult>> UploadAsync(
        MediaUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return CmsResult<MediaUploadResult>.Forbidden("Uploading media is not permitted.", MediaCodes.Forbidden);
        }

        var screened = await ScreenAsync(request, requireAltText: true, cancellationToken).ConfigureAwait(false);

        if (!screened.IsSuccess) return CmsResult<MediaUploadResult>.Invalid(screened.Diagnostics);

        var (descriptor, prepare) = screened.Value!;

        // Step 7 — hash and dedupe. Taken over the normalized bytes rather than the upload, so that
        // the same photograph uploaded twice from two phones — identical pixels, different EXIF —
        // deduplicates. Hashing the raw upload would treat those as two files.
        prepare.Content.Position = 0;

        var sha256 = await SHA256.HashDataAsync(prepare.Content, cancellationToken).ConfigureAwait(false);

        var existing = await context.MediaItems
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Sha256 == sha256, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            logger.LogInformation(
                "Upload of {FileName} matched existing media item {MediaItemId}.",
                Safe(request.FileName),
                existing.Id);

            return CmsResult<MediaUploadResult>.Success(
                new MediaUploadResult(MediaProjections.ToDetail(existing), Deduplicated: true, prepare.Removals));
        }

        // Steps 8 and 9 — persist the original, then write the row. In that order: an object with no
        // row is an orphan a sweep reclaims, while a row with no object is an item that renders as a
        // broken image on every page that used it.
        var storageKey = MediaStorageKeys.ForOriginal(sha256, prepare.Extension);

        prepare.Content.Position = 0;

        var stored = await store
            .PutAsync(storageKey, prepare.Content, prepare.ContentType, cancellationToken)
            .ConfigureAwait(false);

        var item = new MediaItem
        {
            FolderId = request.FolderId,
            FileName = Path.GetFileName(storageKey),
            OriginalFileName = Truncate(Path.GetFileName(request.FileName), FieldLengths.FileName),
            ContentType = prepare.ContentType,
            SizeBytes = stored.SizeBytes,
            Sha256 = sha256,
            StorageKey = storageKey,
            MediaKind = descriptor.Kind,
            Width = prepare.Width,
            Height = prepare.Height,
            AltText = request.IsDecorative ? null : request.AltText?.Trim(),
            IsDecorative = request.IsDecorative,
            Title = request.Title?.Trim(),
            Caption = request.Caption?.Trim(),
            Credit = request.Credit?.Trim(),
        };

        context.MediaItems.Add(item);

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            // The deduplication index is the authority, not the SELECT above: two uploads of one
            // file racing each other both miss the query and one of them loses here. Losing that
            // race is a successful upload — the bytes are stored under a key derived from their own
            // hash, so the winner's object and this one are the same object.
            logger.LogInformation(exception, "Concurrent upload of identical bytes; returning the stored item.");

            context.ChangeTracker.Clear();

            var winner = await context.MediaItems
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Sha256 == sha256, cancellationToken)
                .ConfigureAwait(false);

            if (winner is null) throw;

            return CmsResult<MediaUploadResult>.Success(
                new MediaUploadResult(MediaProjections.ToDetail(winner), Deduplicated: true, prepare.Removals));
        }

        logger.LogInformation(
            "Stored media item {MediaItemId} ({ContentType}, {SizeBytes} bytes).",
            item.Id,
            item.ContentType,
            item.SizeBytes);

        // Step 10 — rendition generation is lazy rather than queued: the first request for a size
        // generates it behind a per-key semaphore and persists it (ADR 0007). Warming the standard
        // set at upload would encode six sizes of every image an editor happens to upload, most of
        // which no page ever asks for.
        return CmsResult<MediaUploadResult>.Success(
            new MediaUploadResult(MediaProjections.ToDetail(item), Deduplicated: false, prepare.Removals));
    }

    /// <inheritdoc />
    public async Task<CmsResult<MediaUploadResult>> ReplaceAsync(
        int mediaItemId,
        MediaUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return CmsResult<MediaUploadResult>.Forbidden("Replacing media is not permitted.", MediaCodes.Forbidden);
        }

        var item = await context.MediaItems
            .FirstOrDefaultAsync(candidate => candidate.Id == mediaItemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return CmsResult<MediaUploadResult>.NotFound(
                $"Media item {mediaItemId} does not exist.", MediaCodes.NotFound);
        }

        // The item already carries alt text or a decorative flag from its original upload, and a
        // replacement is not the moment to make an editor retype it. Anything they do send still
        // wins below.
        var screened = await ScreenAsync(request, requireAltText: false, cancellationToken)
            .ConfigureAwait(false);

        if (!screened.IsSuccess) return CmsResult<MediaUploadResult>.Invalid(screened.Diagnostics);

        var (descriptor, prepare) = screened.Value!;

        prepare.Content.Position = 0;

        var sha256 = await SHA256.HashDataAsync(prepare.Content, cancellationToken).ConfigureAwait(false);

        if (sha256.SequenceEqual(item.Sha256))
        {
            // The same bytes are already here. Reported as a success that changed nothing rather
            // than bumping the edits version, because an editor who re-uploaded the file they
            // already had should not thereby invalidate every cached rendition on the site.
            logger.LogInformation(
                "Replacement of media item {MediaItemId} carried the bytes it already holds.",
                item.Id);

            return CmsResult<MediaUploadResult>.Success(
                new MediaUploadResult(MediaProjections.ToDetail(item), Deduplicated: true, prepare.Removals));
        }

        var duplicate = await context.MediaItems
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Sha256 == sha256, cancellationToken)
            .ConfigureAwait(false);

        if (duplicate is not null)
        {
            // Refused rather than merged. An upload of known bytes is answered with the existing
            // item, which is what the editor wanted; a *replace* with known bytes would leave two
            // ids naming one file and silently redirect every page pointing at this one.
            return CmsResult<MediaUploadResult>.Conflict(
                MediaCodes.Duplicate,
                $"Those bytes are already in the library as item {duplicate.Id}. Point the pages at " +
                "that item instead of replacing this one with a copy of it.");
        }

        var storageKey = MediaStorageKeys.ForOriginal(sha256, prepare.Extension);

        prepare.Content.Position = 0;

        var stored = await store
            .PutAsync(storageKey, prepare.Content, prepare.ContentType, cancellationToken)
            .ConfigureAwait(false);

        var previousKey = item.StorageKey;

        item.FileName = Path.GetFileName(storageKey);
        item.OriginalFileName = Truncate(Path.GetFileName(request.FileName), FieldLengths.FileName);
        item.ContentType = prepare.ContentType;
        item.SizeBytes = stored.SizeBytes;
        item.Sha256 = sha256;
        item.StorageKey = storageKey;
        item.MediaKind = descriptor.Kind;
        item.Width = prepare.Width;
        item.Height = prepare.Height;

        if (!string.IsNullOrWhiteSpace(request.AltText) || request.IsDecorative)
        {
            item.AltText = request.IsDecorative ? null : request.AltText?.Trim();
            item.IsDecorative = request.IsDecorative;
        }

        // The whole point of replace-keeping-id: every ContentReference row, every placement, and
        // every page showing this item is untouched, and the picture they show changes. The counter
        // is what makes that visible — it is folded into every rendition signature, so the URLs the
        // site emits after this call are different from the ones browsers and CDNs have cached
        // (ADR 0007). Without it, pages would keep serving the old picture until the caches expired.
        item.EditsVersion++;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Stale renditions are left where they are. They are keyed by a hash that includes the edits
        // version, so nothing can address them any more, and a request that tried would fail
        // signature validation first; the eviction job reclaims them. Deleting them here would put
        // a burst of store round trips inside an editor's save.
        logger.LogInformation(
            "Media item {MediaItemId} was replaced ({ContentType}, {SizeBytes} bytes); edits version " +
            "is now {EditsVersion}. Previous original {PreviousKey} is now unreferenced.",
            item.Id,
            item.ContentType,
            item.SizeBytes,
            item.EditsVersion,
            previousKey);

        return CmsResult<MediaUploadResult>.Success(
            new MediaUploadResult(MediaProjections.ToDetail(item), Deduplicated: false, prepare.Removals));
    }

    /// <summary>
    /// Runs steps 1 to 6 of the pipeline — everything that decides whether these bytes may be stored
    /// at all.
    /// </summary>
    /// <param name="request">The file and the metadata offered with it.</param>
    /// <param name="requireAltText">
    /// Whether a missing description is a refusal. False on a replacement, where the item already
    /// has one.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bytes to store and what they turned out to be, or the refusal.</returns>
    /// <remarks>
    /// Shared by upload and replace so the two cannot diverge. That matters more here than anywhere
    /// else in the codebase: a replace path that sniffed less thoroughly than the upload path would
    /// be a way to get an HTML file into the library through the back door, and it would look
    /// exactly like a working feature.
    /// </remarks>
    private async Task<CmsResult<ScreenedUpload>> ScreenAsync(
        MediaUploadRequest request,
        bool requireAltText,
        CancellationToken cancellationToken)
    {
        var content = request.Content;

        if (content is null || !content.CanSeek)
        {
            throw new ArgumentException(
                "The upload stream must be seekable; buffer it before calling.", nameof(request));
        }

        // Step 1 — size. Checked here as well as by the endpoint's own limits, because this service
        // is also reachable from the CLI and from tests, and a limit that only exists in the HTTP
        // layer is a limit the next caller does not have.
        if (content.Length is 0)
        {
            return Refuse(MediaCodes.FileMissing, "The uploaded file is empty.");
        }

        // Step 2 — extension allowlist.
        if (!MediaTypeCatalog.TryGetByFileName(request.FileName, out var descriptor))
        {
            logger.LogInformation("Upload refused: extension of {FileName} is not allowed.", Safe(request.FileName));

            return Refuse(
                MediaCodes.ExtensionNotAllowed,
                $"Files of that type cannot be uploaded. Allowed types: {string.Join(", ", MediaTypeCatalog.AllowedExtensions.Order())}.");
        }

        var maxBytes = options.MaxBytesFor(descriptor);

        if (content.Length > maxBytes)
        {
            return Refuse(
                MediaCodes.TooLarge,
                $"That file is {content.Length / (1024 * 1024)} MB; the limit is {maxBytes / (1024 * 1024)} MB.");
        }

        // Step 3 — magic-number sniff. The declared extension has to agree with what the bytes are.
        var header = await MediaTypeSniffer.ReadHeaderAsync(content, cancellationToken).ConfigureAwait(false);
        var sniffed = MediaTypeSniffer.Detect(header);

        if (sniffed is MediaByteFormat.Avif)
        {
            return Refuse(
                MediaCodes.AvifNotSupported,
                "AVIF files cannot be uploaded. Convert the image to JPEG, PNG, or WebP first.");
        }

        if (sniffed != descriptor.Format)
        {
            // Logged at warning: a mismatch is either a corrupted export or somebody probing the
            // upload endpoint, and the second is worth seeing in a log review.
            logger.LogWarning(
                "Upload refused: {FileName} claims {Declared} but its bytes are {Sniffed}.",
                Safe(request.FileName),
                descriptor.Format,
                sniffed);

            return Refuse(
                MediaCodes.TypeMismatch,
                "The file's contents do not match its extension, so it was refused.");
        }

        var upload = new UploadContext(request, descriptor, content);

        // Steps 4 and 5 — decode-bomb guard and SVG policy, whichever applies.
        var prepared = descriptor.Format is MediaByteFormat.Svg
            ? await PrepareSvgAsync(upload, cancellationToken).ConfigureAwait(false)
            : PrepareBinary(upload);

        if (!prepared.IsSuccess) return CmsResult<ScreenedUpload>.Invalid(prepared.Diagnostics);

        var prepare = prepared.Value!;

        // Alt text, asked for while the person who chose the image is still looking at it
        // (spec section 13.7). The publish-time check is the one that cannot be skipped; this one is
        // the one that gets an answer.
        if (requireAltText &&
            options.RequireAltTextOnUpload &&
            descriptor.Kind is MediaKind.Image &&
            string.IsNullOrWhiteSpace(request.AltText) &&
            !request.IsDecorative)
        {
            return Refuse(
                MediaCodes.AltTextRequired,
                "Describe the image for people who cannot see it, or mark it decorative.",
                nameof(request.AltText));
        }

        if (TooLong(request) is { } tooLong) return tooLong;

        // Step 6 — malware scan, before anything is stored and before any row is written.
        var scan = await scanner.ScanAsync(prepare.Content, request.FileName, cancellationToken).ConfigureAwait(false);

        if (scan.IsClean) return CmsResult<ScreenedUpload>.Success(new ScreenedUpload(descriptor, prepare));

        await QuarantineAsync(prepare.Content, cancellationToken).ConfigureAwait(false);

        logger.LogWarning(
            "Upload quarantined: {FileName} was flagged as {Detection}.",
            Safe(request.FileName),
            scan.Detection);

        return Refuse(MediaCodes.MalwareDetected, "The file was flagged by the malware scanner and refused.");
    }

    /// <summary>
    /// Applies the decode-bomb guard and normalizes a raster image (steps 4, 8 of the pipeline).
    /// </summary>
    /// <param name="upload">The upload being processed.</param>
    /// <returns>The bytes to store and what to record about them.</returns>
    private CmsResult<PreparedUpload> PrepareBinary(UploadContext upload)
    {
        if (upload.Descriptor.Kind is not MediaKind.Image)
        {
            // Documents and video are stored exactly as uploaded. There is no decode step to guard
            // and nothing to re-encode; what protects them is the sniffing that already happened and
            // the content-type pinning at delivery.
            return CmsResult<PreparedUpload>.Success(new PreparedUpload(
                upload.Content, upload.Descriptor.MimeType, upload.Descriptor.Extension, null, null, []));
        }

        var probe = processor.Probe(upload.Content);

        if (probe is null)
        {
            return CmsResult<PreparedUpload>.Invalid(
                MediaCodes.Undecodable, "The image could not be read; it may be truncated or corrupt.");
        }

        // Step 4 — the decode bomb. This is the check whose ordering matters most in the whole
        // pipeline: it is answered from the header, so a 16-gigabyte image never becomes a
        // 16-gigabyte allocation (spec section 13.3 step 4).
        if (probe.PixelCount > options.MaxPixels)
        {
            logger.LogWarning(
                "Upload refused: {FileName} declares {Width}x{Height} pixels.",
                Safe(upload.Request.FileName),
                probe.Width,
                probe.Height);

            return CmsResult<PreparedUpload>.Invalid(
                MediaCodes.DimensionsTooLarge,
                $"That image is {probe.Width}×{probe.Height}; the limit is {options.MaxPixels / 1_000_000} megapixels.");
        }

        // GIF is stored untouched: re-encoding it through a still-image pipeline would return one
        // frame of an animation. It carries no EXIF block to strip, so nothing is lost by that.
        if (probe.Format is MediaByteFormat.Gif)
        {
            return CmsResult<PreparedUpload>.Success(new PreparedUpload(
                upload.Content,
                upload.Descriptor.MimeType,
                upload.Descriptor.Extension,
                probe.Width,
                probe.Height,
                []));
        }

        var normalized = processor.NormalizeOriginal(upload.Content, probe);

        if (normalized is null)
        {
            return CmsResult<PreparedUpload>.Invalid(
                MediaCodes.Undecodable, "The image could not be processed; it may be truncated or corrupt.");
        }

        // The stored original is the processor's output, not the upload: orientation baked into the
        // pixels and every metadata block gone, including GPS (spec section 13.3 step 8).
        return CmsResult<PreparedUpload>.Success(new PreparedUpload(
            new MemoryStream(normalized.Bytes, writable: false),
            normalized.MimeType,
            upload.Descriptor.Extension,
            normalized.Width,
            normalized.Height,
            []));
    }

    /// <summary>
    /// Applies the deployment's SVG policy (step 5 of the pipeline).
    /// </summary>
    /// <param name="upload">The upload being processed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sanitized document to store, or the refusal.</returns>
    private async Task<CmsResult<PreparedUpload>> PrepareSvgAsync(
        UploadContext upload,
        CancellationToken cancellationToken)
    {
        if (options.SvgPolicy is SvgUploadPolicy.Reject)
        {
            return CmsResult<PreparedUpload>.Invalid(
                MediaCodes.SvgNotAllowed,
                "SVG files are not accepted. Upload a PNG or WebP version of the graphic instead.");
        }

        upload.Content.Position = 0;

        using var reader = new StreamReader(upload.Content, Encoding.UTF8, leaveOpen: true);

        var source = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var sanitized = await SvgSanitizer.SanitizeAsync(source, cancellationToken).ConfigureAwait(false);

        upload.Content.Position = 0;

        if (sanitized.Svg is null)
        {
            return CmsResult<PreparedUpload>.Invalid(
                MediaCodes.SvgUnsafe,
                "Nothing usable remained after the SVG was cleaned, so it was refused.");
        }

        var removals = sanitized.RemovedElements
            .Concat(sanitized.RemovedAttributes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (removals.Length > 0)
        {
            logger.LogInformation(
                "Sanitized SVG {FileName}, removing {Removals}.",
                Safe(upload.Request.FileName),
                string.Join(", ", removals));
        }

        // What gets stored is the sanitizer's output. Keeping the upload and cleaning on the way out
        // would leave the hostile version on disk, one missed render path from being served
        // (ADR 0008 makes the same argument for HTML).
        return CmsResult<PreparedUpload>.Success(new PreparedUpload(
            new MemoryStream(Encoding.UTF8.GetBytes(sanitized.Svg), writable: false),
            upload.Descriptor.MimeType,
            upload.Descriptor.Extension,
            null,
            null,
            removals));
    }

    /// <summary>
    /// Puts refused bytes somewhere an operator can look at them.
    /// </summary>
    /// <param name="content">The refused upload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the bytes are held.</returns>
    /// <remarks>
    /// Kept rather than discarded, under a key with no extension and no <c>MediaItem</c> row
    /// pointing at it. A scanner false positive would otherwise mean the editor's file is simply
    /// gone, and a true positive is evidence somebody will want.
    /// </remarks>
    private async Task QuarantineAsync(Stream content, CancellationToken cancellationToken)
    {
        content.Position = 0;

        var hash = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);

        content.Position = 0;

        await store
            .PutAsync(MediaStorageKeys.ForQuarantine(hash), content, "application/octet-stream", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Checks the metadata against the columns that store it.</summary>
    /// <param name="request">The upload request.</param>
    /// <returns>The refusal, or null when everything fits.</returns>
    private static CmsResult<ScreenedUpload>? TooLong(MediaUploadRequest request)
    {
        if (request.AltText is { Length: > FieldLengths.ShortDescription })
        {
            return Refuse(MediaCodes.TooLong, "The alternative text is too long.", nameof(request.AltText));
        }

        if (request.Title is { Length: > FieldLengths.EntityName })
        {
            return Refuse(MediaCodes.TooLong, "The title is too long.", nameof(request.Title));
        }

        if (request.Caption is { Length: > FieldLengths.Caption })
        {
            return Refuse(MediaCodes.TooLong, "The caption is too long.", nameof(request.Caption));
        }

        return request.Credit is { Length: > FieldLengths.EntityName }
            ? Refuse(MediaCodes.TooLong, "The credit is too long.", nameof(request.Credit))
            : null;
    }

    /// <summary>A screening refusal, which every caller maps to the same 422.</summary>
    private static CmsResult<ScreenedUpload> Refuse(string code, string message, string? path = null) =>
        CmsResult<ScreenedUpload>.Invalid(code, message, path);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>
    /// Renders a client-supplied file name safely for a log line.
    /// </summary>
    /// <param name="fileName">The untrusted name.</param>
    /// <returns>A bounded, single-line rendering.</returns>
    /// <remarks>
    /// An uploader chooses this string, and it ends up in structured logs that people read in a
    /// terminal and in a log viewer. Newlines let a forged entry be injected into a plain-text sink,
    /// and an unbounded name makes a log line unreadable.
    /// </remarks>
    private static string Safe(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "(unnamed)";

        var cleaned = fileName.Replace('\r', ' ').Replace('\n', ' ');

        return Truncate(cleaned, 120);
    }

    /// <summary>One upload in flight, so the step methods share a parameter list rather than five arguments.</summary>
    /// <param name="Request">The caller's request.</param>
    /// <param name="Descriptor">The allowlist entry the extension matched.</param>
    /// <param name="Content">The uploaded bytes.</param>
    private sealed record UploadContext(
        MediaUploadRequest Request,
        MediaTypeDescriptor Descriptor,
        Stream Content);

    /// <summary>Bytes that survived screening, and what the sniffer decided they are.</summary>
    /// <param name="Descriptor">The allowlist entry the extension matched.</param>
    /// <param name="Prepared">The bytes to store and the facts recorded about them.</param>
    private sealed record ScreenedUpload(MediaTypeDescriptor Descriptor, PreparedUpload Prepared);

    /// <summary>What the type-specific preparation produced.</summary>
    /// <param name="Content">The bytes to store — not necessarily the ones uploaded.</param>
    /// <param name="ContentType">The type to record and later serve as.</param>
    /// <param name="Extension">Canonical extension for the storage key.</param>
    /// <param name="Width">Pixel width, for images.</param>
    /// <param name="Height">Pixel height, for images.</param>
    /// <param name="Removals">What sanitization took out, for the upload report.</param>
    private sealed record PreparedUpload(
        Stream Content,
        string ContentType,
        string Extension,
        int? Width,
        int? Height,
        IReadOnlyList<string> Removals);
}
