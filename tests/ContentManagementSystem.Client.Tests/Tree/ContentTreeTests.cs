using Bunit;

using ContentManagementSystem.Client.Components.Admin.Tree;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ContentManagementSystem.Client.Tests.Tree;

/// <summary>
/// The content tree (task P6-02, spec section 14.2).
/// </summary>
/// <remarks>
/// What these assert is the behaviour a tree gets wrong quietly: that a level is fetched once and
/// only when it is opened, that the ARIA a screen reader navigates by is present and correct, and
/// that the state badge is a word rather than a colour. A tree that renders and looks right can be
/// failing every one of those.
/// </remarks>
public class ContentTreeTests : IDisposable
{
    /// <summary>Fixed, because a scheduled publish is "in the future" only relative to something.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly BunitContext _bunit = new();

    private readonly RecordingPageClient _client = new();

    public ContentTreeTests()
    {
        _bunit.Services.AddSingleton<IPageClient>(_client);
        _bunit.Services.AddSingleton<IToastService>(new SilentToastService());
        _bunit.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
    }

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void TheRootLevelIsFetchedOnceAndRenderedAsATree()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find("[role=tree]").GetAttribute("aria-label").Should().Be("Content tree");
        tree.FindAll("[role=treeitem]").Should().HaveCount(2);
        tree.Markup.Should().Contain("Products").And.Contain("About");

        _client.Requested.Should().ContainSingle().Which.Should().BeNull(
            "the root level is one fetch with no parent, not one per node");
    }

    [Test]
    public void ChildrenAreNotFetchedUntilTheirParentIsOpened()
    {
        var tree = _bunit.Render<ContentTree>();

        _client.Requested.Should().NotContain(1, "nothing has been expanded yet");

        tree.Find(".cms-tree__expander").Click();

        _client.Requested.Should().Contain(1);
        tree.Markup.Should().Contain("Widgets");
    }

    [Test]
    public void AReopenedNodeIsNotFetchedASecondTime()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find(".cms-tree__expander").Click();
        tree.Find(".cms-tree__expander").Click();
        tree.Find(".cms-tree__expander").Click();

        _client.Requested.Count(id => id == 1).Should().Be(
            1,
            "a level already fetched is still true, and refetching it on every open makes the tree " +
            "flicker for nothing");
    }

    [Test]
    public void AnExpandedNodeReportsItsPositionAndDepthToAScreenReader()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find(".cms-tree__expander").Click();

        var items = tree.FindAll("[role=treeitem]");

        items[0].GetAttribute("aria-expanded").Should().Be("true");
        items[0].GetAttribute("aria-level").Should().Be("1");
        items[0].GetAttribute("aria-setsize").Should().Be("2");
        items[0].GetAttribute("aria-posinset").Should().Be("1");

        var child = items.First(item => item.GetAttribute("aria-level") == "2");

        child.GetAttribute("aria-posinset").Should().Be("1");
        child.GetAttribute("aria-setsize").Should().Be("2");
    }

    [Test]
    public void ALeafCarriesNoExpandedStateAtAll()
    {
        var tree = _bunit.Render<ContentTree>();

        var leaf = tree.FindAll("[role=treeitem]")[1];

        leaf.HasAttribute("aria-expanded").Should().BeFalse(
            "aria-expanded=false on a leaf announces a closed node and invites a key press that " +
            "does nothing");
    }

    [Test]
    public void ExactlyOneRowHoldsTheTreesTabStop()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find(".cms-tree__expander").Click();

        var focusable = tree.FindAll("[role=treeitem]")
            .Where(item => item.GetAttribute("tabindex") == "0")
            .ToList();

        focusable.Should().HaveCount(
            1,
            "a roving tabindex is what stops Tab walking through five thousand pages");
    }

    [Test]
    public void ActivatingARowReportsThePageToTheHost()
    {
        PageSummary? activated = null;

        var tree = _bunit.Render<ContentTree>(parameters => parameters
            .Add(component => component.OnActivated, page => activated = page));

        tree.FindAll(".cms-tree__row")[1].Click();

        activated.Should().NotBeNull();
        activated!.Title.Should().Be("About");
    }

    [Test]
    public void ClickingAnExpanderDoesNotAlsoOpenThePage()
    {
        var activations = 0;

        var tree = _bunit.Render<ContentTree>(parameters => parameters
            .Add(component => component.OnActivated, _ => activations++));

        tree.Find(".cms-tree__expander").Click();

        activations.Should().Be(
            0,
            "the expander and the row are two different gestures, and conflating them navigates " +
            "away from the page an editor was only trying to look inside");
    }

    [Test]
    public void EveryStateIsWrittenAsAWordAndNotOnlyAsAColour()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find(".cms-tree__expander").Click();

        // The fixture's second child is scheduled and locked, which is the combination that would
        // be lost if the lock replaced the state instead of joining it.
        tree.Markup.Should().Contain("Scheduled").And.Contain("Open in the editor by Elena");
    }

    [Test]
    [Arguments("Draft", 1, false, null, PageTreeStatus.Published)]
    [Arguments("Draft", 1, true, null, PageTreeStatus.UnpublishedChanges)]
    [Arguments("Draft", null, false, null, PageTreeStatus.NotPublished)]
    [Arguments("InReview", 1, true, null, PageTreeStatus.InReview)]
    [Arguments("Rejected", 1, true, null, PageTreeStatus.Rejected)]
    [Arguments("Draft", 1, true, "future", PageTreeStatus.Scheduled)]
    [Arguments("Draft", 1, true, "past", PageTreeStatus.UnpublishedChanges)]
    public void TheStatusPrecedenceIsWhatTheEditorNeedsToKnowNext(
        string status,
        int? published,
        bool unpublishedChanges,
        string? schedule,
        PageTreeStatus expected)
    {
        var scheduled = schedule switch
        {
            "future" => Now.AddHours(1),
            "past" => Now.AddHours(-1),
            _ => (DateTimeOffset?)null,
        };

        var page = StubPageClient.Page(
            1,
            "Pricing",
            published: published,
            unpublishedChanges: unpublishedChanges,
            status: status,
            scheduled: scheduled);

        PageTreeStatuses.Classify(page, Now).Should().Be(expected);
    }

    /// <summary>
    /// A three-level fixture that records which levels were asked for.
    /// </summary>
    /// <remarks>
    /// Recording the requests rather than counting renders is what makes the lazy-loading assertions
    /// mean something: a tree that fetched everything up front would render identically.
    /// </remarks>
    private sealed class RecordingPageClient : StubPageClient
    {
        /// <summary>Every parent id the tree has asked for, in order. Null is the root level.</summary>
        public List<int?> Requested { get; } = [];

        /// <inheritdoc />
        public override Task<IReadOnlyList<PageTreeNode>> GetTreeAsync(
            int? parentId = null,
            int depth = 1,
            CancellationToken cancellationToken = default)
        {
            Requested.Add(parentId);

            IReadOnlyList<PageTreeNode> nodes = parentId switch
            {
                null =>
                [
                    Node(Page(1, "Products", hasChildren: true)),
                    Node(Page(2, "About")),
                ],
                1 =>
                [
                    Node(Page(3, "Widgets", parentId: 1)),
                    Node(Page(
                        4,
                        "Gadgets",
                        parentId: 1,
                        scheduled: Now.AddDays(1),
                        lockedBy: "Elena")),
                ],
                _ => [],
            };

            return Task.FromResult(nodes);
        }

        private static PageTreeNode Node(PageSummary page) => new(page, []);
    }
}
