using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Interceptors;
using ContentManagementSystem.Data.Models;

using System.Data;
using System.Text.RegularExpressions;

using DotNet.Testcontainers.Builders;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Testcontainers.MsSql;

using TUnit.Core.Interfaces;

namespace ContentManagementSystem.TestSupport;

/// <summary>
/// Starts a disposable SQL Server container shared by every test that asks for one.
/// </summary>
/// <remarks>
/// Container start-up costs tens of seconds, so the fixture is shared and each test asks for its
/// own freshly migrated database via <see cref="CreateDatabaseAsync"/> instead of paying that cost
/// per test. Tests run against real SQL Server because the behaviour under test — filtered unique
/// indexes, <c>rowversion</c> concurrency, query filters — has no faithful in-memory equivalent.
/// <para>
/// <strong>Every database is dropped when the test that asked for it is done with it.</strong> They
/// used to be left in place for inspection, which is a pleasant debugging property and a hard
/// ceiling: one container held every database the run had ever created, and past a few hundred the
/// engine stops servicing new connections promptly — which surfaces as an unrelated test's
/// <c>WebApplicationFactory</c> timing out after five minutes waiting for its host, and then as the
/// whole process aborting. Nothing about that failure points at the cause.
/// </para>
/// <para>
/// To keep a database for inspection, comment out the drop in <see cref="DropDatabaseAsync"/> rather
/// than reintroducing the leak: it is one line, and the name is in the connection string on the
/// context.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [ClassDataSource&lt;SqlServerFixture&gt;(Shared = SharedType.PerTestSession)]
/// [NotInParallel(SqlServerConstraint.Key)]
/// public class MyTests(SqlServerFixture fixture)
/// {
///     [Test]
///     public async Task Works()
///     {
///         await using var database = await fixture.CreateDatabaseAsync();
///         var context = database.Context;
///     }
/// }
/// </code>
/// </example>
public sealed partial class SqlServerFixture : IAsyncInitializer, IAsyncDisposable
{
    private const int ReadinessAttempts = 60;
    private static readonly TimeSpan ReadinessDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Databases created and not yet dropped, so the fixture can sweep any a failing test abandoned.
    /// </summary>
    /// <remarks>
    /// A test that throws between creating its database and disposing the handle would otherwise
    /// leak one — rare, but the leak is exactly what this class exists to stop, and a mechanism that
    /// only works when tests pass is not a mechanism.
    /// </remarks>
    private readonly HashSet<string> _outstanding = new(StringComparer.OrdinalIgnoreCase);

    private readonly MsSqlContainer _container = new MsSqlBuilder(SqlServerImage.Tag)
        // The stock MsSql wait strategy shells out to sqlcmd, which is absent from the Azure SQL
        // Edge image used on arm64. Waiting on the port and then probing with a real connection
        // works identically on both images.
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(MsSqlBuilder.MsSqlPort))
        .Build();

    /// <summary>Gets the connection string for the container's <c>master</c> database.</summary>
    public string MasterConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await WaitUntilAcceptingConnectionsAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // The container is about to be destroyed and every database with it, so this is not about
        // reclaiming space. It is about the count being reported honestly: an outstanding database
        // here is a test that leaked one, and the message says so where somebody will read it.
        string[] leaked;

        lock (_outstanding)
        {
            leaked = [.. _outstanding];
        }

        if (leaked.Length > 0)
        {
            await Console.Error.WriteLineAsync(
                $"SqlServerFixture: {leaked.Length} database(s) were never released — " +
                $"{string.Join(", ", leaked.Take(5))}{(leaked.Length > 5 ? ", …" : string.Empty)}. " +
                "Each is a test that did not dispose what CreateDatabaseAsync handed it.");
        }

        await _container.DisposeAsync();
    }

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
    /// Creates an empty database, applies every EF migration to it, and returns a handle owning
    /// both the database and a context bound to it. <strong>Disposing the handle drops the
    /// database.</strong>
    /// </summary>
    /// <param name="databaseName">
    /// Optional database name. Defaults to a fresh GUID-suffixed name so parallel tests never
    /// collide.
    /// </param>
    /// <param name="cancellationToken">Token observed while migrating.</param>
    /// <returns>The handle. Dispose it — <c>await using</c> — to release the database.</returns>
    /// <remarks>
    /// A handle rather than the context itself, because the two have different lifetimes wherever a
    /// database outlives the context that created it: <c>CmsApplicationFactory</c> migrates, drops
    /// the context, and then runs a whole application against the same database. Those callers use
    /// <see cref="CreateDatabaseOnlyAsync"/> and release it themselves.
    /// </remarks>
    public async Task<TemporaryDatabase> CreateDatabaseAsync(
        string? databaseName = null,
        CancellationToken cancellationToken = default)
    {
        databaseName = await CreateDatabaseOnlyAsync(databaseName, cancellationToken);

        return new TemporaryDatabase(this, databaseName, CreateContext(databaseName));
    }

    /// <summary>
    /// Creates and migrates a database and returns its name, leaving the caller to drop it.
    /// </summary>
    /// <param name="databaseName">Optional name; defaults to a fresh GUID-suffixed one.</param>
    /// <param name="cancellationToken">Token observed while migrating.</param>
    /// <returns>The database's name, which <see cref="DropDatabaseAsync"/> takes.</returns>
    /// <remarks>
    /// For the callers that run something else against the database after the migrating context is
    /// gone — an application host, or two of them. They own the database and must drop it;
    /// <see cref="DisposeAsync"/> reports the ones that did not.
    /// </remarks>
    public async Task<string> CreateDatabaseOnlyAsync(
        string? databaseName = null,
        CancellationToken cancellationToken = default)
    {
        databaseName ??= $"cms_{Guid.NewGuid():N}";

        await using (var context = CreateContext(databaseName))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        lock (_outstanding)
        {
            _outstanding.Add(databaseName);
        }

        return databaseName;
    }

    /// <summary>
    /// Drops a database this fixture created, whatever is still connected to it.
    /// </summary>
    /// <param name="databaseName">The name, as it was created.</param>
    /// <param name="cancellationToken">Token observed while dropping.</param>
    /// <remarks>
    /// Two things make this reliable, and both are needed. <strong>The pool is cleared first</strong>
    /// — ADO.NET keeps connections alive after a context is disposed, and <c>DROP DATABASE</c> fails
    /// while any of them is open. <strong>Then the database is put into single-user mode with
    /// <c>ROLLBACK IMMEDIATE</c></strong>, which evicts anything that connected in the meantime,
    /// such as a background service the host had not finished stopping.
    /// <para>
    /// A failure here is reported and swallowed. Dropping is cleanup: turning a green test red
    /// because its database could not be reclaimed reports the wrong thing about the code under
    /// test, and a message on standard error is where a leak that matters will be noticed.
    /// </para>
    /// </remarks>
    public async Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        try
        {
            // Names are generated here and never come from a test's input, but the value is being
            // concatenated into DDL — which cannot take a parameter — so it is checked rather than
            // trusted, and escaped as an identifier on top of that.
            if (!SafeDatabaseName().IsMatch(databaseName))
            {
                throw new ArgumentException(
                    $"'{databaseName}' is not a database name this fixture creates.",
                    nameof(databaseName));
            }

            SqlConnection.ClearPool(new SqlConnection(ConnectionStringFor(databaseName)));

            await using var connection = new SqlConnection(MasterConnectionString);
            await connection.OpenAsync(cancellationToken);

            var quoted = $"[{databaseName.Replace("]", "]]", StringComparison.Ordinal)}]";

            await using var command = connection.CreateCommand();

            command.CommandText = $"""
                IF DB_ID(@name) IS NOT NULL
                BEGIN
                    ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE {quoted};
                END
                """;

            command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = databaseName });

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException or ArgumentException)
        {
            await Console.Error.WriteLineAsync(
                $"SqlServerFixture: could not drop '{databaseName}': {exception.Message}");
        }
        finally
        {
            lock (_outstanding)
            {
                _outstanding.Remove(databaseName);
            }
        }
    }

    /// <summary>Names this fixture generates, which is what <see cref="DropDatabaseAsync"/> will act on.</summary>
    [GeneratedRegex("^cms[A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeDatabaseName();

    /// <summary>
    /// Creates a context bound to <paramref name="databaseName"/> without applying migrations.
    /// </summary>
    /// <param name="databaseName">Name of the database on the container.</param>
    public ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionStringFor(databaseName))
            // Without this the context builds an older Identity schema than the migrations were
            // generated from; see IdentityModelServices for why every test-owned context needs it.
            .UseApplicationServiceProvider(IdentityModelServices.Instance)
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
