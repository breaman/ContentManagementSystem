namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// What a <see cref="Notification"/> is about (spec section 14.8).
/// </summary>
/// <remarks>
/// The list is closed on purpose: each value has a template behind it, and a notification with no
/// template is a blank line in somebody's inbox.
/// </remarks>
public enum NotificationKind
{
    /// <summary>A page was submitted for review.</summary>
    Submitted = 0,

    /// <summary>A submission was approved.</summary>
    Approved = 1,

    /// <summary>A submission was sent back.</summary>
    Rejected = 2,

    /// <summary>A scheduled publish did what it said.</summary>
    ScheduledPublishSucceeded = 3,

    /// <summary>A scheduled publish refused, and will not be retried.</summary>
    ScheduledPublishFailed = 4,

    /// <summary>Somebody took an edit lock away from its holder.</summary>
    EditLockOverridden = 5,

    /// <summary>Somebody was named in a review comment.</summary>
    CommentMention = 6,
}
