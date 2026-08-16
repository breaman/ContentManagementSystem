using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Creates and reads pages, and edits the metadata around their content (task P2-07).
/// </summary>
/// <remarks>
/// Every method authorizes the caller itself, for the reason <c>ITemplateService</c> gives: the
/// endpoint policy is the door and this is the lock, and a service reached from a CLI verb or a
/// hosted job is subject to the same rules (spec section 20.4).
/// <para>
/// The boundary with the rest of Phase 2 is what a page <em>is</em> versus what happens to it. This
/// service owns the row and its draft's metadata; the payload belongs to <c>DraftService</c>, the
/// lifecycle to <c>PublishingService</c> and <c>RecycleBinService</c>, and the tree position to
/// <c>PageTreeService</c>. Nothing here can change a page's status, template, or parent — accepting
/// any of those as ordinary metadata is the mass-assignment hole spec section 20.1 names.
/// </para>
/// </remarks>
public interface IPageService
{
    /// <summary>
    /// Creates a page from a template, together with the draft version it starts life with.
    /// </summary>
    /// <param name="request">The page to create.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// The created page, an invalid result naming every rule the request broke, a conflict when a
    /// sibling already uses the slug, or a not-found result when the template or parent is gone.
    /// </returns>
    /// <remarks>
    /// The draft's payload is empty and schema-valid: every zone is <em>absent</em> rather than
    /// present-and-null, which is what "never authored" means, and a required zone blocks only a
    /// publish (spec section 8.3). The page and its draft are inserted in one transaction, since a
    /// page whose <c>DraftVersionId</c> was never set is a page no editor can open and no query
    /// reports as broken.
    /// </remarks>
    Task<CmsResult<PageDetail>> CreateAsync(
        CreatePageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a page's metadata and its draft payload.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The page, or a not-found result.</returns>
    Task<CmsResult<PageDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists pages matching a set of filters, one keyset page at a time.
    /// </summary>
    /// <param name="query">Filters and paging.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>
    /// The matching pages with a resume token, or an invalid result when a filter cannot be read.
    /// </returns>
    /// <remarks>
    /// Ordered by identity, which for these rows is creation order. That is not an arbitrary choice:
    /// keyset pagination needs a total order over a unique, indexed column, and the primary key is
    /// the only one Phase 2's schema offers. Ordering by "recently changed", which a browse screen
    /// would prefer, needs a composite <c>(ModifiedOn, Id)</c> keyset over a column with no index,
    /// so every page of results would scan; when a list screen earns that, the index comes with it.
    /// <para>
    /// Deleted pages are excluded by the global query filter. The recycle bin is a separate
    /// collection with a separate shape, because a list mixing live and deleted rows makes every
    /// caller re-filter it (spec section 14.10).
    /// </para>
    /// </remarks>
    Task<CmsResult<CursorPage<PageSummary>>> ListAsync(
        PageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a slice of the content tree, for a lazily expanded tree control.
    /// </summary>
    /// <param name="parentId">Node to expand, or null for the root of the site.</param>
    /// <param name="depth">
    /// How many levels below the node to return. Clamped to <see cref="MaxTreeDepth"/>, because a
    /// tree fetch is a UI convenience and an unbounded one is a request to serialize the whole site.
    /// </param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The node's children in editor order, or a not-found result.</returns>
    /// <remarks>
    /// Answered from the materialized path in one prefix match rather than a query per level, which
    /// is what <c>Page.Path</c> exists for (spec section 10.1).
    /// </remarks>
    Task<CmsResult<IReadOnlyList<PageTreeNode>>> TreeAsync(
        int? parentId,
        int depth = 1,
        CancellationToken cancellationToken = default);

    /// <summary>Deepest slice of the tree one fetch will return.</summary>
    const int MaxTreeDepth = 5;

    /// <summary>
    /// Applies a partial update to a page's title, slug, SEO, and editorial metadata.
    /// </summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="request">The members to change. Omitted members are left alone.</param>
    /// <param name="expectedRowVersion">
    /// The draft's <c>rowversion</c> as the caller last saw it, Base64-encoded — what an
    /// <c>If-Match</c> header carries. Null writes unconditionally, which is the ordinary case: a
    /// patch names the members it changes, so two concurrent patches to different fields merge
    /// correctly and only a caller that read first has a precondition worth stating.
    /// </param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The updated page, a not-found result, an invalid result, or a conflict.</returns>
    /// <remarks>
    /// Mutates the draft version in place and creates no new version row (acceptance criterion
    /// P2 #2). The published version is not touched, so a title corrected in the backoffice does not
    /// reach the public site until someone publishes.
    /// </remarks>
    Task<CmsResult<PageDetail>> PatchMetadataAsync(
        int id,
        PatchPageMetadataRequest request,
        string? expectedRowVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Repositions a page in the tree, rebuilding the URLs of everything below it (task P6-03).
    /// </summary>
    /// <param name="id">Identity of the page to move.</param>
    /// <param name="request">The new parent and position, and whether this is a preview.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// What moved and what redirects were left behind, a not-found result, or an invalid result
    /// naming the rule the move broke.
    /// </returns>
    /// <remarks>
    /// The one exception to this interface's rule that nothing here changes a page's parent. It is
    /// here rather than on <c>IPageTreeService</c> because a move is three things at once — the tree
    /// position, the sibling ordering, and every route beneath it — and only a service holding the
    /// transaction can make those one act (spec section 10.4).
    /// <para>
    /// <strong>A preview is the move.</strong> <c>request.Preview</c> runs every step and then rolls
    /// the transaction back rather than committing it, so the confirmation an editor is shown cannot
    /// disagree with what pressing the button then does. The alternative — a second implementation
    /// that computes URLs without writing them — is a second implementation to keep in step, and the
    /// first time it drifts an editor approves five redirects and gets fifty.
    /// </para>
    /// </remarks>
    Task<CmsResult<PageMoveResult>> MoveAsync(
        int id,
        MovePageRequest request,
        CancellationToken cancellationToken = default);
}
