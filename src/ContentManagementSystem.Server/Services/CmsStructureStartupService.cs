using ContentManagementSystem.Core.Structure;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// Brings the database's content model into agreement with the deployment, at startup
/// (tasks P1-25 and P1-26).
/// </summary>
/// <remarks>
/// The order is the point. Reconciliation runs first, because it is what creates the template row
/// for a component this deployment introduced; the schema sync then puts that template's zones in.
/// Reversed, a promotion that added a template and its zones in one commit would need two restarts.
/// <para>
/// Failures here do not stop the host. A site whose content model is one zone out of date still
/// serves every page it has, whereas a site that will not start serves none — and the health check
/// and the logged report are how the discrepancy gets attention (spec section 8.4). The exception
/// that <em>is</em> allowed through is the reconciler's duplicate-key throw, which means two
/// components claim one key and there is no correct way to serve anything.
/// </para>
/// </remarks>
/// <param name="scopes">Factory for the scope the scoped services need.</param>
/// <param name="environment">Used to decide how loudly to report.</param>
/// <param name="options">Where the schema files are and whether to apply them.</param>
/// <param name="logger">Log for the startup diff.</param>
public sealed class CmsStructureStartupService(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    IOptions<SchemaSyncOptions> options,
    ILogger<CmsStructureStartupService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();

        var reconciler = scope.ServiceProvider.GetRequiredService<ITemplateReconciler>();

        await reconciler.ReconcileAsync(cancellationToken);

        if (!options.Value.ApplyAtStartup)
        {
            logger.LogInformation("Schema sync is disabled by configuration; skipping.");

            return;
        }

        var directory = Path.Combine(environment.ContentRootPath, options.Value.Directory);
        var sync = scope.ServiceProvider.GetRequiredService<ISchemaSyncService>();

        try
        {
            var report = await sync.ApplyAsync(directory, cancellationToken);

            Report(report, directory);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Deliberately swallowed. See the class remarks: a content model one commit out of date
            // is a far better failure than a site that will not start.
            logger.LogError(
                exception,
                "Applying the schema files in {Directory} failed. The site is starting with the " +
                "content model already in the database.",
                directory);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Report(SchemaSyncReport report, string directory)
    {
        if (report.Errors.Count > 0)
        {
            logger.LogError(
                "{Count} schema file(s) in {Directory} could not be read: {Errors}",
                report.Errors.Count,
                directory,
                string.Join(" | ", report.Errors));
        }

        foreach (var change in report.Changes.Where(c => c.Change is SchemaChangeKind.Refused))
        {
            logger.LogWarning(
                "Schema sync refused a change to {Kind} '{Key}': {Detail}",
                change.Kind,
                change.Key,
                change.Detail);
        }

        var applied = report.Changes
            .Where(c => c.Change is SchemaChangeKind.Created or SchemaChangeKind.SlotAdded
                or SchemaChangeKind.SlotUpdated)
            .ToList();

        if (applied.Count == 0)
        {
            logger.LogInformation(
                "Schema sync: {Files} file(s) read, database already matches.",
                report.FilesRead);

            return;
        }

        logger.LogInformation(
            "Schema sync applied {Count} change(s) from {Files} file(s): {Changes}",
            applied.Count,
            report.FilesRead,
            string.Join(" | ", applied.Select(c => $"{c.Key}: {c.Detail}")));

        // In development the kept-but-unlisted entries are the useful half — they are how a
        // developer notices they changed the model in the backoffice and forgot to export.
        if (!environment.IsDevelopment()) return;

        foreach (var change in report.Changes.Where(c => c.Change is SchemaChangeKind.KeptUnlisted))
        {
            logger.LogInformation("Schema sync: {Detail}", change.Detail);
        }
    }
}
