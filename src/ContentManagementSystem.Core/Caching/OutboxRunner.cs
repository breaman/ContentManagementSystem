using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// One pass of the outbox: apply what is new, mark it dispatched, prune what is old
/// (task P8-09, spec section 16.3).
/// </summary>
/// <param name="context">The database.</param>
/// <param name="handlers">
/// What applies each message type — cache eviction, and the search index (task P8-18).
/// </param>
/// <param name="state">This instance's watermark and last-pass reading.</param>
/// <param name="options">Batch size, retention, and whether to run.</param>
/// <param name="clock">Source of the current time.</param>
/// <param name="logger">Log for messages that could not be applied.</param>
/// <remarks>
/// <strong>Every instance applies every message; the row is marked dispatched by whichever gets
/// there first.</strong> That is not the usual outbox shape, and the reason is what is being
/// dispatched to: an in-process cache on each node. A claimed-once message would evict one node's
/// memory and leave the others serving the page they had — the multi-instance failure spec section
/// 16.3 exists to prevent. Eviction is idempotent, so applying a message on four nodes is four
/// no-ops and one useful one.
/// <para>
/// The watermark is in memory rather than in the database for the same reason. A fresh process has
/// nothing cached, so messages enqueued before it started are already irrelevant to it — but the
/// ones nobody has marked dispatched are picked up anyway, which is what stops a restart during a
/// deployment from leaving a message undispatched forever.
/// </para>
/// <para>
/// A message that throws is counted and left pending rather than blocking the queue. One malformed
/// or unevictable message must not stop every later invalidation; the attempt count and the
/// <c>cms-outbox</c> health check are how it becomes visible instead.
/// </para>
/// </remarks>
public sealed class OutboxRunner(
    ApplicationDbContext context,
    IEnumerable<IOutboxMessageHandler> handlers,
    OutboxState state,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxRunner> logger)
{
    /// <summary>
    /// Runs one pass.
    /// </summary>
    /// <param name="cancellationToken">Token observed throughout.</param>
    /// <returns>How many messages were applied.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var batchSize = Math.Clamp(settings.BatchSize, 1, 1000);

        if (state.LastPollOn is null)
        {
            // First pass in this process. Everything already dispatched happened before this
            // instance had a cache to invalidate, so it starts from there rather than replaying the
            // day's history into an empty cache.
            var dispatched = await context.OutboxMessages
                .AsNoTracking()
                .Where(message => message.ProcessedOn != null)
                .MaxAsync(message => (long?)message.Id, cancellationToken) ?? 0;

            state.Advance(dispatched);
        }

        var watermark = state.Watermark;

        var batch = await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.Id > watermark)
            .OrderBy(message => message.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var applied = new List<long>(batch.Count);
        var failed = new List<long>();

        foreach (var message in batch)
        {
            try
            {
                await ApplyAsync(message, cancellationToken);
                applied.Add(message.Id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Outbox message {MessageId} of type {MessageType} could not be applied.",
                    message.Id,
                    message.Type);

                failed.Add(message.Id);

                await context.OutboxMessages
                    .Where(candidate => candidate.Id == message.Id)
                    .ExecuteUpdateAsync(
                        updates => updates
                            .SetProperty(candidate => candidate.AttemptCount, candidate => candidate.AttemptCount + 1)
                            .SetProperty(candidate => candidate.LastError, Truncate(exception.Message)),
                        cancellationToken);
            }
        }

        if (applied.Count > 0)
        {
            var now = clock.GetUtcNow();

            // Marked by whichever instance got here first; the others' updates match nothing, which
            // is the intent rather than a race being lost.
            await context.OutboxMessages
                .Where(candidate => applied.Contains(candidate.Id) && candidate.ProcessedOn == null)
                .ExecuteUpdateAsync(
                    updates => updates.SetProperty(candidate => candidate.ProcessedOn, now),
                    cancellationToken);
        }

        // The watermark advances past failures too. They stay pending in the table — visible to the
        // health check and to anybody reading it — but they must not be retried ahead of everything
        // enqueued since, because that is a queue that never drains.
        if (batch.Count > 0)
        {
            state.Advance(batch[^1].Id);
        }

        await PruneAsync(settings, cancellationToken);
        await RecordAsync(cancellationToken);

        return applied.Count;
    }

    private async Task ApplyAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var applied = false;

        foreach (var handler in handlers)
        {
            if (!string.Equals(handler.MessageType, message.Type, StringComparison.Ordinal)) continue;

            await handler.HandleAsync(message, cancellationToken);
            applied = true;
        }

        if (applied) return;

        // A type nothing handles is not an error to retry. It arrived from a newer deployment
        // writing to the same database, and dropping it is better than failing every pass over it.
        logger.LogWarning(
            "Outbox message {MessageId} has unhandled type {MessageType} and was skipped.",
            message.Id,
            message.Type);
    }

    private async Task PruneAsync(OutboxOptions settings, CancellationToken cancellationToken)
    {
        var retention = TimeSpan.FromHours(Math.Clamp(settings.RetentionHours, 1, 24 * 30));
        var cutoff = clock.GetUtcNow() - retention;

        await context.OutboxMessages
            .Where(message => message.ProcessedOn != null && message.ProcessedOn < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task RecordAsync(CancellationToken cancellationToken)
    {
        var pending = await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.ProcessedOn == null)
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Oldest = group.Min(message => message.CreatedOn) })
            .FirstOrDefaultAsync(cancellationToken);

        state.Record(clock.GetUtcNow(), pending?.Count ?? 0, pending?.Oldest);
    }

    /// <summary>Keeps a stored failure reason inside the column it goes in.</summary>
    private static string Truncate(string message) =>
        message.Length <= FieldLengths.Reason ? message : message[..FieldLengths.Reason];
}
