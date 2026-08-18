using System.Text.Json;
using System.Text.Json.Nodes;

using Bunit;

using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Client.Components.Admin.Fields.BlockList;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Client.Tests.Fields;

/// <summary>
/// The block list editor (tasks P6-06, P6-07, and P6-30; acceptance criterion P6 #4).
/// </summary>
/// <remarks>
/// The whole of criterion P6 #4 is "entirely by keyboard", so every one of these drives a button.
/// Dragging is exercised nowhere here on purpose: it goes through the same write as the arrow
/// buttons, and a test that only covered the pointer path would pass on a build where the buttons
/// had been removed.
/// </remarks>
public class BlockListEditorTests : IDisposable
{
    private readonly FieldEditorHarness _harness = new();

    public BlockListEditorTests()
    {
        _harness.Bunit.Services.AddSingleton<IStructureClient>(new StubStructure());
        _harness.Bunit.Services.AddSingleton<IReusableClient>(new StubBlockTypes());
        _harness.Bunit.Services.AddSingleton<IFieldEditorCatalog>(new FieldEditorCatalog());

        // The block picker is a ModalDialog, which imports its focus-trap module on first render.
        _harness.Bunit.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void ABlockIsAddedByChoosingItsTypeFromThePicker()
    {
        var editor = Render();

        editor.Find(".cms-blocks__actions button").Click();

        // Chosen by name rather than by position: with no allowlist the picker offers every
        // registered type in alphabetical order, which is not the order they were declared in.
        editor.FindAll(".cms-picker--blocks .cms-picker__option")
            .Single(option => option.TextContent.Contains("Hero banner", StringComparison.Ordinal))
            .Click();

        editor.Find(".modal-footer .btn-primary").Click();

        var items = Items(_harness.Last);

        items.Should().ContainSingle();
        items[0]["blockTypeKey"]!.GetValue<string>().Should().Be("hero-banner");

        // The revision is captured at the moment of adding, so a block added today is still laid
        // out by the properties that existed today (spec section 8.5).
        items[0]["blockTypeRevision"]!.GetValue<int>().Should().Be(3);
        Guid.TryParse(items[0]["id"]!.GetValue<string>(), out _).Should().BeTrue();
    }

    [Test]
    public void OnlyTheBlockTypesThePropertyAllowsAreOffered()
    {
        var editor = Render(configuration:
            $$"""{ "{{FieldSettingNames.AllowedBlockTypes}}": ["feature-grid"] }""");

        editor.Find(".cms-blocks__actions button").Click();

        editor.FindAll(".cms-picker--blocks .cms-picker__option")
            .Select(option => option.TextContent.Trim())
            .Should().ContainSingle().Which.Should().Contain("Feature grid");
    }

    [Test]
    public void BlocksAreReorderedByButtonAndKeepTheirIdentity()
    {
        var editor = Render(TwoBlocks);
        var first = Items(TwoBlocks)[0]["id"]!.GetValue<string>();

        // The second block's "move up".
        editor.FindAll(".cms-block")[1].QuerySelector("button[aria-label*='up']")!.Click();

        var items = Items(_harness.Last);

        items[1]["id"]!.GetValue<string>().Should().Be(
            first,
            "a block that moved has to keep its id, or the version diff reports it as removed and added");
    }

    [Test]
    public void TheFirstBlockCannotBeMovedUpAndTheLastCannotBeMovedDown()
    {
        var editor = Render(TwoBlocks);

        var cards = editor.FindAll(".cms-block");

        cards[0].QuerySelector("button[aria-label*='up']")!.HasAttribute("disabled").Should().BeTrue();
        cards[1].QuerySelector("button[aria-label*='down']")!.HasAttribute("disabled").Should().BeTrue();
    }

    [Test]
    public void DuplicatingCopiesTheContentAndGivesTheCopyItsOwnIdentity()
    {
        var editor = Render(TwoBlocks);

        editor.FindAll(".cms-block")[0].QuerySelector("button[aria-label*='Duplicate']")!.Click();

        var items = Items(_harness.Last);

        items.Should().HaveCount(3);
        items[1]["blockTypeKey"]!.GetValue<string>().Should().Be(items[0]["blockTypeKey"]!.GetValue<string>());

        // Two blocks sharing an id make the diff ambiguous and make the editor address the wrong
        // one, which the validator refuses outright.
        items[1]["id"]!.GetValue<string>().Should().NotBe(items[0]["id"]!.GetValue<string>());
    }

    [Test]
    public void DeletingIsOfferedBackBeforeItIsFinal()
    {
        var editor = Render(TwoBlocks);

        editor.FindAll(".cms-block")[0].QuerySelector("button[aria-label*='Remove']")!.Click();

        Items(_harness.Last).Should().ContainSingle();

        var undo = editor.Find(".cms-blocks__undo");

        undo.TextContent.Should().Contain("Removed");

        undo.QuerySelector("button")!.Click();

        var restored = Items(_harness.Last);

        restored.Should().HaveCount(2);
        restored[0]["id"]!.GetValue<string>().Should().Be(
            Items(TwoBlocks)[0]["id"]!.GetValue<string>(),
            "undo has to restore the order as well as the block");
    }

    [Test]
    public void ACollapsedBlockIsSummarisedByItsOwnContentRatherThanByItsType()
    {
        var editor = Render(TwoBlocks);

        // A twelve-block page can only be read as a list if the summary carries the block's
        // content; "Hero banner" twelve times is a list nobody dares collapse.
        editor.FindAll(".cms-block__summary")[0].TextContent.Trim()
            .Should().Be("What our plans cost");
    }

    [Test]
    public void ABlockWithNothingInTheSummaryFallsBackToItsTypeName()
    {
        var editor = Render(EmptyBlock);

        editor.Find(".cms-block__summary").TextContent.Trim().Should().Be("Hero banner");
    }

    [Test]
    public void CollapsingHidesTheBodyAndSaysSo()
    {
        var editor = Render(TwoBlocks);

        var disclosure = editor.FindAll(".cms-block__disclosure")[0];

        disclosure.GetAttribute("aria-expanded").Should().Be("true");

        disclosure.Click();

        editor.FindAll(".cms-block__disclosure")[0].GetAttribute("aria-expanded").Should().Be("false");
        editor.FindAll(".cms-block__body")[0].HasAttribute("hidden").Should().BeTrue();
    }

    [Test]
    public void ABlockPropertyIsDrawnByTheSameEditorAZoneWouldGet()
    {
        var editor = Render(TwoBlocks);

        // The catalog dispatches both, so a plainText property inside a hero banner is the same
        // single-line control a plainText zone gets (ADR-0014).
        var input = editor.FindAll(".cms-block__property input[type=text]")[0];

        input.GetAttribute("value").Should().Be("What our plans cost");
    }

    [Test]
    public void EditingAPropertyRewritesOnlyThatBlocksProperty()
    {
        var editor = Render(TwoBlocks);

        editor.FindAll(".cms-block__property input[type=text]")[0].Input("Changed");

        var items = Items(_harness.Last);

        items[0]["properties"]!["headline"]!["value"]!.GetValue<string>().Should().Be("Changed");
        items[1]["properties"]!["headline"]!["value"]!.GetValue<string>().Should().Be("Every feature");
    }

    [Test]
    public void AValidationProblemIsBadgedOnTheBlockItNames()
    {
        var diagnostics = new ZoneDiagnostics(
            [new ApiDiagnostic(
                "content.required",
                "This has to be filled in before publishing.",
                "zones.zone.items[1].properties.headline")],
            []);

        var editor = Render(TwoBlocks, diagnostics: diagnostics);

        var badges = editor.FindAll(".cms-block").Select(card => card.QuerySelector(".cms-block__badge"));

        badges.First().Should().BeNull("nothing was said about the first block");

        // A word and a count, never a colour alone (P6-39).
        badges.Last()!.TextContent.Trim().Should().Be("1 problem");
    }

    [Test]
    public void ABlockTypeThisBuildNoLongerHasCanStillBeMovedAndRemoved()
    {
        var editor = Render(UnknownBlock);

        editor.Find(".cms-block__body").TextContent.Should().Contain("no longer has");
        editor.Find("button[aria-label*='Remove']").HasAttribute("disabled").Should().BeFalse();
    }

    [Test]
    public void AFullListWillNotTakeAnother()
    {
        var editor = Render(TwoBlocks, configuration: $$"""{ "{{FieldSettingNames.Max}}": 2 }""");

        editor.Find(".cms-blocks__actions button").HasAttribute("disabled").Should().BeTrue();
        editor.FindAll("button[aria-label*='Duplicate']").Should().AllSatisfy(
            button => button.HasAttribute("disabled").Should().BeTrue());
    }

    [Test]
    public void AReadOnlyFormOffersNoneOfTheControls()
    {
        var editor = Render(TwoBlocks, disabled: true);

        editor.FindAll(".cms-block__controls button").Should().AllSatisfy(
            button => button.HasAttribute("disabled").Should().BeTrue());
    }

    private IRenderedComponent<BlockListEditor> Render(
        string value = "",
        string? configuration = null,
        bool disabled = false,
        ZoneDiagnostics? diagnostics = null)
    {
        var slot = FieldEditorHarness.Slot(FieldTypeKeys.Blocks, configuration);

        return _harness.Render<BlockListEditor>(
            FieldEditorHarness.Context(slot, disabled, diagnostics),
            value);
    }

    private static List<JsonObject> Items(string? json) =>
        [.. (JsonNode.Parse(json ?? "{}")?["items"] as JsonArray ?? []).OfType<JsonObject>()];

    private const string TwoBlocks =
        """
        {
          "type": "blocks",
          "items": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "blockTypeKey": "hero-banner",
              "blockTypeRevision": 3,
              "properties": { "headline": { "type": "plainText", "value": "What our plans cost" } }
            },
            {
              "id": "22222222-2222-2222-2222-222222222222",
              "blockTypeKey": "hero-banner",
              "blockTypeRevision": 3,
              "properties": { "headline": { "type": "plainText", "value": "Every feature" } }
            }
          ]
        }
        """;

    private const string EmptyBlock =
        """
        {
          "type": "blocks",
          "items": [
            {
              "id": "33333333-3333-3333-3333-333333333333",
              "blockTypeKey": "hero-banner",
              "blockTypeRevision": 3,
              "properties": {}
            }
          ]
        }
        """;

    private const string UnknownBlock =
        """
        {
          "type": "blocks",
          "items": [
            {
              "id": "44444444-4444-4444-4444-444444444444",
              "blockTypeKey": "something-removed",
              "blockTypeRevision": 1,
              "properties": {}
            }
          ]
        }
        """;

    /// <summary>The block types this deployment has.</summary>
    private sealed class StubStructure : StubStructureClient
    {
        public override Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BlockTypeSummary>>(
            [
                new BlockTypeSummary(
                    1, "hero-banner", "Hero banner", "The banner above the fold.", "HeroBanner",
                    "megaphone", "{headline}", IsOrphaned: false, IsBuiltIn: false,
                    CurrentRevision: 3, PropertyCount: 1),
                new BlockTypeSummary(
                    2, "feature-grid", "Feature grid", null, "FeatureGrid", "grid", null,
                    IsOrphaned: false, IsBuiltIn: false, CurrentRevision: 1, PropertyCount: 0),
                new BlockTypeSummary(
                    3, "retired", "Retired", null, null, null, null,
                    IsOrphaned: true, IsBuiltIn: false, CurrentRevision: 1, PropertyCount: 0),
            ]);
    }

    /// <summary>The captured property snapshots the blocks were authored against.</summary>
    private sealed class StubBlockTypes : StubReusableClient
    {
        public override Task<IReadOnlyList<CapturedSlot>> GetPropertiesAsync(
            int blockTypeId,
            int revision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CapturedSlot>>(blockTypeId == 1
                ?
                [
                    new CapturedSlot(
                        "headline", "Headline", FieldTypeKeys.PlainText,
                        IsRequired: true, SortOrder: 0, Configuration: null),
                ]
                : []);
    }
}
