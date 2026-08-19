using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Search;

/// <summary>
/// Applies a <see cref="SearchIndexMessage"/>: rebuild these documents, once (task P8-18).
/// </summary>
/// <param name="context">The database, for the claim.</param>
/// <param name="indexer">Rebuilds the documents the message names.</param>
/// <param name="clock">Source of the claim timestamp.</param>
/// <param name="logger">Log for a payload that named nothing.</param>
/// <remarks>
/// <strong>This handler claims its message; the cache invalidation handler deliberately does not.</strong>
/// The difference is what is being written. Cache eviction touches each node's own memory, so every
/// node must apply every message; the search index is one shared table, and N nodes rebuilding the
/// same document concurrently is N−1 wasted passes and a real chance of two inserts racing on the
/// unique key.
/// <para>
/// The claim costs a window: an instance that claims a message and then dies leaves those documents
/// unindexed until the nightly reconcile repairs them. That is the trade the reconcile exists to
/// make affordable — the alternative, an unclaimed message, trades a certain cost on every publish
/// for an unlikely one after a crash.
/// </para>
/// </remarks>
public sealed class SearchIndexHandler(
    ApplicationDbContext context,
    ISearchIndexer indexer,
    TimeProvider clock,
    ILogger<SearchIndexHandler> logger) : IOutboxMessageHandler
{
    /// <inheritdoc />
    public string MessageType => SearchIndexMessage.MessageType;

    /// <inheritdoc />
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (SearchIndexMessage.FromJson(message.PayloadJson) is not { EntityIds.Count: > 0 } payload)
        {
            logger.LogWarning(
                "Outbox message {MessageId} carried nothing to index and was skipped.",
                message.Id);

            return;
        }

        // One statement, so the claim is atomic against every other instance polling the same table.
        var claimed = await context.OutboxMessages
            .Where(candidate => candidate.Id == message.Id && candidate.ProcessedOn == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(candidate => candidate.ProcessedOn, clock.GetUtcNow()),
                cancellationToken);

        if (claimed == 0) return;

        await indexer.IndexAsync(payload.Kind, payload.EntityIds, cancellationToken);
    }
}
