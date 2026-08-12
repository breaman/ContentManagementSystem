using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Data.Tests;

/// <summary>
/// Shares one SQL Server container across every data-integration test in this assembly.
/// </summary>
[CollectionDefinition(SqlServerCollectionNames.SqlServer)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
