using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// Starts a bulk operation and follows it to the end (task P6-29).
/// </summary>
/// <remarks>
/// The polling half of spec section 14.11's background execution, in one place rather than in every
/// screen that runs a batch. A small selection finishes inside the request and never polls at all,
/// which is why the loop is written around <c>BulkJobStatus.IsFinished</c> rather than around a
/// count: the screen asks for the same thing either way and the server decides which it gets.
/// <para>
/// The clock is a <see cref="TimeProvider"/> so a test can advance a poll interval rather than wait
/// one out — the same reason <c>AutosaveController</c> takes one.
/// </para>
/// </remarks>
public static class BulkOperationRunner
{
    /// <summary>How long to wait between polls of a running job.</summary>
    /// <remarks>
    /// A second. Fast enough that a progress bar moves, slow enough that a five-minute batch is
    /// three hundred requests rather than fifteen thousand.
    /// </remarks>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Runs an operation and reports its progress until it stops.
    /// </summary>
    /// <param name="client">The page API.</param>
    /// <param name="clock">Source of the delay between polls.</param>
    /// <param name="request">The operation and the selection.</param>
    /// <param name="onProgress">
    /// Called with every status the run produces, including the first and the last. Screens redraw
    /// from this rather than from a return value they would only get at the end.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops the <em>polling</em>, not the batch. A background job belongs to the server once it has
    /// been accepted, and pretending otherwise would let a closed dialog imply a cancelled publish.
    /// </param>
    /// <returns>The job's final state, or the refusal that stopped it from starting.</returns>
    public static async Task<StructureClientResult<BulkJobStatus>> RunAsync(
        IPageClient client,
        TimeProvider clock,
        BulkOperationRequest request,
        Func<BulkJobStatus, Task> onProgress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(onProgress);

        var started = await client.StartBulkAsync(request, cancellationToken);

        if (!started.IsSuccess || started.Value is not { } job) return started;

        await onProgress(job);

        while (!job.IsFinished && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(PollInterval, clock, cancellationToken);

            // A poll that finds nothing is a server that has forgotten the job — a restart, or a
            // second instance answering. Reported as the last state seen rather than as a failure:
            // the items already applied are still applied, and inventing a failure would say
            // otherwise.
            if (await client.GetBulkAsync(job.Id, cancellationToken) is not { } polled) break;

            job = polled;

            await onProgress(job);
        }

        return StructureClientResult<BulkJobStatus>.Success(job);
    }
}
