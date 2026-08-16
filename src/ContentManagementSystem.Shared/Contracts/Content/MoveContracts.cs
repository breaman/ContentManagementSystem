namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Body of <c>POST /api/cms/v1/pages/{id}/move</c> (task P6-03, spec section 14.2).
/// </summary>
/// <param name="ParentId">
/// The page's new parent, or null to move it to the root of the site. Passing the parent it already
/// has is a reorder rather than a reparent, and is the ordinary case.
/// </param>
/// <param name="Position">
/// Zero-based position among its new siblings, counted with the page itself removed from the list
/// first. Null appends it after them.
/// </param>
/// <param name="Preview">
/// When true the move is worked out, its consequences reported, and then rolled back. Nothing is
/// written and nothing is redirected.
/// </param>
/// <remarks>
/// A position rather than a "before this sibling" reference. A drag produces an index — the gap the
/// pointer was let go over — and translating that to a neighbour's identity on the client means the
/// client has to know which sibling is at the boundary, which is exactly the thing that goes wrong
/// when the list has been virtualized.
/// </remarks>
public sealed record MovePageRequest(int? ParentId, int? Position = null, bool Preview = false);

/// <summary>
/// One page whose URL a move changed (task P6-03).
/// </summary>
/// <param name="PageId">Identity of the page.</param>
/// <param name="Title">Its title, so the confirmation names pages rather than numbers.</param>
/// <param name="OldUrl">The URL it served before, or null if it had none.</param>
/// <param name="NewUrl">The URL it serves now.</param>
/// <param name="RedirectCreated">
/// Whether a redirect was left behind at the old URL. False for a page that was never published —
/// there is nothing at the old address for anyone to have bookmarked.
/// </param>
public sealed record PageUrlChangeSummary(
    int PageId,
    string Title,
    string? OldUrl,
    string NewUrl,
    bool RedirectCreated);

/// <summary>
/// What a move did, or — when it was a preview — what it would do.
/// </summary>
/// <param name="PageId">The page that moved.</param>
/// <param name="ParentId">Its parent afterwards, or null for the root of the site.</param>
/// <param name="Position">Its position among its siblings afterwards.</param>
/// <param name="UrlChanges">
/// Every page whose URL moved, the subject page first. A move that changed no URL — a reorder among
/// siblings — reports an empty list, which is what tells the editor no confirmation is needed.
/// </param>
/// <param name="IsPreview">Whether this describes a move that was rolled back rather than committed.</param>
/// <remarks>
/// The same shape for both, deliberately. A preview computed by a different code path from the move
/// is a preview that can be wrong, and a confirmation dialog that lies about how many redirects it
/// is about to create is worse than no dialog at all — so the preview <em>is</em> the move, run
/// inside a transaction that is rolled back instead of committed.
/// </remarks>
public sealed record PageMoveResult(
    int PageId,
    int? ParentId,
    int Position,
    IReadOnlyList<PageUrlChangeSummary> UrlChanges,
    bool IsPreview)
{
    /// <summary>How many redirects the move left behind, which is the number worth confirming.</summary>
    public int RedirectCount => UrlChanges.Count(change => change.RedirectCreated);
}
