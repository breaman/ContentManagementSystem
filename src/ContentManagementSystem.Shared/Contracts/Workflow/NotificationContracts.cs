namespace ContentManagementSystem.Shared.Contracts.Workflow;

/// <summary>
/// One item of the in-app inbox (spec section 14.8).
/// </summary>
/// <param name="Id">Identity of the notification.</param>
/// <param name="Kind">
/// What it is about, as one of the closed set of template keys — <c>Submitted</c>, <c>Approved</c>,
/// <c>Rejected</c>, <c>ScheduledPublishSucceeded</c>, <c>ScheduledPublishFailed</c>,
/// <c>EditLockOverridden</c>, <c>CommentMention</c>.
/// </param>
/// <param name="Subject">One-line summary, as the list shows it.</param>
/// <param name="Body">The detail, in the same words the email carried.</param>
/// <param name="PageId">The page it concerns, when it concerns one.</param>
/// <param name="Link">Where clicking it goes — always a backoffice path, never an absolute URL.</param>
/// <param name="CreatedOn">When it was raised.</param>
/// <param name="ReadOn">When the recipient read it, or null while it is unread.</param>
public sealed record NotificationSummary(
    int Id,
    string Kind,
    string Subject,
    string Body,
    int? PageId,
    string? Link,
    DateTimeOffset CreatedOn,
    DateTimeOffset? ReadOn);

/// <summary>The inbox, with the count the shell's badge shows.</summary>
/// <param name="Items">The notifications, newest first.</param>
/// <param name="UnreadCount">
/// How many are unread in total, not how many unread are in <paramref name="Items"/> — the badge has
/// to be right even when the list is truncated.
/// </param>
public sealed record NotificationInbox(IReadOnlyList<NotificationSummary> Items, int UnreadCount);
