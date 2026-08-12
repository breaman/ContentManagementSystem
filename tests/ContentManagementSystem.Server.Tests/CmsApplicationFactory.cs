using ContentManagementSystem.ServiceDefaults;
using ContentManagementSystem.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ContentManagementSystem.Server.Tests;

/// <summary>
/// Boots the real server against a throwaway SQL Server database.
/// </summary>
/// <remarks>
/// The factory runs in the Development environment on purpose: <c>MapDefaultEndpoints</c> only
/// exposes <c>/health</c> and <c>/alive</c> there, and those endpoints are part of what the
/// integration suite verifies.
/// </remarks>
public sealed class CmsApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{Constants.DatabaseConnectionString}"] = connectionString,
                // The OTLP exporter would otherwise try to reach a collector that is not running.
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
            });
        });
    }

    /// <summary>
    /// Creates a factory bound to a freshly migrated database on the shared SQL container.
    /// </summary>
    /// <param name="fixture">The container fixture supplying the SQL Server instance.</param>
    /// <param name="cancellationToken">Token observed while migrating.</param>
    public static async Task<CmsApplicationFactory> CreateAsync(
        SqlServerFixture fixture,
        CancellationToken cancellationToken = default)
    {
        var databaseName = $"cms_srv_{Guid.NewGuid():N}";

        await using (var context = await fixture.CreateDatabaseAsync(databaseName, cancellationToken))
        {
        }

        return new CmsApplicationFactory(fixture.ConnectionStringFor(databaseName));
    }
}
