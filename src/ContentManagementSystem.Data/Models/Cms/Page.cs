using ContentManagementSystem.Data.Interfaces;

namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A node in the content tree: an address, a template, and a pair of pointers into its own version
/// history.
/// </summary>
/// <remarks>
/// The page row carries no content. Everything an editor types lives on a <see cref="PageVersion"/>,
/// and the page holds only <see cref="DraftVersionId"/> and <see cref="PublishedVersionId"/> — which
/// is precisely what makes the requirement's central promise mechanical rather than careful.
/// Anonymous delivery reads <see cref="PublishedVersionId"/> and has no way to reach the draft
/// (spec section 11.1).
/// </remarks>
public class Page : FingerPrintEntityBase, ISoftDeletable
{
    /// <summary>Parent node, or null for a page at the root of the site.</summary>
    /// <remarks>
    /// The site root is synthetic: it is the absence of a parent rather than a row, so there is no
    /// node that has to exist before the first page can be created.
    /// </remarks>
    public int? ParentId { get; set; }

    /// <summary>Parent node, or null for a page at the root of the site.</summary>
    public Page? Parent { get; set; }

    /// <summary>Direct children, ordered by <see cref="SortOrder"/>.</summary>
    public ICollection<Page> Children { get; set; } = [];

    /// <summary>
    /// Stable external identifier, safe to expose outside the database.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="EntityBase.Id"/>, which is a sequential integer and therefore tells
    /// an outsider how many pages the site has and lets them walk the neighbours. Content payloads
    /// still store the integer id — they are internal, and a join on a GUID costs more than the
    /// enumeration risk saves inside the database.
    /// </remarks>
    public Guid PublicId { get; set; }

    /// <summary>Single URL path segment this page contributes to its descendants' URLs.</summary>
    public string Slug { get; set; } = null!;

    /// <summary>Whether the page's URL is stated outright rather than built from the tree.</summary>
    /// <remarks>
    /// The escape hatch for the requirement that an editor may dictate a URL (spec section 10.2).
    /// It is opt-in so that the tree stays the sane default: a page that opts out no longer moves
    /// with its ancestors.
    /// </remarks>
    public bool UseExplicitUrl { get; set; }

    /// <summary>Full site-relative URL when <see cref="UseExplicitUrl"/> is set.</summary>
    public string? ExplicitUrl { get; set; }

    /// <summary>
    /// Materialized ancestor path including this page, such as <c>/1/8/44/</c>.
    /// </summary>
    /// <remarks>
    /// Denormalised so that "every descendant of this page" — asked constantly by the tree UI,
    /// permission inheritance, and any move — is one indexed prefix match rather than a recursive
    /// CTE (spec section 10.1). <c>PageTreeService</c> owns it; nothing else may write it, because a
    /// path that disagrees with <see cref="ParentId"/> is a subtree that silently stops being found.
    /// </remarks>
    public string Path { get; set; } = null!;

    /// <summary>Number of ancestors above this page. Zero for a page at the root.</summary>
    public int Depth { get; set; }

    /// <summary>Order among siblings.</summary>
    public int SortOrder { get; set; }

    /// <summary>Template that lays out this page.</summary>
    public int TemplateId { get; set; }

    /// <summary>Template that lays out this page.</summary>
    public Template Template { get; set; } = null!;

    /// <summary>
    /// The single mutable working version. Null only in the instant between inserting the page and
    /// inserting its first draft, inside the creating transaction.
    /// </summary>
    /// <remarks>
    /// The column is nullable because <see cref="Page"/> and <see cref="PageVersion"/> reference
    /// each other: neither row can be inserted with the other's key already in place, so the draft
    /// pointer is set by a second statement in the same transaction (spec section 23.5). A page
    /// observed outside that transaction always has one.
    /// </remarks>
    public int? DraftVersionId { get; set; }

    /// <summary>The single mutable working version.</summary>
    public PageVersion? DraftVersion { get; set; }

    /// <summary>The immutable version served publicly, or null until the page is first published.</summary>
    public int? PublishedVersionId { get; set; }

    /// <summary>The immutable version served publicly.</summary>
    public PageVersion? PublishedVersion { get; set; }

    /// <summary>Every version of this page, draft and archived alike.</summary>
    public ICollection<PageVersion> Versions { get; set; } = [];

    /// <summary>Whether generated navigation menus include this page.</summary>
    public bool ShowInNavigation { get; set; } = true;

    /// <summary>Editor accountable for keeping this page current. Null when nobody owns it.</summary>
    public int? OwnerUserId { get; set; }

    /// <summary>Editor accountable for keeping this page current.</summary>
    public User? Owner { get; set; }

    /// <summary>Date the content is due a review. Stored as a plain date: nobody reviews at 09:00.</summary>
    public DateOnly? ReviewByDate { get; set; }

    /// <summary>Note to other editors. Never rendered publicly.</summary>
    public string? InternalNotes { get; set; }

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
