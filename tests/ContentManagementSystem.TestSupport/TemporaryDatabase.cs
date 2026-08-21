using ContentManagementSystem.Data.Models;

namespace ContentManagementSystem.TestSupport;

/// <summary>
/// A database that lives as long as the test that asked for it, and a context bound to it.
/// </summary>
/// <remarks>
/// The pair exists because the two have different lifetimes everywhere else in this harness: a
/// context is a cheap thing a test opens and closes, and a database is an expensive thing the
/// container has a finite number of. Handing back only the context is what let every database
/// created by a run survive it — several hundred in one container, until the engine stopped
/// answering and an unrelated test failed with a five-minute host-build timeout.
/// <para>
/// <strong>Dispose it and the database is gone.</strong> The context is disposed first, so its
/// connections are back in the pool before <see cref="SqlServerFixture.DropDatabaseAsync"/> clears
/// it.
/// </para>
/// </remarks>
/// <param name="fixture">The fixture that created the database, and will drop it.</param>
/// <param name="name">The database's name on the container.</param>
/// <param name="context">A context bound to it.</param>
public sealed class TemporaryDatabase(
    SqlServerFixture fixture,
    string name,
    ApplicationDbContext context) : IAsyncDisposable
{
    private bool disposed;

    /// <summary>The database's name on the container, which is also in the context's connection string.</summary>
    public string Name { get; } = name;

    /// <summary>A context bound to the database, migrated and ready.</summary>
    /// <remarks>
    /// Owned by this handle. Do not dispose it separately — disposing the handle does that, in the
    /// order the drop needs.
    /// </remarks>
    public ApplicationDbContext Context { get; } = context;

    /// <summary>The connection string tests hand to anything that has to open its own connection.</summary>
    public string ConnectionString => fixture.ConnectionStringFor(this.Name);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this.disposed) return;

        this.disposed = true;

        await this.Context.DisposeAsync();
        await fixture.DropDatabaseAsync(this.Name);
    }
}
