using ContentManagementSystem.Core.Caching;

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Hybrid;

namespace ContentManagementSystem.Server.Caching;

/// <summary>
/// Evicts both caches a tag can appear in (task P8-10, spec section 16.2).
/// </summary>
/// <param name="store">The output cache — in memory, or Redis when one is configured.</param>
/// <param name="cache">The published-content and route cache.</param>
/// <param name="logger">Log for what was evicted, which is what an invalidation bug is read from.</param>
/// <remarks>
/// Both stores, in that order, because a page is assembled from both: the output cache holds the
/// finished HTML and the hybrid cache holds the content it was rendered from. Evicting only the
/// first would re-render the page from a cached version of the content that has just changed —
/// which looks exactly like an invalidation that did not work.
/// <para>
/// <c>IOutputCacheStore.EvictByTagAsync</c> takes one tag at a time and is the only tag-aware
/// operation the interface offers, which is why this loops.
/// </para>
/// </remarks>
public sealed class OutputCacheInvalidator(
    IOutputCacheStore store,
    HybridCache cache,
    ILogger<OutputCacheInvalidator> logger) : ICacheInvalidator
{
    /// <inheritdoc />
    public async Task InvalidateAsync(
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);

        if (tags.Count == 0) return;

        foreach (var tag in tags)
        {
            await store.EvictByTagAsync(tag, cancellationToken);
        }

        await cache.RemoveByTagAsync(tags, cancellationToken);

        logger.LogInformation("Evicted cache tags {CacheTags}.", string.Join(", ", tags));
    }
}
