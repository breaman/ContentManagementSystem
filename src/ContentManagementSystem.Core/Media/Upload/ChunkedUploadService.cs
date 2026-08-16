using System.Text.Json;

using ContentManagementSystem.Core.Media.Stores;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Media.Upload;

/// <summary>
/// Resumable uploads for files too large to send in one request (task P5-08, spec section 13.3).
/// </summary>
/// <remarks>
/// <strong>This is a transport, not a second way into the library.</strong> Every part is staged
/// under the <c>incoming</c> prefix and nothing becomes a <c>MediaItem</c> until the assembled bytes
/// have been through <see cref="IMediaUploadService"/> — the same sniffing, the same decode-bomb
/// guard, the same SVG policy, the same scan, the same dedupe. A chunked path that wrote its own row
/// at the end would be a back door that looked like a feature, which is the mistake the replace
/// endpoint deliberately avoids in the same way.
/// <para>
/// Parts arrive strictly in order, and the session reports the index it wants next. That is what
/// makes an interrupted upload resumable rather than merely restartable: a client that lost its
/// connection asks where the server got to and continues from there. Sequencing also removes a race
/// the alternative would have — two parts arriving at once would both rewrite the manifest, and one
/// of the two updates would be lost.
/// </para>
/// <para>
/// The session's state lives in the store beside its fragments rather than in a table. An upload in
/// progress is transient data with exactly the lifetime of the bytes it describes, so keeping the
/// two together means abandoning one is a delete rather than a row and a sweep that could disagree.
/// </para>
/// </remarks>
public interface IChunkedUploadService
{
    /// <summary>
    /// Opens a session.
    /// </summary>
    /// <param name="request">What is about to be uploaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new session, or why it was refused.</returns>
    /// <remarks>
    /// The extension allowlist and the size ceiling are applied here, from the declared name and
    /// size — before a byte is transferred. Applying them only at completion would mean a client
    /// could spend an hour uploading a file that was never going to be accepted.
    /// </remarks>
    Task<CmsResult<ChunkedUploadSession>> StartAsync(
        StartChunkedUploadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports where a session has got to, which is how a client resumes.
    /// </summary>
    /// <param name="uploadId">Identity of the session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session, or a not-found result when it never existed or has expired.</returns>
    Task<CmsResult<ChunkedUploadSession>> GetAsync(
        string uploadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts the next part.
    /// </summary>
    /// <param name="uploadId">Identity of the session.</param>
    /// <param name="chunkIndex">Zero-based position of the part, which must be the expected one.</param>
    /// <param name="content">The part's bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session as it now stands, or why the part was refused.</returns>
    Task<CmsResult<ChunkedUploadSession>> AppendAsync(
        string uploadId,
        int chunkIndex,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assembles the parts and runs them through the ordinary upload pipeline.
    /// </summary>
    /// <param name="uploadId">Identity of the session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the upload produced, or why it was refused.</returns>
    Task<CmsResult<MediaUploadResult>> CompleteAsync(
        string uploadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abandons a session and discards everything staged for it.
    /// </summary>
    /// <param name="uploadId">Identity of the session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identity that was discarded, or a not-found result.</returns>
    Task<CmsResult<string>> AbandonAsync(string uploadId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IChunkedUploadService" />
/// <param name="store">Where parts and the manifest are staged.</param>
/// <param name="uploads">The pipeline the assembled bytes go through.</param>
/// <param name="options">The deployment's limits.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="clock">Source of the current time, for session expiry.</param>
/// <param name="logger">Log for every session opened, finished, and abandoned.</param>
public sealed class ChunkedUploadService(
    IMediaStore store,
    IMediaUploadService uploads,
    MediaUploadOptions options,
    ICmsAuthorization authorization,
    TimeProvider clock,
    ILogger<ChunkedUploadService> logger) : IChunkedUploadService
{
    /// <summary>
    /// Most parts one session may be cut into.
    /// </summary>
    /// <remarks>
    /// A bound on the number of objects one session can create, independent of the size ceiling.
    /// Without it, a client could open a session for 50 MB and send it as fifty thousand one-kilobyte
    /// parts — every one of them a store round trip and a manifest rewrite.
    /// </remarks>
    private const int MaxChunks = 4096;

    /// <summary>The media type the staged fragments are stored as.</summary>
    /// <remarks>
    /// Deliberately not the type the file claims to be. A fragment is not a file of any type — its
    /// bytes have not been sniffed and its first part may be the only one carrying a header — so
    /// naming it anything else would be a claim nothing has checked.
    /// </remarks>
    private const string PartContentType = "application/octet-stream";

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<CmsResult<ChunkedUploadSession>> StartAsync(
        StartChunkedUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return CmsResult<ChunkedUploadSession>.Forbidden(
                "Uploading media is not permitted.", MediaCodes.Forbidden);
        }

        if (!MediaTypeCatalog.TryGetByFileName(request.FileName, out var descriptor))
        {
            return CmsResult<ChunkedUploadSession>.Invalid(
                MediaCodes.ExtensionNotAllowed,
                $"'{Path.GetExtension(request.FileName)}' is not a file type this site accepts. " +
                $"Accepted types: {string.Join(", ", MediaTypeCatalog.AllowedExtensions.Order())}.");
        }

        if (request.TotalBytes <= 0)
        {
            return CmsResult<ChunkedUploadSession>.Invalid(
                MediaCodes.FileMissing, "An upload has to say how large the file is.");
        }

        var ceiling = options.MaxBytesFor(descriptor);

        if (request.TotalBytes > ceiling)
        {
            // Refused before a byte is transferred. The pipeline enforces the same ceiling on the
            // bytes that actually arrive, so a client that lied here gains nothing but a wasted hour.
            return CmsResult<ChunkedUploadSession>.Invalid(
                MediaCodes.TooLarge,
                $"That file is larger than the {ceiling / (1024 * 1024)} MB limit for {descriptor.Kind} uploads.");
        }

        var chunkSize = ChunkSize(request.TotalBytes);
        var now = clock.GetUtcNow();

        var manifest = new UploadManifest
        {
            UploadId = Guid.NewGuid().ToString("N"),
            FileName = Path.GetFileName(request.FileName),
            TotalBytes = request.TotalBytes,
            ChunkSize = chunkSize,
            ReceivedBytes = 0,
            NextChunkIndex = 0,
            FolderId = request.FolderId,
            AltText = request.AltText,
            IsDecorative = request.IsDecorative,
            Title = request.Title,
            Caption = request.Caption,
            Credit = request.Credit,
            StartedOn = now,
            ExpiresOn = now + options.UploadSessionLifetime,
        };

        await WriteManifestAsync(manifest, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Opened resumable upload {UploadId} for {TotalBytes} bytes in {ChunkSize}-byte parts.",
            manifest.UploadId,
            manifest.TotalBytes,
            manifest.ChunkSize);

        return CmsResult<ChunkedUploadSession>.Success(manifest.ToSession());
    }

    /// <inheritdoc />
    public async Task<CmsResult<ChunkedUploadSession>> GetAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return CmsResult<ChunkedUploadSession>.Forbidden(
                "Uploading media is not permitted.", MediaCodes.Forbidden);
        }

        var manifest = await ReadManifestAsync(uploadId, cancellationToken).ConfigureAwait(false);

        return manifest is null ? NoSuchSession<ChunkedUploadSession>() : CmsResult<ChunkedUploadSession>.Success(manifest.ToSession());
    }

    /// <inheritdoc />
    public async Task<CmsResult<ChunkedUploadSession>> AppendAsync(
        string uploadId,
        int chunkIndex,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return CmsResult<ChunkedUploadSession>.Forbidden(
                "Uploading media is not permitted.", MediaCodes.Forbidden);
        }

        var manifest = await ReadManifestAsync(uploadId, cancellationToken).ConfigureAwait(false);

        if (manifest is null) return NoSuchSession<ChunkedUploadSession>();

        if (manifest.ReceivedBytes >= manifest.TotalBytes)
        {
            return CmsResult<ChunkedUploadSession>.Invalid(
                MediaCodes.UploadChunkOutOfOrder,
                "Every byte of this upload has already arrived; finish the session instead.");
        }

        if (chunkIndex != manifest.NextChunkIndex)
        {
            // Carries the expected index in the message, because the correct client behaviour is to
            // seek there and continue rather than to give up.
            return CmsResult<ChunkedUploadSession>.Invalid(
                MediaCodes.UploadChunkOutOfOrder,
                $"This upload expects part {manifest.NextChunkIndex}, not part {chunkIndex}. " +
                $"{manifest.ReceivedBytes} of {manifest.TotalBytes} bytes have arrived.");
        }

        // Buffered so the length is known before anything is stored: a part is bounded by the chunk
        // size the server chose, and the copy is what enforces that rather than trusting a
        // Content-Length the client wrote.
        using var buffer = new MemoryStream(manifest.ChunkSize);

        var remaining = manifest.TotalBytes - manifest.ReceivedBytes;
        var allowed = (int)Math.Min(manifest.ChunkSize, remaining);

        var copied = await CopyBoundedAsync(content, buffer, allowed, cancellationToken).ConfigureAwait(false);

        if (copied is null)
        {
            return CmsResult<ChunkedUploadSession>.Invalid(
                MediaCodes.TooLarge,
                $"A part may be at most {manifest.ChunkSize} bytes, and no more than the " +
                $"{remaining} bytes still outstanding.");
        }

        if (copied is 0)
        {
            return CmsResult<ChunkedUploadSession>.Invalid(
                MediaCodes.FileMissing, "That part carried no bytes.");
        }

        buffer.Position = 0;

        await store
            .PutAsync(
                MediaStorageKeys.ForUploadChunk(manifest.UploadId, chunkIndex),
                buffer,
                PartContentType,
                cancellationToken)
            .ConfigureAwait(false);

        // The manifest is written after the part, never before: a manifest that counted a part the
        // store does not hold would make the session unfinishable, while a part no manifest counts
        // is simply overwritten when it is re-sent.
        var updated = manifest with
        {
            ReceivedBytes = manifest.ReceivedBytes + copied.Value,
            NextChunkIndex = chunkIndex + 1,
        };

        await WriteManifestAsync(updated, cancellationToken).ConfigureAwait(false);

        return CmsResult<ChunkedUploadSession>.Success(updated.ToSession());
    }

    /// <inheritdoc />
    public async Task<CmsResult<MediaUploadResult>> CompleteAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return CmsResult<MediaUploadResult>.Forbidden(
                "Uploading media is not permitted.", MediaCodes.Forbidden);
        }

        var manifest = await ReadManifestAsync(uploadId, cancellationToken).ConfigureAwait(false);

        if (manifest is null) return NoSuchSession<MediaUploadResult>();

        if (manifest.ReceivedBytes != manifest.TotalBytes)
        {
            return CmsResult<MediaUploadResult>.Invalid(
                MediaCodes.UploadIncomplete,
                $"{manifest.ReceivedBytes} of {manifest.TotalBytes} bytes have arrived; " +
                $"send part {manifest.NextChunkIndex} next.");
        }

        // Assembled in memory, bounded by the size ceiling that was applied when the session opened.
        // The single-request endpoint buffers the same way and for the same reason: the pipeline
        // reads the bytes several times — to sniff, to scan, to hash, to store — and a concatenation
        // of forward-only streams cannot be read twice.
        using var assembled = new MemoryStream((int)Math.Min(manifest.TotalBytes, int.MaxValue));

        for (var index = 0; index < manifest.NextChunkIndex; index++)
        {
            var key = MediaStorageKeys.ForUploadChunk(manifest.UploadId, index);

            await using var part = await store.GetAsync(key, cancellationToken).ConfigureAwait(false);

            if (part is null)
            {
                logger.LogWarning(
                    "Resumable upload {UploadId} is missing part {ChunkIndex}; the session is discarded.",
                    manifest.UploadId,
                    index);

                await DiscardAsync(manifest, cancellationToken).ConfigureAwait(false);

                return CmsResult<MediaUploadResult>.Invalid(
                    MediaCodes.UploadIncomplete,
                    "Part of this upload is no longer in storage. Start it again.");
            }

            await part.CopyToAsync(assembled, cancellationToken).ConfigureAwait(false);
        }

        assembled.Position = 0;

        // The one path into the library. Everything the single-request endpoint refuses is refused
        // here too, on exactly the same code, which is what stops the chunked route being a way in
        // for a file the other route would not take.
        var result = await uploads.UploadAsync(
            new MediaUploadRequest(
                assembled,
                manifest.FileName,
                manifest.FolderId,
                manifest.AltText,
                manifest.IsDecorative,
                manifest.Title,
                manifest.Caption,
                manifest.Credit),
            cancellationToken)
            .ConfigureAwait(false);

        // The staged fragments go whatever the pipeline decided. A refused upload has no more claim
        // on the storage than an accepted one, and leaving the parts behind so the client can "try
        // again" would only let it try the same refusal again.
        await DiscardAsync(manifest, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Resumable upload {UploadId} finished with outcome {Outcome}.", manifest.UploadId, result.Outcome);

        return result;
    }

    /// <inheritdoc />
    public async Task<CmsResult<string>> AbandonAsync(
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return CmsResult<string>.Forbidden("Uploading media is not permitted.", MediaCodes.Forbidden);
        }

        var manifest = await ReadManifestAsync(uploadId, cancellationToken).ConfigureAwait(false);

        if (manifest is null) return NoSuchSession<string>();

        await DiscardAsync(manifest, cancellationToken).ConfigureAwait(false);

        return CmsResult<string>.Success(manifest.UploadId);
    }

    /// <summary>
    /// Chooses the part size for a file.
    /// </summary>
    /// <param name="totalBytes">How large the whole file is.</param>
    /// <returns>The part size in bytes.</returns>
    /// <remarks>
    /// The configured size, raised when it would produce more parts than <see cref="MaxChunks"/>.
    /// Raising rather than refusing: a file within the size ceiling must always be uploadable, and
    /// the part count is an implementation concern the uploader should never have to reason about.
    /// </remarks>
    private int ChunkSize(long totalBytes)
    {
        var configured = Math.Max(options.ChunkBytes, 64 * 1024);
        var needed = (long)Math.Ceiling((double)totalBytes / MaxChunks);

        return (int)Math.Max(configured, needed);
    }

    /// <summary>
    /// Copies at most <paramref name="limit"/> bytes, and reports an overrun rather than truncating.
    /// </summary>
    /// <param name="source">The incoming part.</param>
    /// <param name="destination">Where to buffer it.</param>
    /// <param name="limit">Most bytes this part may carry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of bytes copied, or null when the source held more than the limit.</returns>
    /// <remarks>
    /// One byte past the limit is read deliberately, so an oversized part is refused rather than
    /// silently cut short — a truncated part would assemble into a corrupt file that passed every
    /// length check on the way.
    /// </remarks>
    private static async Task<int?> CopyBoundedAsync(
        Stream source,
        Stream destination,
        int limit,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var total = 0;

        while (true)
        {
            var read = await source
                .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);

            if (read is 0) return total;

            if (total + read > limit) return null;

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            total += read;
        }
    }

    /// <summary>Reads a session's manifest, treating an expired one as absent.</summary>
    /// <param name="uploadId">The identity a client presented.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The manifest, or null when there is no usable session.</returns>
    /// <remarks>
    /// The identity is checked for shape before it reaches a key, because it is the one part of a
    /// media storage key that arrives from a client. An expired session reads as absent and is swept
    /// on the way past, so expiry needs no background job to be correct — only to be tidy.
    /// </remarks>
    private async Task<UploadManifest?> ReadManifestAsync(string uploadId, CancellationToken cancellationToken)
    {
        if (!MediaStorageKeys.IsValidUploadId(uploadId)) return null;

        await using var content = await store
            .GetAsync(MediaStorageKeys.ForUploadManifest(uploadId), cancellationToken)
            .ConfigureAwait(false);

        if (content is null) return null;

        UploadManifest? manifest;

        try
        {
            manifest = await JsonSerializer
                .DeserializeAsync<UploadManifest>(content, ManifestJson, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Resumable upload {UploadId} has an unreadable manifest.", uploadId);

            return null;
        }

        if (manifest is null) return null;

        if (clock.GetUtcNow() <= manifest.ExpiresOn) return manifest;

        logger.LogInformation("Resumable upload {UploadId} expired and was discarded.", manifest.UploadId);

        await DiscardAsync(manifest, cancellationToken).ConfigureAwait(false);

        return null;
    }

    private async Task WriteManifestAsync(UploadManifest manifest, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson));

        await store
            .PutAsync(
                MediaStorageKeys.ForUploadManifest(manifest.UploadId),
                content,
                "application/json",
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Removes every object a session staged.</summary>
    /// <param name="manifest">The session to discard.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The manifest goes last. A session whose manifest survived its parts would be resumable in
    /// name only — the client would be told to send part twelve of a file whose first eleven parts
    /// no longer exist.
    /// </remarks>
    private async Task DiscardAsync(UploadManifest manifest, CancellationToken cancellationToken)
    {
        for (var index = 0; index < manifest.NextChunkIndex; index++)
        {
            await store
                .DeleteAsync(MediaStorageKeys.ForUploadChunk(manifest.UploadId, index), cancellationToken)
                .ConfigureAwait(false);
        }

        await store
            .DeleteAsync(MediaStorageKeys.ForUploadManifest(manifest.UploadId), cancellationToken)
            .ConfigureAwait(false);
    }

    private static CmsResult<T> NoSuchSession<T>() =>
        CmsResult<T>.NotFound(
            "That upload session does not exist, or it expired and its parts were discarded.",
            MediaCodes.UploadSessionNotFound);

    /// <summary>
    /// What the store holds about a session in progress.
    /// </summary>
    /// <remarks>
    /// A private type, and it stays private: it is written and read by this class alone, so its
    /// shape is free to change without being a wire contract. What clients see is
    /// <see cref="ChunkedUploadSession"/>, which deliberately carries none of the metadata — an
    /// upload's alt text is not something a caller should be able to read back out of a session id.
    /// </remarks>
    private sealed record UploadManifest
    {
        public required string UploadId { get; init; }

        public required string FileName { get; init; }

        public required long TotalBytes { get; init; }

        public required int ChunkSize { get; init; }

        public required long ReceivedBytes { get; init; }

        public required int NextChunkIndex { get; init; }

        public int? FolderId { get; init; }

        public string? AltText { get; init; }

        public bool IsDecorative { get; init; }

        public string? Title { get; init; }

        public string? Caption { get; init; }

        public string? Credit { get; init; }

        public required DateTimeOffset StartedOn { get; init; }

        public required DateTimeOffset ExpiresOn { get; init; }

        public ChunkedUploadSession ToSession() => new(
            UploadId,
            FileName,
            TotalBytes,
            ReceivedBytes,
            NextChunkIndex,
            ChunkSize,
            ReceivedBytes >= TotalBytes,
            ExpiresOn);
    }
}
