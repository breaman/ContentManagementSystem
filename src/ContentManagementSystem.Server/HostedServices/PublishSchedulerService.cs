using ContentManagementSystem.Core.Scheduling;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.HostedServices;

/// <summary>
/// Ticks the scheduled-publish poller every thirty seconds (task P7-13, spec section 11.6).
/// </summary>
/// <param name="runner">One pass: claim what is due, run it, record what happened.</param>
/// <param name="options">Whether to run at all, and how often.</param>
/// <param name="clock">Source of the timer, so a test host can drive it without waiting.</param>
/// <param name="logger">Log for the loop's own lifecycle and for a pass that threw.</param>
/// <remarks>
/// Deliberately thin. Everything that decides anything is in <see cref="ScheduledJobRunner"/>, which
/// a test drives directly; this is a timer and an exception boundary, and both of those are things
/// there is no point asserting on.
/// <para>
/// A pass that throws is logged and the loop continues. The alternative — letting the exception
/// escape and stop the hosted service — means one malformed job silently ends scheduled publishing
/// for the lifetime of the process, which is the failure the <c>cms-scheduler</c> health check
/// exists to catch and which is better not to have.
/// </para>
/// </remarks>
public sealed class PublishSchedulerService(
    ScheduledJobRunner runner,
    IOptions<PublishSchedulerOptions> options,
    TimeProvider clock,
    ILogger<PublishSchedulerService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("The publish scheduler is switched off; no schedules will run here.");

            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.PollSeconds, 5, 3600));

        logger.LogInformation("The publish scheduler is polling every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval, clock);

        // Once before the first tick, so a process that starts up with an overdue schedule acts on
        // it immediately rather than after a poll interval of the page still not being live.
        await SafeRunAsync(stoppingToken);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            await SafeRunAsync(stoppingToken);
        }
    }

    private async Task SafeRunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await runner.RunOnceAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "A publish scheduler pass failed. The loop continues.");
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
