namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Filters and paging for <c>GET /api/cms/v1/pages</c> (spec section 22.1).
/// </summary>
/// <param name="ParentId">
/// Restricts the list to one parent's direct children. Null means "anywhere in the tree", which is
/// not the same as the site root — <paramref name="RootOnly"/> asks for that.
/// </param>
/// <param name="RootOnly">
/// Restricts the list to pages at the root of the site. A separate flag because a null
/// <paramref name="ParentId"/> is already how the query says "do not filter by parent", and one
/// nullable value cannot mean both.
/// </param>
/// <param name="TemplateId">Restricts the list to pages laid out by one template.</param>
/// <param name="Status">
/// Restricts the list to pages whose draft sits at one lifecycle status. Matched against
/// <c>PageVersionStatus</c> by name, case-insensitively; an unrecognised value is refused rather
/// than silently returning everything.
/// </param>
/// <param name="Search">
/// Free-text filter over the draft's title and the page's slug. Deliberately a <c>LIKE</c> over two
/// short columns rather than a content search — full-text over payloads arrives with
/// <c>SearchDocument</c> in Phase 8, and pretending to offer it here would set an expectation the
/// index cannot yet meet.
/// </param>
/// <param name="ModifiedAfter">Restricts the list to pages whose row changed after an instant.</param>
/// <param name="Cursor">Opaque resume token from the previous page's <c>nextCursor</c>.</param>
/// <param name="Limit">How many items to return, clamped to the collection's ceiling.</param>
/// <remarks>
/// The <c>tag</c> filter spec section 22.1 lists is not here: tags are projected by the
/// <c>tags</c> field type into a table that arrives in Phase 8, and a filter that silently matched
/// nothing would read as "no pages are tagged" rather than as "tagging has not shipped".
/// </remarks>
public sealed record PageQuery(
    int? ParentId = null,
    bool RootOnly = false,
    int? TemplateId = null,
    string? Status = null,
    string? Search = null,
    DateTimeOffset? ModifiedAfter = null,
    string? Cursor = null,
    int? Limit = null);

/// <summary>
/// One node of the content tree, as <c>GET /api/cms/v1/pages/tree</c> returns it.
/// </summary>
/// <param name="Page">The page itself.</param>
/// <param name="Children">
/// Its children, as deep as the requested depth allowed. Empty both for a leaf and for a node the
/// fetch stopped at — <c>Page.HasChildren</c> is what tells the two apart, and it is why the summary
/// carries that flag at all.
/// </param>
public sealed record PageTreeNode(PageSummary Page, IReadOnlyList<PageTreeNode> Children);
