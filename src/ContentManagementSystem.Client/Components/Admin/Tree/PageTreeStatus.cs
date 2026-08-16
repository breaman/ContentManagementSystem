using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Client.Components.Admin.Tree;

/// <summary>
/// The one state a tree row reports about a page (task P6-02, spec section 14.2).
/// </summary>
/// <remarks>
/// Six states, exactly the six the spec's legend lists. A page can be several things at once — a
/// published page with a scheduled replacement draft is both — so this is an ordering of what the
/// editor most needs to know, not a partition of reality. What "most needs to know" means here is
/// "what is about to happen without anyone doing anything else": a review or a schedule is a
/// commitment already made, so it outranks the steady states below it.
/// <para>
/// Whether somebody has the page <em>open</em> is deliberately not in this list. It is orthogonal to
/// every value here — any of them can be locked — and folding it in would mean losing the publishing
/// state whenever a colleague happened to have the page on screen.
/// </para>
/// </remarks>
public enum PageTreeStatus
{
    /// <summary>Live, and what is live is what the draft says.</summary>
    Published = 0,

    /// <summary>Live, but the draft has moved on and nobody has published it.</summary>
    UnpublishedChanges = 1,

    /// <summary>The draft is due to go live at a time somebody set.</summary>
    Scheduled = 2,

    /// <summary>Submitted for approval and waiting on somebody.</summary>
    InReview = 3,

    /// <summary>Sent back with comments.</summary>
    Rejected = 4,

    /// <summary>Not on the public site: never published, or taken down again.</summary>
    NotPublished = 5,
}

/// <summary>
/// Turns a page summary into the badge one tree row shows.
/// </summary>
/// <remarks>
/// A static classifier rather than a property on the summary, because it is a presentation decision
/// — the precedence above is what a tree needs, and a dashboard tile counting "pages awaiting
/// review" wants the raw facts instead. Kept separate from the component so the precedence can be
/// asserted directly.
/// <para>
/// Every state carries an icon <em>and</em> a word. Nothing in the tree is distinguished by colour
/// alone, which is what P6-39 gates and what makes the tree readable to a person who cannot tell
/// the amber badge from the green one.
/// </para>
/// </remarks>
public static class PageTreeStatuses
{
    /// <summary>Classifies one page.</summary>
    /// <param name="page">The page as the tree received it.</param>
    /// <param name="now">The current time, against which a schedule is past or future.</param>
    /// <returns>The single state the row reports.</returns>
    public static PageTreeStatus Classify(PageSummary page, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (string.Equals(page.Status, nameof(PageTreeStatus.InReview), StringComparison.OrdinalIgnoreCase))
        {
            return PageTreeStatus.InReview;
        }

        if (string.Equals(page.Status, nameof(PageTreeStatus.Rejected), StringComparison.OrdinalIgnoreCase))
        {
            return PageTreeStatus.Rejected;
        }

        // A schedule in the past is not a schedule; it is a publish that has either happened or
        // failed, and both of those are told by the states below.
        if (page.ScheduledPublishOn is { } due && due > now)
        {
            return PageTreeStatus.Scheduled;
        }

        if (page.PublishedVersionNumber is null)
        {
            return PageTreeStatus.NotPublished;
        }

        return page.HasUnpublishedChanges
            ? PageTreeStatus.UnpublishedChanges
            : PageTreeStatus.Published;
    }

    /// <summary>The word a screen reader announces and a monochrome screen still shows.</summary>
    public static string Label(PageTreeStatus status) => status switch
    {
        PageTreeStatus.Published => "Published",
        PageTreeStatus.UnpublishedChanges => "Unpublished changes",
        PageTreeStatus.Scheduled => "Scheduled",
        PageTreeStatus.InReview => "In review",
        PageTreeStatus.Rejected => "Rejected",
        PageTreeStatus.NotPublished => "Not published",
        _ => "Unknown",
    };

    /// <summary>The Bootstrap icon that carries the same distinction as a shape.</summary>
    public static string Icon(PageTreeStatus status) => status switch
    {
        PageTreeStatus.Published => "bi-circle-fill",
        PageTreeStatus.UnpublishedChanges => "bi-circle-half",
        PageTreeStatus.Scheduled => "bi-clock-fill",
        PageTreeStatus.InReview => "bi-eye-fill",
        PageTreeStatus.Rejected => "bi-x-circle-fill",
        PageTreeStatus.NotPublished => "bi-circle",
        _ => "bi-question-circle",
    };

    /// <summary>The colour, which is an addition to the icon and the word rather than a substitute.</summary>
    public static string CssClass(PageTreeStatus status) => status switch
    {
        PageTreeStatus.Published => "text-success",
        PageTreeStatus.UnpublishedChanges => "text-warning-emphasis",
        PageTreeStatus.Scheduled => "text-info-emphasis",
        PageTreeStatus.InReview => "text-primary",
        PageTreeStatus.Rejected => "text-danger",
        PageTreeStatus.NotPublished => "text-secondary",
        _ => "text-secondary",
    };
}
