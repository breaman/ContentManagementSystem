using System.Collections.Concurrent;

namespace ContentManagementSystem.Core.Media.Renditions;

/// <summary>
/// One lock per rendition key, so a burst of cold requests produces one encode (task P5-13,
/// spec section 13.5).
/// </summary>
/// <remarks>
/// The scenario this exists for: a popular page is published, twenty visitors arrive at once, and
/// every one of them requests the same not-yet-generated hero image. Without a lock that is twenty
/// simultaneous decodes of a 4000 px original — enough to saturate every core on the instance for
/// the sake of nineteen results that are thrown away.
/// <para>
/// <strong>Per key, not global.</strong> A single lock around all generation would serialise
/// unrelated images and turn one slow encode into a queue behind it. The point is to deduplicate
/// identical work, not to stop concurrent work.
/// </para>
/// <para>
/// Locks are removed once the last waiter leaves, so the dictionary is bounded by concurrent
/// generations in flight rather than by the number of renditions the site has ever produced.
/// Registered as a singleton — a per-request instance would hand each request its own lock, which is
/// the same as having none.
/// </para>
/// </remarks>
public sealed class RenditionKeyLocks
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>How many keys are currently being generated. Diagnostics and tests only.</summary>
    public int ActiveCount => _entries.Count;

    /// <summary>
    /// Waits for exclusive use of one rendition key.
    /// </summary>
    /// <param name="key">The rendition's canonical key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A handle to dispose once generation is finished.</returns>
    /// <example>
    /// <code>
    /// using var handle = await locks.AcquireAsync(spec.ToCanonicalString(), cancellationToken);
    /// // re-check storage here: the request that held the lock before this one may have
    /// // generated exactly what is being asked for.
    /// </code>
    /// </example>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var entry = _entries.AddOrUpdate(
            key,
            _ => new Entry(),
            (_, existing) => existing.Retain());

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A cancelled waiter still holds a reference count, and leaving it behind would leak the
            // entry for the lifetime of the process.
            Release(key, entry, releaseSemaphore: false);

            throw;
        }

        return new Handle(this, key, entry);
    }

    private void Release(string key, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore) entry.Semaphore.Release();

        if (entry.ReleaseReference() is not 0) return;

        // Removed only when this reference count reaches zero and the dictionary still holds this
        // exact entry — a request that arrived in between will have added its own, and removing that
        // one would let two callers generate the same rendition at once.
        if (_entries.TryRemove(new KeyValuePair<string, Entry>(key, entry)))
        {
            entry.Semaphore.Dispose();
        }
    }

    private sealed class Entry
    {
        private int _references = 1;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public Entry Retain()
        {
            Interlocked.Increment(ref _references);

            return this;
        }

        public int ReleaseReference() => Interlocked.Decrement(ref _references);
    }

    private sealed class Handle(RenditionKeyLocks owner, string key, Entry entry) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            owner.Release(key, entry, releaseSemaphore: true);
        }
    }
}
