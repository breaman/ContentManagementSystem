namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One state of a page's content and metadata: the draft an editor is working on, the version the
/// public sees, or a frozen record of one that used to be.
/// </summary>
/// <remarks>
/// Publishing <em>snapshots</em> the draft into a new row rather than promoting the draft row
/// itself (spec section 11.2). That is the whole mechanism behind the requirement: the draft
/// survives a publish and stays editable, and no edit to it can reach the row delivery is reading.
/// <para>
/// Only a <see cref="PageVersionStatus.Draft"/> row is ever updated after insert. Every other status
/// is frozen by the service layer, because a version whose content can change is not a version.
/// </para>
/// </remarks>
public class PageVersion : FingerPrintEntityBase
{
    /// <summary>Page this version belongs to.</summary>
    public int PageId { get; set; }

    /// <summary>Page this version belongs to.</summary>
    public Page Page { get; set; } = null!;

    /// <summary>Monotonically increasing number, starting at 1, unique within the page.</summary>
    public int VersionNumber { get; set; }

    /// <summary>Where this version sits in the editorial lifecycle.</summary>
    public PageVersionStatus Status { get; set; }

    /// <summary>
    /// Editor-supplied name for a checkpoint, such as "before the big rewrite". Null for the
    /// ordinary versions publishing produces.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>Page title as at this version. Lives here, not on the page, so it is versioned.</summary>
    public string Title { get; set; } = null!;

    /// <summary>
    /// The content payload (spec section 6.2), serialised whole.
    /// </summary>
    /// <remarks>
    /// Written and read as a document and never queried into. The relational answers a payload
    /// cannot give — where-used, link integrity, cache invalidation — come from the
    /// <see cref="ContentReference"/> rows projected out of it on every save.
    /// </remarks>
    public string ContentJson { get; set; } = null!;

    /// <summary>Template this version's payload was authored against.</summary>
    public int TemplateId { get; set; }

    /// <summary>
    /// Template revision this version's payload was authored against.
    /// </summary>
    /// <remarks>
    /// Captured rather than looked up, which is what makes template evolution safe: the version
    /// validates and renders against the structure as it stood, so a later structural change cannot
    /// retroactively invalidate content that is already live (spec section 8.5). The payload carries
    /// the same pair as key and number; these columns are the relational copy, so "which versions
    /// use this template" is an indexed query rather than a scan over JSON.
    /// </remarks>
    public int TemplateRevision { get; set; }

    /// <summary>Overrides the <c>&lt;title&gt;</c> element. Falls back to <see cref="Title"/>.</summary>
    public string? MetaTitle { get; set; }

    /// <summary>Meta description rendered into the page head.</summary>
    public string? MetaDescription { get; set; }

    /// <summary>Explicit canonical URL, for pages reachable at more than one address.</summary>
    public string? CanonicalUrl { get; set; }

    /// <summary>Whether search engines may index this page.</summary>
    public bool RobotsIndex { get; set; } = true;

    /// <summary>Whether search engines may follow links out of this page.</summary>
    public bool RobotsFollow { get; set; } = true;

    /// <summary>Open Graph title. Falls back to <see cref="MetaTitle"/>, then <see cref="Title"/>.</summary>
    public string? OgTitle { get; set; }

    /// <summary>Open Graph description. Falls back to <see cref="MetaDescription"/>.</summary>
    public string? OgDescription { get; set; }

    /// <summary>
    /// Media item used as the Open Graph image. Untyped <c>int</c> until <c>MediaItem</c> arrives in
    /// Phase 5, when the foreign key is added.
    /// </summary>
    public int? OgImageMediaId { get; set; }

    /// <summary>Open Graph object type, such as <c>article</c>.</summary>
    public string? OgType { get; set; }

    /// <summary>Twitter card type, such as <c>summary_large_image</c>.</summary>
    public string? TwitterCard { get; set; }

    /// <summary>Hand-authored JSON-LD emitted into the page head.</summary>
    public string? StructuredDataJson { get; set; }

    /// <summary>Sitemap change-frequency hint, such as <c>weekly</c>.</summary>
    public string? ChangeFreq { get; set; }

    /// <summary>Sitemap priority between 0.0 and 1.0.</summary>
    public decimal? Priority { get; set; }

    /// <summary>
    /// When this version should go live. Null publishes on request rather than on a schedule.
    /// </summary>
    /// <remarks>
    /// Stored UTC. The editor presents and accepts the site's configured time zone with the offset
    /// shown, because "publish at 9am" across a DST transition is a real support ticket
    /// (spec section 11.6).
    /// </remarks>
    public DateTimeOffset? PublishOn { get; set; }

    /// <summary>When this version should stop being served. Null leaves it live indefinitely.</summary>
    public DateTimeOffset? UnpublishOn { get; set; }

    /// <summary>When this version actually went live. Null until it does.</summary>
    public DateTimeOffset? PublishedOn { get; set; }

    /// <summary>Identity of the user who published it.</summary>
    public int? PublishedBy { get; set; }

    /// <summary>
    /// Concurrency token. Two editors saving the same draft is the ordinary case, and the second
    /// save fails rather than silently discarding the first (spec section 11.8).
    /// </summary>
    public byte[] RowVersion { get; set; } = null!;
}
