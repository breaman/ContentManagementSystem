namespace ContentManagementSystem.Core.Scheduling;

/// <summary>
/// What the scheduler last saw, shared between the poller, the gauge, and the health check
/// (task P7-17).
/// </summary>
/// <remarks>
/// A singleton holding two numbers rather than a query behind the metric. An observable gauge is
/// read by the metrics collector on its own schedule, from a thread with no scope and no
/// cancellation, so a gauge that went to the database would open a connection per scrape — and would
/// report the collector's view of the lag rather than the poller's.
/// <para>
/// Written by one poller and read by others; the fields are volatile-by-construction through
/// <see cref="Interlocked"/> so a reader never sees a torn value.
/// </para>
/// </remarks>
public sealed class SchedulerState
{
    private long _lastPollTicks;
    private long _lagSeconds;

    /// <summary>How overdue the oldest waiting job was at the last poll, in seconds.</summary>
    /// <remarks>
    /// Zero when nothing is waiting, which is the ordinary state and is deliberately not "no
    /// reading": a gauge that reported nothing while the site was quiet would be indistinguishable
    /// from a scheduler that had stopped.
    /// </remarks>
    public long LagSeconds => Interlocked.Read(ref _lagSeconds);

    /// <summary>When the poller last completed a pass, or null before the first one.</summary>
    public DateTimeOffset? LastPollOn
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastPollTicks);

            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Records the outcome of one pass.</summary>
    /// <param name="pollOn">When the pass ran.</param>
    /// <param name="lag">How overdue the oldest waiting job was.</param>
    public void Record(DateTimeOffset pollOn, TimeSpan lag)
    {
        Interlocked.Exchange(ref _lastPollTicks, pollOn.UtcTicks);
        Interlocked.Exchange(ref _lagSeconds, (long)Math.Max(0, lag.TotalSeconds));
    }
}
