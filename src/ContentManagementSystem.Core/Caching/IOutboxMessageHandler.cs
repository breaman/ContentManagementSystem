using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// Applies one kind of outbox message (spec section 16.3).
/// </summary>
/// <remarks>
/// The outbox began as a single-purpose queue for cache invalidation and gained a second message
/// type with the search index (task P8-18). Both want the same guarantee — enqueued inside the
/// writer's transaction, dispatched afterwards by a poller — so they share the table and the runner
/// and differ only here.
/// <para>
/// <strong>Every instance runs every handler over every message.</strong> That is right for cache
/// eviction, where each node has its own in-process cache, and wrong for anything writing shared
/// state, which is why <see cref="HandleAsync"/> receives the message row itself: a handler that
/// must run once claims it, and the two policies stay visible in the handler that chose them rather
/// than buried in the runner.
/// </para>
/// </remarks>
public interface IOutboxMessageHandler
{
    /// <summary>The <c>OutboxMessage.Type</c> value this handler applies.</summary>
    string MessageType { get; }

    /// <summary>
    /// Applies one message.
    /// </summary>
    /// <param name="message">The stored row, payload and all.</param>
    /// <param name="cancellationToken">Token observed throughout.</param>
    /// <remarks>
    /// Throwing leaves the message pending and counted against it; the runner logs it and carries
    /// on with the rest of the batch. A payload that can never be applied should be skipped rather
    /// than thrown on — one bad row must not become a queue that never drains.
    /// </remarks>
    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
