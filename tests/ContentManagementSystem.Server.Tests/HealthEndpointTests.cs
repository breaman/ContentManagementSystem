using System.Net;

using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests;

/// <summary>
/// Baseline for the API and delivery integration suite (tasks P0-10, P0-02).
/// </summary>
/// <remarks>
/// Every later delivery test builds on this factory, so keeping a health assertion here means a
/// broken host configuration surfaces as one obvious failure rather than as a wall of unrelated
/// endpoint failures.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class HealthEndpointTests(SqlServerFixture fixture)
{
    [Test]
    public async Task HealthReportsHealthy()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var factory = await CmsApplicationFactory.CreateAsync(fixture, cancellationToken);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(cancellationToken)).Should().Be("Healthy");
    }

    [Test]
    public async Task AliveReportsHealthy()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var factory = await CmsApplicationFactory.CreateAsync(fixture, cancellationToken);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/alive", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
