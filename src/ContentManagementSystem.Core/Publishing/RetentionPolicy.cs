using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Publishing;

/// <summary>
/// Why a version survived a retention sweep (spec section 11.7).
/// </summary>
/// <remarks>
/// The reason is carried rather than inferred so the decision can be asserted clause by clause
/// (task P2-24). Every clause protects something an editor would be upset to lose, and a sweep that
/// kept the right rows for the wrong reason is one clause away from keeping none of them.
/// </remarks>
public enum RetentionReason
{
    /// <summary>Nothing protects it; it is older than the window and outside the recent set.</summary>
    Prunable = 0,

    /// <summary>It is the page's current draft or its published version.</summary>
    Pointer = 1,

    /// <summary>It was live at some point, so it is what a rollback goes back to.</summary>
    Published = 2,

    /// <summary>An editor named it, which is the whole reason a checkpoint exists.</summary>
    Checkpoint = 3,

    /// <summary>It falls inside the retention window.</summary>
    InsideWindow = 4,

    /// <summary>It is one of the most recent <see cref="RetentionPolicy.KeepPerPage"/> versions.</summary>
    RecentlyEnough = 5,
}

/// <summary>
/// One version, reduced to what the retention decision reads.
/// </summary>
/// <param name="Id">Identity of the version.</param>
/// <param name="Rank">
/// Its position counting back from the newest, starting at one. Rank rather than version number,
/// because a page whose history has already been pruned has gaps in its numbering and "the last
/// twenty" means twenty rows, not a span of twenty numbers.
/// </param>
/// <param name="Status">Its current status.</param>
/// <param name="Label">The name an editor gave a checkpoint, or null.</param>
/// <param name="PublishedOn">When it went live, or null if it never did.</param>
/// <param name="CreatedOn">When it was written.</param>
/// <param name="IsPointedAt">Whether the page's draft or published pointer names it.</param>
public readonly record struct RetentionCandidate(
    int Id,
    int Rank,
    PageVersionStatus Status,
    string? Label,
    DateTimeOffset? PublishedOn,
    DateTimeOffset? CreatedOn,
    bool IsPointedAt);

/// <summary>
/// Decides which versions a retention sweep may destroy (task P2-13, spec section 11.7).
/// </summary>
/// <remarks>
/// Pure, and deliberately separate from <see cref="VersionService"/>, which supplies the rows. The
/// clauses are the interesting part and the query is not: each one exists to protect content whose
/// loss is silent and permanent, and none of them can be exercised through a database without
/// arranging ninety days of history first.
/// <para>
/// Pages in the recycle bin are not represented here at all. <see cref="VersionService"/> excludes
/// them from the sweep outright, because a restore that came back with no history is not a restore —
/// expressing that as a sixth clause would invite a caller to pass a deleted page's rows in and rely
/// on this class to notice.
/// </para>
/// </remarks>
public static class RetentionPolicy
{
    /// <summary>Versions kept per page regardless of age (spec section 11.7).</summary>
    public const int KeepPerPage = 20;

    /// <summary>Retention window used when <c>SiteSettings</c> does not name one.</summary>
    public const int DefaultRetentionDays = 90;

    /// <summary>
    /// Chooses the retention window in force.
    /// </summary>
    /// <param name="configured">The value on <c>SiteSettings</c>, or null when there is no row.</param>
    /// <returns>The window, in days.</returns>
    /// <remarks>
    /// Zero and negative both fall back to the default rather than meaning "keep nothing". The
    /// seeded settings row carries zero, so reading it literally would make a fresh deployment's
    /// first nightly sweep the most destructive one it ever runs.
    /// </remarks>
    public static int WindowDays(int? configured) =>
        configured is > 0 ? configured.Value : DefaultRetentionDays;

    /// <summary>
    /// Computes the instant before which a version is no longer inside the window.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="configured">The configured window in days, or null.</param>
    public static DateTimeOffset CutoffFrom(DateTimeOffset now, int? configured) =>
        now.AddDays(-WindowDays(configured));

    /// <summary>
    /// Decides whether one version may be destroyed, and why not when it may not.
    /// </summary>
    /// <param name="candidate">The version.</param>
    /// <param name="cutoff">The instant returned by <see cref="CutoffFrom"/>.</param>
    /// <returns>
    /// <see cref="RetentionReason.Prunable"/> when nothing protects it, and otherwise the first
    /// clause that does.
    /// </returns>
    /// <remarks>
    /// A version with no <c>CreatedOn</c> is treated as inside the window. The stamp is written by
    /// the audit interceptor and its absence means the row's age is unknown, which is not a licence
    /// to delete it.
    /// </remarks>
    public static RetentionReason Decide(RetentionCandidate candidate, DateTimeOffset cutoff)
    {
        if (candidate.IsPointedAt) return RetentionReason.Pointer;

        if (candidate.PublishedOn is not null || candidate.Status is PageVersionStatus.Published)
        {
            return RetentionReason.Published;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Label)) return RetentionReason.Checkpoint;

        if ((candidate.CreatedOn ?? DateTimeOffset.MaxValue) >= cutoff) return RetentionReason.InsideWindow;

        return candidate.Rank <= KeepPerPage ? RetentionReason.RecentlyEnough : RetentionReason.Prunable;
    }
}
