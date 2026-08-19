using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Notifications;

/// <summary>
/// The wording of every notification the CMS raises (task P7-19, spec section 14.8).
/// </summary>
/// <remarks>
/// One file, one function per kind, because the email and the in-app row have to say the same thing.
/// The alternative — composing each message at its call site — produces an inbox and a mailbox that
/// disagree about what happened, and there is no way to notice.
/// <para>
/// Rendered at write time rather than at read time, so an inbox from last month still reads the way
/// it did when it arrived. The cost is that changing the wording does not change old rows, which is
/// the correct trade for a record of what somebody was told.
/// </para>
/// <para>
/// Plain text throughout. Every value interpolated here — a page title, an editor's name, a rejection
/// note — is written by a person, and the body is rendered into a mail client and a Blazor component
/// that both encode it. Nothing here may become HTML.
/// </para>
/// </remarks>
public static class NotificationTemplates
{
    /// <summary>A rendered notification, ready to store and to send.</summary>
    /// <param name="Subject">One line.</param>
    /// <param name="Body">The detail.</param>
    public readonly record struct Rendered(string Subject, string Body);

    /// <summary>Renders one notification.</summary>
    /// <param name="kind">Which template.</param>
    /// <param name="pageTitle">The page it is about.</param>
    /// <param name="actor">Who caused it, where somebody did.</param>
    /// <param name="note">The reason or comment carried with it, where there is one.</param>
    /// <returns>The subject and body.</returns>
    public static Rendered Render(
        NotificationKind kind,
        string pageTitle,
        string? actor = null,
        string? note = null)
    {
        var who = string.IsNullOrWhiteSpace(actor) ? "Somebody" : actor;
        var because = string.IsNullOrWhiteSpace(note) ? null : $"\n\nThey said: {note.Trim()}";

        return kind switch
        {
            NotificationKind.Submitted => new Rendered(
                $"'{pageTitle}' is waiting for review",
                $"{who} submitted '{pageTitle}' for review.{because}"),

            NotificationKind.Approved => new Rendered(
                $"'{pageTitle}' was approved",
                $"{who} approved '{pageTitle}'. It can be published now.{because}"),

            NotificationKind.Rejected => new Rendered(
                $"'{pageTitle}' was sent back",
                $"{who} sent '{pageTitle}' back for changes. Your draft has been restored so you " +
                $"can carry on from where it was.{because}"),

            NotificationKind.ScheduledPublishSucceeded => new Rendered(
                $"'{pageTitle}' published as scheduled",
                $"The scheduled publish of '{pageTitle}' ran and the page is live."),

            NotificationKind.ScheduledPublishFailed => new Rendered(
                $"'{pageTitle}' did not publish",
                $"The scheduled publish of '{pageTitle}' was refused and will not be retried, " +
                $"because retrying would fail the same way. Open the page, fix what is wrong, and " +
                $"schedule it again.{(because ?? string.Empty)}"),

            NotificationKind.EditLockOverridden => new Rendered(
                $"{who} took over editing '{pageTitle}'",
                $"{who} opened '{pageTitle}' while you had it open, and chose to edit anyway. " +
                $"Anything you save from here may conflict with their changes."),

            NotificationKind.CommentMention => new Rendered(
                $"{who} mentioned you on '{pageTitle}'",
                $"{who} left a comment on '{pageTitle}' naming you.{because}"),

            _ => new Rendered($"'{pageTitle}' changed", $"{who} changed '{pageTitle}'.{because}"),
        };
    }
}
