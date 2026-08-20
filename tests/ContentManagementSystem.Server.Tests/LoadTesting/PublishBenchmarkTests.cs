using System.Diagnostics;

using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.LoadTesting;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.LoadTesting;

/// <summary>
/// NFR-7: publishing a typical page takes under two seconds, invalidation included (task P9-13).
/// </summary>
/// <remarks>
/// Measured against the seeded dataset rather than against a fixture of a dozen pages, because the
/// costs this requirement is about — the URL rebuild's collision check, the version query, the
/// where-used walk behind the eviction — are all queries whose plans change with the size of the
/// tables. Five thousand pages is the largest size that still seeds inside a test run; the shape is
/// the same site the full run builds, in miniature.
/// <para>
/// <strong>Invalidation is inside the measurement.</strong> A publish enqueues its eviction in the
/// same transaction and a background service dispatches it, so timing <c>PublishAsync</c> alone
/// would time half of what NFR-7 names. The outbox is dispatched here by calling the runner
/// directly, exactly as <c>CachingTests</c> does, so the clock covers the whole path from "the
/// editor pressed publish" to "the public URL would re-render".
/// </para>
/// <para>
/// The second half is the reusable footer, which every landing page in the dataset places
/// late-bound. That publish evicts a thousand pages at once, and it is the trigger risk <c>R8</c>
/// is stated against — the first measurement of it at a scale that could plausibly breach the
/// budget.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class PublishBenchmarkTests(SqlServerFixture fixture)
{
    private const int SeededPages = 5_000;

    private const double BudgetMilliseconds = 2_000;

    private const int Samples = 20;

    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync()
    {
        _bench = await PageWorkbench.CreateAsync(
            fixture,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

        await _bench.Resolve<ILoadTestSeeder>().SeedAsync(
            new LoadTestSeedOptions
            {
                Pages = SeededPages,
                MediaItems = 500,
                DistinctImages = 3,
                Tags = 50,
                Redirects = 50,
                BatchSize = 2_000,
            },
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

        _bench.Context.ChangeTracker.Clear();
    }

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task PublishingAPageOnAFiveThousandPageSiteStaysInsideTheBudget()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var pages = await _bench.Context.Pages
            .AsNoTracking()
            .Where(page => page.PublishedVersionId != null)
            .OrderBy(page => page.Id)
            .Select(page => page.Id)
            .Take(Samples + 1)
            .ToListAsync(cancellationToken);

        pages.Should().HaveCount(Samples + 1);

        // The first is discarded. It pays for everything that is cold once per process rather than
        // once per publish — the field type registry, the first compiled query, the JIT — none of
        // which NFR-7 is about.
        await PublishAsync(pages[0], cancellationToken);

        var elapsed = new List<double>(Samples);

        foreach (var pageId in pages.Skip(1))
        {
            elapsed.Add(await PublishAsync(pageId, cancellationToken));
        }

        elapsed.Sort();

        var p95 = elapsed[(int)Math.Ceiling(elapsed.Count * 0.95) - 1];

        await TestContext.Current!.OutputWriter.WriteLineAsync(
            $"page publish over {SeededPages} pages: median {elapsed[elapsed.Count / 2]:F0} ms, " +
            $"p95 {p95:F0} ms, slowest {elapsed[^1]:F0} ms.");

        p95.Should().BeLessThan(
            BudgetMilliseconds,
            "NFR-7 allows {0} ms; the slowest of {1} publishes took {2:F0} ms",
            BudgetMilliseconds,
            Samples,
            elapsed[^1]);
    }

    [Test]
    public async Task PublishingTheFooterEveryLandingPageShowsStaysInsideTheBudget()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var footer = await _bench.Context.ReusableContents
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        var impact = await _bench.Resolve<IReferenceQueryService>().WhereUsedAsync(
            ContentReferenceTargetType.ReusableContent,
            footer.Id,
            cancellationToken);

        impact.AffectedPageCount.Should().BeGreaterThan(
            100,
            "the fan-out is only worth timing if there is one");

        // Warm, for the same reason the page benchmark discards its first sample.
        await PublishFooterAsync(footer.Id, cancellationToken);

        var elapsed = await PublishFooterAsync(footer.Id, cancellationToken);

        await TestContext.Current!.OutputWriter.WriteLineAsync(
            $"reusable publish fanning out to {impact.AffectedPageCount} pages: {elapsed:F0} ms.");

        elapsed.Should().BeLessThan(
            BudgetMilliseconds,
            "R8's trigger is a publish that exceeds NFR-7's {0} ms because of the fan-out; this one " +
            "reached {1} pages in {2:F0} ms",
            BudgetMilliseconds,
            impact.AffectedPageCount,
            elapsed);
    }

    /// <summary>Publishes a page and dispatches its eviction, returning the milliseconds both took.</summary>
    private async Task<double> PublishAsync(int pageId, CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        var started = Stopwatch.GetTimestamp();

        var result = await _bench.Resolve<IPublishingService>().PublishAsync(
            pageId,
            cancellationToken: cancellationToken);

        await DispatchAsync(cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        result.IsSuccess.Should().BeTrue(PageWorkbench.Because(result));

        return elapsed;
    }

    private async Task<double> PublishFooterAsync(int reusableContentId, CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        var started = Stopwatch.GetTimestamp();

        var result = await _bench.Resolve<IReusableContentService>().PublishAsync(
            reusableContentId,
            acknowledgeWarnings: true,
            cancellationToken: cancellationToken);

        await DispatchAsync(cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        result.IsSuccess.Should().BeTrue(PageWorkbench.Because(result));

        return elapsed;
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _bench.NewScope();

        await scope.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(cancellationToken);
    }
}
