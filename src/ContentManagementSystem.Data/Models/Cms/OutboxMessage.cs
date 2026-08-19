namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A message enqueued inside the transaction that caused it, dispatched afterwards
/// (spec section 16.3).
/// </summary>
/// <remarks>
/// The transactional outbox, and it is here for one specific failure. Cache invalidation fired
/// in-process at the end of a publish is lost if the process dies between the commit and the call —
/// leaving a page that is published in the database and stale on every reader, with nothing to
/// notice it. A row written <em>in the publish's own transaction</em> either commits with the
/// publish or does not exist, so a committed publish always has an eviction waiting for it, and
/// every instance sees it because it is in the database rather than in one process's memory
/// (task P8-09, risk R17).
/// <para>
/// Machine-written and high-churn, and so excluded from audit capture. The publish it accompanies is
/// audited by the ordinary path.
/// </para>
/// </remarks>
public class OutboxMessage
{
    /// <summary>
    /// Identity. <c>bigint</c>, unlike every other key in this schema.
    /// </summary>
    /// <remarks>
    /// This table takes several rows per publish and is pruned rather than kept, so its identity
    /// values are consumed at a rate no content table approaches. An <c>int</c> would be a column to
    /// widen under load on a table that must not be locked.
    /// </remarks>
    public long Id { get; set; }

    /// <summary>What kind of message this is, which decides who handles it.</summary>
    public string Type { get; set; } = null!;

    /// <summary>The message body, as JSON.</summary>
    public string PayloadJson { get; set; } = null!;

    /// <summary>When it was enqueued.</summary>
    public DateTimeOffset CreatedOn { get; set; }

    /// <summary>When it was dispatched, or null while it is still pending.</summary>
    public DateTimeOffset? ProcessedOn { get; set; }

    /// <summary>How many dispatch attempts have been made.</summary>
    /// <remarks>
    /// A message is retried rather than dropped, and the count is what stops a permanently failing
    /// one from being retried forever — and what makes "this deployment cannot evict its cache"
    /// visible in a health check rather than only in a log nobody reads.
    /// </remarks>
    public int AttemptCount { get; set; }

    /// <summary>Why the last attempt failed.</summary>
    public string? LastError { get; set; }
}
