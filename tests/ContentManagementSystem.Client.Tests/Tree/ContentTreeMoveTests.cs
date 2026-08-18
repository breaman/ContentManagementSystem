using Bunit;

using ContentManagementSystem.Client.Components.Admin.Tree;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ContentManagementSystem.Client.Tests.Tree;

/// <summary>
/// Moving a page from the tree (task P6-03, acceptance criterion P6 #4).
/// </summary>
/// <remarks>
/// The keyboard path is what these assert, because it is the one that has to work and the one a
/// mouse-driven implementation quietly omits. What is checked is not that a key press does
/// something, but that it produces the same request a drag would — the same parent, the same
/// position — and that the confirmation appears exactly when a URL is about to change.
/// </remarks>
public class ContentTreeMoveTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    private readonly MovingPageClient _client = new();

    public ContentTreeMoveTests()
    {
        // The confirmation dialog imports its focus-trap module. There is no document here for it to
        // trap focus in, and the component is written to carry on without one — which is exactly
        // what loose mode reproduces.
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

    [Test]
    public void AltArrowDownMovesThePageDownOnePositionAmongItsSiblings()
    {
        // A reorder among siblings changes no address, so it goes straight through and both halves
        // of the conversation are visible in one press.
        _client.UrlChanges = [];

        var tree = _bunit.Render<ContentTree>();

        Press(tree, "ArrowDown", alt: true);

        _client.Requests.Should().SatisfyRespectively(
            preview =>
            {
                preview.Request.Preview.Should().BeTrue("nothing is written before the outcome is known");
                preview.Request.ParentId.Should().BeNull();
                preview.Request.Position.Should().Be(1);
            },
            commit =>
            {
                commit.Request.Preview.Should().BeFalse();
                commit.Request.Position.Should().Be(1);
            });
    }

    [Test]
    public void AltArrowUpOnTheFirstSiblingAsksForNothing()
    {
        var tree = _bunit.Render<ContentTree>();

        Press(tree, "ArrowUp", alt: true);

        _client.Requests.Should().BeEmpty(
            "there is nowhere above the first sibling, and a request that would be refused is a " +
            "round trip spent to tell the editor what the tree already showed");
    }

    [Test]
    public void AltArrowUpDoesNotAlsoMoveTheSelection()
    {
        var tree = _bunit.Render<ContentTree>();

        // Focus the second root first, so a fall-through to plain ArrowUp would be visible.
        Press(tree, "ArrowDown");
        Press(tree, "ArrowUp", alt: true);

        var focusable = tree.FindAll("[role=treeitem]")
            .Where(item => item.GetAttribute("tabindex") == "0")
            .ToList();

        focusable.Should().ContainSingle()
            .Which.TextContent.Should().Contain(
                "About",
                "Alt is the modifier that means 'move the page', so it must not also navigate");
    }

    [Test]
    public void AMoveThatChangesNoUrlIsAppliedWithoutADialog()
    {
        _client.UrlChanges = [];

        var tree = _bunit.Render<ContentTree>();

        Press(tree, "ArrowDown", alt: true);

        tree.FindAll("[role=dialog]").Should().BeEmpty(
            "a confirmation that says nothing will change trains editors to dismiss the one that " +
            "matters");

        _client.Requests.Should().HaveCount(2, "the preview and then the move itself");
    }

    [Test]
    public void AMoveThatChangesUrlsWaitsForAConfirmationListingThem()
    {
        var tree = _bunit.Render<ContentTree>();

        // Onto the second root, so Alt+Right has a sibling above it to move into.
        Press(tree, "ArrowDown");
        Press(tree, "ArrowRight", alt: true);

        _client.Requests.Should().ContainSingle()
            .Which.Request.Preview.Should().BeTrue("nothing may be written before the editor agrees");

        var dialog = tree.Find("[role=dialog]");

        dialog.TextContent.Should().Contain("/about").And.Contain("/pricing/about");
        dialog.TextContent.Should().Contain("redirect");
    }

    [Test]
    public void ConfirmingTheDialogAppliesTheSameMoveThatWasPreviewed()
    {
        var tree = _bunit.Render<ContentTree>();

        Press(tree, "ArrowDown");
        Press(tree, "ArrowRight", alt: true);

        tree.Find("[role=dialog] .btn-warning").Click();

        _client.Requests.Should().HaveCount(2);
        _client.Requests[1].Request.Should().Be(
            _client.Requests[0].Request with { Preview = false },
            "the confirmed move must be the previewed move, or the dialog described something else");
    }

    [Test]
    public void CancellingTheDialogWritesNothing()
    {
        var tree = _bunit.Render<ContentTree>();

        Press(tree, "ArrowDown");
        Press(tree, "ArrowRight", alt: true);

        tree.Find("[role=dialog] .btn-outline-secondary").Click();

        tree.FindAll("[role=dialog]").Should().BeEmpty();
        _client.Requests.Should().ContainSingle().Which.Request.Preview.Should().BeTrue();
    }

    [Test]
    public void ARefusedMoveIsReportedInWordsRatherThanSwallowed()
    {
        _client.Refusal = "A page cannot be moved inside itself or one of its own descendants.";

        var tree = _bunit.Render<ContentTree>();

        Press(tree, "ArrowDown", alt: true);

        tree.Find("[role=alert]").TextContent.Should().Contain("moved inside itself");
    }

    /// <summary>Sends a key press to the tree, which handles them all in one place.</summary>
    private static void Press(IRenderedComponent<ContentTree> tree, string key, bool alt = false) =>
        tree.Find(".cms-tree").KeyDown(new KeyboardEventArgs { Key = key, AltKey = alt });

    /// <summary>
    /// Two roots with a child, and a record of every move asked for.
    /// </summary>
    /// <remarks>
    /// The preview it returns is fixed rather than computed: what these tests are about is the
    /// conversation between the tree and the server, and a fake that tried to work out real URLs
    /// would be asserting its own arithmetic.
    /// </remarks>
    private sealed class MovingPageClient : StubPageClient
    {
        /// <summary>Every move requested, preview and commit alike, in order.</summary>
        public List<(int Id, MovePageRequest Request)> Requests { get; } = [];

        /// <summary>What the preview reports. Empty is a reorder that changes no address.</summary>
        public IReadOnlyList<PageUrlChangeSummary> UrlChanges { get; set; } =
        [
            new(2, "About", "/about", "/pricing/about", true),
        ];

        /// <summary>When set, every move is refused with this message.</summary>
        public string? Refusal { get; set; }

        /// <inheritdoc />
        public override Task<IReadOnlyList<PageTreeNode>> GetTreeAsync(
            int? parentId = null,
            int depth = 1,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PageTreeNode> nodes = parentId switch
            {
                null => [new(Page(1, "Pricing"), []), new(Page(2, "About"), [])],
                _ => [],
            };

            return Task.FromResult(nodes);
        }

        /// <inheritdoc />
        public override Task<StructureClientResult<PageMoveResult>> MoveAsync(
            int id,
            MovePageRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((id, request));

            if (Refusal is not null)
            {
                return Task.FromResult(StructureClientResult<PageMoveResult>.Failure(
                    PageCodes.MoveWouldCreateCycle,
                    Refusal));
            }

            return Task.FromResult(StructureClientResult<PageMoveResult>.Success(
                new PageMoveResult(
                    id,
                    request.ParentId,
                    request.Position ?? 0,
                    UrlChanges,
                    request.Preview)));
        }
    }

}
