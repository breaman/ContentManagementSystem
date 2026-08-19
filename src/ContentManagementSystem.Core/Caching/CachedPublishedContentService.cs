using ContentManagementSystem.Core.Delivery;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// The published-content cache of spec section 16.1, wrapped around the real reader (task P8-08).
/// </summary>
/// <param name="inner">The service that actually queries.</param>
/// <param name="cache">The hybrid cache.</param>
/// <param name="options">Lifetime and whether to cache at all.</param>
/// <remarks>
/// A decorator rather than caching inside <c>PublishedContentService</c>, so that the query and the
/// caching of it can be read, tested, and switched off independently — and so preview, which must
/// never be served from a cache, simply does not go through this type.
/// <para>
/// <strong>A miss is cached too.</strong> A page that resolves to no published version stores a null
/// under the same <c>page:{id}</c> tag, which is what stops a hammered URL becoming one query per
/// request — and publishing that page evicts the tag, so the null cannot outlive the condition that
/// produced it.
/// </para>
/// <para>
/// The entry is tagged rather than keyed by anything mutable, so an editor's publish evicts exactly
/// this page and nothing else (acceptance criterion P8 #4).
/// </para>
/// <para>
/// <strong>The stored row is cached, not the rendered-ready object.</strong> <c>HybridCache</c>
/// serializes everything it writes — the <c>[ImmutableObject(true)]</c> optimization applies to
/// reads — and a <c>PublishedContent</c> carries a live <c>JsonElement</c> and a captured schema
/// whose unset field configurations are <c>default(JsonElement)</c>, which
/// <c>System.Text.Json</c> refuses to write at all. What the cache saves is the database round trip;
/// parsing the payload again per request costs microseconds and keeps the cached form something a
/// serializer can actually store.
/// </para>
/// </remarks>
public sealed class CachedPublishedContentService(
    PublishedContentService inner,
    HybridCache cache,
    IOptions<DeliveryCacheOptions> options) : IPublishedContentService
{
    /// <inheritdoc />
    public async Task<PublishedContent?> GetAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (pageId <= 0) return null;

        var settings = options.Value;

        if (!settings.Enabled) return await inner.GetAsync(pageId, cancellationToken);

        var row = await cache.GetOrCreateAsync(
            $"cms.content.page:{pageId}",
            (Inner: inner, PageId: pageId),
            static (state, token) =>
                new ValueTask<PublishedContentService.PublishedContentRow?>(
                    state.Inner.GetRowAsync(state.PageId, token)),
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(Math.Clamp(settings.ContentMinutes, 1, 1440)),
            },
            tags: [CacheTags.Page(pageId)],
            cancellationToken: cancellationToken);

        return row is null ? null : inner.Materialize(row);
    }
}
