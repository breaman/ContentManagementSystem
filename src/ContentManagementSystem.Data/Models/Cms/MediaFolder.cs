using ContentManagementSystem.Data.Interfaces;

namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A node in the media library's organizing tree (spec section 23.3).
/// </summary>
/// <remarks>
/// Organization only. A folder is not part of any address and not part of any storage key — an item
/// keeps its <see cref="MediaItem.StorageKey"/>, and therefore every rendition URL already handed
/// out, when an editor drags it into a different folder. Moving an item that turned out to be
/// filed wrongly must not break the pages showing it, which is exactly what would happen if the
/// folder path were folded into the key the way a filesystem would.
/// <para>
/// The tree is the same shape as the content tree — a nullable parent, a materialized
/// <see cref="Path"/>, and a sort order — so "everything under this folder" is one indexed prefix
/// match rather than a recursive query (spec section 10.1 gives the reasoning in full).
/// </para>
/// </remarks>
public class MediaFolder : FingerPrintEntityBase, ISoftDeletable
{
    /// <summary>Parent folder, or null for a folder at the root of the library.</summary>
    /// <remarks>
    /// The library root is synthetic, as the site root is: it is the absence of a parent rather than
    /// a row, so an empty library needs no seeded folder before the first upload can land.
    /// </remarks>
    public int? ParentId { get; set; }

    /// <summary>Parent folder, or null for a folder at the root of the library.</summary>
    public MediaFolder? Parent { get; set; }

    /// <summary>Direct child folders, ordered by <see cref="SortOrder"/>.</summary>
    public ICollection<MediaFolder> Children { get; set; } = [];

    /// <summary>Editor-facing folder name.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Materialized ancestor path including this folder, such as <c>/1/8/44/</c>.</summary>
    /// <remarks>
    /// Ids rather than names, for the reason <see cref="Page.Path"/> gives: a rename would otherwise
    /// have to rewrite every descendant's path, and any row missed by that rewrite silently stops
    /// being found by the subtree query.
    /// </remarks>
    public string Path { get; set; } = null!;

    /// <summary>Order among sibling folders.</summary>
    public int SortOrder { get; set; }

    /// <summary>Items filed directly in this folder.</summary>
    public ICollection<MediaItem> Items { get; set; } = [];

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedOn { get; set; }

    /// <inheritdoc />
    public int? DeletedBy { get; set; }
}
