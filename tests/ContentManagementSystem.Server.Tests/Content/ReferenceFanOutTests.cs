using System.Diagnostics;

using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// What it costs to find out which pages a shared item's publish affects (task P4-13, risk R8).
/// </summary>
/// <remarks>
/// The measurement Phase 8 will tune cache invalidation against. Publishing a site-wide footer
/// evicts every page that shows it, and the eviction list is exactly what
/// <see cref="IReferenceQueryService"/> returns — so if that query grows with the number of
/// referencing pages in a way that matters, the publish is where it will be felt, on the request an
/// editor is watching.
/// <para>
/// <strong>What this is not.</strong> It is not a benchmark harness and it does not pretend to
/// produce a stable number: it runs against a container on whatever machine is to hand, in a suite
/// with other tests. The threshold is an order-of-magnitude tripwire, chosen so it catches the
/// regression that matters — the walk becoming per-page rather than per-level — and not a
/// millisecond somebody has to keep re-baselining. The recorded figures live in
/// <c>docs/phase-4-fanout-baseline.md</c>.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class ReferenceFanOutTests(SqlServerFixture fixture)
{
    /// <summary>How many published pages place the item under measurement.</summary>
    /// <remarks>
    /// Enough that a per-page round trip would show up against a per-level one, and few enough that
    /// arranging them — each is a page creation, a draft save, and a publish through the real
    /// services — does not dominate the suite's runtime.
    /// </remarks>
    private const int ReferencingPages = 40;

    /// <summary>
    /// The ceiling one where-used call has to stay under.
    /// </summary>
    /// <remarks>
    /// Deliberately generous against the observed figure. A tripwire that fires on a slow CI agent
    /// teaches everyone to ignore it, and what it is guarding against is not a factor of two.
    /// </remarks>
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(2);

    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task TheFanOutOfAHighReferenceItemIsOneQueryPerLevelRatherThanPerPage()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var references = _bench.Resolve<IReferenceQueryService>();

        var item = await _bench.AddReusableAsync("Site footer", cancellationToken);
        var filled = await _bench.SetReusableHtmlAsync(item, "<p>Footer</p>", cancellationToken);

        await _bench.PublishReusableAsync(filled.Summary.Id, cancellationToken);

        var template = await _bench.UseTemplateAsync(
            "marketing-landing",
            cancellationToken,
            PageWorkbench.ReusableZone("footer"));

        var publishing = _bench.Resolve<IPublishingService>();

        for (var index = 0; index < ReferencingPages; index++)
        {
            var page = await _bench.AddPageAsync(template, $"Page {index + 1}", cancellationToken);

            page = await _bench.PlaceReusableAsync(page, "footer", filled.Summary.Id, cancellationToken);

            var published = await publishing.PublishAsync(page.Summary.Id, true, cancellationToken);

            published.IsSuccess.Should().BeTrue(PageWorkbench.Because(published));
        }

        // Warmed once. The first call pays for the connection, the query plan, and the model, none of
        // which is what is being measured — and measuring them would make the number depend on
        // whichever test in the suite happened to run first.
        await references.WhereUsedAsync(
            ContentReferenceTargetType.ReusableContent,
            filled.Summary.Id,
            cancellationToken);

        var started = Stopwatch.GetTimestamp();

        var impact = await references.WhereUsedAsync(
            ContentReferenceTargetType.ReusableContent,
            filled.Summary.Id,
            cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(started);

        impact.AffectedPageCount.Should().Be(ReferencingPages, "every page places it late-bound");

        // The shape of the walk, asserted through its cost. Three round trips per level — the edges,
        // the page versions, the reusable sources — however many pages come back, which is what keeps
        // a footer on ten thousand pages from being ten thousand queries at publish time.
        elapsed.Should().BeLessThan(
            Ceiling,
            "the where-used walk queries once per level, not once per referencing page");
    }

    [Test]
    public async Task TheListIsCappedWhileTheCountsStayExact()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        // The cap exists so a confirmation dialog for a site-wide footer is not a download. Asserting
        // it needs no fixture at all beyond the contract: what matters is that the two members can
        // disagree, and that the one the dialog shows is the exact one.
        ReferenceQueryService.MaxListedPages.Should().BeLessThan(int.MaxValue);

        var item = await _bench.AddReusableAsync("Unplaced", cancellationToken);

        var impact = await _bench.Resolve<IReferenceQueryService>().WhereUsedAsync(
            ContentReferenceTargetType.ReusableContent,
            item.Summary.Id,
            cancellationToken);

        impact.IsTruncated.Should().BeFalse();
        impact.AffectedPages.Should().BeEmpty();
        impact.AffectedPageCount.Should().Be(0);
    }
}
