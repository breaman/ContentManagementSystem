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
        Action<IServiceCollection, string>? configureServices = null)
    {
        var databaseName = $"cms_srv_{Guid.NewGuid():N}";

        await using (var context = await fixture.CreateDatabaseAsync(databaseName, cancellationToken))
        {
        }

        return new CmsApplicationFactory(fixture.ConnectionStringFor(databaseName))
        {
            ConfigureServices = configureServices,
        };
    }
}
