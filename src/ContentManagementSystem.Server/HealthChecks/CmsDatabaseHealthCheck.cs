using ContentManagementSystem.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ContentManagementSystem.Server.HealthChecks;

/// <summary>
/// Reports whether this instance can reach its database and is running against the schema it expects
/// (task P9-20, spec section 24.2).
/// </summary>
/// <param name="context">The application database context.</param>
/// <remarks>
/// Spec section 24.2 names <c>cms-database</c> as one of five checks and it was the one with no
/// implementation: Aspire's <c>EnrichSqlServerDbContext</c> registers a connectivity check of its
/// own, named <c>ApplicationDbContext</c>, which no runbook and no alert rule refers to. A check
/// nobody can name is a check nobody has a monitor on, so that one is switched off in favour of this
/// — two checks reporting the same fact under two names is worse than one.
/// <para>
/// <strong>Two questions, not one.</strong> Connectivity is the obvious failure and the loud one —
/// every request fails and something notices within seconds. The second is the quiet one: an
/// instance that connects successfully to a database missing the migration this build needs. That
/// deployment starts, serves, and fails on whichever request first touches the new column, and the
/// symptom is a scattering of 500s rather than an unhealthy instance. It is the shape a half-finished
/// blue/green cutover takes.
/// </para>
/// <para>
/// A pending migration is <strong>degraded</strong> rather than unhealthy, deliberately. During a
/// rolling deployment the new build is up before the migration has run everywhere, and reporting
/// unhealthy would take the instance out of rotation for a condition that resolves itself in
/// seconds — while an operator still sees it.
/// </para>
/// </remarks>
public sealed class CmsDatabaseHealthCheck(ApplicationDbContext context) : IHealthCheck
{
    /// <summary>Name this check is registered under.</summary>
    public const string Name = "cms-database";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await context.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy(
                    "The database did not accept a connection. Nothing this instance serves is " +
                    "coming from content.");
            }

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

            if (pending.Length > 0)
            {
                return HealthCheckResult.Degraded(
                    $"The database is missing {pending.Length} migration(s) this build expects, " +
                    $"beginning with '{pending[0]}'. Requests touching the newer schema will fail.",
                    data: new Dictionary<string, object> { ["pendingMigrations"] = pending });
            }

            return HealthCheckResult.Healthy("The database is reachable and its schema is current.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Caught rather than allowed to propagate: an unhandled exception in a health check is
            // reported as unhealthy anyway, but without the message, and the message is the whole
            // value of the check to whoever is woken by it.
            return HealthCheckResult.Unhealthy(
                "Checking the database failed: " + exception.Message,
                exception);
        }
    }
}
