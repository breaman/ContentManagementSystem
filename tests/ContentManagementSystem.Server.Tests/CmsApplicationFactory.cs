using System.Net.Http.Json;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.ServiceDefaults;
using ContentManagementSystem.TestSupport;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    /// <summary>
    /// Extra registrations applied last, for suites that drive services directly.
    /// </summary>
    /// <remarks>
    /// The service-layer suites replace <c>ICmsAuthorization</c> and <c>IUserService</c>, which
    /// outside a request answer "no permissions" and "user 0" — correct behaviour, and not what a
    /// test of what a service does with a caller who <em>has</em> permissions is about. Everything
    /// else stays the real graph, so a service the container cannot build still fails here.
    /// </remarks>
    public Action<IServiceCollection, string>? ConfigureServices { get; init; }

    /// <summary>
    /// Extra configuration values, applied over the defaults below.
    /// </summary>
    /// <remarks>
    /// Applied through <c>UseSetting</c> rather than <c>ConfigureAppConfiguration</c>, and the
    /// difference matters: the configuration sources added below are only visible once the host is
    /// being built, which is <em>after</em> <c>Program</c> has read configuration to decide what to
    /// register. A test that has to change one of those decisions — which output cache store is
    /// registered, say — must set the value where the application builder can see it.
    /// </remarks>
    public IReadOnlyDictionary<string, string?>? Settings { get; init; }

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
                // The publish scheduler polls every thirty seconds and publishes what it finds. In
                // a suite where several hosts share one container that is a background writer no
                // test asked for; the scheduler's own behaviour is driven directly through
                // ScheduledJobRunner instead, which is where the decisions are.
                ["Cms:Scheduler:Enabled"] = "false",
                // And the retention sweeps, for a sharper version of the same reason: they delete.
                // The twenty-minute startup delay puts them outside any run this suite has taken so
                // far, which makes leaving them on a bet on how long the suite stays that fast —
                // and the failure would be a version sweep pruning history a test had just arranged
                // (task P9-25). Registration is still asserted, by AuditRetentionTests.
                ["Cms:Retention:Enabled"] = "false",
            });
        });

        foreach (var setting in Settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.ConfigureTestServices(services =>
        {
            // Registered last, so this overrides the Identity cookie as the default scheme. Only
            // the proof of identity is replaced; the authorization policies and the service-layer
            // permission checks the API tests exercise are the ones the application registers.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            ConfigureServices?.Invoke(services, connectionString);
        });
    }

    /// <summary>
    /// Creates a client that calls the API as a user holding the given roles.
    /// </summary>
    /// <param name="roles">Roles the caller holds. None at all means an anonymous caller.</param>
    /// <returns>A client whose requests carry that identity.</returns>
    public HttpClient CreateClientAs(params string[] roles)
    {
        var client = CreateClient();

        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        return client;
    }

    /// <summary>
    /// Fetches an antiforgery token and configures the client to send it on every later request.
    /// </summary>
    /// <param name="client">A client already carrying the caller's identity.</param>
    /// <param name="cancellationToken">Token observed while fetching.</param>
    /// <returns>The same client, for chaining.</returns>
    /// <remarks>
    /// The cookie half of the pair is stored by the client's own cookie container, which is what
    /// makes this two lines here and one interceptor in the real backoffice.
    /// </remarks>
    public static async Task<HttpClient> WithAntiforgeryTokenAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        var tokens = await client.GetFromJsonAsync<AntiforgeryTokenResponse>(
            $"{CmsApiEndpoints.BasePath}/antiforgery-token",
            cancellationToken);

        client.DefaultRequestHeaders.Remove(tokens!.HeaderName);
        client.DefaultRequestHeaders.Add(tokens.HeaderName, tokens.RequestToken);

        return client;
    }

    /// <summary>
    /// Creates a factory bound to a freshly migrated database on the shared SQL container.
    /// </summary>
    /// <param name="fixture">The container fixture supplying the SQL Server instance.</param>
    /// <param name="cancellationToken">Token observed while migrating.</param>
    /// <param name="configureServices">Extra registrations, applied after the application's own.</param>
    public static async Task<CmsApplicationFactory> CreateAsync(
        SqlServerFixture fixture,
        CancellationToken cancellationToken = default,
        Action<IServiceCollection, string>? configureServices = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var databaseName = $"cms_srv_{Guid.NewGuid():N}";

        await using (var context = await fixture.CreateDatabaseAsync(databaseName, cancellationToken))
        {
        }

        return new CmsApplicationFactory(fixture.ConnectionStringFor(databaseName))
        {
            ConfigureServices = configureServices,
            Settings = settings,
        };
    }
}
