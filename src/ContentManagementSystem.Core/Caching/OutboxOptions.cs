namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// How the outbox is polled and pruned (task P8-09, spec section 24.4).
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Cms:Outbox";

    /// <summary>Whether this instance dispatches outbox messages at all.</summary>
    /// <remarks>
    /// On by default and on every instance, unlike the publish scheduler. Each instance has its own
    /// in-process caches to evict, so an instance that does not poll is an instance serving stale
    /// pages — this is not work that can be given to one node.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between passes.</summary>
    /// <remarks>
    /// Five, from spec section 16.3. It is the visible delay between pressing Publish and the public
    /// URL changing, so it is short; the query it costs is one indexed seek against a filtered index
    /// holding only the pending rows.
    /// </remarks>
    public int PollSeconds { get; set; } = 5;

    /// <summary>Most messages dispatched in one pass.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>How long dispatched messages are kept before they are pruned.</summary>
    /// <remarks>
    /// Kept at all so that "why did this page not update" has an answer for a day afterwards. The
    /// rows are the only record that an invalidation was enqueued and dispatched.
    /// </remarks>
    public int RetentionHours { get; set; } = 24;

    /// <summary>
    /// How long a message may stay undispatched before the health check reports a fault.
    /// </summary>
    public int UnhealthyBacklogMinutes { get; set; } = 5;
}
