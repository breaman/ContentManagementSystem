using ContentManagementSystem.Core.Search;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.HostedServices;

/// <summary>
/// Runs the search reconcile nightly (task P8-18, spec section 17.1).
/// </summary>
/// <param name="services">Root provider, from which each pass takes its own scope.</param>
/// <param name="options">Whether to run, how often, and how long to wait first.</param>
/// <param name="clock">Source of the timer, so a test host can drive it without waiting.</param>
/// <param name="logger">Log for the loop's own lifecycle and for a pass that threw.</param>
/// <remarks>
/// Thin, like the outbox poller and the publish scheduler: everything that decides anything is in
/// <see cref="ISearchIndexer.ReconcileAsync"/>, which a test drives directly.
/// <para>
/// This is the mitigation for risk R18 rather than an optimisation. Indexing is asynchronous, and
/// every asynchronous path has a way to lose a message — a claim taken by an instance that then
/// died, an exception the runner counted and moved past, a write path added later that forgot to
/// enqueue. None of those announce themselves: a missing search result looks exactly like content
/// that does not mention the word.
/// </para>
/// </remarks>
public sealed class SearchReconcileService(
    IServiceScopeFactory services,
    IOptions<SearchOptions> options,
    TimeProvider clock,
    ILogger<SearchReconcileService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.ReconcileEnabled)
        {
            logger.LogInformation(
                "The search reconcile is switched off on this instance; the index is only as current " +
                "as the outbox has managed to keep it.");

            return;
        }

        var interval = TimeSpan.FromHours(Math.Clamp(settings.ReconcileHours, 1, 24 * 7));
        var delay = TimeSpan.FromMinutes(Math.Clamp(settings.ReconcileStartupDelayMinutes, 0, 24 * 60));

        logger.LogInformation(
            "The search reconcile runs every {Interval}, starting {Delay} from now.",
            interval,
            delay);

        try
        {
            // Deliberately not at startup. Several instances coming back at once would otherwise
            // all scan the content tables in the same second, on top of whatever restarted them.
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

    private async Task SafeRunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();

            var report = await scope.ServiceProvider
                .GetRequiredService<ISearchIndexer>()
                .ReconcileAsync(stoppingToken);

            if (report.FoundNothingWrong)
            {
                logger.LogInformation(
                    "The search reconcile examined {ExaminedCount} item(s) and found the index correct.",
                    report.Examined);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The loop continues. A reconcile that throws once must not end the repair pass for the
            // lifetime of the process — this is the backstop, and a backstop that gives up quietly
            // is worse than none.
            logger.LogError(exception, "A search reconcile pass failed. The loop continues.");
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
