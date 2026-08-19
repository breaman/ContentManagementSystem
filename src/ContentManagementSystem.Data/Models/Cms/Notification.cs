namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One item in an editor's in-app inbox (spec section 14.8).
/// </summary>
/// <remarks>
/// The in-app half of a notification, written in the same transaction as the thing it is about. Mail
/// is the other half and is sent afterwards through <c>IEmailSender</c>, which can fail — the inbox
/// row is what makes "you were never told" recoverable when it does.
/// <para>
/// The body is rendered from a template at write time rather than at read time, so an inbox from
/// last month still reads the way it did when it arrived even after the wording changed. It is plain
/// text and is never interpreted as markup.
/// </para>
/// </remarks>
public class Notification : EntityBase
{
    /// <summary>Who it is for.</summary>
    public int UserId { get; set; }

    /// <summary>Who it is for.</summary>
    public User User { get; set; } = null!;

    /// <summary>What it is about.</summary>
    public NotificationKind Kind { get; set; }

    /// <summary>One-line summary, as the inbox lists it.</summary>
    public string Subject { get; set; } = null!;

    /// <summary>The detail, in the same words the email carried.</summary>
    public string Body { get; set; } = null!;

    /// <summary>The page it concerns, when it concerns one.</summary>
    public int? PageId { get; set; }

    /// <summary>The page it concerns.</summary>
    public Page? Page { get; set; }

    /// <summary>Where clicking it goes — a backoffice path, never an absolute URL.</summary>
    public string? Link { get; set; }

    /// <summary>When it was raised.</summary>
    public DateTimeOffset CreatedOn { get; set; }

    /// <summary>When the recipient read it, or null while it is still unread.</summary>
    public DateTimeOffset? ReadOn { get; set; }
}
