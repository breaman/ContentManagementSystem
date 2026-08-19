using ContentManagementSystem.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Search;

/// <summary>
/// Answers whether this database can serve a full-text query (task P8-18).
/// </summary>
/// <param name="options">The configured override, if a deployment set one.</param>
/// <param name="logger">Log for the answer, once, and for a probe that failed.</param>
/// <remarks>
/// SQL Server and Azure SQL Database have a full-text engine; <strong>Azure SQL Edge does not</strong>,
/// and it is both the arm64 development fallback and a legitimate small deployment target. Migration
/// #8 creates the catalog only where the engine exists, so the runtime has to ask the same question
/// the migration did rather than assume the index is there.
/// <para>
/// Asked once per process and remembered. The answer changes only when a migration runs or the
/// connection string is repointed, and neither happens without a restart; asking per query would put
/// a metadata round trip in front of every search.
/// </para>
/// </remarks>
public sealed class SearchCapabilities(IOptions<SearchOptions> options, ILogger<SearchCapabilities> logger)
{
    private const string ProbeSql = """
        SELECT CAST(CASE WHEN SERVERPROPERTY('IsFullTextInstalled') = 1
                          AND EXISTS (SELECT 1 FROM sys.fulltext_indexes
                                      WHERE object_id = OBJECT_ID(N'dbo.SearchDocuments'))
                    THEN 1 ELSE 0 END AS int) AS Value
        """;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool? _fullText;

    /// <summary>
    /// Whether full-text search is available here.
    /// </summary>
    /// <param name="context">A context on the database being asked about.</param>
    /// <param name="cancellationToken">Token observed while probing.</param>
    /// <returns>True when the catalog and index exist and the engine is installed.</returns>
    public async ValueTask<bool> FullTextAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (options.Value.UseFullText is { } configured) return configured;

        if (_fullText is { } known) return known;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_fullText is { } raced) return raced;

            try
            {
                var answer = await context.Database
                    .SqlQueryRaw<int>(ProbeSql)
                    .FirstAsync(cancellationToken);

                _fullText = answer == 1;

                logger.LogInformation(
                    _fullText.Value
                        ? "Search is using the SQL Server full-text index."
                        : "This database has no full-text index on SearchDocuments; search falls back " +
                          "to a scan, which is correct and does not scale (spec section 17.1).");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A probe that fails answers no. The fallback returns the same rows more slowly,
                // where guessing yes turns one unanswerable question into every search failing.
                logger.LogWarning(
                    exception,
                    "Probing for the full-text index failed; search will use the fallback scan.");

                _fullText = false;
            }

            return _fullText.Value;
        }
        finally
        {
            _gate.Release();
        }
    }
}
