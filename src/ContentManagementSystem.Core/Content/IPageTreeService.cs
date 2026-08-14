using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// How a move ended.
/// </summary>
/// <remarks>
/// A plain enum rather than a diagnostic-carrying result: every refusal here is a single fact about
/// the tree with nothing further to say about it, and the caller — <c>PageService</c> or an endpoint
/// — is the layer that knows how to phrase it for whoever asked.
/// </remarks>
public enum PageMoveOutcome
{
    /// <summary>The page and its descendants were repositioned.</summary>
    Moved = 0,

    /// <summary>The requested parent does not exist, or has been deleted.</summary>
    ParentNotFound = 1,

    /// <summary>
    /// The requested parent is the page itself or one of its own descendants, which would detach
    /// the subtree from the tree entirely.
    /// </summary>
    WouldCreateCycle = 2,

    /// <summary>The move would push some descendant past <see cref="IPageTreeService.MaxDepth"/>.</summary>
    TooDeep = 3,
}

/// <summary>
/// Owns <see cref="Page.Path"/>, <see cref="Page.Depth"/>, and <see cref="Page.ParentId"/>.
/// </summary>
/// <remarks>
/// The materialized path (<c>/1/8/44/</c>) is what turns "every descendant of this page" — asked by
/// the tree UI, by permission inheritance, by every move, and by the recycle bin — into one indexed
/// prefix match instead of a recursive CTE (spec section 10.1).
/// <para>
/// Denormalised data is only worth having if exactly one thing writes it. A path that disagrees with
/// <see cref="Page.ParentId"/> produces a subtree that queries silently stop finding, and nothing
/// fails loudly enough to notice, so no other service may assign these three properties.
/// </para>
/// <para>
/// Nothing here calls <c>SaveChanges</c>. Both operations mutate tracked entities so the caller can
/// commit them alongside whatever else its transaction is doing — a move that succeeded while the
/// route rebuild beside it failed would leave the tree and the URLs disagreeing.
/// </para>
/// </remarks>
public interface IPageTreeService
{
    /// <summary>
    /// Deepest a page may sit, counting the root level as zero.
    /// </summary>
    /// <remarks>
    /// A real limit rather than a defensive one: it is what bounds <see cref="Page.Path"/> below the
    /// length of its column, and therefore below SQL Server's index key limit. Fifty levels of
    /// ten-digit ids is 551 characters against a column of 800. It is also far past any navigable
    /// site — a tree this deep is a modelling mistake, and refusing it names the mistake at the
    /// point it is made.
    /// </remarks>
    const int MaxDepth = 50;

    /// <summary>
    /// Sets a freshly inserted page's path and depth from its parent.
    /// </summary>
    /// <param name="page">The page, already inserted so that it has an identity.</param>
    /// <param name="cancellationToken">Token observed while loading the parent.</param>
    /// <remarks>
    /// Called in a second statement inside the creating transaction, for the same reason
    /// <see cref="Page.DraftVersionId"/> is: the path contains the page's own id, and the database
    /// assigns that on insert.
    /// </remarks>
    /// <exception cref="ArgumentException">The page has not been inserted yet.</exception>
    Task AttachAsync(Page page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Repositions a page and rewrites the path and depth of every descendant.
    /// </summary>
    /// <param name="page">The page to move. Tracked by the calling context.</param>
    /// <param name="newParentId">The page's new parent, or null to move it to the root.</param>
    /// <param name="cancellationToken">Token observed while loading the parent and the subtree.</param>
    /// <returns>Whether the move happened, and if not, why not.</returns>
    Task<PageMoveOutcome> MoveAsync(
        Page page,
        int? newParentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the path a page would have under a given parent.
    /// </summary>
    /// <param name="parentPath">The parent's path, or null for a page at the root.</param>
    /// <param name="pageId">Identity of the page.</param>
    /// <returns>The materialized path, such as <c>/1/8/44/</c>.</returns>
    static string BuildPath(string? parentPath, int pageId) =>
        string.IsNullOrEmpty(parentPath) ? $"/{pageId}/" : $"{parentPath}{pageId}/";
}
