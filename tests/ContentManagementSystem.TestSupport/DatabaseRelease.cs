namespace ContentManagementSystem.TestSupport;

/// <summary>
/// Drops a database created with <see cref="SqlServerFixture.CreateDatabaseOnlyAsync"/> when the
/// test that owns it finishes.
/// </summary>
/// <remarks>
/// For the case a <see cref="TemporaryDatabase"/> cannot express: several things share one database
/// and none of them owns it, so the test does. Declare it <em>before</em> whatever connects to the
/// database — <c>await using</c> disposes in reverse, so the database outlives its users rather than
/// being dropped from under them.
/// </remarks>
/// <param name="fixture">The fixture that created the database.</param>
/// <param name="databaseName">The database to drop.</param>
public sealed class DatabaseRelease(SqlServerFixture fixture, string databaseName) : IAsyncDisposable
{
    private bool disposed;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this.disposed) return;

        this.disposed = true;

        await fixture.DropDatabaseAsync(databaseName);
    }
}
