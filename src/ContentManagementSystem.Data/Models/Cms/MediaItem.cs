using ContentManagementSystem.Data.Interfaces;

namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One uploaded file: its immutable original bytes, the metadata an editor maintains, and the
/// non-destructive edits applied to every usage of it (spec sections 13.1 and 23.3).
/// </summary>
/// <remarks>
/// The row describes the file; it never holds the file. The bytes live in an <c>IMediaStore</c>
/// under <see cref="StorageKey"/>, which is derived from <see cref="Sha256"/> and therefore
/// content-addressed: identical bytes produce an identical key, which is what makes deduplication a
/// property of the naming scheme rather than a check that could be skipped (spec section 13.1).
/// <para>
/// <strong>The original is never rewritten.</strong> Rotating, cropping, or re-pointing the focal
/// point writes <see cref="EditsJson"/> and bumps <see cref="EditsVersion"/>; the stored bytes are
/// untouched, so revert-to-original is always available and costs nothing (spec section 13.4).
/// <see cref="EditsVersion"/> is folded into every rendition signature, so one library edit changes
/// every URL the site emits for this item and busts client and CDN caches without a purge
/// (<see href="../../../docs/adr/0007-non-destructive-media-editing-signed-renditions.md">ADR 0007</see>).
/// </para>
/// <para>
/// Metadata here is the item's own. A single placement may override the alternative text, the focal
/// point, and the crop in the page payload — those are usage-scope edits and live on the
/// <c>media</c> field value, not on this row, because they must affect one page and no other
/// (spec section 13.4).
/// </para>
/// </remarks>
public class MediaItem : FingerPrintEntityBase, ISoftDeletable
{
    /// <summary>Folder the item is filed in, or null for one at the root of the library.</summary>
    public int? FolderId { get; set; }

    /// <summary>Folder the item is filed in, or null for one at the root of the library.</summary>
    public MediaFolder? Folder { get; set; }

    /// <summary>
    /// Server-generated file name, safe to place in a URL and on a disk.
    /// </summary>
    /// <remarks>
    /// Never the name the client sent. An uploader controls that string completely, and it is the
    /// vector behind <c>../</c> traversal, a second extension, a right-to-left override that hides
    /// one, and a Windows reserved device name. The original is kept in
    /// <see cref="OriginalFileName"/> for display and for the download-original action, and is
    /// treated as untrusted text everywhere it appears (spec section 13.2).
    /// </remarks>
    public string FileName { get; set; } = null!;

    /// <summary>The file name as the client sent it, for display only.</summary>
    public string OriginalFileName { get; set; } = null!;

    /// <summary>
    /// The MIME type the bytes actually are, as determined by magic-number sniffing.
    /// </summary>
    /// <remarks>
    /// The sniffed type, never the declared one. This column is what the delivery endpoint pins
    /// <c>Content-Type</c> to, so storing what the client claimed would let an HTML file renamed
    /// <c>.jpg</c> be served back as HTML from the site's own origin (spec section 20.7).
    /// </remarks>
    public string ContentType { get; set; } = null!;

    /// <summary>Size of the stored original in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// SHA-256 of the stored original's bytes.
    /// </summary>
    /// <remarks>
    /// Carries the deduplication constraint — unique among live rows — and names the storage key.
    /// Filtered on <c>IsDeleted = 0</c> so that an item in the recycle bin does not stop the same
    /// file being uploaded again (spec section 23.3).
    /// </remarks>
    public byte[] Sha256 { get; set; } = null!;

    /// <summary>Key the original's bytes are stored under in the media store.</summary>
    public string StorageKey { get; set; } = null!;

    /// <summary>What the file is, decided from the sniffed bytes.</summary>
    public MediaKind MediaKind { get; set; }

    /// <summary>Pixel width of the original after orientation was baked in. Null for non-images.</summary>
    /// <remarks>
    /// Post-rotation, deliberately. EXIF orientation is applied to the pixels at upload, so a
    /// portrait photo a camera stored as landscape-plus-a-flag is 3000×4000 here and everywhere
    /// downstream — which is what stops the picker's <c>minWidth</c> check and every rendition
    /// calculation having to know about orientation at all (spec section 13.9.1).
    /// </remarks>
    public int? Width { get; set; }

    /// <summary>Pixel height of the original after orientation was baked in. Null for non-images.</summary>
    public int? Height { get; set; }

    /// <summary>Playing time in seconds for audio and video. Null for everything else.</summary>
    public double? DurationSeconds { get; set; }

    /// <summary>
    /// Alternative text describing the image to someone who cannot see it.
    /// </summary>
    /// <remarks>
    /// Required for an image unless <see cref="IsDecorative"/> is set — enforced at upload and again
    /// as a publish-time validation error, because a missing alt attribute discovered by an audit
    /// after launch is a hundred pages to revisit (spec section 13.7).
    /// </remarks>
    public string? AltText { get; set; }

    /// <summary>
    /// Whether the image carries no information and should render <c>alt=""</c>.
    /// </summary>
    /// <remarks>
    /// An explicit statement rather than the absence of one. A screen reader needs to be told a
    /// divider is decorative; an empty <see cref="AltText"/> alone cannot distinguish "there is
    /// nothing to say" from "nobody said anything yet" (spec section 13.7).
    /// </remarks>
    public bool IsDecorative { get; set; }

    /// <summary>Editor-facing title, shown in the library.</summary>
    public string? Title { get; set; }

    /// <summary>Caption rendered alongside the image where a template supports one.</summary>
    public string? Caption { get; set; }

    /// <summary>Attribution line — the photographer, the agency, the licence holder.</summary>
    public string? Credit { get; set; }

    /// <summary>
    /// Horizontal focal point as a fraction of the width, or null to centre.
    /// </summary>
    /// <remarks>
    /// Normalized rather than in pixels so it survives a replacement with a higher-resolution
    /// original, and so a crop to any aspect ratio can honour it (spec section 13.4).
    /// </remarks>
    public double? FocalPointX { get; set; }

    /// <summary>Vertical focal point as a fraction of the height, or null to centre.</summary>
    public double? FocalPointY { get; set; }

    /// <summary>
    /// Library-scope non-destructive edits, as a JSON document. Null when none have been made.
    /// </summary>
    /// <remarks>
    /// Rotate, flip, crop, and focal point, in the normalized form spec section 13.4 defines. These
    /// apply to every usage of the item — the editor straightens a sideways photograph once and
    /// forty pages are fixed — which is the difference between this and the identically-shaped edits
    /// a single placement stores in its page payload.
    /// </remarks>
    public string? EditsJson { get; set; }

    /// <summary>
    /// Number of times <see cref="EditsJson"/> has changed. Zero on an unedited item.
    /// </summary>
    /// <remarks>
    /// A cache generation, not a history counter. It is folded into every rendition signature, so
    /// incrementing it changes every URL this item appears at and thereby invalidates browser and
    /// CDN copies that no purge could reach. Renditions carry the value they were generated at, so a
    /// stale one is recognisable rather than merely old (ADR 0007).
    /// </remarks>
    public int EditsVersion { get; set; }

    /// <summary>Renditions derived from this item. Regenerable; not backed up.</summary>
    public ICollection<MediaRendition> Renditions { get; set; } = [];

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedOn { get; set; }

    /// <inheritdoc />
    public int? DeletedBy { get; set; }

    /// <summary>
    /// Concurrency token. Two editors saving metadata on one item cannot silently overwrite each
    /// other (spec section 11.8).
    /// </summary>
    public byte[] RowVersion { get; set; } = null!;
}
