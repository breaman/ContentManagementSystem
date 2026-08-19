using ContentManagementSystem.Server.HealthChecks;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Tests;

/// <summary>
/// Every health check has a monitor and an alert threshold (task P9-20, spec section 24.2).
/// </summary>
/// <remarks>
/// <strong>"Has a monitor" is not a thing a test can assert about a monitoring system it cannot
/// see.</strong> What it can assert is the half that goes wrong in practice: a check added to the
/// application and to nothing else. An alert rule is written from a name, so a check whose name
/// appears nowhere in the operations documentation is a check nobody is watching — and that is a
/// document nobody updated, which is exactly the failure this catches.
/// <para>
/// The documentation is read from disk rather than restated here. Restating it would make this a
/// test of a list against itself.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class HealthCheckContractTests(SqlServerFixture fixture)
{
    /// <summary>The five checks spec section 24.2 names, plus the framework's liveness probe.</summary>
    private static readonly string[] Expected =
    [
        CmsDatabaseHealthCheck.Name,
        CmsMediaStoreHealthCheck.Name,
        CmsTemplatesHealthCheck.Name,
        CmsSchedulerHealthCheck.Name,
        CmsOutboxHealthCheck.Name,
        "self",
    ];

    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task TheApplicationRegistersExactlyTheChecksTheSpecificationNames()
    {
        var registered = Registrations().Select(check => check.Name).ToArray();

        // Both directions. A missing check is a failure nobody is told about; an extra one is a
        // check nobody is watching, and cms-database was in the second state until P9-20 — Aspire
        // registers a connectivity check under the context's full type name, which no alert rule
        // refers to.
        registered.Should().BeEquivalentTo(Expected);

        await Task.CompletedTask;
    }

    [Test]
    public async Task EveryCmsCheckIsReadyRatherThanLive()
    {
        // /alive answers "is this process running" and /health answers "should it take traffic". A
        // CMS check tagged live would take an instance out of rotation for a degraded template
        // catalog, and a restart does not fix a degraded template catalog.
        foreach (var check in Registrations().Where(check => check.Name.StartsWith("cms-", StringComparison.Ordinal)))
        {
            check.Tags.Should().Contain("ready", check.Name).And.NotContain("live");
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task EveryCheckIsNamedInTheOperationsDocumentation()
    {
        var operations = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "docs", "operations.md"),
            TestContext.Current!.Execution.CancellationToken);

        foreach (var check in Registrations().Where(check => check.Name.StartsWith("cms-", StringComparison.Ordinal)))
        {
            operations.Should().Contain(
                check.Name,
                "'{0}' needs a row in the health-check table, with what makes it degraded, what makes " +
                "it unhealthy, and which of those pages somebody",
                check.Name);
        }
    }

    private IEnumerable<HealthCheckRegistration> Registrations() =>
        _bench.Resolve<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

    /// <summary>Walks up from the test binaries to the repository root.</summary>
    /// <returns>The root directory.</returns>
    /// <exception cref="InvalidOperationException">The root could not be located.</exception>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
