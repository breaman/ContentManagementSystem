using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests;

/// <summary>
/// Shares one SQL Server container across every API and delivery test in this assembly.
/// </summary>
[CollectionDefinition(SqlServerCollectionNames.SqlServer)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
