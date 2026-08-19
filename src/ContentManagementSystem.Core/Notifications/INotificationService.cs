using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Workflow;

namespace ContentManagementSystem.Core.Notifications;

/// <summary>
/// Tells an editor something happened, in the app and by mail (task P7-19, spec section 14.8).
/// </summary>
/// <remarks>
/// Both halves, in that order, and the order is the design. The inbox row is written and committed
/// first; the mail is attempted afterwards and its failure is logged rather than propagated. A
/// notification is a side effect of an editorial action that has already happened, so a mail server
/// having a bad afternoon must not turn a successful approval into a failed one.
/// </remarks>
public interface INotificationService
{
    /// <summary>
    /// Raises one notification for one recipient.
    /// </summary>
    /// <param name="userId">Who to tell.</param>
    /// <param name="kind">Which template to use.</param>
    /// <param name="pageId">The page it is about, when it is about one.</param>
    /// <param name="pageTitle">That page's title, for the wording.</param>
    /// <param name="actor">Who caused it.</param>
    /// <param name="note">The reason or comment to carry.</param>
    /// <param name="link">Backoffice path to open, relative to the site root.</param>
    /// <param name="includeCaller">
    /// Whether to tell the signed-in caller as well, when they are the recipient.
    /// </param>
    /// <param name="cancellationToken">Token observed while writing and sending.</param>
    /// <remarks>
    /// Telling somebody about their own action is a no-op by default: an approver does not need an
    /// email saying they approved something. That is decided here rather than at each call site,
    /// because it is the same rule every time and forgetting it once is an inbox full of noise.
    /// <para>
    /// <paramref name="includeCaller"/> is the exception the scheduler needs. A scheduled publish
    /// runs <em>as</em> the editor who scheduled it, so the caller and the recipient are the same
    /// person — and "your scheduled publish failed" is precisely the message they must receive. The
    /// rule is about somebody having just pressed a button, and there, nobody did.
    /// </para>
    /// </remarks>
    Task NotifyAsync(
        int userId,
        NotificationKind kind,
        int? pageId,
        string pageTitle,
        string? actor = null,
        string? note = null,
        string? link = null,
        bool includeCaller = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Raises the same notification for several recipients, skipping duplicates and the actor.
    /// </summary>
    /// <param name="userIds">Who to tell.</param>
    /// <param name="kind">Which template to use.</param>
    /// <param name="pageId">The page it is about.</param>
    /// <param name="pageTitle">That page's title.</param>
    /// <param name="actor">Who caused it.</param>
    /// <param name="note">The reason to carry.</param>
    /// <param name="link">Backoffice path to open.</param>
    /// <param name="includeCaller">
    /// Whether to tell the signed-in caller as well, when they are among the recipients.
    /// </param>
    /// <param name="cancellationToken">Token observed while writing and sending.</param>
    Task NotifyManyAsync(
        IEnumerable<int> userIds,
        NotificationKind kind,
        int? pageId,
        string pageTitle,
        string? actor = null,
        string? note = null,
        string? link = null,
        bool includeCaller = false,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the caller's own inbox.</summary>
    /// <param name="unreadOnly">Whether to leave out what has been read.</param>
    /// <param name="limit">Most rows to return.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The inbox and the unread count the shell's badge shows.</returns>
    Task<CmsResult<NotificationInbox>> InboxAsync(
        bool unreadOnly = false,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>Marks one of the caller's notifications read, or all of them.</summary>
    /// <param name="notificationId">The one to mark, or null for every unread one.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>How many rows were marked.</returns>
    /// <remarks>
    /// Scoped to the caller in the query rather than checked after loading, so marking somebody
    /// else's notification read is not a thing that can be attempted with a guessed id.
    /// </remarks>
    Task<CmsResult<int>> MarkReadAsync(int? notificationId, CancellationToken cancellationToken = default);
}
