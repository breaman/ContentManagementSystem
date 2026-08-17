using System.Collections.Concurrent;

using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// The bulk jobs this process is running, and the ones it has recently finished (task P6-29).
/// </summary>
/// <remarks>
/// <strong>In memory, for the life of the process.</strong> A job is progress and a per-item report
/// — both of which stop being interesting minutes after the batch ends — and persisting them would
/// mean a table, a migration, and a retention sweep for data whose only reader is a progress bar.
/// <para>
/// The cost of that choice is stated rather than hidden: a poll that arrives after a restart is
/// answered with <see cref="PageCodes.JobNotFound"/>, and a site running on more than one instance
/// can only poll the instance that accepted the batch. That is <strong>Q4</strong> — whether the
/// deployment is scaled out at launch — and the answer changes this class rather than its callers:
/// the same registry over a distributed cache satisfies the same interface.
/// </para>
/// </remarks>
public sealed class BulkOperationJobs
{
    /// <summary>How many finished jobs are kept before the oldest are dropped.</summary>
    /// <remarks>
    /// Enough that an editor who ran three batches and went to lunch can still read all three, and
    /// few enough that a long-lived process holding a per-item report for every batch it has ever run
    /// is not a leak with a slow fuse.
    /// </remarks>
    public const int RetainedJobs = 50;

    private readonly ConcurrentDictionary<Guid, BulkJob> _jobs = new();

    /// <summary>Registers a new job and returns it, ready to record results against.</summary>
    /// <param name="operation">What the job does to each item.</param>
    /// <param name="total">How many items it was given.</param>
    /// <param name="startedOn">When it was accepted.</param>
    /// <returns>The job.</returns>
    public BulkJob Start(BulkOperation operation, int total, DateTimeOffset startedOn)
    {
        var job = new BulkJob(Guid.NewGuid(), operation, total, startedOn);

        _jobs[job.Id] = job;

        Trim();

        return job;
    }

    /// <summary>Finds a job by its identity.</summary>
    /// <param name="id">Identity of the job.</param>
    /// <returns>The job, or null when this process has never had one with that identity.</returns>
    public BulkJob? Find(Guid id) => _jobs.TryGetValue(id, out var job) ? job : null;

    /// <summary>
    /// Drops the oldest finished jobs once there are more than <see cref="RetainedJobs"/> of them.
    /// </summary>
    /// <remarks>
    /// Running jobs are never dropped, however old: a batch of 400 pages can outlive fifty small ones
    /// started after it, and evicting the one job somebody is actually watching would be the worst
    /// possible choice of victim.
    /// </remarks>
    private void Trim()
    {
        if (_jobs.Count <= RetainedJobs) return;

        var finished = _jobs.Values
            .Where(job => job.Snapshot().IsFinished)
            .OrderBy(job => job.StartedOn)
            .ToList();

        for (var i = 0; i < finished.Count && _jobs.Count > RetainedJobs; i++)
        {
            _jobs.TryRemove(finished[i].Id, out _);
        }
    }
}

/// <summary>
/// One bulk job's mutable progress, safe to write from the runner and read from a poll.
/// </summary>
/// <param name="id">Identity of the job.</param>
/// <param name="operation">What the job does to each item.</param>
/// <param name="total">How many items it was given.</param>
/// <param name="startedOn">When it was accepted.</param>
/// <remarks>
/// The mutable half is deliberately not the contract. <see cref="Snapshot"/> produces an immutable
/// <see cref="BulkJobStatus"/> under the lock, so a poll arriving mid-item reads a consistent count
/// and result list rather than a list being appended to as it is serialized.
/// </remarks>
public sealed class BulkJob(Guid id, BulkOperation operation, int total, DateTimeOffset startedOn)
{
    private readonly Lock _gate = new();
    private readonly List<BulkItemResult> _results = new(total);

    private BulkJobState _state = BulkJobState.Running;
    private DateTimeOffset? _finishedOn;

    /// <summary>Identity of the job, which is what a client polls with.</summary>
    public Guid Id { get; } = id;

    /// <summary>When the job was accepted.</summary>
    public DateTimeOffset StartedOn { get; } = startedOn;

    /// <summary>Records what happened to one item.</summary>
    /// <param name="result">The item's outcome.</param>
    public void Record(BulkItemResult result)
    {
        lock (_gate)
        {
            _results.Add(result);
        }
    }

    /// <summary>Marks the job as no longer attempting items.</summary>
    /// <param name="state">How it ended.</param>
    /// <param name="finishedOn">When it ended.</param>
    public void Finish(BulkJobState state, DateTimeOffset finishedOn)
    {
        lock (_gate)
        {
            _state = state;
            _finishedOn = finishedOn;
        }
    }

    /// <summary>Takes a consistent copy of the job's progress.</summary>
    /// <returns>The status as of this instant.</returns>
    public BulkJobStatus Snapshot()
    {
        lock (_gate)
        {
            return new BulkJobStatus(
                Id,
                operation,
                _state,
                total,
                [.. _results],
                StartedOn,
                _finishedOn);
        }
    }
}
