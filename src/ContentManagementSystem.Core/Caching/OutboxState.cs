namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// What the outbox poller last saw, shared between the loop, the health check, and the metrics
/// (task P8-13).
/// </summary>
/// <remarks>
/// A singleton holding the instance's own view. <see cref="Watermark"/> in particular is per
/// instance and deliberately so: a message is applied by <em>every</em> node, because each has its
/// own in-process cache to evict, and a claim shared through the database would let one node's
/// dispatch leave the others serving stale pages (spec section 16.3).
/// </remarks>
public sealed class OutboxState
{
    private long _watermark;

    /// <summary>Highest message id this instance has already applied.</summary>
    public long Watermark => Interlocked.Read(ref _watermark);

    /// <summary>When the last pass completed, or null before the first one.</summary>
    public DateTimeOffset? LastPollOn { get; private set; }

    /// <summary>Messages that no instance has marked dispatched, as of the last pass.</summary>
    public int PendingCount { get; private set; }

    /// <summary>When the oldest undispatched message was enqueued, as of the last pass.</summary>
    public DateTimeOffset? OldestPendingOn { get; private set; }

    /// <summary>Moves the watermark forward, never back.</summary>
    /// <param name="id">The highest id applied in this pass.</param>
    public void Advance(long id)
    {
        long current;

        do
        {
            current = Interlocked.Read(ref _watermark);

            if (id <= current) return;
        }
        while (Interlocked.CompareExchange(ref _watermark, id, current) != current);
    }

    /// <summary>Records what a completed pass observed.</summary>
    /// <param name="completedOn">When the pass finished.</param>
    /// <param name="pendingCount">Undispatched messages remaining.</param>
    /// <param name="oldestPendingOn">When the oldest of them was enqueued.</param>
    public void Record(DateTimeOffset completedOn, int pendingCount, DateTimeOffset? oldestPendingOn)
    {
        LastPollOn = completedOn;
        PendingCount = pendingCount;
        OldestPendingOn = oldestPendingOn;
    }
}
