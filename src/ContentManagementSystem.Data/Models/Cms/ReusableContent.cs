using ContentManagementSystem.Data.Interfaces;

namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// Content authored once and placed on many pages — a footer, a promotional banner, a carousel
/// (spec section 9.1).
/// </summary>
/// <remarks>
/// The same shape as <see cref="Page"/> with the address removed. It carries no content of its own:
/// everything an editor types lives on a <see cref="ReusableContentVersion"/>, and this row holds
/// only <see cref="DraftVersionId"/> and <see cref="PublishedVersionId"/>. That pointer pair is what
/// makes goal G4 mechanical — a page renders whatever <see cref="PublishedVersionId"/> currently
/// names, so publishing a new version of this item changes forty pages without any of them being
/// republished (spec section 9.3).
/// <para>
/// <strong>It has no URL and is never directly addressable</strong> (spec section 9.1). There is no
/// slug, no parent, no tree, and no <c>PageRoute</c> — a reusable item reaches a visitor only as
/// part of a page that placed it, which is why none of the routing machinery of spec section 10
/// applies here.
/// </para>
/// <para>
/// Its shape is a <see cref="BlockType"/>. That is the whole reason the property model is not
/// duplicated: a reusable item is an instance of a block type exactly as a block inside a zone is,
/// including the built-in <c>rawHtml</c> type behind free-form HTML, so its properties validate and
/// render through the machinery blocks already use.
/// </para>
/// </remarks>
public class ReusableContent : FingerPrintEntityBase, ISoftDeletable
{
    /// <summary>
    /// Stable identifier an editor picks this item out by. Immutable once content references it.
    /// </summary>
    /// <remarks>
    /// Unique across the site, and a key rather than a slug: it is never part of a URL, so it exists
    /// only to be recognised in a picker and quoted in an import. Placements store the integer id,
    /// not this — renaming an item must not break the forty pages showing it (spec section 9.2).
    /// </remarks>
    public string Key { get; set; } = null!;

    /// <summary>Editor-facing display name, shown in the library and the picker.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Optional help text describing when to reach for this item.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Organizational grouping inside the reusable-content library (spec section 9.1).
    /// </summary>
    /// <remarks>
    /// Untyped <c>int</c> with no foreign key, deliberately. v1 ships no folder table for reusable
    /// content — the library is a flat, searchable list — and the column is carried from the outset
    /// so that adding the tree later is a foreign key on an existing column rather than a migration
    /// that has to backfill every row.
    /// </remarks>
    public int? FolderId { get; set; }

    /// <summary>Block type whose property set is this item's shape.</summary>
    public int BlockTypeId { get; set; }

    /// <summary>Block type whose property set is this item's shape.</summary>
    public BlockType BlockType { get; set; } = null!;

    /// <summary>
    /// The single mutable working version. Null only in the instant between inserting the item and
    /// inserting its first draft, inside the creating transaction.
    /// </summary>
    /// <remarks>
    /// Nullable for the reason <see cref="Page.DraftVersionId"/> is: this row and its version row
    /// reference each other, so neither insert can carry the other's key and the draft pointer is
    /// set by a second statement in the same transaction (spec section 23.5).
    /// </remarks>
    public int? DraftVersionId { get; set; }

    /// <summary>The single mutable working version.</summary>
    public ReusableContentVersion? DraftVersion { get; set; }

    /// <summary>
    /// The immutable version every late-bound placement renders, or null until first published.
    /// </summary>
    /// <remarks>
    /// Null is an ordinary state with a visible consequence: a placement of an unpublished item
    /// renders nothing and is logged, and the item shows up in the broken-references report
    /// (spec section 15.3).
    /// </remarks>
    public int? PublishedVersionId { get; set; }

    /// <summary>The immutable version every late-bound placement renders.</summary>
    public ReusableContentVersion? PublishedVersion { get; set; }

    /// <summary>Every version of this item, draft and archived alike.</summary>
    public ICollection<ReusableContentVersion> Versions { get; set; } = [];

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedOn { get; set; }

    /// <inheritdoc />
    public int? DeletedBy { get; set; }

    /// <summary>
    /// Concurrency token. A save that lost a race fails rather than overwriting the winner
    /// (spec section 11.8).
    /// </summary>
    public byte[] RowVersion { get; set; } = null!;
}
