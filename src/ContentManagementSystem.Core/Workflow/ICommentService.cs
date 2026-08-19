using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Workflow;

namespace ContentManagementSystem.Core.Workflow;

/// <summary>
/// Threaded review remarks on a page, optionally anchored to a zone (task P7-11, spec section 11.9).
/// </summary>
/// <remarks>
/// Comments belong to the page rather than to a version, which is what makes them survive the
/// rejection that created them (criterion P7 #4). The version a remark was made against is recorded
/// so a thread can be shown as historical, not so the thread can be thrown away with it.
/// </remarks>
public interface ICommentService
{
    /// <summary>Lists a page's threads, oldest first, replies nested under their parent.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="zoneKey">Only remarks anchored to this zone, or null for all of them.</param>
    /// <param name="includeResolved">Whether to include threads already dealt with.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The threads.</returns>
    Task<CmsResult<IReadOnlyList<CommentSummary>>> ListAsync(
        int pageId,
        string? zoneKey = null,
        bool includeResolved = true,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a remark or a reply.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="request">What to say, and what it is about.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The comment as stored.</returns>
    Task<CmsResult<CommentSummary>> AddAsync(
        int pageId,
        CreateCommentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a thread dealt with, or reopens it.</summary>
    /// <param name="commentId">Identity of the thread's root comment.</param>
    /// <param name="resolved">Whether it is now dealt with.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The comment as it now stands.</returns>
    /// <remarks>
    /// Resolution is a property of the thread rather than of each reply, so this is refused on a
    /// reply: a half-resolved thread is a thread whose state nobody can read at a glance.
    /// </remarks>
    Task<CmsResult<CommentSummary>> ResolveAsync(
        int commentId,
        bool resolved,
        CancellationToken cancellationToken = default);
}
