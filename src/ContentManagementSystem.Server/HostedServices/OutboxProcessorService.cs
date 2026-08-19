using ContentManagementSystem.Core.Caching;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.HostedServices;

/// <summary>
/// Ticks the outbox every five seconds (task P8-09, spec sections 16.3 and 24.4).
/// </summary>
/// <param name="services">Root provider, from which each pass takes its own scope.</param>
/// <param name="options">Whether to run, and how often.</param>
/// <param name="clock">Source of the timer, so a test host can drive it without waiting.</param>
/// <param name="logger">Log for the loop's own lifecycle and for a pass that threw.</param>
/// <remarks>
/// Thin, like the publish scheduler and for the same reason: everything that decides anything is in
/// <see cref="OutboxRunner"/>, which a test drives directly.
/// <para>
/// A scope per pass, because the runner holds a database context and this service outlives every
/// request. A context held for the process's lifetime accumulates a change tracker nobody clears
/// and returns increasingly stale reads.
/// </para>
/// <para>
/// Unlike the scheduler, this runs on <em>every</em> instance. Each has its own in-process caches to
/// evict, so an instance that does not poll is an instance serving pages the publish already
/// replaced.
/// </para>
/// </remarks>
public sealed class OutboxProcessorService(
    IServiceScopeFactory services,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxProcessorService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogWarning(
                "The cache invalidation outbox is switched off on this instance. Published changes " +
                "will not evict its caches until their entries expire.");

            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.PollSeconds, 1, 300));

        logger.LogInformation("The cache invalidation outbox is polling every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval, clock);

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
            await using var scope = services.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The loop continues. One failed pass must not end invalidation for the lifetime of the
            // process; the cms-outbox health check is what makes a persistent failure visible.
            logger.LogError(exception, "An outbox pass failed. The loop continues.");
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
