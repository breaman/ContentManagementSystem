using Bunit;

using ContentManagementSystem.Client.Components.Admin.Tree;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ContentManagementSystem.Client.Tests.Tree;

/// <summary>
/// The tree's context menu, clipboard, and delete confirmation (task P6-04, spec section 14.2).
/// </summary>
/// <remarks>
/// Two things get most of the attention. The menu must be reachable without a pointer, because every
/// operation the tree offers is behind it — and the delete must state what it is about to take
/// before it takes it, which is acceptance criterion P6 #10 and the only one of these operations
/// that is unpleasant to get wrong.
/// </remarks>
public class ContentTreeMenuTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    private readonly MenuPageClient _client = new();

    private readonly SilentToastService _toasts = new();

    public ContentTreeMenuTests()
    {
        _bunit.JSInterop.Mode = JSRuntimeMode.Loose;

        _bunit.Services.AddSingleton<IPageClient>(_client);
        _bunit.Services.AddSingleton<IToastService>(_toasts);
        _bunit.Services.AddSingleton<TimeProvider>(
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero)));
    }

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void ShiftF10OpensTheMenuOnTheFocusedRow()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find(".cms-tree").KeyDown(new KeyboardEventArgs { Key = "F10", ShiftKey = true });

        var menu = tree.Find("[role=menu]");

        menu.GetAttribute("aria-label").Should().Be(
            "Actions for Pricing",
            "a menu with no name is one a screen reader announces as an unlabelled group");
        menu.QuerySelectorAll("[role=menuitem]").Should().NotBeEmpty();
    }

    [Test]
    public void TheContextMenuKeyOpensItToo()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find(".cms-tree").KeyDown(new KeyboardEventArgs { Key = "ContextMenu" });

        tree.FindAll("[role=menu]").Should().ContainSingle(
            "Mac keyboards have no Context Menu key and PC keyboards do, so both gestures have to work");
    }

    [Test]
    public void ARightClickOpensTheMenuWhereThePointerIs()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find(".cms-tree__row").ContextMenu(new MouseEventArgs { ClientX = 120, ClientY = 240 });

        tree.Find("[role=menu]").GetAttribute("style")
            .Should().Contain("120px").And.Contain("240px");
    }

    [Test]
    public void EscapeDismissesTheMenuWithoutRunningAnything()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find(".cms-tree").KeyDown(new KeyboardEventArgs { Key = "ContextMenu" });
        tree.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        tree.FindAll("[role=menu]").Should().BeEmpty();
        _client.Duplicated.Should().BeEmpty();
    }

    [Test]
    public void APageWithNothingPublishedIsNotOfferedUnpublish()
    {
        var tree = _bunit.Render<ContentTree>();

        // "Careers" is the fixture's never-published root.
        tree.FindAll(".cms-tree__row")[1].ContextMenu(new MouseEventArgs());

        var labels = tree.FindAll("[role=menuitem]").Select(item => item.TextContent.Trim()).ToList();

        labels.Should().Contain("Publish");
        labels.Should().NotContain(
            "Unpublish",
            "an entry that cannot do anything is one an editor reads and dismisses every time");
    }

    [Test]
    public void PasteIsOfferedOnlyOnceSomethingIsOnTheClipboardAndNeverOnItself()
    {
        var tree = _bunit.Render<ContentTree>();

        Labels(tree, row: 0).Should().NotContain("Paste inside");

        Choose(tree, row: 0, "Copy");

        Labels(tree, row: 0).Should().NotContain(
            "Paste inside",
            "pasting a page inside itself is the one target that cannot work");
        Labels(tree, row: 1).Should().Contain("Paste inside");
    }

    [Test]
    public void PastingACopiedPageDuplicatesItDeeplyIntoTheTarget()
    {
        var tree = _bunit.Render<ContentTree>();

        Choose(tree, row: 0, "Copy");
        Choose(tree, row: 1, "Paste inside");

        _client.Duplicated.Should().ContainSingle().Which.Should().Be(
            (1, true, 3),
            "pasting half a section somewhere is never what was meant, so a paste is always deep");
    }

    [Test]
    public void PastingACutPageGoesThroughTheMoveConfirmation()
    {
        var tree = _bunit.Render<ContentTree>();

        Choose(tree, row: 0, "Move to…");
        Choose(tree, row: 1, "Paste inside");

        _client.Moves.Should().ContainSingle()
            .Which.Preview.Should().BeTrue(
                "paste-as-move is the same move a drag makes, confirmation and all");

        tree.Find("[role=dialog]").TextContent.Should().Contain("changes");

        // Dismissed before the test ends. A dialog left open at teardown is torn down mid-flight,
        // and its focus-trap interop then runs against a renderer that is going away.
        tree.FindAll("[role=dialog] .btn-outline-secondary").Single().Click();
    }

    [Test]
    public void DeleteStatesHowMuchItTakesBeforeAnythingHappens()
    {
        var tree = _bunit.Render<ContentTree>();

        Choose(tree, row: 0, "Delete…");

        var dialog = tree.Find("[role=dialog]");

        dialog.TextContent.Should().Contain("4 page(s)").And.Contain("3 of them are live");
        _client.Deleted.Should().BeEmpty("the count comes before the delete, not after it");
    }

    [Test]
    public void ConfirmingTheDeleteSendsItAndSaysWhatWent()
    {
        var tree = _bunit.Render<ContentTree>();

        Choose(tree, row: 0, "Delete…");
        tree.Find("[role=dialog] .btn-danger").Click();

        _client.Deleted.Should().Equal(1);
        _toasts.Shown.Should().ContainSingle()
            .Which.Message.Should().Contain("4 page(s) beneath it");
    }

    [Test]
    public void PublishBranchIsOfferedOnlyOnAPageThatHasChildren()
    {
        var tree = _bunit.Render<ContentTree>();

        Labels(tree, row: 0).Should().Contain("Publish branch…");
        Labels(tree, row: 1).Should().NotContain(
            "Publish branch…",
            "on a leaf it is the entry above it wearing a longer name");
    }

    [Test]
    public void PublishBranchStatesWhatTheBranchCoversBeforePublishingAny()
    {
        var tree = _bunit.Render<ContentTree>();

        Choose(tree, row: 0, "Publish branch…");

        var dialog = tree.Find("[role=dialog]");

        dialog.TextContent.Should().Contain("You selected 1 page(s)").And.Contain("publish 3");
        _client.BulkStarts.Should().BeEmpty("the impact comes before the batch, not after it");

        tree.FindAll("[role=dialog] .btn-outline-secondary").Single().Click();
    }

    [Test]
    public void ConfirmingAPublishBranchReportsThePagesThatCouldNotBePublished()
    {
        var tree = _bunit.Render<ContentTree>();

        Choose(tree, row: 0, "Publish branch…");
        tree.Find("[role=dialog] .btn-primary").Click();

        _client.BulkStarts.Should().ContainSingle()
            .Which.Selection.IncludeDescendants.Should().BeTrue(
                "publishing a branch is one selection the server resolves, not a walk of the tree");

        var dialog = tree.Find("[role=dialog]");

        dialog.TextContent.Should().Contain("2 of 3 page(s) were published");
        dialog.TextContent.Should().Contain("Careers").And.Contain("Hero title");

        tree.FindAll("[role=dialog] .btn-primary").Single().Click();
        tree.FindAll("[role=dialog]").Should().BeEmpty();
    }

    [Test]
    public void NewChildAsksTheHostRatherThanCreatingThePageItself()
    {
        PageSummary? under = null;

        var tree = _bunit.Render<ContentTree>(parameters => parameters
            .Add(component => component.OnCreateRequested, page => under = page));

        Choose(tree, row: 0, "New child page");

        under.Should().NotBeNull();
        under!.Id.Should().Be(1);
    }

    [Test]
    public void TypingInTheFilterReplacesTheTreeWithASiteWideResultList()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find("input[type=search]").Input("ent");

        // The debounce has to elapse before the search is sent.
        tree.WaitForAssertion(() =>
            tree.Find("[aria-label='Filter results']").TextContent.Should().Contain("Enterprise"));

        tree.FindAll("[role=tree]").Should().BeEmpty(
            "a lazily loaded tree can only hide what it has already fetched, so a filter that " +
            "pruned it would answer 'no results' for most of the site");

        _client.Searches.Should().Contain("ent");
    }

    [Test]
    public void ClearingTheFilterPutsTheTreeBack()
    {
        var tree = _bunit.Render<ContentTree>();

        tree.Find("input[type=search]").Input("ent");
        tree.WaitForAssertion(() => tree.Find("[aria-label='Filter results']").TextContent
            .Should().Contain("Enterprise"));

        // Retried, and the click is inside the retry. Immediately after an awaited search settles,
        // a click can be dispatched against the handler of a superseded render and do nothing at
        // all — silently, because there is no exception for an event handler that has gone. Finding
        // and clicking again on the next attempt is what makes this assert the button rather than
        // the renderer's timing.
        tree.WaitForAssertion(() =>
        {
            tree.FindAll(".cms-tree__filter-clear").Single().Click();
            tree.FindAll("[role=tree]").Should().ContainSingle();
        });
    }

    /// <summary>Opens a row's menu and reads the labels it offers.</summary>
    private static List<string> Labels(IRenderedComponent<ContentTree> tree, int row)
    {
        tree.FindAll(".cms-tree__row")[row].ContextMenu(new MouseEventArgs());

        var labels = tree.FindAll("[role=menuitem]").Select(item => item.TextContent.Trim()).ToList();

        // Dismissed again, so the caller can open another row's menu without two being open at once.
        tree.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        return labels;
    }

    /// <summary>Opens a row's menu and chooses the entry with a given label.</summary>
    private static void Choose(IRenderedComponent<ContentTree> tree, int row, string label)
    {
        tree.FindAll(".cms-tree__row")[row].ContextMenu(new MouseEventArgs());

        tree.FindAll("[role=menuitem]")
            .First(item => item.TextContent.Trim() == label)
            .Click();
    }

    /// <summary>Two roots — one published with children, one never published — and a record of calls.</summary>
    private sealed class MenuPageClient : StubPageClient
    {
        /// <summary>Every duplicate asked for, as (id, deep, parentId).</summary>
        public List<(int Id, bool Deep, int? ParentId)> Duplicated { get; } = [];

        /// <summary>Every page a delete was sent for.</summary>
        public List<int> Deleted { get; } = [];

        /// <summary>Every move requested.</summary>
        public List<MovePageRequest> Moves { get; } = [];

        /// <summary>Every term the filter searched for.</summary>
        public List<string> Searches { get; } = [];

        /// <summary>Every batch that was actually started.</summary>
        public List<BulkOperationRequest> BulkStarts { get; } = [];

        /// <inheritdoc />
        /// <remarks>
        /// One selected page resolving to three, which is the whole reason the branch dialog exists:
        /// the number worth confirming is not the number the editor clicked.
        /// </remarks>
        public override Task<StructureClientResult<BulkImpact>> PreviewBulkAsync(
            BulkOperationRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            return Task.FromResult(StructureClientResult<BulkImpact>.Success(new BulkImpact(
                request.Operation,
                [
                    new BulkImpactItem(1, "Pricing", IsPublished: true, WasSelected: true),
                    new BulkImpactItem(2, "Enterprise", IsPublished: false, WasSelected: false),
                    new BulkImpactItem(4, "Careers", IsPublished: false, WasSelected: false),
                ],
                SelectedCount: 1,
                PublishedCount: 1,
                RunsInBackground: false,
                [])));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Finished on the first answer, which is what a batch under the background threshold does,
        /// and carrying one failure — a report that only ever shows successes never renders the half
        /// of the dialog that matters.
        /// </remarks>
        public override Task<StructureClientResult<BulkJobStatus>> StartBulkAsync(
            BulkOperationRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            BulkStarts.Add(request);

            return Task.FromResult(StructureClientResult<BulkJobStatus>.Success(new BulkJobStatus(
                Guid.NewGuid(),
                request.Operation,
                BulkJobState.Completed,
                3,
                [
                    new BulkItemResult(1, "Pricing", Succeeded: true, []),
                    new BulkItemResult(2, "Enterprise", Succeeded: true, []),
                    new BulkItemResult(4, "Careers", Succeeded: false,
                        [new ApiDiagnostic("field.required", "\"Hero title\" is required.", null)]),
                ],
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch)));
        }

        /// <inheritdoc />
        public override Task<IReadOnlyList<PageTreeNode>> GetTreeAsync(
            int? parentId = null,
            int depth = 1,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PageTreeNode> nodes = parentId switch
            {
                null =>
                [
                    new(Page(1, "Pricing", hasChildren: true), []),
                    new(Page(3, "Careers", published: null), []),
                ],
                1 => [new(Page(2, "Enterprise", parentId: 1), [])],
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

            return Task.FromResult(new CursorPage<PageSummary>(
                [Page(2, "Enterprise", parentId: 1)],
                null));
        }

        /// <inheritdoc />
        public override Task<StructureClientResult<PageDetail>> DuplicateAsync(
            int id,
            bool deep = false,
            int? parentId = null,
            CancellationToken cancellationToken = default)
        {
            Duplicated.Add((id, deep, parentId));

            return Task.FromResult(StructureClientResult<PageDetail>.Failure(
                "test.not-needed",
                "The duplicate's body is not what these tests are about."));
        }

        /// <inheritdoc />
        public override Task<SubtreeImpact?> DescribeDeleteAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SubtreeImpact?>(new SubtreeImpact(id, "Pricing", 4, 3));

        /// <inheritdoc />
        public override Task<StructureClientResult<SubtreeResult>> DeleteAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            Deleted.Add(id);

            return Task.FromResult(StructureClientResult<SubtreeResult>.Success(
                new SubtreeResult(id, [id, 2, 4, 5, 6], UnpublishedCount: 3, [])));
        }

        /// <inheritdoc />
        public override Task<StructureClientResult<PageMoveResult>> MoveAsync(
            int id,
            MovePageRequest request,
            CancellationToken cancellationToken = default)
        {
            Moves.Add(request);

            return Task.FromResult(StructureClientResult<PageMoveResult>.Success(
                new PageMoveResult(
                    id,
                    request.ParentId,
                    request.Position ?? 0,
                    [new PageUrlChangeSummary(id, "Pricing", "/pricing", "/careers/pricing", true)],
                    request.Preview)));
        }
    }
}
