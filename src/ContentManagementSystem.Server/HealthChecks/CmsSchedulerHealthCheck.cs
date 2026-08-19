using ContentManagementSystem.Core.Scheduling;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.HealthChecks;

/// <summary>
/// Reports whether scheduled publishing is actually happening (task P7-17).
/// </summary>
/// <param name="state">What the poller last saw.</param>
/// <param name="options">The lag threshold and the poll interval it is judged against.</param>
/// <param name="clock">Source of the current time.</param>
/// <remarks>
/// Two ways to be unhealthy, and they are different failures.
/// <list type="bullet">
/// <item><description><strong>Lag.</strong> Something is overdue by more than the configured
/// threshold — five minutes by default. The poller is running and is not keeping up, or a job keeps
/// being claimed and abandoned.</description></item>
/// <item><description><strong>Silence.</strong> No pass has completed in several poll intervals.
/// The loop has stopped, which is the failure that produces no symptom at all until somebody notices
/// a page that never went live.</description></item>
/// </list>
/// <para>
/// A process with the poller switched off reports healthy and says so. A deployment that runs the
/// scheduler on one instance out of four should not have three instances failing their probes.
/// </para>
/// </remarks>
public sealed class CmsSchedulerHealthCheck(
    SchedulerState state,
    IOptions<PublishSchedulerOptions> options,
    TimeProvider clock) : IHealthCheck
{
    /// <summary>Name this check is registered under.</summary>
    public const string Name = "cms-scheduler";

    /// <summary>How many poll intervals of silence are treated as a stopped loop.</summary>
    private const int SilentIntervals = 4;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "The publish scheduler is switched off on this instance."));
        }

        var lag = state.LagSeconds;
        var data = new Dictionary<string, object> { ["lagSeconds"] = lag };

        if (state.LastPollOn is not { } last)
        {
            // Degraded rather than unhealthy: a process that has just started has not polled yet,
            // and failing the probe on startup would take an instance out of rotation for the sake
            // of a reading it has not had time to take.
            return Task.FromResult(HealthCheckResult.Degraded(
                "The publish scheduler has not completed a pass yet.",
                data: data));
        }

        data["lastPollOn"] = last;

        var silence = clock.GetUtcNow() - last;
        var allowedSilence = TimeSpan.FromSeconds(
            Math.Clamp(settings.PollSeconds, 5, 3600) * SilentIntervals);

        if (silence > allowedSilence)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"The publish scheduler has not polled for {silence.TotalSeconds:N0} seconds. " +
                "Scheduled publishes are not running.",
                data: data));
        }

        if (lag > settings.UnhealthyLagSeconds)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Scheduled publishing is {lag:N0} seconds behind, past the " +
                $"{settings.UnhealthyLagSeconds} second threshold.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "Scheduled publishing is up to date.",
            data: data));
    }
}
