using System.Diagnostics;

using ContentManagementSystem.Core.Search;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Search;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Search;

/// <summary>
/// Backoffice search at the scale the acceptance criterion names (task P8-25, criterion P8 #10).
/// </summary>
/// <remarks>
/// Fifty thousand documents are seeded with one set-based insert rather than through the page
/// services: this measures the query, and creating fifty thousand pages one publish at a time would
/// take longer than the whole suite and measure the writer instead.
/// <para>
/// <strong>The 500 ms bar is asserted only where a full-text index exists.</strong> The correctness
/// half runs everywhere — the needle has to come back by title, by body, and by slug on both
/// engines — but Azure SQL Edge, which the arm64 fixture runs, has no full-text engine at all and
/// answers with the fallback scan. Holding a scan to a full-text budget would be asserting that the
/// fallback is something it was never claimed to be. Set <c>CMS_TEST_SQL_IMAGE</c> to a SQL Server
/// image to run the timed half on an arm64 machine.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class SearchPerformanceTests(SqlServerFixture fixture)
{
    private const int SeededDocuments = 50_000;
    private const int BudgetMilliseconds = 500;

    /// <summary>A word that appears in exactly one seeded document, in one column each time.</summary>
    private const string Needle = "quixotrope";

    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task SearchAnswersByTitleBodyAndSlugAcrossFiftyThousandDocuments()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await SeedAsync(cancellationToken);

        var fullText = await FullTextAsync(cancellationToken);

        if (fullText) await WaitForPopulationAsync(cancellationToken);

        // One query before the clock starts. The first search of a process compiles a plan and
        // loads the field-type registry, and timing that would be timing start-up.
        await SearchAsync(new SearchQuery("widget"), cancellationToken);

        foreach (var (label, query) in Queries())
        {
            var stopwatch = Stopwatch.StartNew();
            var results = await SearchAsync(query, cancellationToken);

            stopwatch.Stop();

            results.Hits.Should().ContainSingle(
                $"the seeded needle is findable by {label} among {SeededDocuments} documents");
            results.FullText.Should().Be(fullText);

            await TestContext.Current!.OutputWriter.WriteLineAsync(
                $"{label}: {stopwatch.ElapsedMilliseconds} ms over {SeededDocuments} documents " +
                $"({(fullText ? "full-text" : "fallback scan")}).");

            if (fullText)
            {
                stopwatch.ElapsedMilliseconds.Should().BeLessThan(
                    BudgetMilliseconds,
                    $"criterion P8 #10 gives search {BudgetMilliseconds} ms to answer by {label}");
            }
        }

        Skip.Unless(
            fullText,
            "This database has no full-text index — Azure SQL Edge has no full-text engine — so the " +
            "500 ms budget was not asserted. The rows were still checked. Set CMS_TEST_SQL_IMAGE to " +
            "a SQL Server image to run the timed half here.");
    }

    /// <summary>The three columns the criterion names, one query each.</summary>
    private static IEnumerable<(string Label, SearchQuery Query)> Queries() =>
    [
        ("title", new SearchQuery(Needle, Kind: SearchResultKind.Reusable)),
        ("body", new SearchQuery(Needle, Kind: SearchResultKind.Media)),
        ("slug", new SearchQuery(Needle, Kind: SearchResultKind.Page)),
    ];

    /// <summary>
    /// Writes fifty thousand documents plus three needles, one per searchable column.
    /// </summary>
    /// <remarks>
    /// The needles are told apart by kind rather than by three separate assertions on one row, so
    /// each query can assert a single hit and therefore that it matched the column it was meant to.
    /// </remarks>
    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        await _bench.Context.Database.ExecuteSqlRawAsync(
            $"""
            WITH numbers AS (
                SELECT TOP ({SeededDocuments})
                       ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO SearchDocuments (EntityType, EntityId, Title, Body, Keywords, Url, IsPublished, UpdatedOn)
            SELECT 0,
                   1000 + n,
                   CONCAT('Seeded page ', n),
                   CONCAT('Body text for page ', n, ' describing widgets, sprockets and gearboxes'),
                   CONCAT('seeded-page-', n),
                   CONCAT('/seeded/', n),
                   1,
                   SYSDATETIMEOFFSET()
            FROM numbers;

            INSERT INTO SearchDocuments (EntityType, EntityId, Title, Body, Keywords, Url, IsPublished, UpdatedOn)
            VALUES
                (2, 1, '{Needle} in a title', 'Body with nothing special in it', 'ordinary', NULL, 1, SYSDATETIMEOFFSET()),
                (1, 1, 'An ordinary title', 'Body mentioning {Needle} once', 'ordinary', NULL, 1, SYSDATETIMEOFFSET()),
                (0, 1, 'Another ordinary title', 'Body with nothing special in it', '{Needle}', NULL, 1, SYSDATETIMEOFFSET());
            """,
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();
    }

    /// <summary>Whether this database has the full-text index migration #8 creates where it can.</summary>
    private async Task<bool> FullTextAsync(CancellationToken cancellationToken) =>
        await _bench.Context.Database
            .SqlQueryRaw<int>(
                """
                SELECT CAST(CASE WHEN SERVERPROPERTY('IsFullTextInstalled') = 1
                                  AND EXISTS (SELECT 1 FROM sys.fulltext_indexes
                                              WHERE object_id = OBJECT_ID(N'dbo.SearchDocuments'))
                            THEN 1 ELSE 0 END AS int) AS Value
                """)
            .FirstAsync(cancellationToken) == 1;

    /// <summary>
    /// Waits for the full-text index to catch up with the insert.
    /// </summary>
    /// <remarks>
    /// <c>CHANGE_TRACKING AUTO</c> populates the index in the background, so a query run immediately
    /// after a bulk insert legitimately returns nothing. Waiting here rather than inside the timed
    /// section is the difference between measuring the query and measuring the crawl.
    /// </remarks>
    private async Task WaitForPopulationAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var populating = await _bench.Context.Database
                .SqlQueryRaw<int>(
                    "SELECT CAST(FULLTEXTCATALOGPROPERTY('CmsSearchCatalog', 'PopulateStatus') AS int) AS Value")
                .FirstAsync(cancellationToken);

            if (populating == 0 &&
                (await SearchAsync(new SearchQuery(Needle), cancellationToken)).Hits.Count == 3)
            {
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException(
            "The full-text index did not finish populating within a minute of the seed insert.");
    }

    private async Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        await using var scope = _bench.NewScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISearchService>()
            .SearchAsync(query, cancellationToken);

        result.IsSuccess.Should().BeTrue();

        return result.Value!;
    }
}
