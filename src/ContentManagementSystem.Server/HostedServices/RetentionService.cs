using ContentManagementSystem.Core.Auditing;
using ContentManagementSystem.Core.Publishing;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.HostedServices;

/// <summary>
/// Runs both retention sweeps nightly (task P9-25, spec section 11.7).
/// </summary>
/// <param name="services">Root provider, from which each pass takes its own scope.</param>
/// <param name="options">Whether to run, how often, and how long to wait first.</param>
/// <param name="clock">Source of the timer, so a test host can drive it without waiting.</param>
/// <param name="logger">Log for the loop's own lifecycle and for a pass that threw.</param>
/// <remarks>
/// <strong>Both</strong>, and that is the part worth reading. The version sweep has existed since
/// <c>P2-13</c> and implements all five clauses of spec section 11.7 — and nothing called it. It was
/// reachable from a test and from nowhere else, so a deployment kept every version of every page
/// forever while a policy that said otherwise sat in the code. The audit sweep this task adds would
/// have been the second one in the same position.
/// <para>
/// Thin, like the outbox poller, the publish scheduler, and the search reconcile beside it:
/// everything that decides anything is in the two services, which a test drives directly. What is
/// here is the loop and the decision to keep going after a failure.
/// </para>
/// </remarks>
public sealed class RetentionService(
    IServiceScopeFactory services,
    IOptions<RetentionOptions> options,
    TimeProvider clock,
    ILogger<RetentionService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation(
                "Retention sweeps are switched off on this instance; superseded versions and audit " +
                "rows accumulate until something else removes them.");

            return;
        }

        var interval = TimeSpan.FromHours(Math.Clamp(settings.IntervalHours, 1, 24));
        var delay = TimeSpan.FromMinutes(Math.Clamp(settings.StartupDelayMinutes, 0, 24 * 60));

        logger.LogInformation(
            "Retention sweeps run every {Interval}, starting {Delay} from now.",
            interval,
            delay);

        try
        {
            await Task.Delay(delay, clock, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval, clock);

        await SafeRunAsync(stoppingToken);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            await SafeRunAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Runs one pass of each sweep.
    /// </summary>
    /// <param name="stoppingToken">Token observed while sweeping.</param>
    /// <remarks>
    /// Each in a scope of its own, so a failure in one leaves the other's context untouched — and
    /// each caught separately, so a version sweep that throws does not mean audit rows are kept for
    /// another day.
    /// </remarks>
    private async Task SafeRunAsync(CancellationToken stoppingToken)
    {
        await SafeAsync(
            "version",
            async provider => await provider.GetRequiredService<IVersionService>().PruneAsync(stoppingToken));

        await SafeAsync(
            "audit",
            async provider => await provider.GetRequiredService<IAuditRetentionService>().SweepAsync(stoppingToken));
    }

    private async Task SafeAsync(string name, Func<IServiceProvider, Task> sweep)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();

            await sweep(scope.ServiceProvider);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The loop continues. A sweep that throws once must not end retention for the lifetime
            // of the process; that is how a table grows without bound while a policy says it does not.
            logger.LogError(exception, "The {Sweep} retention sweep failed. The loop continues.", name);
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
