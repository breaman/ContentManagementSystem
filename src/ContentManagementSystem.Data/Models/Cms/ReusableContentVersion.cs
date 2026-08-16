namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One state of a reusable item's content: the draft an editor is working on, the version every
/// late-bound page currently shows, or a frozen record of one that used to be (spec section 9.1).
/// </summary>
/// <remarks>
/// The lifecycle is <see cref="PageVersion"/>'s, verbatim and deliberately so. Publishing snapshots
/// the draft into a new immutable row rather than promoting the draft row itself, which is what lets
/// an editor keep working on next month's banner while this month's stays byte-for-byte as it was on
/// every page showing it (spec section 11.2).
/// <para>
/// <strong>What it does not carry is as important as what it does.</strong> There is no title, no
/// URL, no SEO metadata and no template: a reusable item is not a document, it is a fragment placed
/// inside one, and the page that places it owns every one of those. Its shape comes instead from the
/// block type named on <see cref="ReusableContent.BlockTypeId"/>, and
/// <see cref="BlockTypeRevision"/> captures which revision of that shape this version was authored
/// against — the same rule, for the same reason, as <see cref="PageVersion.TemplateRevision"/>.
/// </para>
/// </remarks>
public class ReusableContentVersion : FingerPrintEntityBase
{
    /// <summary>Reusable item this version belongs to.</summary>
    public int ReusableContentId { get; set; }

    /// <summary>Reusable item this version belongs to.</summary>
    public ReusableContent ReusableContent { get; set; } = null!;

    /// <summary>Monotonically increasing number, starting at 1, unique within the item.</summary>
    /// <remarks>
    /// The number a pinned placement names (spec section 9.2), which is what makes it the one part
    /// of a version an editor is ever shown. It is minted by <c>VersionNumbers</c> from the highest
    /// ever issued, so retention pruning the middle of the history cannot make an old number mean a
    /// different version later.
    /// </remarks>
    public int VersionNumber { get; set; }

    /// <summary>Where this version sits in the editorial lifecycle.</summary>
    /// <remarks>
    /// <see cref="PageVersionStatus"/> rather than an enum of its own. The states are identical —
    /// spec section 23.2 gives <c>WorkflowTask</c> a nullable key to each kind of version precisely
    /// because one approval flow serves both — and a second, numerically identical enum would be two
    /// places to keep the <c>tinyint</c> contract in step.
    /// </remarks>
    public PageVersionStatus Status { get; set; }

    /// <summary>
    /// Editor-supplied name for a checkpoint, such as "before the rebrand". Null for the ordinary
    /// versions publishing produces.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// The content payload (spec section 6.2), serialised whole.
    /// </summary>
    /// <remarks>
    /// The same envelope a page version stores, with one substitution stated outright: its
    /// <c>templateKey</c> and <c>templateRevision</c> members carry the <em>block type</em> key and
    /// revision, and its <c>zones</c> object holds the block's properties. That is not a shortcut —
    /// a zone and a block-type property are the same thing to every reader of a payload, a keyed
    /// value carrying the field type that wrote it — and it is what lets the schema walk, the
    /// reference indexer, the payload diff, and the reference remapper serve this table without a
    /// second implementation of any of them.
    /// </remarks>
    public string ContentJson { get; set; } = null!;

    /// <summary>
    /// Block type revision this version's payload was authored against.
    /// </summary>
    /// <remarks>
    /// Captured rather than looked up, which is what makes structural evolution safe: the version
    /// validates and renders against the property set as it stood, so adding a required property
    /// today cannot retroactively invalidate a banner that is already live on forty pages
    /// (spec section 8.5). The payload carries the same pair; this column is the relational copy, so
    /// "which reusable versions use this block type revision" is an indexed query rather than a scan
    /// over JSON.
    /// </remarks>
    public int BlockTypeRevision { get; set; }

    /// <summary>When this version should go live. Null publishes on request rather than on a schedule.</summary>
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
