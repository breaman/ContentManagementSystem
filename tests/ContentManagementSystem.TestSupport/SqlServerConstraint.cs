namespace ContentManagementSystem.TestSupport;

/// <summary>
/// The parallel-constraint key shared by every suite that runs against the SQL Server container.
/// </summary>
/// <remarks>
/// TUnit runs tests in parallel by default — including the tests inside a single class — and one
/// container cannot absorb fifty suites migrating their own databases at once. Naming the key here
/// rather than repeating a literal keeps every such suite in the same queue: tests carrying it run
/// one at a time, which is the behaviour the xUnit collection this replaced used to give.
/// <para>
/// Sharing the container is a separate concern, handled by
/// <c>[ClassDataSource&lt;SqlServerFixture&gt;(Shared = SharedType.PerTestSession)]</c>. A suite
/// needs both: the data source to get the container, and this key to take its turn on it.
/// </para>
/// </remarks>
public static class SqlServerConstraint
{
    /// <summary>Constraint key for tests that need the SQL Server container.</summary>
    public const string Key = "SqlServer";
}
