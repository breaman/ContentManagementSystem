using ContentManagementSystem.Data.Models.Cms;

using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// Applies a <see cref="CacheInvalidationMessage"/>: evict these tags, here (task P8-10).
/// </summary>
/// <param name="invalidator">Evicts the tags a message names, from both stores.</param>
/// <param name="logger">Log for a payload that carried nothing to evict.</param>
/// <remarks>
/// Runs on <strong>every</strong> instance, deliberately, and does not claim the message. Each node
/// has its own in-process cache, so a message claimed by one node would leave the others serving
/// what they had — the multi-instance failure spec section 16.3 exists to prevent. Eviction is
/// idempotent, so N nodes is N−1 no-ops.
/// </remarks>
public sealed class CacheInvalidationHandler(
    ICacheInvalidator invalidator,
    ILogger<CacheInvalidationHandler> logger) : IOutboxMessageHandler
{
    /// <inheritdoc />
    public string MessageType => CacheInvalidationMessage.MessageType;

    /// <inheritdoc />
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (CacheInvalidationMessage.FromJson(message.PayloadJson) is not { Tags.Count: > 0 } payload)
        {
            logger.LogWarning(
                "Outbox message {MessageId} carried no readable cache tags and was skipped.",
                message.Id);

            return;
        }

        await invalidator.InvalidateAsync(payload.Tags, cancellationToken);
    }
}
