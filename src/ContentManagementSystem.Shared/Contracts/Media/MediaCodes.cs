namespace ContentManagementSystem.Shared.Contracts.Media;

/// <summary>
/// Stable diagnostic codes returned by the media API (spec sections 13 and 22.2).
/// </summary>
/// <remarks>
/// Its own vocabulary, for the reason the page and reusable-content vocabularies are separate: a
/// code is what a client switches on to decide which remedy to offer, and the remedies here are
/// particular to files. "The bytes are not what the extension says" is answered by re-exporting the
/// file, not by renaming it — which is exactly what a client shown a generic validation failure
/// would tell the editor to try.
/// <para>
/// The rejection codes are deliberately specific about <em>what</em> was wrong and deliberately
/// vague about how it was detected. An uploader learning which byte offset a sniffer disagreed at
/// is being handed the feedback loop for defeating it.
/// </para>
/// <para>
/// Codes do not change once shipped; the wording beside them may be rewritten freely.
/// </para>
/// </remarks>
public static class MediaCodes
{
    /// <summary>The media item, folder, or rendition addressed does not exist.</summary>
    public const string NotFound = "media.not-found";

    /// <summary>The caller is authenticated but holds no role permitting this.</summary>
    public const string Forbidden = "media.forbidden";

    /// <summary>No file was supplied, or the supplied file was empty.</summary>
    public const string FileMissing = "media.file-missing";

    /// <summary>The upload is larger than the configured limit for its kind.</summary>
    public const string TooLarge = "media.too-large";

    /// <summary>The file's extension is not on the allowlist.</summary>
    public const string ExtensionNotAllowed = "media.extension-not-allowed";

    /// <summary>
    /// The bytes are not the kind of file the name and declared type claim.
    /// </summary>
    /// <remarks>
    /// The type-confusion rejection: an HTML document renamed <c>.jpg</c>, a script with a JPEG
    /// header bolted on, a declared <c>image/png</c> whose bytes are a PDF. All of them are one code
    /// because they are one problem — what the file <em>is</em> disagrees with what it says it is
    /// (spec section 13.3 step 3).
    /// </remarks>
    public const string TypeMismatch = "media.type-mismatch";

    /// <summary>
    /// The image's pixel dimensions exceed the decode-bomb limit.
    /// </summary>
    /// <remarks>
    /// Raised from the header, before any pixels are decoded. A 64,000 × 64,000 PNG is a few hundred
    /// kilobytes compressed and sixteen gigabytes decoded; discovering that during the decode means
    /// the process is already gone (spec section 13.3 step 4).
    /// </remarks>
    public const string DimensionsTooLarge = "media.dimensions-too-large";

    /// <summary>The image header is missing, truncated, or internally inconsistent.</summary>
    public const string Undecodable = "media.undecodable";

    /// <summary>SVG upload was refused by the deployment's SVG policy.</summary>
    /// <remarks>
    /// Distinct from <see cref="ExtensionNotAllowed"/> so the message can say the true thing: the
    /// file is a valid SVG and this site does not take them, rather than implying a rename would
    /// help (spec section 13.3 step 5).
    /// </remarks>
    public const string SvgNotAllowed = "media.svg-not-allowed";

    /// <summary>The SVG contained markup that the strict profile removes and could not be salvaged.</summary>
    public const string SvgUnsafe = "media.svg-unsafe";

    /// <summary>The malware scanner flagged the upload; the bytes are quarantined.</summary>
    public const string MalwareDetected = "media.malware-detected";

    /// <summary>AVIF is not accepted in v1.</summary>
    /// <remarks>
    /// Its own code because it is a capability statement rather than a safety judgement: the image
    /// library cannot re-encode AVIF, so an accepted upload would be an item whose renditions could
    /// never be generated (spec section 13.9.1).
    /// </remarks>
    public const string AvifNotSupported = "media.avif-not-supported";

    /// <summary>An image was uploaded with neither alternative text nor a decorative flag.</summary>
    public const string AltTextRequired = "media.alt-text-required";

    /// <summary>A supplied value is longer than the column that stores it.</summary>
    public const string TooLong = "media.too-long";

    /// <summary>The rendition request is not signed, or the signature does not match.</summary>
    public const string SignatureInvalid = "media.signature-invalid";

    /// <summary>The requested rendition size, mode, or format is not one the site offers.</summary>
    public const string RenditionNotAllowed = "media.rendition-not-allowed";

    /// <summary>Permanent deletion was refused because content still references the item.</summary>
    public const string StillReferenced = "media.still-referenced";

    /// <summary>The edit document is not one of the operations spec section 13.4 defines.</summary>
    public const string EditsInvalid = "media.edits-invalid";

    /// <summary>Geometry was applied to something that has no pixels.</summary>
    /// <remarks>
    /// Its own code rather than <see cref="EditsInvalid"/>: the document was perfectly well formed
    /// and the remedy is not to fix it but to stop pointing it at a PDF.
    /// </remarks>
    public const string NotAnImage = "media.not-an-image";

    /// <summary>
    /// The bytes offered are already in the library under a different item.
    /// </summary>
    /// <remarks>
    /// Raised by replace rather than by upload, and the asymmetry is the point: an upload of known
    /// bytes is answered with the existing item, which is what the editor wanted anyway. A
    /// <em>replace</em> with known bytes would merge two identities into one and silently redirect
    /// every page pointing at the loser, so it is refused with the id of the item that already
    /// holds them (spec section 13.1).
    /// </remarks>
    public const string Duplicate = "media.duplicate";

    /// <summary>The item is not in the recycle bin, so there is nothing to restore.</summary>
    public const string NotDeleted = "media.not-deleted";

    /// <summary>A folder was created or renamed with no name at all.</summary>
    public const string NameRequired = "media.name-required";

    /// <summary>A folder still holds items or child folders and was not deleted.</summary>
    public const string FolderNotEmpty = "media.folder-not-empty";

    /// <summary>The folder move names a parent that does not exist, or one beneath the folder itself.</summary>
    public const string FolderInvalidParent = "media.folder-invalid-parent";

    /// <summary>The save lost a concurrency race and was refused rather than overwriting the winner.</summary>
    public const string Conflict = "media.conflict";

    /// <summary>The resumable upload session named does not exist, or has expired and been swept.</summary>
    /// <remarks>
    /// Distinct from <see cref="NotFound"/> so a client can tell "that file is gone" from "your
    /// upload timed out"; the remedy for the second is to start a new session and re-send, which is
    /// something an uploader can do without a person being involved (task P5-08).
    /// </remarks>
    public const string UploadSessionNotFound = "media.upload-session-not-found";

    /// <summary>
    /// The part offered is not the one the session expects next.
    /// </summary>
    /// <remarks>
    /// The response carries the expected index, so the correct client behaviour is to seek there and
    /// continue rather than to fail — which is what makes an interrupted upload resumable rather
    /// than merely restartable.
    /// </remarks>
    public const string UploadChunkOutOfOrder = "media.upload-chunk-out-of-order";

    /// <summary>The parts received do not add up to the size the session was opened for.</summary>
    public const string UploadIncomplete = "media.upload-incomplete";
}
