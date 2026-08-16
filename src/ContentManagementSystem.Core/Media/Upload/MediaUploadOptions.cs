namespace ContentManagementSystem.Core.Media.Upload;

/// <summary>
/// What a deployment does with an uploaded SVG (spec section 13.3 step 5, open question Q7).
/// </summary>
/// <remarks>
/// SVG is an XML document that browsers execute: it carries <c>&lt;script&gt;</c>,
/// <c>&lt;foreignObject&gt;</c>, event-handler attributes, and external references, and it is served
/// from the site's own origin. That makes it the one image format where "accept it" is a security
/// decision rather than a storage one, which is why it is configuration with a refusing default
/// rather than a behaviour baked in.
/// </remarks>
public enum SvgUploadPolicy
{
    /// <summary>
    /// Refuse SVG uploads outright. The default, and the recommended answer to Q7.
    /// </summary>
    /// <remarks>
    /// Sanitizing SVG is possible and this codebase does it; refusing is still better where nothing
    /// needs it. A sanitizer is a parser agreeing with the browser's parser about a large, actively
    /// exploited grammar, and the failure mode when they disagree is stored cross-site scripting.
    /// A site that uploads no SVGs has no such disagreement to lose.
    /// </remarks>
    Reject = 0,

    /// <summary>
    /// Accept SVGs, rewritten to the strict profile — no scripts, no external references, no event
    /// handlers, no <c>foreignObject</c>.
    /// </summary>
    /// <remarks>
    /// The stored bytes are the sanitizer's output, not the upload. Keeping the original and
    /// sanitizing on the way out would mean one missed render path serves the raw file.
    /// </remarks>
    Sanitize = 1,
}

/// <summary>
/// The limits and policies the upload pipeline enforces (tasks P5-05 to P5-07,
/// spec section 13.3).
/// </summary>
/// <remarks>
/// Every value here is a refusal threshold, so each default is chosen to be the one that fails
/// safely: a site that never configures this rejects SVGs, caps uploads at sizes a CMS has no reason
/// to exceed, and refuses images too large to decode. Widening any of them is a deliberate act with
/// a visible line of configuration behind it.
/// </remarks>
public sealed class MediaUploadOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Cms:MediaUpload";

    /// <summary>Default ceiling on an uploaded image, in bytes (spec section 13.3 step 1).</summary>
    public const long DefaultMaxImageBytes = 25L * 1024 * 1024;

    /// <summary>Default ceiling on an uploaded document or video, in bytes.</summary>
    public const long DefaultMaxDocumentBytes = 50L * 1024 * 1024;

    /// <summary>Default ceiling on an image's pixel count (spec section 13.3 step 4).</summary>
    /// <remarks>
    /// 100 megapixels, well above any camera an editor will use and far below what it takes to
    /// exhaust a server. The limit is on pixels rather than on file size because those are barely
    /// related: a 64,000 × 64,000 single-colour PNG compresses to a few hundred kilobytes and
    /// decodes to sixteen gigabytes.
    /// </remarks>
    public const long DefaultMaxPixels = 100L * 1000 * 1000;

    /// <summary>Largest image upload accepted, in bytes.</summary>
    public long MaxImageBytes { get; set; } = DefaultMaxImageBytes;

    /// <summary>Largest document or video upload accepted, in bytes.</summary>
    public long MaxDocumentBytes { get; set; } = DefaultMaxDocumentBytes;

    /// <summary>
    /// Largest image accepted, as width × height.
    /// </summary>
    /// <remarks>
    /// Checked against the header before a single pixel is decoded. Checking after the decode is not
    /// a check — it is a report of what already happened to the process.
    /// </remarks>
    public long MaxPixels { get; set; } = DefaultMaxPixels;

    /// <summary>Default part size a resumable upload is cut into, in bytes.</summary>
    /// <remarks>
    /// 4 MB. Small enough that losing a part to a dropped connection costs a second or two on a slow
    /// link, large enough that a 50 MB video is a dozen requests rather than hundreds — and each part
    /// is buffered in memory on the way in, so this is also the per-request memory cost.
    /// </remarks>
    public const int DefaultChunkBytes = 4 * 1024 * 1024;

    /// <summary>What to do with an uploaded SVG.</summary>
    public SvgUploadPolicy SvgPolicy { get; set; } = SvgUploadPolicy.Reject;

    /// <summary>Part size a resumable upload is cut into, in bytes (task P5-08).</summary>
    /// <remarks>
    /// The server chooses it and tells the client, rather than accepting whatever size arrives. A
    /// client-chosen part size is a client-chosen per-request memory allocation on the server, which
    /// is the same denial of service the rendition signature exists to prevent, one endpoint over.
    /// </remarks>
    public int ChunkBytes { get; set; } = DefaultChunkBytes;

    /// <summary>
    /// How long an untouched resumable upload survives before its fragments are discarded.
    /// </summary>
    /// <remarks>
    /// Long enough to outlast a laptop lid closing over lunch, short enough that an abandoned
    /// session is not still occupying storage next week. Sessions that expire leave nothing behind
    /// but bytes under the <c>incoming</c> prefix, which is why that prefix holds nothing else.
    /// </remarks>
    public TimeSpan UploadSessionLifetime { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Whether an image must arrive with alternative text or a decorative flag.
    /// </summary>
    /// <remarks>
    /// Enforced at upload as well as at publish. Both, because the two catch different things: the
    /// publish check is the one that cannot be skipped, and this one is the one that asks while the
    /// person who chose the image is still looking at it (spec section 13.7).
    /// </remarks>
    public bool RequireAltTextOnUpload { get; set; } = true;

    /// <summary>
    /// The ceiling that applies to a file of a given kind.
    /// </summary>
    /// <param name="descriptor">The allowlist entry the upload matched.</param>
    /// <returns>The largest number of bytes accepted for it.</returns>
    public long MaxBytesFor(MediaTypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return descriptor.Kind is Data.Models.Cms.MediaKind.Image ? MaxImageBytes : MaxDocumentBytes;
    }
}
