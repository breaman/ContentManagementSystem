using System.ComponentModel;

using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Shared.Common;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// The route cache of spec section 16.1: URL to page, and nothing else (task P8-08).
/// </summary>
/// <param name="inner">The resolver that actually queries.</param>
/// <param name="cache">The hybrid cache.</param>
/// <param name="options">Lifetime and whether to cache at all.</param>
/// <remarks>
/// <strong>Only a resolution to a live page is cached.</strong> The other two answers are left to
/// the database every time, and each for its own reason:
/// <list type="bullet">
/// <item><description>A <em>miss</em> has no tag that could evict it. What changes it is somebody
/// publishing a page at that URL — an event that knows the page but not the URLs that failed to
/// find it — so a cached miss would 404 a newly published page for a quarter of an hour after it
/// went live.</description></item>
/// <item><description>A <em>redirect</em> is counted each time it is followed, so caching it would
/// trade an indexed lookup for an undercounted report; and a redirect an editor deletes has to stop
/// being followed at once.</description></item>
/// <item><description>A <em>non-canonical spelling</em> is a 301 to the right URL rather than a
/// page, and it must not be stored under a key the canonical form also uses.</description></item>
/// </list>
/// Which is why this reads and writes the cache in two steps rather than through
/// <c>GetOrCreateAsync</c>: that method stores whatever the factory returned, and the entry has to
/// carry the <c>page:{id}</c> tag of the page it resolved to — a tag that is only known afterwards.
/// Unpublishing or moving that page then evicts its URL alongside its rendered response.
/// </remarks>
public sealed class CachedRouteResolver(
    RouteResolver inner,
    HybridCache cache,
    IOptions<DeliveryCacheOptions> options) : IRouteResolver
{
    /// <summary>Reads the cache without falling through to the factory.</summary>
    private static readonly HybridCacheEntryOptions ReadOnly = new()
    {
        Flags = HybridCacheEntryFlags.DisableUnderlyingData,
    };

    /// <inheritdoc />
    public async Task<RouteResolution> ResolveAsync(
        string? url,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (!settings.Enabled) return await inner.ResolveAsync(url, cancellationToken);

        var normalized = SiteUrls.Normalize(url);

        // Only a request already in canonical form may be answered from the cache. The key is the
        // normalized URL, so `/About/` and `/about` share one entry — and a cached hit would serve
        // the page at both addresses instead of sending the first to the second with a 301
        // (spec section 10.3). A non-canonical spelling is rare, so paying a query for it is the
        // cheap half of this trade.
        if (!string.Equals(url, normalized, StringComparison.Ordinal))
        {
            return await inner.ResolveAsync(url, cancellationToken);
        }

        var key = $"cms.route:{normalized}";

        var cached = await cache.GetOrCreateAsync<CachedRoute?>(
            key,
            static _ => ValueTask.FromResult<CachedRoute?>(null),
            ReadOnly,
            cancellationToken: cancellationToken);

        if (cached is { PageId: > 0 }) return new RouteResolution(RouteResolutionKind.Page, cached.PageId);

        var resolved = await inner.ResolveAsync(url, cancellationToken);

        if (resolved is { Kind: RouteResolutionKind.Page, PageId: > 0, CanonicalUrl: null })
        {
            await cache.SetAsync(
                key,
                new CachedRoute(resolved.PageId),
                new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromMinutes(Math.Clamp(settings.RouteMinutes, 1, 1440)),
                },
                tags: [CacheTags.Page(resolved.PageId), CacheTags.All],
                cancellationToken: cancellationToken);
        }

        return resolved;
    }

    /// <summary>
    /// The cacheable part of a resolution: the page a URL is served by.
    /// </summary>
    /// <remarks>
    /// Immutable and sealed so the cache may hand the same instance to every caller rather than
    /// deserializing one per read.
    /// </remarks>
    [ImmutableObject(true)]
    public sealed record CachedRoute(int PageId);
}
