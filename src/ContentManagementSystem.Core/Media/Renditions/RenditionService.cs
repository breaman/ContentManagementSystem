using System.Diagnostics;

using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Core.Media.Stores;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Media;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Media.Renditions;

/// <summary>A rendition ready to be written to a response.</summary>
/// <param name="Content">The encoded bytes. The caller disposes.</param>
/// <param name="ContentType">Media type to pin the response to.</param>
/// <param name="Width">Width of the produced image.</param>
/// <param name="Height">Height of the produced image.</param>
/// <param name="SizeBytes">Size of the encoded file.</param>
/// <param name="WasGenerated">Whether this request paid for the encode.</param>
public sealed record RenditionContent(
    Stream Content,
    string ContentType,
    int Width,
    int Height,
    long SizeBytes,
    bool WasGenerated);

/// <summary>Why a rendition could not be produced.</summary>
public enum RenditionFailure
{
    /// <summary>The item does not exist, or is in the recycle bin.</summary>
    NotFound = 0,

    /// <summary>
    /// The URL was signed against an older generation of the item's edits.
    /// </summary>
    /// <remarks>
    /// The refusal that makes non-destructive editing safe to cache for a year. A stale URL is
    /// perfectly well signed — it was signed by this site — so serving it would be easy and wrong:
    /// the rendition would be produced from the item's <em>current</em> edits and then cached under
    /// the <em>old</em> version's key, which is a permanently wrong picture at a URL that says
    /// <c>immutable</c>. Refusing means an old link stops working, and the page that emits links
    /// re-signs them on its next render (ADR 0007).
    /// </remarks>
    Stale,

    /// <summary>The item is not an image, so there is nothing to render.</summary>
    NotAnImage,

    /// <summary>The item's stored original is missing from the media store.</summary>
    OriginalMissing,

    /// <summary>The original could not be decoded or encoded.</summary>
    ProcessingFailed,
}

/// <summary>
/// Produces renditions, generating them once and reusing them thereafter (task P5-13,
/// spec section 13.5).
/// </summary>
public interface IRenditionService
{
    /// <summary>
    /// Returns a rendition, generating it if this is the first request for it.
    /// </summary>
    /// <param name="spec">The rendition asked for. Assumed already validated and signed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rendition, or why it could not be produced.</returns>
    Task<(RenditionContent? Content, RenditionFailure? Failure)> GetAsync(
        RenditionSpec spec,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRenditionService" />
/// <param name="context">The application database context.</param>
/// <param name="store">Where originals are read from and renditions are written.</param>
/// <param name="processor">The image processor.</param>
/// <param name="locks">Per-key locks, so concurrent cold requests produce one encode.</param>
/// <param name="metrics">The CMS meter.</param>
/// <param name="clock">Source of the current time.</param>
/// <param name="logger">Logger.</param>
public sealed class RenditionService(
    ApplicationDbContext context,
    IMediaStore store,
    IImageProcessor processor,
    RenditionKeyLocks locks,
    CmsMetrics metrics,
    TimeProvider clock,
    ILogger<RenditionService> logger) : IRenditionService
{
    /// <inheritdoc />
    public async Task<(RenditionContent? Content, RenditionFailure? Failure)> GetAsync(
        RenditionSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var specHash = spec.ComputeHash();

        // The cheap path, taken by every request after the first. Deliberately outside the lock:
        // taking a lock to discover that nothing needs doing would serialise a hot image behind
        // itself for no reason.
        if (await TryServeStoredAsync(spec, specHash, cancellationToken).ConfigureAwait(false) is { } cached)
        {
            return (cached, null);
        }

        // The item is loaded before the lock so that a request for something that does not exist
        // does not queue behind a generation it will never use.
        var item = await context.MediaItems
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == spec.MediaItemId, cancellationToken)
            .ConfigureAwait(false);

        if (item is null) return (null, RenditionFailure.NotFound);

        if (item.MediaKind is not MediaKind.Image) return (null, RenditionFailure.NotAnImage);

        // The check that keeps a year-long immutable cache honest. Rendering here would apply the
        // item's current edits and then store the result under a hash containing the old version,
        // which is the one failure the version counter exists to make impossible (ADR 0007).
        if (spec.EditsVersion != item.EditsVersion)
        {
            logger.LogDebug(
                "Rendition of media item {MediaItemId} was asked for at edits version {Requested}; " +
                "the item is at {Current}.",
                item.Id,
                spec.EditsVersion,
                item.EditsVersion);

            return (null, RenditionFailure.Stale);
        }

        using var handle = await locks.AcquireAsync(spec.ToCanonicalString(), cancellationToken).ConfigureAwait(false);

        // Re-checked while holding the lock. Nineteen of twenty concurrent cold requests take this
        // branch: they waited, the first one generated, and they now serve what it produced. Without
        // this the lock would only stagger twenty encodes rather than prevent nineteen of them.
        if (await TryServeStoredAsync(spec, specHash, cancellationToken).ConfigureAwait(false) is { } justGenerated)
        {
            return (justGenerated, null);
        }

        return await GenerateAsync(spec, specHash, item, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serves a rendition that has already been generated, when its bytes are still in the store.
    /// </summary>
    /// <param name="spec">The rendition asked for.</param>
    /// <param name="specHash">Its hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored rendition, or null when it has to be generated.</returns>
    /// <remarks>
    /// A row whose object has gone is treated as absent rather than as an error. Renditions are
    /// derived data that is explicitly not backed up, so an emptied container or a wiped disk should
    /// cost a regeneration, not a broken image on every page.
    /// <para>
    /// The item's current edits version is part of the predicate, so a row left behind by an earlier
    /// generation is invisible here rather than served. It is a join rather than a second query on
    /// purpose: this is the path every image request after the first takes, and paying two round
    /// trips to serve a cached rendition would be the most expensive line in the delivery path.
    /// </para>
    /// </remarks>
    private async Task<RenditionContent?> TryServeStoredAsync(
        RenditionSpec spec,
        byte[] specHash,
        CancellationToken cancellationToken)
    {
        var stored = await context.MediaRenditions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                rendition => rendition.MediaItemId == spec.MediaItemId &&
                    rendition.SpecHash == specHash &&
                    rendition.EditsVersion == rendition.MediaItem.EditsVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (stored is null) return null;

        var content = await store.GetAsync(stored.StorageKey, cancellationToken).ConfigureAwait(false);

        if (content is null)
        {
            logger.LogWarning(
                "Rendition {RenditionId} has no stored object; it will be regenerated.", stored.Id);

            context.MediaRenditions.Remove(new MediaRendition { Id = stored.Id });

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }

        return new RenditionContent(
            content, spec.MimeType, stored.Width, stored.Height, stored.SizeBytes, WasGenerated: false);
    }

    /// <summary>
    /// Encodes a rendition and records it.
    /// </summary>
    /// <param name="spec">The rendition to produce.</param>
    /// <param name="specHash">Its hash.</param>
    /// <param name="item">The item being rendered.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The produced rendition, or why it could not be produced.</returns>
    private async Task<(RenditionContent? Content, RenditionFailure? Failure)> GenerateAsync(
        RenditionSpec spec,
        byte[] specHash,
        MediaItem item,
        CancellationToken cancellationToken)
    {
        await using var original = await store.GetAsync(item.StorageKey, cancellationToken).ConfigureAwait(false);

        if (original is null)
        {
            logger.LogError(
                "Media item {MediaItemId} has no stored original at {StorageKey}.", item.Id, item.StorageKey);

            return (null, RenditionFailure.OriginalMissing);
        }

        // Buffered because the processor decodes the same content more than once and a blob stream
        // is forward-only. Bounded by the original's size, which the upload limits already cap.
        using var buffered = new MemoryStream();

        await original.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);

        var started = Stopwatch.GetTimestamp();
        var rendered = processor.Render(buffered, spec, MediaEdits.Parse(item.EditsJson));
        var elapsed = Stopwatch.GetElapsedTime(started);

        if (rendered is null) return (null, RenditionFailure.ProcessingFailed);

        metrics.RecordRenditionGenerated(spec.Extension, elapsed);

        var storageKey = MediaStorageKeys.ForRendition(item.Id, specHash, spec.Extension);

        using (var content = new MemoryStream(rendered.Bytes, writable: false))
        {
            await store.PutAsync(storageKey, content, rendered.MimeType, cancellationToken).ConfigureAwait(false);
        }

        context.MediaRenditions.Add(new MediaRendition
        {
            MediaItemId = item.Id,
            SpecHash = specHash,
            Spec = spec.ToCanonicalString(),
            Width = rendered.Width,
            Height = rendered.Height,
            Format = spec.Extension,
            Quality = spec.Quality,
            SizeBytes = rendered.Bytes.LongLength,
            StorageKey = storageKey,
            EditsVersion = spec.EditsVersion,
            GeneratedOn = clock.GetUtcNow(),
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            // The unique index is the backstop behind the lock: another instance of the application
            // generated the same rendition while this one did. Losing that race is not a failure —
            // the bytes are identical and already stored under the same content-derived key — so the
            // response is served rather than refused.
            logger.LogInformation(
                exception, "Rendition {Spec} was recorded concurrently by another instance.", spec.ToCanonicalString());

            context.ChangeTracker.Clear();
        }

        logger.LogDebug(
            "Generated rendition {Spec} in {ElapsedMs} ms.", spec.ToCanonicalString(), elapsed.TotalMilliseconds);

        return (
            new RenditionContent(
                new MemoryStream(rendered.Bytes, writable: false),
                rendered.MimeType,
                rendered.Width,
                rendered.Height,
                rendered.Bytes.LongLength,
                WasGenerated: true),
            null);
    }
}
