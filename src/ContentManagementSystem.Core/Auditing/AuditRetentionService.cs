using ContentManagementSystem.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Auditing;

/// <summary>
/// What one audit retention sweep did.
/// </summary>
/// <param name="Cutoff">Rows written before this instant were eligible, or null when nothing was.</param>
/// <param name="Removed">How many rows were deleted.</param>
/// <param name="Remaining">Whether more rows were eligible than one sweep would take.</param>
public readonly record struct AuditSweepResult(DateTimeOffset? Cutoff, int Removed, bool Remaining)
{
    /// <summary>The result of a sweep on a site that has not configured a window.</summary>
    /// <remarks>
    /// Keeping everything is what an unanswered compliance question deserves: a sweep that invented a
    /// window would be a system deciding, on its own, how long an organisation's evidence lasts.
    /// </remarks>
    public static AuditSweepResult KeptEverything { get; } = new(null, 0, false);
}

/// <summary>
/// Prunes <c>AuditLog</c> to a configured window (task P9-25, spec section 11.7).
/// </summary>
/// <remarks>
/// <strong>The table this prunes grows with editorial activity and nothing else bounds it.</strong>
/// <c>AuditLogInterceptor</c> writes a row for every tracked change, so a busy site adds rows for as
/// long as it is used — and the cost is not only disk: the table is written on the same
/// <c>SaveChanges</c> as the content, so every insert into it is on the path of every save an editor
/// makes.
/// <para>
/// The window is <c>SiteSettings.AuditLogRetentionDays</c>, matching what
/// <c>RetentionPolicy</c> does for versions, and <strong>zero means keep everything</strong> — which
/// is the default and is the honest answer while <strong>Q9</strong> is unanswered. What Legal
/// decides is the number; the sweep is the part that is the same either way.
/// </para>
/// <para>
/// Deliberately simpler than the version sweep, and that is a statement about the data rather than
/// about effort. A version has five reasons it might be spared, each protecting content an editor
/// would be upset to lose. An audit row has one property that matters — when it was written — and a
/// clause that spared some rows and not others would produce a log with holes in it, which is worse
/// evidence than a shorter one.
/// </para>
/// </remarks>
public interface IAuditRetentionService
{
    /// <summary>
    /// Deletes audit rows older than the configured window.
    /// </summary>
    /// <param name="cancellationToken">Token observed between batches.</param>
    /// <returns>What the sweep did.</returns>
    Task<AuditSweepResult> SweepAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAuditRetentionService" />
/// <param name="context">The application database context.</param>
/// <param name="clock">Source of now, so a test can place the cutoff without waiting.</param>
/// <param name="logger">Where a sweep reports what it removed.</param>
public sealed class AuditRetentionService(
    ApplicationDbContext context,
    TimeProvider clock,
    ILogger<AuditRetentionService> logger) : IAuditRetentionService
{
    /// <summary>
    /// Rows deleted per statement.
    /// </summary>
    /// <remarks>
    /// Batched rather than deleted in one statement, and the reason is lock escalation: SQL Server
    /// escalates row locks to a table lock somewhere around five thousand of them, and a table lock
    /// on <c>AuditLog</c> blocks every <c>SaveChanges</c> in the application — the interceptor writes
    /// to it on all of them. A first sweep on a site that has never had one would delete millions of
    /// rows, and the whole site would wait.
    /// </remarks>
    public const int BatchSize = 2_000;

    /// <summary>
    /// Batches one sweep will run before stopping.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than a target. A backlog is worked off over several nights instead of in one
    /// pass that holds the database for an hour, and <see cref="AuditSweepResult.Remaining"/> says
    /// when that is happening so it is visible rather than inferred.
    /// </remarks>
    public const int MaxBatchesPerSweep = 50;

    /// <inheritdoc />
    public async Task<AuditSweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        var days = await context.SiteSettings
            .AsNoTracking()
            .Select(settings => (int?)settings.AuditLogRetentionDays)
            .FirstOrDefaultAsync(cancellationToken);

        if (days is not > 0)
        {
            return AuditSweepResult.KeptEverything;
        }

        var cutoff = clock.GetUtcNow().AddDays(-days.Value);
        var removed = 0;

        for (var batch = 0; batch < MaxBatchesPerSweep; batch++)
        {
            // ExecuteDelete rather than loading and removing: an audit row has no navigation
            // properties and no soft delete, so there is nothing for the change tracker to do except
            // materialise two thousand entities on the way to discarding them.
            var deleted = await context.AuditLogs
                .Where(row => row.DateTime < cutoff)
                .OrderBy(row => row.Id)
                .Take(BatchSize)
                .ExecuteDeleteAsync(cancellationToken);

            removed += deleted;

            if (deleted < BatchSize)
            {
                logger.LogInformation(
                    "Audit retention removed {Removed} row(s) written before {Cutoff}.",
                    removed,
                    cutoff);

                return new AuditSweepResult(cutoff, removed, Remaining: false);
            }
        }

        // The ceiling was reached, so there is more to do than one night's sweep takes. Reported at
        // warning level: a backlog that never clears is a window that is not being kept.
        logger.LogWarning(
            "Audit retention removed {Removed} row(s) written before {Cutoff} and stopped at its " +
            "batch ceiling. More rows are eligible; the next sweep continues.",
            removed,
            cutoff);

        return new AuditSweepResult(cutoff, removed, Remaining: true);
    }
}
