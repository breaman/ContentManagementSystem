using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Server.Delivery;

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ContentManagementSystem.Server.Caching;

/// <summary>
/// The output-cache policy public pages are served under (tasks P8-06, P8-07, P8-12).
/// </summary>
/// <param name="options">The entry lifetime.</param>
/// <param name="metrics">Records hits and misses, which is where the hit ratio comes from.</param>
/// <remarks>
/// Three rules, and each of them is a correctness requirement from spec section 16.4 rather than a
/// tuning choice.
/// <list type="number">
/// <item><description><strong>Anonymous only.</strong> A request carrying an identity — an
/// authenticated principal, an <c>Authorization</c> header, or an identity cookie — neither reads
/// from nor writes to the cache. This runs after the authentication middleware precisely so the
/// principal is known here; an editor's request must never be stored where a visitor can be handed
/// it, nor served from an entry a visitor populated (acceptance criterion P8 #6).</description></item>
/// <item><description><strong>Tags come from the render.</strong> The delivery endpoint leaves the
/// tags the page actually depended on on <c>HttpContext.Items</c>, and they are attached to the
/// entry on the way out. Deriving them here instead would mean a second model of what a page
/// contains, and the two would eventually disagree (spec section 16.2).</description></item>
/// <item><description><strong>A bounded lifetime.</strong> Entries expire after an hour even though
/// publishing evicts them, so an invalidation that never arrived self-heals within one rather than
/// leaving a page stale until somebody edits it again (task P8-12, risk R17).</description></item>
/// </list>
/// <para>
/// Nothing varies by cookie, per spec section 16.4: a cache keyed on a cookie is a cache with one
/// entry per visitor, which is not a cache.
/// </para>
/// </remarks>
public sealed class CmsPageCachePolicy(IOptions<DeliveryCacheOptions> options, CmsMetrics metrics)
    : IOutputCachePolicy
{
    /// <summary>Prefix of the cookies ASP.NET Core Identity issues.</summary>
    /// <remarks>
    /// Matched by prefix rather than by exact name so that the application, external, and two-factor
    /// cookies are all covered, including under a renamed scheme. A request holding any of them
    /// belongs to somebody who is signing in, and its response is not shared content.
    /// </remarks>
    public const string IdentityCookiePrefix = ".AspNetCore.";

    /// <inheritdoc />
    ValueTask IOutputCachePolicy.CacheRequestAsync(
        OutputCacheContext context,
        CancellationToken cancellationToken)
    {
        var eligible = options.Value.Enabled && IsCacheable(context.HttpContext);

        context.EnableOutputCaching = eligible;
        context.AllowCacheLookup = eligible;
        context.AllowCacheStorage = eligible;

        // Locking on, so a cold entry under load is populated once rather than by every concurrent
        // request at the same moment — the same stampede the content cache guards against, one
        // layer up.
        context.AllowLocking = true;

        context.ResponseExpirationTimeSpan = TimeSpan.FromMinutes(
            Math.Clamp(options.Value.OutputMinutes, 1, 1440));

        // Varies by nothing. Spec section 16.4 allows exactly one Vary in the application, and it is
        // Accept on the media endpoint.
        context.CacheVaryByRules.QueryKeys = "*";

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    ValueTask IOutputCachePolicy.ServeFromCacheAsync(
        OutputCacheContext context,
        CancellationToken cancellationToken)
    {
        metrics.RecordCacheHit();

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    ValueTask IOutputCachePolicy.ServeResponseAsync(
        OutputCacheContext context,
        CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;

        // A response that sets a cookie is about the visitor who asked for it, whatever the request
        // looked like. Storing one hands the next visitor somebody else's cookie.
        var cacheable = response.StatusCode is StatusCodes.Status200OK &&
                        string.IsNullOrEmpty(response.Headers.SetCookie);

        context.AllowCacheStorage = context.AllowCacheStorage && cacheable;

        if (!context.AllowCacheStorage) return ValueTask.CompletedTask;

        foreach (var tag in Tags(context.HttpContext))
        {
            context.Tags.Add(tag);
        }

        metrics.RecordCacheMiss();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The tags the render accumulated, or the site-wide tag when a response published none.
    /// </summary>
    /// <remarks>
    /// The fallback matters: an entry with no tags at all can only be evicted by waiting for it to
    /// expire, so a response that forgot to publish its dependencies is at least reachable by a
    /// purge-all rather than being stuck for an hour.
    /// </remarks>
    private static IReadOnlyList<string> Tags(HttpContext http) =>
        http.Items.TryGetValue(DeliveryEndpoint.CacheTagsItemKey, out var value) &&
        value is IReadOnlyList<string> { Count: > 0 } tags
            ? tags
            : [CacheTags.All];

    private static bool IsCacheable(HttpContext http)
    {
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
        {
            return false;
        }

        if (http.User.Identity?.IsAuthenticated is true) return false;

        if (!string.IsNullOrEmpty(http.Request.Headers[HeaderNames.Authorization])) return false;

        foreach (var cookie in http.Request.Cookies)
        {
            if (cookie.Key.StartsWith(IdentityCookiePrefix, StringComparison.Ordinal)) return false;
        }

        return true;
    }
}
