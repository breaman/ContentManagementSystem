namespace ContentManagementSystem.Core.Scheduling;

/// <summary>
/// How the scheduled-publish poller behaves (spec section 11.6, task P7-13).
/// </summary>
public sealed class PublishSchedulerOptions
{
    /// <summary>Configuration section these are bound from.</summary>
    public const string SectionName = "Cms:Scheduler";

    /// <summary>Whether the poller runs at all.</summary>
    /// <remarks>
    /// Off in tests and in any process that is not meant to publish anything, on everywhere else. A
    /// switch rather than a conditional registration so that turning it off leaves the health check
    /// and the schedule endpoints in place — a deployment where nothing publishes should still be
    /// able to say so.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between passes. Thirty, from spec section 11.6.</summary>
    /// <remarks>
    /// Clamped where it is read rather than validated at startup: a nonsensical value should make
    /// the poller behave sensibly, not refuse to boot the whole site.
    /// </remarks>
    public int PollSeconds { get; set; } = 30;

    /// <summary>Most jobs one pass claims.</summary>
    /// <remarks>
    /// Bounded so a backlog is worked through over several passes rather than in one transaction
    /// that holds a claim on everything and loses the lot if the process is recycled halfway.
    /// </remarks>
    public int BatchSize { get; set; } = 25;

    /// <summary>
    /// Minutes after which a claim is treated as abandoned and the job is claimable again.
    /// </summary>
    /// <remarks>
    /// The answer to an instance that died mid-publish. Long enough that a slow publish is never
    /// stolen from the instance still running it, short enough that a scheduled page is not stuck
    /// until somebody notices.
    /// </remarks>
    public int StaleClaimMinutes { get; set; } = 10;

    /// <summary>Lag in seconds beyond which the <c>cms-scheduler</c> health check fails.</summary>
    /// <remarks>Five minutes, from task P7-17.</remarks>
    public int UnhealthyLagSeconds { get; set; } = 300;
}
