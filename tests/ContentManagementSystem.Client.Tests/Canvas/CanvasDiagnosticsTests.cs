using ContentManagementSystem.Client.Components.Admin.Canvas;
using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Client.Tests.Canvas;

/// <summary>
/// Sorting a publish check onto the cards it concerns (task P6-05, spec section 14.6).
/// </summary>
public class CanvasDiagnosticsTests
{
    [Fact]
    public void ADiagnosticIsFiledUnderTheZoneItsPathNamesHoweverDeepItGoes()
    {
        var sorted = CanvasDiagnostics.From(
            [Error("zones.hero.items[0].properties.headline")],
            null);

        // A problem four levels inside a block still belongs to the card an editor can actually see.
        sorted.For("hero").Errors.Should().ContainSingle();
        sorted.For("body").Any.Should().BeFalse();
    }

    [Theory]
    [InlineData("zones.hero", "hero")]
    [InlineData("zones.hero.value", "hero")]
    [InlineData("zones.hero[0]", "hero")]
    [InlineData("slug", null)]
    [InlineData("zones", null)]
    [InlineData("zones.", null)]
    [InlineData(null, null)]
    public void AZoneKeyIsReadFromThePathOrNotAtAll(string? path, string? expected) =>
        CanvasDiagnostics.ZoneKeyOf(path).Should().Be(expected);

    [Fact]
    public void ADiagnosticThatNamesNoZoneIsReportedAboveTheCards()
    {
        var sorted = CanvasDiagnostics.From(
            [Error(property: null), Error("zones.hero")],
            null);

        var unplaced = sorted.Unplaced(["hero"]);

        // A URL collision or a disabled template belongs to no card and still blocks the publish.
        unplaced.Errors.Should().ContainSingle().Which.Property.Should().BeNull();
    }

    [Fact]
    public void ADiagnosticAboutAZoneWithNoCardIsNotSwallowed()
    {
        var sorted = CanvasDiagnostics.From(null, [Warning("zones.retired")]);

        var unplaced = sorted.Unplaced(["hero"]);

        // A payload can hold a zone its revision no longer declares. Bucketed under a card that is
        // never drawn, that warning would be more hidden than it was before the canvas existed.
        unplaced.Warnings.Should().ContainSingle()
            .Which.Property.Should().Be("zones.retired");
    }

    [Fact]
    public void SeverityIsTheWorstOfWhatWasSaid()
    {
        var sorted = CanvasDiagnostics.From([Error("zones.hero")], [Warning("zones.hero")]);

        sorted.For("hero").Severity.Should().Be(ZoneSeverity.Error);
        sorted.For("body").Severity.Should().Be(ZoneSeverity.None);
        CanvasDiagnostics.From(null, [Warning("zones.body")]).For("body").Severity
            .Should().Be(ZoneSeverity.Warning);
    }

    [Fact]
    public void TheTotalsCountEveryDiagnosticExactlyOnce()
    {
        var sorted = CanvasDiagnostics.From(
            [Error("zones.hero"), Error("zones.body"), Error(property: null)],
            [Warning("zones.hero")]);

        sorted.TotalErrors.Should().Be(3);
        sorted.TotalWarnings.Should().Be(1);
        sorted.Any.Should().BeTrue();
    }

    [Fact]
    public void NothingCheckedIsNothingReported()
    {
        CanvasDiagnostics.From(null, null).Should().BeSameAs(CanvasDiagnostics.Empty);
        CanvasDiagnostics.From([], []).Should().BeSameAs(CanvasDiagnostics.Empty);
        CanvasDiagnostics.Empty.Any.Should().BeFalse();
        CanvasDiagnostics.Empty.Unplaced([]).Any.Should().BeFalse();
    }

    private static ApiDiagnostic Error(string? property) =>
        new("content.required", "This zone has to be filled in before publishing.", property);

    private static ApiDiagnostic Warning(string? property) =>
        new("content.orphaned", "This zone is not declared by the template revision.", property);
}
