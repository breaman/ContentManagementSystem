using System.Runtime.InteropServices;

namespace ContentManagementSystem.TestSupport;

/// <summary>
/// Chooses the SQL Server container image used by integration tests.
/// </summary>
/// <remarks>
/// The image is pinned rather than floating on <c>latest</c> so that a new upstream tag cannot
/// change behaviour underneath a green build (task P0-17). Microsoft publishes no arm64 image for
/// SQL Server itself, so arm64 hosts and CI agents fall back to Azure SQL Edge — the same fallback
/// the Aspire AppHost already applies. Override with <c>CMS_TEST_SQL_IMAGE</c> when a specific
/// build needs pinning to something else.
/// </remarks>
public static class SqlServerImage
{
    /// <summary>Environment variable that overrides the selected image.</summary>
    public const string OverrideVariable = "CMS_TEST_SQL_IMAGE";

    private const string SqlServerImageTag = "mcr.microsoft.com/mssql/server:2022-CU20-ubuntu-22.04";
    private const string AzureSqlEdgeImageTag = "mcr.microsoft.com/azure-sql-edge:1.0.7";

    /// <summary>
    /// Gets the fully qualified container image tag to run SQL Server integration tests against.
    /// </summary>
    /// <example>
    /// <code>
    /// var container = new MsSqlBuilder().WithImage(SqlServerImage.Tag).Build();
    /// </code>
    /// </example>
    public static string Tag
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(OverrideVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? AzureSqlEdgeImageTag
                : SqlServerImageTag;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the selected image is Azure SQL Edge, which supports a
    /// reduced feature set (notably no full-text search).
    /// </summary>
    public static bool IsAzureSqlEdge => Tag.Contains("azure-sql-edge", StringComparison.OrdinalIgnoreCase);
}
