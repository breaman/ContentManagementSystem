using Bunit;

using ContentManagementSystem.Client.Components.Admin.Canvas;
using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Tests.Canvas;

/// <summary>
/// The editing canvas (task P6-05, spec sections 14.1 and 14.3).
/// </summary>
/// <remarks>
/// What these assert is the part of a card that is invisible when it looks right: that the zone's
/// name actually names the control inside it, that its help text is announced rather than only
/// drawn, that a validation badge is a word and a count rather than a colour, and that a card can be
/// linked to — which is what P6-20's "deep-link to the offending field" will be built on.
/// </remarks>
public class EditingCanvasTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CardsAreDrawnInSortOrderUnderTheirGroupHeadings()
    {
        var canvas = Render(
            [
                Zone("metaTitle", "Meta title", 1, group: "SEO"),
                Zone("hero", "Hero", 0),
                Zone("metaDescription", "Meta description", 2, group: "SEO"),
            ]);

        canvas.FindAll(".cms-canvas__zone-name").Select(node => node.TextContent.Trim())
            .Should().Equal("Hero", "Meta title", "Meta description");

        var group = canvas.Find(".cms-canvas__group-name");

        group.TextContent.Should().Be("SEO");
        canvas.FindAll(".cms-canvas__group[aria-labelledby]").Should().ContainSingle()
            .Which.GetAttribute("aria-labelledby").Should().Be(
                group.Id,
                "an unnamed run of cards is a section with no heading to name it");
    }

    [Fact]
    public void ACardIsAddressableSoAPublishDialogCanSendAnEditorToIt()
    {
        var canvas = Render([Zone("hero", "Hero", 0)]);

        var card = canvas.Find("#zone-hero");

        // Focusable by script but not by Tab: following the link should move focus to the card, and
        // the canvas should not grow one extra tab stop per zone to make that possible.
        card.GetAttribute("tabindex").Should().Be("-1");
        ZoneCard.AnchorFor("hero").Should().Be("zone-hero");
    }

    [Fact]
    public void TheCardsHeadingNamesTheControlInsideIt()
    {
        var canvas = Render([Zone("hero", "Hero", 0)]);

        var heading = canvas.Find(".cms-canvas__zone-name");

        canvas.Find("[data-editor]").GetAttribute("aria-labelledby").Should().Be(
            heading.Id,
            "a label pointing at one control would be a lie for the field types that are several");
    }

    [Fact]
    public void HelpTextIsAnnouncedAndNotOnlyDrawn()
    {
        var canvas = Render([Zone("hero", "Hero", 0, description: "Shown above the fold.")]);

        var help = canvas.Find(".cms-canvas__zone-help");

        help.TextContent.Should().Be("Shown above the fold.");
        canvas.Find("[data-editor]").GetAttribute("aria-describedby").Should().Be(help.Id);
    }

    [Fact]
    public void AZoneWithNoHelpTextDescribesTheControlByNothingAtAll()
    {
        var canvas = Render([Zone("hero", "Hero", 0)]);

        canvas.FindAll(".cms-canvas__zone-help").Should().BeEmpty();
        canvas.Find("[data-editor]").HasAttribute("aria-describedby").Should().BeFalse(
            "an aria-describedby pointing at an element that is not there describes nothing");
    }

    [Fact]
    public void ARequiredZoneSaysSoInWordsAndNotOnlyWithAnAsterisk()
    {
        var canvas = Render([Zone("hero", "Hero", 0, required: true)]);

        canvas.Find(".cms-canvas__required").GetAttribute("aria-hidden").Should().Be("true");
        canvas.Find(".cms-canvas__zone-name .visually-hidden").TextContent
            .Should().Be("(required to publish)");
    }

    [Fact]
    public void AZoneWithProblemsCountsThemOnItsBadgeAndMarksItsControlInvalid()
    {
        var canvas = Render(
            [Zone("hero", "Hero", 0)],
            CanvasDiagnostics.From(
                [Diagnostic("zones.hero"), Diagnostic("zones.hero.items[0].properties.headline")],
                null));

        // A word and a count, never a colour alone (P6-39, spec section 28).
        canvas.Find(".cms-canvas__zone-badge").TextContent.Should().Be("2 problems");
        canvas.Find(".cms-canvas__zone").ClassList.Should().Contain("cms-canvas__zone--error");
        canvas.Find("[data-editor]").GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void AHealthyZoneCarriesNoBadgeAndIsNotMarkedInvalid()
    {
        var canvas = Render([Zone("hero", "Hero", 0)]);

        canvas.FindAll(".cms-canvas__zone-badge").Should().BeEmpty();
        canvas.Find("[data-editor]").HasAttribute("aria-invalid").Should().BeFalse();
    }

    [Fact]
    public void ADiagnosticFoundInsideABlockSaysWhereWithoutRepeatingTheZone()
    {
        var canvas = Render(
            [Zone("hero", "Hero", 0)],
            CanvasDiagnostics.From([Diagnostic("zones.hero.items[0].properties.headline")], null));

        // The card already says which zone this is; repeating it pushes the part that differs off
        // the end of a narrow canvas.
        canvas.Find(".cms-canvas__diagnostic-path").TextContent
            .Should().Be("items[0].properties.headline");
    }

    [Fact]
    public void ADiagnosticThatBelongsToNoCardIsReportedAboveThem()
    {
        var canvas = Render(
            [Zone("hero", "Hero", 0)],
            CanvasDiagnostics.From([Diagnostic(property: null), Diagnostic("zones.retired")], null));

        var reported = canvas.Find(".alert-danger").TextContent;

        reported.Should().Contain("zones.retired");
        canvas.FindAll(".cms-canvas__zone").Should().ContainSingle(
            "a zone the revision no longer declares has no card, which is exactly why its problem " +
            "has to be shown somewhere else");
    }

    [Fact]
    public void TheActionBarCountsTheProblemsAndHoldsTheHostsButtons()
    {
        var canvas = _bunit.Render<EditingCanvas>(parameters => parameters
            .Add(p => p.Zones, [Zone("hero", "Hero", 0)])
            .Add(p => p.Diagnostics, CanvasDiagnostics.From([Diagnostic("zones.hero")], null))
            .Add(p => p.Editor, Body)
            .Add(p => p.Status, "Saved 14:32")
            .Add(p => p.Actions, "<button type=\"button\">Publish</button>"));

        var bar = canvas.Find(".cms-canvas__actions");

        bar.QuerySelector(".cms-canvas__summary")!.TextContent
            .Should().Be("1 problem blocks publishing");
        bar.QuerySelector(".cms-canvas__status")!.TextContent.Should().Be("Saved 14:32");
        bar.QuerySelector("button")!.TextContent.Should().Be("Publish");
    }

    [Fact]
    public void AnUncheckedPageWithNothingToSayHasNoActionBar()
    {
        var canvas = Render([Zone("hero", "Hero", 0)]);

        canvas.FindAll(".cms-canvas__actions").Should().BeEmpty();
    }

    [Fact]
    public void ATemplateRevisionThatCapturedNoZonesSaysSoRatherThanRenderingNothing()
    {
        var canvas = _bunit.Render<EditingCanvas>(parameters => parameters
            .Add(p => p.Zones, [])
            .Add(p => p.Editor, Body));

        canvas.Find(".cms-canvas__empty").TextContent.Should().Contain("no zones");
        canvas.FindAll(".cms-canvas__zone").Should().BeEmpty();
    }

    [Fact]
    public void ADisabledCanvasPassesThatOnToEveryCardBody()
    {
        var canvas = _bunit.Render<EditingCanvas>(parameters => parameters
            .Add(p => p.Zones, [Zone("hero", "Hero", 0), Zone("body", "Body", 1)])
            .Add(p => p.Editor, Body)
            .Add(p => p.Disabled, true));

        canvas.FindAll("[data-editor][data-disabled=True]").Should().HaveCount(2);
    }

    /// <summary>Renders the canvas with a stand-in body that reports what the card handed it.</summary>
    private IRenderedComponent<EditingCanvas> Render(
        IReadOnlyList<CapturedSlot> zones,
        CanvasDiagnostics? diagnostics = null) =>
        _bunit.Render<EditingCanvas>(parameters => parameters
            .Add(p => p.Zones, zones)
            .Add(p => p.Diagnostics, diagnostics ?? CanvasDiagnostics.Empty)
            .Add(p => p.Editor, Body));

    /// <summary>
    /// A card body that is nothing but the wiring, which is what these tests are about.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>PlainZoneEditor</c>: what the canvas promises a body is the contract in
    /// <see cref="FieldEditorContext"/>, and testing it through today's textarea would let a change
    /// to that textarea decide whether the contract still holds.
    /// </remarks>
    private static string Body(FieldEditorContext context) =>
        $"""
         <input data-editor id="{context.ControlId}" data-disabled="{context.Disabled}"
                aria-labelledby="{context.LabelledBy}"
                {Attribute("aria-describedby", context.DescribedBy)}
                {Attribute("aria-invalid", context.AriaInvalid)} />
         """;

    private static string Attribute(string name, string? value) =>
        value is null ? string.Empty : $"{name}=\"{value}\"";

    private static ApiDiagnostic Diagnostic(string? property) =>
        new("content.required", "This has to be filled in before publishing.", property);

    private static CapturedSlot Zone(
        string key,
        string name,
        int sortOrder,
        string? group = null,
        string? description = null,
        bool required = false) =>
        new(key, name, FieldTypeKeys.PlainText, required, sortOrder, Configuration: null,
            description, group);
}
