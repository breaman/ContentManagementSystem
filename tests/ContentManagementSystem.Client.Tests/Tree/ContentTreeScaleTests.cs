using System.Diagnostics;

using Bunit;

using ContentManagementSystem.Client.Components.Admin.Tree;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ContentManagementSystem.Client.Tests.Tree;

/// <summary>
/// The tree at content scale (task P6-35, acceptance criterion P6 #7).
/// </summary>
/// <remarks>
/// The criterion is "responsive at 5,000 pages with 500 siblings under one parent", and the two
/// halves are answered by two different mechanisms — so they are asserted separately rather than by
/// timing one big render and hoping.
/// <list type="bullet">
/// <item><description>
/// <strong>5,000 pages</strong> is answered by lazy loading: the tree fetches one level per
/// expansion, so the size of the site never reaches the browser at all. What is asserted is the
/// request count, because a tree that quietly fetched depth 5 would still look fast on a fixture and
/// would not be on a real site.
/// </description></item>
/// <item><description>
/// <strong>500 siblings</strong> is answered by virtualization: what is asserted is that the DOM
/// holds a bounded number of rows rather than 500, because rendering 500 rows is exactly the thing
/// that is slow and no timing threshold is stable enough to catch it on every machine.
/// </description></item>
/// </list>
/// <para>
/// A wall-clock budget is asserted too, but a deliberately loose one: it exists to catch an
/// accidental O(n²) — a walk of every sibling per sibling — rather than to police milliseconds on
/// somebody's laptop.
/// </para>
/// </remarks>
public class ContentTreeScaleTests : IDisposable
{
    /// <summary>Siblings under the one crowded parent, as the criterion names it.</summary>
    private const int Siblings = 500;

    /// <summary>Roots the fixture's site has.</summary>
    private const int Roots = 20;

    private readonly BunitContext _bunit = new();

    private readonly LargeSitePageClient _client = new();

    public ContentTreeScaleTests()
    {
        _bunit.JSInterop.Mode = JSRuntimeMode.Loose;

        _bunit.Services.AddSingleton<IPageClient>(_client);
        _bunit.Services.AddSingleton<IToastService>(new SilentToastService());
        _bunit.Services.AddSingleton<TimeProvider>(
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero)));
    }

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void OpeningTheTreeReadsOneLevelHoweverLargeTheSiteIs()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.FindAll(".cms-tree__row").Should().HaveCount(Roots);

        _client.Fetches.Should().ContainSingle()
            .Which.Should().Be(
                (null, 1),
                "a tree that fetched deeper would carry the whole site to a browser that shows " +
                "twenty rows of it");
    }

    [Fact]
    public void AParentWithFiveHundredChildrenRendersABoundedNumberOfRows()
    {
        var tree = _bunit.Render<ContentTree>();

        var stopwatch = Stopwatch.StartNew();

        tree.FindAll(".cms-tree__expander")[0].Click();

        tree.WaitForAssertion(() => _client.Fetches.Should().HaveCount(2));

        stopwatch.Stop();

        var rows = tree.FindAll(".cms-tree__row").Count;

        rows.Should().BeLessThan(
            Siblings,
            "500 rows in the document is the thing that is slow; Virtualize is what stops them " +
            "being there (task P6-02)");

        // Loose on purpose. The point is to catch a walk of every sibling per sibling, not to police
        // milliseconds on hardware this suite cannot know anything about.
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(
            2000,
            "expanding one crowded level took {0}ms, which is long enough to suspect quadratic work",
            stopwatch.ElapsedMilliseconds);

        // And the level really did arrive: a bounded row count is only good news if the data is
        // there to be scrolled through.
        _client.Fetches.Should().Contain((1, 1));
    }

    [Fact]
    public void TheFilterSearchesTheServerRatherThanTheRowsItHappensToHold()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find("input[type=search]").Input("page 4000");

        tree.WaitForAssertion(() => _client.Searches.Should().NotBeEmpty());

        // The whole reason the filter is a search: at 5,000 pages the tree holds twenty of them, so
        // a client-side filter would answer "no results" for 99.6% of the site (task P6-04).
        _client.Searches.Should().Contain("page 4000");
    }

    /// <summary>A site of twenty roots, one of which has five hundred children.</summary>
    private sealed class LargeSitePageClient : StubPageClient
    {
        /// <summary>Every level fetched, as (parentId, depth).</summary>
        public List<(int? ParentId, int Depth)> Fetches { get; } = [];

        /// <summary>Every term the filter searched for.</summary>
        public List<string> Searches { get; } = [];

        /// <inheritdoc />
        public override Task<IReadOnlyList<PageTreeNode>> GetTreeAsync(
            int? parentId = null,
            int depth = 1,
            CancellationToken cancellationToken = default)
        {
            Fetches.Add((parentId, depth));

            IReadOnlyList<PageTreeNode> nodes = parentId switch
            {
                null =>
                [
                    // The crowded parent first, so the test can expand it by clicking the first
                    // expander it finds.
                    new(Page(1, "Products", hasChildren: true), []),
                    .. Enumerable.Range(2, Roots - 1)
                        .Select(id => new PageTreeNode(Page(id, $"Section {id}"), [])),
                ],
                1 =>
                [
                    .. Enumerable.Range(1000, Siblings)
                        .Select(id => new PageTreeNode(Page(id, $"Page {id}", parentId: 1), [])),
                ],
                _ => [],
            };

            return Task.FromResult(nodes);
        }

        /// <inheritdoc />
        public override Task<CursorPage<PageSummary>> ListAsync(
            PageQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);

            Searches.Add(query.Search ?? string.Empty);

            return Task.FromResult(new CursorPage<PageSummary>([Page(4000, "Page 4000", parentId: 1)], null));
        }
    }
}
