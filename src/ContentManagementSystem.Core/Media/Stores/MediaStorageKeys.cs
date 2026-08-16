using System.Diagnostics.CodeAnalysis;

using ContentManagementSystem.Shared.Common;

namespace ContentManagementSystem.Core.Media.Stores;

/// <summary>
/// Builds and validates the keys media is stored under (spec section 13.2).
/// </summary>
/// <remarks>
/// Two jobs, kept together because they are two halves of one guarantee. The builders make keys that
/// are derived entirely from server-side facts — a content hash, an item id, a spec hash — so no
/// part of a key is ever a string an uploader chose. <see cref="Validate"/> then refuses anything
/// that does not have that shape, which is what stops a key that reached the database by some other
/// route from escaping the store's root.
/// <para>
/// <strong>Content-addressing is deliberate and does more than name things.</strong> Identical bytes
/// produce an identical key, so deduplication falls out of the naming scheme rather than depending
/// on a check; and no key contains the folder an item is filed in, so moving an item in the library
/// does not change where its bytes live or invalidate a single URL already handed out
/// (spec section 13.1).
/// </para>
/// </remarks>
public static class MediaStorageKeys
{
    /// <summary>Prefix under which uploaded originals are stored.</summary>
    public const string OriginalsPrefix = "originals";

    /// <summary>Prefix under which generated renditions are stored.</summary>
    /// <remarks>
    /// Separate from the originals so that "delete every derived byte and let them regenerate" is a
    /// prefix operation. Renditions are regenerable and are not backed up; originals are neither.
    /// </remarks>
    public const string RenditionsPrefix = "renditions";

    /// <summary>Prefix under which uploads held back by the malware scanner are stored.</summary>
    /// <remarks>
    /// Quarantined bytes are kept, not discarded: an operator has to be able to look at what was
    /// rejected, and a scanner false positive would otherwise mean the file is simply gone. Nothing
    /// serves from this prefix — no <c>MediaItem</c> row points into it (spec section 13.3 step 6).
    /// </remarks>
    public const string QuarantinePrefix = "quarantine";

    /// <summary>
    /// Prefix under which the parts of an in-progress chunked upload are staged (task P5-08).
    /// </summary>
    /// <remarks>
    /// Nothing under here is a media item. It is the only prefix whose contents are <em>not</em>
    /// content-addressed — the bytes are incomplete, so their hash is not yet knowable — which is
    /// exactly why it is a prefix of its own: "everything still arriving" is then one prefix to sweep
    /// when a session is abandoned or expires, and nothing that serves bytes ever reads from it.
    /// </remarks>
    public const string IncomingPrefix = "incoming";

    /// <summary>
    /// Longest a key may be, matching the column that stores it.
    /// </summary>
    public const int MaxLength = FieldLengths.StorageKey;

    /// <summary>
    /// Builds the key an original's bytes are stored under.
    /// </summary>
    /// <param name="sha256">SHA-256 of the bytes.</param>
    /// <param name="extension">Lower-case extension including the dot, such as <c>.jpg</c>.</param>
    /// <returns>A content-addressed key, such as <c>originals/3f/9a/3f9a…c1.jpg</c>.</returns>
    /// <remarks>
    /// The two leading fan-out directories come from the hash itself. They exist for the filesystem
    /// store: a flat directory holding a hundred thousand files is slow to enumerate on every common
    /// filesystem and unpleasant to look at on all of them. Blob storage does not care, but one key
    /// shape across both stores means an object can be copied between them unchanged.
    /// </remarks>
    /// <example>
    /// <code>
    /// var key = MediaStorageKeys.ForOriginal(SHA256.HashData(bytes), ".jpg");
    /// </code>
    /// </example>
    public static string ForOriginal(byte[] sha256, string extension)
    {
        ArgumentNullException.ThrowIfNull(sha256);

        var hex = Convert.ToHexStringLower(sha256);

        return $"{OriginalsPrefix}/{hex[..2]}/{hex[2..4]}/{hex}{NormalizeExtension(extension)}";
    }

    /// <summary>
    /// Builds the key a generated rendition is stored under.
    /// </summary>
    /// <param name="mediaItemId">Item the rendition derives from.</param>
    /// <param name="specHash">SHA-256 of the normalized rendition spec.</param>
    /// <param name="format">Output format token, such as <c>webp</c>.</param>
    /// <returns>A key such as <c>renditions/812/9c4b….webp</c>.</returns>
    /// <remarks>
    /// Grouped by item rather than by hash so that permanently deleting one item is a single prefix
    /// delete. The spec hash already carries the edits version, so a library edit produces new keys
    /// rather than overwriting the renditions the previous edit left behind — those are unreachable
    /// the moment the version moves, and are swept later.
    /// </remarks>
    public static string ForRendition(int mediaItemId, byte[] specHash, string format)
    {
        ArgumentNullException.ThrowIfNull(specHash);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mediaItemId);

        return $"{RenditionsPrefix}/{mediaItemId}/{Convert.ToHexStringLower(specHash)}.{NormalizeFormat(format)}";
    }

    /// <summary>
    /// Builds the key a quarantined upload is held under.
    /// </summary>
    /// <param name="sha256">SHA-256 of the rejected bytes.</param>
    /// <returns>A key under <see cref="QuarantinePrefix"/>.</returns>
    /// <remarks>
    /// No extension, deliberately. A quarantined file is one something believes to be hostile, and
    /// the one thing that must never happen to it is being handed to a program that opens it by
    /// extension — including an operator's own file manager.
    /// </remarks>
    public static string ForQuarantine(byte[] sha256)
    {
        ArgumentNullException.ThrowIfNull(sha256);

        var hex = Convert.ToHexStringLower(sha256);

        return $"{QuarantinePrefix}/{hex[..2]}/{hex}";
    }

    /// <summary>
    /// Builds the key one part of an in-progress chunked upload is staged under.
    /// </summary>
    /// <param name="uploadId">The session, as a 32-character lower-case hexadecimal identity.</param>
    /// <param name="chunkIndex">Zero-based position of the part within the file.</param>
    /// <returns>A key such as <c>incoming/3f9a…/000007.part</c>.</returns>
    /// <remarks>
    /// The index is zero-padded so the parts sort in the order they must be concatenated in — which
    /// matters on the day somebody has to reassemble a stalled upload by hand, and costs nothing on
    /// every other day. The <c>.part</c> extension is there so that nothing, human or otherwise,
    /// mistakes a fragment for a file.
    /// </remarks>
    public static string ForUploadChunk(string uploadId, int chunkIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunkIndex);

        return $"{IncomingPrefix}/{NormalizeUploadId(uploadId)}/{chunkIndex:D6}.part";
    }

    /// <summary>
    /// Builds the key an in-progress chunked upload's manifest is stored under.
    /// </summary>
    /// <param name="uploadId">The session, as a 32-character lower-case hexadecimal identity.</param>
    /// <returns>A key such as <c>incoming/3f9a…/session.json</c>.</returns>
    /// <remarks>
    /// <strong>Session state lives in the store rather than in a table, and that is a deliberate
    /// trade.</strong> A resumable upload has to survive the client reconnecting to a different
    /// instance, which rules out memory; a table would do it too, but an in-progress upload is
    /// transient data with the same lifetime as the fragments it describes, so keeping the two
    /// together means abandoning a session is one prefix delete rather than a row and a sweep that
    /// could disagree with each other.
    /// </remarks>
    public static string ForUploadManifest(string uploadId) =>
        $"{IncomingPrefix}/{NormalizeUploadId(uploadId)}/session.json";

    /// <summary>
    /// Reports whether a string is an upload identity this application issued.
    /// </summary>
    /// <param name="uploadId">The candidate identity.</param>
    /// <returns><see langword="true"/> when it is 32 lower-case hexadecimal characters.</returns>
    /// <remarks>
    /// Checked before the identity reaches a key, because an upload id is the one part of a media
    /// key that arrives from a client. It is a GUID this server generated, so the shape is exact and
    /// anything else is refused rather than sanitized.
    /// </remarks>
    public static bool IsValidUploadId([NotNullWhen(true)] string? uploadId) =>
        uploadId is { Length: 32 } && uploadId.All(character =>
            character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static string NormalizeUploadId(string uploadId)
    {
        if (!IsValidUploadId(uploadId))
        {
            throw new ArgumentException(
                "Upload identities are server-generated 32-character hexadecimal strings.",
                nameof(uploadId));
        }

        return uploadId;
    }

    /// <summary>
    /// Reports whether a key has the shape this application generates.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns><see langword="true"/> when the key is safe to hand to a store.</returns>
    /// <remarks>
    /// An allowlist, not a blocklist of dangerous sequences. Blocklists here are the classic
    /// failure: <c>..</c> can arrive percent-encoded, doubly encoded, as an overlong UTF-8 sequence,
    /// or spelled with a backslash on Windows, and each of those is a separate rule to remember.
    /// Requiring every segment to be lower-case alphanumerics, dots, dashes, and underscores — and
    /// refusing a segment that is entirely dots — leaves nothing for any of those spellings to
    /// become.
    /// </remarks>
    public static bool IsValid([NotNullWhen(true)] string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxLength) return false;

        // A rooted or trailing-slash key would produce an empty segment, and an empty segment
        // resolves to the parent directory on some paths and to nothing on others.
        if (key[0] is '/' || key[^1] is '/') return false;

        foreach (var segment in key.AsSpan().Split('/'))
        {
            if (!IsValidSegment(key.AsSpan()[segment])) return false;
        }

        return true;
    }

    /// <summary>
    /// Throws unless a key has the shape this application generates.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="parameterName">Name of the argument being validated.</param>
    /// <exception cref="ArgumentException">The key is not one this application could have generated.</exception>
    /// <remarks>
    /// Stores call this on every operation rather than trusting their caller. The keys they are
    /// handed come from the database, and "the database only ever holds keys we generated" is an
    /// invariant this check is what maintains — not one it is entitled to assume.
    /// </remarks>
    public static void Validate([NotNull] string? key, string parameterName = "key")
    {
        if (!IsValid(key))
        {
            throw new ArgumentException(
                "Storage keys are server-generated and must consist of lower-case path segments.",
                parameterName);
        }
    }

    private static bool IsValidSegment(ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty) return false;

        var allDots = true;

        foreach (var character in segment)
        {
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not ('.' or '-' or '_'))
            {
                return false;
            }

            if (character is not '.') allDots = false;
        }

        // "." and ".." are the two segments that mean something other than a name.
        return !allDots;
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        var normalized = extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";

        if (!IsValidSegment(normalized.AsSpan(1)))
        {
            throw new ArgumentException("Extension must be alphanumeric.", nameof(extension));
        }

        return normalized;
    }

    private static string NormalizeFormat(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        var normalized = format.ToLowerInvariant();

        if (!IsValidSegment(normalized))
        {
            throw new ArgumentException("Format must be alphanumeric.", nameof(format));
        }

        return normalized;
    }
}
