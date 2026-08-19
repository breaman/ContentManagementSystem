using ContentManagementSystem.Core.Caching;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.HealthChecks;

/// <summary>
/// Reports whether cache invalidation is actually being dispatched (task P8-13, spec section 24.2).
/// </summary>
/// <param name="state">What the outbox poller last saw.</param>
/// <param name="options">The backlog threshold and the poll interval it is judged against.</param>
/// <param name="clock">Source of the current time.</param>
/// <remarks>
/// The failure this exists for has no other symptom. When the outbox stops draining, every request
/// still succeeds and every page still renders — with content that was replaced hours ago. Nothing
/// in the logs says so, and the first report is a person asking why their edit is not live.
/// <list type="bullet">
/// <item><description><strong>Backlog.</strong> A message has been waiting longer than the
/// threshold — five minutes by default.</description></item>
/// <item><description><strong>Silence.</strong> No pass has completed in several poll intervals,
/// which means the loop itself has stopped.</description></item>
/// </list>
/// </remarks>
public sealed class CmsOutboxHealthCheck(
    OutboxState state,
    IOptions<OutboxOptions> options,
    TimeProvider clock) : IHealthCheck
{
    /// <summary>Name this check is registered under.</summary>
    public const string Name = "cms-outbox";

    /// <summary>How many poll intervals of silence are treated as a stopped loop.</summary>
    private const int SilentIntervals = 6;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            // Degraded rather than healthy, unlike the scheduler's equivalent. A deployment may
            // legitimately run the publish scheduler on one instance; an instance that does not
            // drain the outbox is serving stale pages, and there is no configuration in which that
            // is fine.
            return Task.FromResult(HealthCheckResult.Degraded(
                "Cache invalidation is switched off on this instance. Published changes will not " +
                "evict its caches until their entries expire."));
        }

        var data = new Dictionary<string, object>
        {
            ["pendingCount"] = state.PendingCount,
            ["watermark"] = state.Watermark,
        };

        if (state.LastPollOn is not { } last)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "The cache invalidation outbox has not completed a pass yet.",
                data: data));
        }

        data["lastPollOn"] = last;

        var now = clock.GetUtcNow();
        var silence = now - last;
        var allowedSilence = TimeSpan.FromSeconds(
            Math.Clamp(settings.PollSeconds, 1, 300) * SilentIntervals);

        if (silence > allowedSilence)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"The cache invalidation outbox has not polled for {silence.TotalSeconds:N0} seconds. " +
                "Published changes are not evicting caches.",
                data: data));
        }

        if (state.OldestPendingOn is { } oldest)
        {
            var backlog = now - oldest;

            data["oldestPendingAgeSeconds"] = backlog.TotalSeconds;

            if (backlog > TimeSpan.FromMinutes(Math.Clamp(settings.UnhealthyBacklogMinutes, 1, 1440)))
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"A cache invalidation has been waiting {backlog.TotalMinutes:N0} minutes. " +
                    "Published pages may be served stale.",
                    data: data));
            }
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "Cache invalidation is up to date.",
            data: data));
    }
}
