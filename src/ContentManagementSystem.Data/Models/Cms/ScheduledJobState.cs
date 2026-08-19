namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// Where a scheduled job has got to (spec section 11.6).
/// </summary>
/// <remarks>
/// The states exist to make double-execution impossible rather than unlikely: a job leaves
/// <see cref="Pending"/> only through an atomic <c>UPDATE … OUTPUT</c>, so exactly one instance ever
/// sees it as its own (risk R16).
/// </remarks>
public enum ScheduledJobState
{
    /// <summary>Waiting for its due time, or waiting to be claimed.</summary>
    Pending = 0,

    /// <summary>Claimed by one instance and being run right now.</summary>
    Claimed = 1,

    /// <summary>Ran and did what it said.</summary>
    Completed = 2,

    /// <summary>
    /// Ran and refused — usually because the version no longer validates.
    /// </summary>
    /// <remarks>
    /// Terminal. The owner is notified and the job is <em>not</em> retried: a version that fails
    /// validation at 09:00 fails it at 09:30 too, and a blind retry turns one notification into
    /// forty-eight a day (spec section 11.6).
    /// </remarks>
    Failed = 3,

    /// <summary>Withdrawn before it ran, because the schedule was changed or removed.</summary>
    Cancelled = 4,
}
