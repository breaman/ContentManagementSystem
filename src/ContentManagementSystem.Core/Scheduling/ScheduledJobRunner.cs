using System.Data;

using ContentManagementSystem.Core.Notifications;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Scheduling;

/// <summary>
/// One pass of the scheduled-publish poller: claim what is due, run it, record what happened
/// (tasks P7-13 to P7-15, spec section 11.6).
/// </summary>
/// <param name="scopes">Factory for the scope the claim runs in.</param>
/// <param name="identities">Runs each job as the editor who scheduled it.</param>
/// <param name="state">Where the lag reading the gauge and health check share is written.</param>
/// <param name="options">Poll interval, batch size, and how long a claim may go stale.</param>
/// <param name="clock">Source of the current time, so a test can make a schedule come due.</param>
/// <param name="logger">Log for every claim and every outcome.</param>
/// <remarks>
/// The whole of the risk here is <strong>R16</strong>: two instances polling one table and both
/// publishing the same page. The answer is that a job leaves <c>Pending</c> only through a single
/// <c>UPDATE … OUTPUT</c>, which is atomic against every other writer — the row is claimed and its
/// identity returned in one statement, so a second instance running the same statement a millisecond
/// later matches nothing and does nothing. No lock is taken, no read-then-write window exists, and
/// there is nothing to get wrong in a retry (criterion P7 #7).
/// <para>
/// Separated from the hosted service that ticks it so it can be driven directly by a test: the
/// interesting behaviour is what one pass does, and asserting on it through a timer would mean a
/// suite that waits.
/// </para>
/// </remarks>
public sealed class ScheduledJobRunner(
    IServiceScopeFactory scopes,
    IJobIdentityScopeFactory identities,
    SchedulerState state,
    IOptions<PublishSchedulerOptions> options,
    TimeProvider clock,
    ILogger<ScheduledJobRunner> logger)
{
    /// <summary>Identifies this process in a claim, for diagnosing one that never finished.</summary>
    private static readonly string Instance =
        $"{Environment.MachineName}/{Environment.ProcessId}"[
            ..Math.Min($"{Environment.MachineName}/{Environment.ProcessId}".Length, FieldLengths.SchedulerInstance)];

    /// <summary>
    /// Claims the jobs that are due and runs them.
    /// </summary>
    /// <param name="cancellationToken">Token observed between jobs and while claiming.</param>
    /// <returns>How many jobs this pass ran.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var settings = options.Value;

        var claimed = await ClaimAsync(now, settings, cancellationToken);

        await RecordLagAsync(now, cancellationToken);

        if (claimed.Count == 0) return 0;

        logger.LogInformation("Scheduler claimed {Count} job(s) due at or before {Now}.", claimed.Count, now);

        var ran = 0;

        foreach (var job in claimed)
        {
            if (cancellationToken.IsCancellationRequested) break;

            await RunAsync(job, cancellationToken);
            ran++;
        }

        return ran;
    }

    /// <summary>
    /// Takes ownership of every due job in one atomic statement.
    /// </summary>
    /// <remarks>
    /// Written as raw ADO rather than through EF. <c>UPDATE … OUTPUT</c> is a statement EF has no
    /// expression for, and the alternatives it does have — read the rows, then write them — reopen
    /// exactly the window this exists to close.
    /// <para>
    /// The second half of the predicate reclaims jobs whose claimant went away: a process killed
    /// between claiming and finishing would otherwise leave a page permanently scheduled and never
    /// published, which is the one failure a scheduler must not have.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<ClaimedJob>> ClaimAsync(
        DateTimeOffset now,
        PublishSchedulerOptions settings,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE TOP (@batch) [ScheduledJobs]
            SET [State] = @claimed,
                [ClaimedOn] = @now,
                [ClaimedBy] = @instance
            OUTPUT inserted.[Id], inserted.[PageId], inserted.[PageVersionId], inserted.[Kind],
                   inserted.[OwnerUserId]
            WHERE ([State] = @pending AND [RunOn] <= @now)
               OR ([State] = @claimed AND [ClaimedOn] < @stale)
            """;

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var connection = context.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;

        if (opened) await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            // Every parameter is given its type explicitly rather than inferred from the value, and
            // that is not fussiness. `new SqlParameter("@pending", 0)` binds to the
            // (string, SqlDbType) overload — the literal zero converts to any enum, and SqlDbType
            // zero is BigInt — producing a parameter with a type and no value, and a query that
            // fails at run time saying the parameter was never supplied.
            command.Parameters.Add(Parameter("@batch", SqlDbType.Int, Math.Clamp(settings.BatchSize, 1, 500)));
            command.Parameters.Add(Parameter("@pending", SqlDbType.Int, (int)ScheduledJobState.Pending));
            command.Parameters.Add(Parameter("@claimed", SqlDbType.Int, (int)ScheduledJobState.Claimed));
            command.Parameters.Add(Parameter("@now", SqlDbType.DateTimeOffset, now));
            command.Parameters.Add(Parameter(
                "@stale",
                SqlDbType.DateTimeOffset,
                now.AddMinutes(-Math.Clamp(settings.StaleClaimMinutes, 1, 1440))));
            command.Parameters.Add(Parameter("@instance", SqlDbType.NVarChar, Instance));

            var claimed = new List<ClaimedJob>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                claimed.Add(new ClaimedJob(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    (ScheduledJobKind)reader.GetInt32(3),
                    reader.GetInt32(4)));
            }

            return claimed;
        }
        finally
        {
            if (opened) await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Builds one command parameter with its type stated rather than inferred.</summary>
    private static SqlParameter Parameter(string name, SqlDbType type, object value) =>
        new(name, type) { Value = value };

    /// <summary>Runs one claimed job as the editor who scheduled it.</summary>
    private Task RunAsync(ClaimedJob job, CancellationToken cancellationToken) =>
        identities.RunAsAsync(
            job.OwnerUserId,
            async (services, token) =>
            {
                var context = services.GetRequiredService<ApplicationDbContext>();
                var publishing = services.GetRequiredService<IPublishingService>();

                string? failure = null;

                try
                {
                    // The identical path a manual publish takes, permission checks and validation
                    // included (spec section 11.6). A second publishing routine for scheduled
                    // publishes is how the two quietly stop agreeing about what a valid page is.
                    failure = job.Kind is ScheduledJobKind.Publish
                        ? await PublishAsync(publishing, job, token)
                        : await UnpublishAsync(publishing, job, token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Scheduled job {JobId} threw.", job.Id);
                    failure = "The scheduled operation failed unexpectedly. The error is in the server log.";
                }

                await SettleAsync(context, services, job, failure, token);
            },
            cancellationToken);

    private static async Task<string?> PublishAsync(
        IPublishingService publishing,
        ClaimedJob job,
        CancellationToken cancellationToken)
    {
        // Warnings are acknowledged, errors are not. An editor scheduling a publish has already been
        // shown the warnings on the way in; refusing at midnight for something they were told about
        // at four o'clock would be a schedule that never runs.
        var result = await publishing.PublishAsync(job.PageId, acknowledgeWarnings: true, cancellationToken);

        return result.IsSuccess ? null : Describe(result.Diagnostics);
    }

    /// <summary>
    /// Retires a page, including whatever redirect the site is configured to leave behind.
    /// </summary>
    /// <remarks>
    /// The redirect behaviour of task P7-15 lives in <c>PublishingService.UnpublishAsync</c> rather
    /// than here, so that pressing the button and asking for it to be pressed at midnight do the
    /// same thing. A scheduled retirement that quietly differed from a manual one would be a
    /// difference nobody could see until the traffic reports came in.
    /// </remarks>
    private static async Task<string?> UnpublishAsync(
        IPublishingService publishing,
        ClaimedJob job,
        CancellationToken cancellationToken)
    {
        var result = await publishing.UnpublishAsync(job.PageId, cancellationToken);

        return result.IsSuccess ? null : Describe(result.Diagnostics);
    }

    /// <summary>Marks the job done or failed, and tells its owner which.</summary>
    private async Task SettleAsync(
        ApplicationDbContext context,
        IServiceProvider services,
        ClaimedJob job,
        string? failure,
        CancellationToken cancellationToken)
    {
        var row = await context.ScheduledJobs
            .Include(candidate => candidate.Page)
            .ThenInclude(page => page!.DraftVersion)
            .FirstOrDefaultAsync(candidate => candidate.Id == job.Id, cancellationToken);

        if (row is null) return;

        // Failed is terminal and is not retried. A version that fails validation at nine fails it at
        // half past, and a blind retry turns one notification into one every thirty seconds
        // (spec section 11.6, criterion P7 #8).
        row.State = failure is null ? ScheduledJobState.Completed : ScheduledJobState.Failed;
        row.CompletedOn = clock.GetUtcNow();
        row.FailureReason = failure is null
            ? null
            : failure[..Math.Min(failure.Length, FieldLengths.Reason)];

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Scheduled {Kind} of page {PageId} {Outcome}.",
            job.Kind,
            job.PageId,
            failure is null ? "succeeded" : $"failed: {failure}");

        await services.GetRequiredService<INotificationService>().NotifyAsync(
            row.OwnerUserId,
            failure is null
                ? NotificationKind.ScheduledPublishSucceeded
                : NotificationKind.ScheduledPublishFailed,
            row.PageId,
            row.Page.DraftVersion?.Title ?? $"Page {row.PageId}",
            actor: "The scheduler",
            note: failure,
            link: $"/admin/pages/{row.PageId}",

            // The job runs as the editor who scheduled it, so they are the caller as well as the
            // recipient — and "your scheduled publish failed" is exactly the message they must get.
            // The suppress-your-own-actions rule is about somebody having pressed a button just now.
            includeCaller: true,
            cancellationToken: cancellationToken);
    }

    /// <summary>Reads how overdue the oldest waiting job is, for the gauge and the health check.</summary>
    private async Task RecordLagAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var oldest = await context.ScheduledJobs
            .AsNoTracking()
            .Where(job => job.State == ScheduledJobState.Pending && job.RunOn <= now)
            .OrderBy(job => job.RunOn)
            .Select(job => (DateTimeOffset?)job.RunOn)
            .FirstOrDefaultAsync(cancellationToken);

        state.Record(now, oldest is { } due ? now - due : TimeSpan.Zero);
    }

    /// <summary>Turns a refusal into the sentence its owner is shown.</summary>
    private static string Describe(ValidationResult diagnostics) =>
        diagnostics.Diagnostics.Count == 0
            ? "The scheduled operation was refused with no reason given."
            : string.Join(" ", diagnostics.Diagnostics.Select(diagnostic => diagnostic.Message));

    /// <summary>One job, as the claim statement returned it.</summary>
    private sealed record ClaimedJob(
        int Id,
        int PageId,
        int? PageVersionId,
        ScheduledJobKind Kind,
        int OwnerUserId);
}
