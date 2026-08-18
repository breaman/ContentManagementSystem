using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Interceptors;
using ContentManagementSystem.Data.Models;

using DotNet.Testcontainers.Builders;

using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Testcontainers.MsSql;

using Xunit;

namespace ContentManagementSystem.TestSupport;

/// <summary>
/// Starts a disposable SQL Server container shared by every test in a collection.
/// </summary>
/// <remarks>
/// Container start-up costs tens of seconds, so the fixture is shared and each test asks for its
/// own freshly migrated database via <see cref="CreateDatabaseAsync"/> instead of paying that cost
/// per test. Tests run against real SQL Server because the behaviour under test — filtered unique
/// indexes, <c>rowversion</c> concurrency, query filters — has no faithful in-memory equivalent.
/// </remarks>
/// <example>
/// <code>
/// [Collection(SqlServerCollection.Name)]
/// public class MyTests(SqlServerFixture fixture)
/// {
///     [Fact]
///     public async Task Works()
///     {
///         await using var db = await fixture.CreateDatabaseAsync();
///     }
/// }
/// </code>
/// </example>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const int ReadinessAttempts = 60;
    private static readonly TimeSpan ReadinessDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Minimal service provider supplying the Identity options that shape the EF model. The web
    /// host configures the same value in <c>AddIdentityCore</c>; both read it from
    /// <see cref="IdentitySchema"/> so they cannot drift.
    /// </summary>
    private static readonly IServiceProvider IdentityModelServices = new ServiceCollection()
        .Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchema.Version)
        .BuildServiceProvider();

    private readonly MsSqlContainer _container = new MsSqlBuilder(SqlServerImage.Tag)
        // The stock MsSql wait strategy shells out to sqlcmd, which is absent from the Azure SQL
        // Edge image used on arm64. Waiting on the port and then probing with a real connection
        // works identically on both images.
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(MsSqlBuilder.MsSqlPort))
        .Build();

    /// <summary>Gets the connection string for the container's <c>master</c> database.</summary>
    public string MasterConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await WaitUntilAcceptingConnectionsAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Builds a connection string pointing at a uniquely named database on the container.
    /// </summary>
    /// <param name="databaseName">Name of the database, unique per test.</param>
    public string ConnectionStringFor(string databaseName) =>
        new SqlConnectionStringBuilder(MasterConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;

    /// <summary>
    /// Creates an empty database, applies every EF migration to it, and returns a context bound to
    /// it. Disposing the context leaves the database in place for inspection; the whole container
    /// is discarded at the end of the run.
    /// </summary>
    /// <param name="databaseName">
    /// Optional database name. Defaults to a fresh GUID-suffixed name so parallel tests never
    /// collide.
    /// </param>
    /// <param name="cancellationToken">Token observed while migrating.</param>
    public async Task<ApplicationDbContext> CreateDatabaseAsync(
        string? databaseName = null,
        CancellationToken cancellationToken = default)
    {
        databaseName ??= $"cms_{Guid.NewGuid():N}";

        var context = CreateContext(databaseName);
        await context.Database.MigrateAsync(cancellationToken);

        return context;
    }

    /// <summary>
    /// Creates a context bound to <paramref name="databaseName"/> without applying migrations.
    /// </summary>
    /// <param name="databaseName">Name of the database on the container.</param>
    public ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionStringFor(databaseName))
            // IdentityDbContext reads the store schema version out of the application service
            // provider while building the model. Without it the context builds an older Identity
            // schema than the migrations were generated from, and EF then reports the model as
            // having pending changes.
            .UseApplicationServiceProvider(IdentityModelServices)
            // Soft-delete rewriting, fingerprint stamping, and audit capture. They are options-level
            // behaviour rather than part of the context type, so a fixture that skipped them would
            // hand tests a context that saves without any of it — and the suites here assert on all
            // three. There is nobody signed in, which is what a fixture writing rows directly is.
            .AddInterceptors(CmsSaveInterceptors.Create(users: null, TimeProvider.System))
            .Options;

        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Polls until the SQL Server instance accepts logins. The port opens well before the engine
    /// finishes recovery, so connecting is the only reliable readiness signal.
    /// </summary>
    private async Task WaitUntilAcceptingConnectionsAsync()
    {
        for (var attempt = 1; attempt <= ReadinessAttempts; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(MasterConnectionString);
                await connection.OpenAsync();

                return;
            }
            catch (SqlException) when (attempt < ReadinessAttempts)
            {
                await Task.Delay(ReadinessDelay);
            }
        }
    }
}
