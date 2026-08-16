using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// One page that would be affected by a change to the entity being asked about (spec section 9.4).
/// </summary>
/// <param name="Id">The page's identity.</param>
/// <param name="Title">Its title as at the version holding the reference.</param>
/// <param name="Url">
/// Its site-relative URL, or null when the page has none an editor could visit. Reported so the
/// where-used panel can link to the page rather than only naming it.
/// </param>
/// <param name="IsPublished">
/// Whether the reference is held by the page's <em>published</em> version. This is the split that
/// matters: a reference from a draft costs nothing when the target changes, and a reference from a
/// published version is a live page whose content is about to move under it.
/// </param>
/// <param name="IsPinned">
/// Whether the placement names an exact version rather than following the item (spec section 9.2).
/// A pinned page appears in this list and is <em>not</em> counted as changing.
/// </param>
/// <param name="ZoneKey">Zone the placement sits in, when the reference row recorded one.</param>
/// <param name="PropertyKey">Block property holding it, when the placement is inside a block.</param>
public sealed record AffectedPage(
    int Id,
    string Title,
    string? Url,
    bool IsPublished,
    bool IsPinned,
    string? ZoneKey = null,
    string? PropertyKey = null);

/// <summary>
/// One reusable item whose own content places the entity being asked about.
/// </summary>
/// <param name="Id">The item's identity.</param>
/// <param name="Key">Its stable key.</param>
/// <param name="Name">Its display name.</param>
/// <param name="IsPublished">Whether the placement is in the item's <em>published</em> version.</param>
/// <remarks>
/// Reusable content nests: a banner may sit inside a footer that sits on every page. Reporting these
/// alongside the pages is what makes the where-used panel honest — an item nothing places directly
/// can still be on the whole site — and it is what the delete guard needs, since removing an item
/// that another item places breaks that item rather than any page.
/// </remarks>
public sealed record AffectedReusableItem(int Id, string Key, string Name, bool IsPublished);

/// <summary>
/// What changing one entity would do to the pages that reference it — the answer to "where is this
/// used?" and the body of the publish-impact dialog (spec section 9.4).
/// </summary>
/// <param name="AffectedPages">
/// Every page holding a reference, published and draft alike, ordered by page id so two calls
/// agree. Capped: see <paramref name="IsTruncated"/>.
/// </param>
/// <param name="AffectedPageCount">
/// How many distinct <em>published</em> pages would change — late-bound placements only. This is the
/// number the confirmation dialog is required to show, and it deliberately excludes pinned pages,
/// which is what makes "40 pages will change" a true statement rather than an upper bound.
/// </param>
/// <param name="PinnedPageCount">
/// How many distinct published pages pin an exact version and will therefore <em>not</em> change.
/// Reported beside the other count because an editor asking "did my change reach everything?" needs
/// the number that did not as much as the number that did.
/// </param>
/// <param name="AffectedReusableItems">
/// Every reusable item whose own content places the entity, at any depth. Ordered by item id.
/// </param>
/// <param name="Warnings">
/// Non-blocking diagnostics about the change, such as <see cref="ReusableCodes.BlastRadius"/>.
/// </param>
/// <param name="IsTruncated">
/// Whether <paramref name="AffectedPages"/> holds fewer rows than the counts describe. The counts
/// are always exact; the list is bounded so that a footer placed on every page of a large site does
/// not return the whole site in a confirmation dialog.
/// </param>
/// <remarks>
/// One shape for pages, media, and reusable content alike, because the question is one question: the
/// rows being aggregated are <c>ContentReference</c> either way, and a second shape would mean the
/// where-used panel had to be written twice.
/// <para>
/// <strong>Reached transitively.</strong> A page that places a footer which places this banner is an
/// affected page, and reporting only direct placements would tell an editor that changing a
/// site-wide banner affects nothing.
/// </para>
/// </remarks>
public sealed record ReferenceImpact(
    IReadOnlyList<AffectedPage> AffectedPages,
    int AffectedPageCount,
    int PinnedPageCount,
    IReadOnlyList<AffectedReusableItem> AffectedReusableItems,
    IReadOnlyList<ApiDiagnostic> Warnings,
    bool IsTruncated)
{
    /// <summary>An entity nothing points at.</summary>
    public static ReferenceImpact None { get; } = new([], 0, 0, [], [], false);

    /// <summary>Whether anything at all points at the entity.</summary>
    /// <remarks>
    /// The delete guard's question, and deliberately wider than
    /// <see cref="RequiresConfirmation"/>: an item placed only in an unpublished draft is not about
    /// to change any live page, but deleting it still breaks that draft the moment it is published
    /// (spec section 9.4).
    /// </remarks>
    public bool IsReferenced => AffectedPages.Count > 0 || AffectedReusableItems.Count > 0;

    /// <summary>Whether publishing this change requires an explicit confirmation.</summary>
    /// <remarks>
    /// The UI rule of spec section 9.4 stated once, on the contract, rather than as a comparison
    /// each screen makes for itself — a screen that forgot it would publish to forty pages with no
    /// dialog at all.
    /// </remarks>
    public bool RequiresConfirmation => AffectedPageCount > 0;
}
