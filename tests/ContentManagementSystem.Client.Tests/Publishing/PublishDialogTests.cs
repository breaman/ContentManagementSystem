using Bunit;

using ContentManagementSystem.Client.Components.Admin.Publishing;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Tests.Publishing;

/// <summary>
/// The publish dialog (task P6-20, spec sections 14.6 and 22.2).
/// </summary>
/// <remarks>
/// A flat list of diagnostics is what the API returns and the last thing an editor can act on.
/// These assert the translation: grouped by zone, named as the canvas names them, each group a link
/// into the card — and warnings acknowledged once, deliberately, by somebody who has been shown
/// what they are publishing past.
/// </remarks>
public class PublishDialogTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    public PublishDialogTests()
    {
        _bunit.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ProblemsAreGroupedByZoneAndNamedTheWayTheCanvasNamesThem()
    {
        var dialog = Render(
            errors:
            [
                new ApiDiagnostic("zone.required", "This zone is required.", "zones.body"),
                new ApiDiagnostic("media.alt-text-required", "No alt text.", "zones.hero.items[0].properties.image"),
            ]);

        var headings = dialog.FindAll(".cms-publish__group h3")
            .Select(heading => heading.TextContent.Trim())
            .ToList();

        // The canvas's own order, not the order the diagnostics happened to arrive in: the dialog
        // reads down the page the way the page reads.
        headings.Should().Equal("Hero banner", "Body");
    }

    [Fact]
    public void EachGroupIsALinkIntoTheZoneItIsAbout()
    {
        string? went = null;

        var dialog = Render(
            errors: [new ApiDiagnostic("zone.required", "This zone is required.", "zones.body")],
            onGoToZone: key => went = key);

        dialog.Find(".cms-publish__link").Click();

        went.Should().Be("body");
    }

    [Fact]
    public void AProblemThatNamesNoZoneIsGroupedUnderThePageRatherThanDropped()
    {
        var dialog = Render(
            errors: [new ApiDiagnostic("page.url-taken", "That URL is already in use.", "Slug")]);

        dialog.Find(".cms-publish__group h3").TextContent.Trim().Should().Be("This page");
        dialog.FindAll(".cms-publish__link").Should().BeEmpty(
            "there is no card to send the editor to");
        dialog.Find(".cms-publish__entry").TextContent.Should().Contain("already in use");
    }

    [Fact]
    public void ErrorsBlockPublishingOutright()
    {
        var dialog = Render(
            errors: [new ApiDiagnostic("zone.required", "This zone is required.", "zones.body")]);

        Confirm(dialog).HasAttribute("disabled").Should().BeTrue();
        dialog.Find(".cms-publish__blocked").TextContent.Should().Contain("draft is saved either way");
        dialog.FindAll(".cms-publish__acknowledge").Should().BeEmpty(
            "there is nothing to acknowledge while something is still blocking");
    }

    [Fact]
    public void WarningsAreAcknowledgedOnceAndOnPurpose()
    {
        bool? acknowledged = null;

        var dialog = Render(
            warnings: [new ApiDiagnostic("seo.description-missing", "No meta description.")],
            onPublish: value => acknowledged = value);

        var confirm = Confirm(dialog);

        confirm.TextContent.Should().Contain("Publish anyway");
        confirm.HasAttribute("disabled").Should().BeTrue("nobody has said they read the warning yet");

        dialog.Find("#cms-publish-acknowledge").Change(true);

        Confirm(dialog).HasAttribute("disabled").Should().BeFalse();

        Confirm(dialog).Click();

        acknowledged.Should().BeTrue(
            "the API refuses the first attempt with the warnings in it and accepts the second one " +
            "with this set (spec section 22.2)");
    }

    [Fact]
    public void ReopeningTheDialogStartsUnacknowledged()
    {
        var dialog = Render(
            warnings: [new ApiDiagnostic("seo.description-missing", "No meta description.")]);

        dialog.Find("#cms-publish-acknowledge").Change(true);

        dialog.Render(parameters => parameters.Add(publish => publish.IsOpen, false));
        dialog.Render(parameters => parameters.Add(publish => publish.IsOpen, true));

        dialog.Find("#cms-publish-acknowledge").HasAttribute("checked").Should().BeFalse(
            "the warnings may not be the ones that were acknowledged last time, and consent to a " +
            "list nobody is looking at is not consent");
        Confirm(dialog).HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void AClearCheckSaysSoAndPublishesInOneClick()
    {
        bool? acknowledged = null;

        var dialog = Render(onPublish: value => acknowledged = value);

        dialog.Find(".cms-publish__clear").TextContent.Should().Contain("Nothing is blocking this");

        var confirm = Confirm(dialog);

        confirm.TextContent.Should().Contain("Publish");
        confirm.TextContent.Should().NotContain("anyway");

        confirm.Click();

        acknowledged.Should().BeFalse();
    }

    [Fact]
    public void TheCheckStillRunningIsSaidRatherThanShownAsAllClear()
    {
        var dialog = _bunit.Render<PublishDialog>(parameters => parameters
            .Add(publish => publish.IsOpen, true)
            .Add(publish => publish.IsChecking, true));

        dialog.Find("[role=status]").TextContent.Should().Contain("Running the publish checks");
        Confirm(dialog).HasAttribute("disabled").Should().BeTrue(
            "publishing before the check has answered would be publishing on no information");
    }

    private IRenderedComponent<PublishDialog> Render(
        IReadOnlyList<ApiDiagnostic>? errors = null,
        IReadOnlyList<ApiDiagnostic>? warnings = null,
        Action<bool>? onPublish = null,
        Action<string>? onGoToZone = null) =>
        _bunit.Render<PublishDialog>(parameters => parameters
            .Add(publish => publish.IsOpen, true)
            .Add(publish => publish.Errors, errors)
            .Add(publish => publish.Warnings, warnings)
            .Add(publish => publish.Zones, Zones)
            .Add(publish => publish.OnPublish, value => onPublish?.Invoke(value))
            .Add(publish => publish.OnGoToZone, key => onGoToZone?.Invoke(key)));

    /// <summary>The dialog's own confirm button, which belongs to the modal it is drawn in.</summary>
    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<PublishDialog> dialog) =>
        dialog.Find(".modal-footer .btn-success");

    /// <summary>Two zones, in the order the canvas draws them.</summary>
    private static IReadOnlyList<CapturedSlot> Zones { get; } =
    [
        new("hero", "Hero banner", FieldTypeKeys.Blocks, false, 0, null),
        new("body", "Body", FieldTypeKeys.RichText, true, 1, null),
    ];
}
