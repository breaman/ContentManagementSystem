namespace ContentManagementSystem.Shared.Contracts.Workflow;

/// <summary>
/// A new review remark (spec section 11.9).
/// </summary>
/// <param name="Body">
/// What to say. Plain text: it is stored verbatim and displayed encoded, and nothing anywhere
/// interprets it as markup.
/// </param>
/// <param name="ZoneKey">
/// The zone the remark is about, or null for a remark about the page. Anchoring is what turns
/// "the hero headline is wrong" into a note on the hero card rather than a paragraph to translate.
/// </param>
/// <param name="ParentCommentId">The comment being replied to, or null to open a thread.</param>
/// <param name="PageVersionId">
/// The version on screen, so a thread can later be shown as being about a version the page has moved
/// on from.
/// </param>
public sealed record CreateCommentRequest(
    string Body,
    string? ZoneKey = null,
    int? ParentCommentId = null,
    int? PageVersionId = null);

/// <summary>
/// One remark and its replies.
/// </summary>
/// <param name="Id">Identity of the comment.</param>
/// <param name="PageId">Page it is about.</param>
/// <param name="PageVersionId">Version it was made against, when it was made against one.</param>
/// <param name="ZoneKey">Zone it is anchored to, or null for the page as a whole.</param>
/// <param name="Body">What was said.</param>
/// <param name="AuthorUserId">Who said it.</param>
/// <param name="AuthorName">Their name.</param>
/// <param name="CreatedOn">When.</param>
/// <param name="ResolvedOn">When the thread was marked dealt with, or null while it is open.</param>
/// <param name="ResolvedByName">Who marked it dealt with.</param>
/// <param name="Replies">Replies, oldest first.</param>
public sealed record CommentSummary(
    int Id,
    int PageId,
    int? PageVersionId,
    string? ZoneKey,
    string Body,
    int AuthorUserId,
    string? AuthorName,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? ResolvedOn,
    string? ResolvedByName,
    IReadOnlyList<CommentSummary> Replies);
