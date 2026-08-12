namespace ContentManagementSystem.TestSupport;

/// <summary>
/// Names shared by the per-assembly SQL Server collection definitions.
/// </summary>
/// <remarks>
/// xUnit requires <c>[CollectionDefinition]</c> to live in the same assembly as the tests that use
/// it (rule xUnit1041), so each test project declares its own definition. Only the name is shared,
/// which keeps the collections consistently addressable without duplicating the fixture itself.
/// </remarks>
public static class SqlServerCollectionNames
{
    /// <summary>Collection name for tests that need a SQL Server container.</summary>
    public const string SqlServer = "SqlServer";
}
