using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Shared.Content;

using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// The delivery half of reusable content: what a placement renders, and what it does when it cannot
/// (tasks P4-04 and P4-14, spec section 9.2).
/// </summary>
/// <remarks>
/// Every case here is asserted through <c>CmsZone</c> rather than by calling the renderer, because
/// what is under test is the dispatch as delivery performs it — the stored value's discriminator
/// selecting the renderer, the renderer resolving an item, the item's block type key selecting a
/// component. Constructing the renderer directly would assert the last step and assume the first
/// three.
/// </remarks>
public class ReusableRendererTests : IDisposable
{
    /// <summary>Key of the built-in block type, which has a deployed component to render through.</summary>
    private const string RawHtml = "rawHtml";

    /// <summary>Key of the test block type whose markup names a nested reusable placement.</summary>
    private const string Nestable = "nestable";

    private readonly FieldRendererHarness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();

        GC.SuppressFinalize(this);
    }

    [Test]
    public void ALateBoundPlacementRendersWhicheverVersionIsPublishedNow()
    {
        _harness.Reusable.Add(
            3,
            RawHtml,
            [(101, Fragment("<p>January banner</p>")), (102, Fragment("<p>February banner</p>"))],
            publishedVersionId: 102);

        var markup = _harness.Render("""{"type":"reusable","reusableContentId":3}""");

        // The mechanism behind G4 seen from the page's side: nothing about this placement changed,
        // and it now shows the newer version because the item's published pointer moved.
        markup.Should().Contain("February banner").And.NotContain("January banner");
    }

    [Test]
    public void APinnedPlacementRendersTheVersionItNamesRatherThanTheLatest()
    {
        _harness.Reusable.Add(
            3,
            RawHtml,
            [(101, Fragment("<p>January banner</p>")), (102, Fragment("<p>February banner</p>"))],
            publishedVersionId: 102);

        var markup = _harness.Render(
            """{"type":"reusable","reusableContentId":3,"pinnedVersionId":101}""");

        // The compliance escape hatch of spec section 9.2: this page alone stops following the item.
        markup.Should().Contain("January banner").And.NotContain("February banner");
    }

    [Test]
    public void APlacementOfAnUnpublishedItemRendersNothingAndSaysWhy()
    {
        _harness.Reusable.Add(3, RawHtml, [(101, Fragment("<p>Draft banner</p>"))], publishedVersionId: null);

        var markup = _harness.Render("""{"type":"reusable","reusableContentId":3}""");

        // Spec section 15.3: render nothing, log a warning, and let the broken-references report pick
        // it up. The log is asserted for its reason and not merely its level, because "not published"
        // and "deleted" have different remedies and the report is built from this line.
        markup.Should().BeEmpty();
        _harness.Logs.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("is not published"));
    }

    [Test]
    public void AnUnpublishedItemStillRendersInPreviewSoAnEditorCanSeeIt()
    {
        _harness.Reusable.Add(3, RawHtml, [(101, Fragment("<p>Draft banner</p>"))], publishedVersionId: null);

        var markup = _harness.Render(
            """{"type":"reusable","reusableContentId":3}""",
            mode: CmsRenderMode.Preview);

        // The same fixture the previous test renders nothing for. Asserting the pair is what makes
        // this a statement about the audience rather than about two different items.
        markup.Should().Contain("Draft banner");
    }

    [Test]
    public void APlacementIsBadgedInPreviewAndNotOnThePublicSite()
    {
        _harness.Reusable.Add(3, RawHtml, [(101, Fragment("<p>Footer</p>"))], publishedVersionId: 101);

        var live = _harness.Render("""{"type":"reusable","reusableContentId":3}""");
        var preview = _harness.Render(
            """{"type":"reusable","reusableContentId":3}""",
            mode: CmsRenderMode.Preview);

        // An anonymous visitor is looking at a footer, not at the fact that it is shared. An editor
        // needs to be told before they try to edit it here.
        live.Should().NotContain("cms-reusable-badge");
        preview.Should().Contain("cms-reusable-badge");
    }

    [Test]
    public void AStalePinIsMarkedForTheUpdateToLatestAction()
    {
        _harness.Reusable.Add(
            3,
            RawHtml,
            [(101, Fragment("<p>v1</p>")), (102, Fragment("<p>v2</p>"))],
            publishedVersionId: 102);

        var preview = _harness.Render(
            """{"type":"reusable","reusableContentId":3,"pinnedVersionId":101}""",
            mode: CmsRenderMode.Preview);

        // Task P4-05's affordance, carried as attributes rather than as a control: the previewed
        // page is static SSR with no interactivity beneath it, so the backoffice reads these out of
        // the frame and offers the action in its own chrome.
        preview.Should().Contain("""data-cms-reusable-pinned="true" """.TrimEnd());
        preview.Should().Contain("""data-cms-reusable-stale="true" """.TrimEnd());
        preview.Should().Contain("""data-cms-reusable-latest="2" """.TrimEnd());
    }

    [Test]
    public void APinToTheVersionThatIsCurrentIsNotStale()
    {
        _harness.Reusable.Add(3, RawHtml, [(101, Fragment("<p>v1</p>"))], publishedVersionId: 101);

        var preview = _harness.Render(
            """{"type":"reusable","reusableContentId":3,"pinnedVersionId":101}""",
            mode: CmsRenderMode.Preview);

        // Offering "update to latest" here would be an action that does nothing, which is worse than
        // not offering it: it teaches an editor that the badge means nothing.
        preview.Should().Contain("""data-cms-reusable-stale="false" """.TrimEnd());
    }

    [Test]
    public void APinToAVersionThatIsGoneRendersNothingAndPointsAtTheRemedy()
    {
        _harness.Reusable.Add(3, RawHtml, [(101, Fragment("<p>v1</p>"))], publishedVersionId: 101);

        var markup = _harness.Render(
            """{"type":"reusable","reusableContentId":3,"pinnedVersionId":999}""");

        markup.Should().BeEmpty();

        // The item is fine and the page is wrong, so the remedy named is the placement's action and
        // not anything done to the item.
        _harness.Logs.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("update to latest"));
    }

    [Test]
    public void ANestedPlacementRendersThroughTheSameDispatch()
    {
        // Item 4 is a footer whose own content places item 3, the banner.
        _harness.Reusable.Add(3, RawHtml, [(101, Fragment("<p>Inner banner</p>"))], publishedVersionId: 101);
        _harness.Reusable.Add(
            4,
            Nestable,
            [(201, Nested("<p>Outer footer</p>", reusableContentId: 3))],
            publishedVersionId: 201);

        var markup = _harness.Render("""{"type":"reusable","reusableContentId":4}""");

        // Both levels render, which is what makes "publishing the banner changes every page showing
        // the footer" true rather than aspirational.
        markup.Should().Contain("Outer footer").And.Contain("Inner banner");
    }

    [Test]
    public void AnItemThatPlacesItselfRendersOnceAndStops()
    {
        // A cycle is refused when content is written, so this payload could only have arrived by an
        // import, a restore, or a hand edit. On a public request the only acceptable answer is to
        // stop, log, and render the rest of the page.
        _harness.Reusable.Add(
            3,
            Nestable,
            [(101, Nested("<p>Recursive footer</p>", reusableContentId: 3))],
            publishedVersionId: 101);

        var markup = _harness.Render("""{"type":"reusable","reusableContentId":3}""");

        markup.Should().Contain("Recursive footer");
        markup.Split("Recursive footer").Length.Should().Be(2, "the loop is cut at the second visit");
        _harness.Logs.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("closes a loop"));
    }

    [Test]
    public void NestingDeeperThanTheCeilingIsTruncatedRatherThanFollowed()
    {
        // A chain of items, each placing the next, longer than the delivery path will follow. Built
        // out of distinct items so it is the depth guard being tested and not the cycle guard.
        var depth = ReusableResolutionChain.MaxDepth + 2;

        for (var level = 1; level <= depth; level++)
        {
            var isLast = level == depth;

            _harness.Reusable.Add(
                level,
                isLast ? RawHtml : Nestable,
                [(100 + level, isLast
                    ? Fragment($"<p>Level {level}</p>")
                    : Nested($"<p>Level {level}</p>", reusableContentId: level + 1))],
                publishedVersionId: 100 + level);
        }

        var markup = _harness.Render("""{"type":"reusable","reusableContentId":1}""");

        // Exactly the ceiling renders. One more would mean the guard counted wrong; one fewer would
        // mean content an editor could legitimately author stopped short.
        markup.Should().Contain($"Level {ReusableResolutionChain.MaxDepth}");
        markup.Should().NotContain($"Level {ReusableResolutionChain.MaxDepth + 1}");
        _harness.Logs.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("nests deeper"));
    }

    [Test]
    public void EveryOutcomeStillDeclaresTheDependency()
    {
        _harness.Reusable.Add(3, RawHtml, [(101, Fragment("<p>Footer</p>"))], publishedVersionId: 101);

        var resolved = RenderTagged("""{"type":"reusable","reusableContentId":3}""");
        var missing = RenderTagged("""{"type":"reusable","reusableContentId":9}""");

        // The tag is what one publish of a shared banner updates forty pages through, and a page that
        // rendered nothing because the item was missing has to be evicted when the item arrives.
        resolved.CacheTags.Contains(CacheTags.Reusable(3)).Should().BeTrue();
        missing.CacheTags.Contains(CacheTags.Reusable(9)).Should().BeTrue();
    }

    [Test]
    public void TheTagNamesTheItemEvenWhenThePlacementPinsAVersion()
    {
        _harness.Reusable.Add(
            3,
            RawHtml,
            [(101, Fragment("<p>v1</p>")), (102, Fragment("<p>v2</p>"))],
            publishedVersionId: 102);

        var context = RenderTagged("""{"type":"reusable","reusableContentId":3,"pinnedVersionId":101}""");

        // A pinned placement does not follow the item's publishes, but it does still have to be
        // evicted when that version is deleted or the item is removed. One tag per item keeps the
        // eviction side from having to know which placements pinned what.
        context.CacheTags.Contains(CacheTags.Reusable(3)).Should().BeTrue();
    }

    private RenderContext RenderTagged(string zoneJson)
    {
        var context = RenderingHarness.Context(RenderingHarness.Payload(("hero", zoneJson)));

        _harness.RenderIn(context, "hero");

        return context;
    }

    /// <summary>A <c>rawHtml</c> item's content: one HTML property, stored as a payload's zones.</summary>
    private static ContentPayload Fragment(string html) =>
        RenderingHarness.PayloadFor(RawHtml, ("content", $$"""{"type":"html","value":{{Quote(html)}}}"""));

    /// <summary>
    /// A <c>nestable</c> item: the same HTML property, plus a placement of another item.
    /// </summary>
    /// <remarks>
    /// A different block type from <see cref="Fragment"/> on purpose. Nesting is a property of the
    /// block type rather than of the reusable machinery — a component renders the properties its
    /// markup names — and the built-in <c>rawHtml</c> shape has exactly one HTML property, so an
    /// item shaped by it can never contain another however its payload is authored.
    /// </remarks>
    private static ContentPayload Nested(string html, int reusableContentId) =>
        RenderingHarness.PayloadFor(
            Nestable,
            ("content", $$"""{"type":"html","value":{{Quote(html)}}}"""),
            ("nested", $$"""{"type":"reusable","reusableContentId":{{reusableContentId}}}"""));

    private static string Quote(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}
