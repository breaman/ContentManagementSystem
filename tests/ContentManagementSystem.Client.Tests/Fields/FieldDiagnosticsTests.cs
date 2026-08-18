using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Tests.Fields;

/// <summary>
/// Narrowing a validation result onto the value it actually names (tasks P6-05 and P6-06).
/// </summary>
/// <remarks>
/// This is what lets a twelve-block zone say which block is wrong instead of "3 problems", so the
/// boundary rule is the part worth pinning: matching has to stop at a member or index separator, or
/// two keys sharing a prefix quietly claim each other's diagnostics — an off-by-one that stays
/// invisible until somebody adds a zone called <c>heroine</c>.
/// </remarks>
public class FieldDiagnosticsTests
{
    [Test]
    public void APathCoversItselfAndEverythingBeneathIt()
    {
        ZoneDiagnostics.Covers("zones.hero", "zones.hero").Should().BeTrue();
        ZoneDiagnostics.Covers("zones.hero", "zones.hero.value").Should().BeTrue();
        ZoneDiagnostics.Covers("zones.hero", "zones.hero.items[0].properties.title").Should().BeTrue();
    }

    [Test]
    public void APathDoesNotCoverASiblingThatMerelyStartsTheSame()
    {
        ZoneDiagnostics.Covers("zones.hero", "zones.heroine.value").Should().BeFalse();
        ZoneDiagnostics.Covers("zones.body.items[1]", "zones.body.items[10].id").Should().BeFalse();
    }

    [Test]
    public void ADiagnosticAboutNothingInParticularIsCoveredByNothing()
    {
        ZoneDiagnostics.Covers("zones.hero", null).Should().BeFalse();
        ZoneDiagnostics.Covers("zones.hero", string.Empty).Should().BeFalse();
    }

    [Test]
    public void NarrowingKeepsTheSeverityOfWhatSurvivedRatherThanWhatItCameFrom()
    {
        var zone = new ZoneDiagnostics(
            [Diagnostic("zones.body.items[1].properties.headline")],
            [Diagnostic("zones.body.items[0].properties.headline")]);

        // A block list with one bad block is an error at the zone, but the block beside it is fine —
        // and marking both aria-invalid would send a screen reader user to check the wrong one.
        zone.Severity.Should().Be(ZoneSeverity.Error);
        zone.Within("zones.body.items[0]").Severity.Should().Be(ZoneSeverity.Warning);
        zone.Within("zones.body.items[1]").Severity.Should().Be(ZoneSeverity.Error);
        zone.Within("zones.body.items[2]").Severity.Should().Be(ZoneSeverity.None);
    }

    [Test]
    public void ANestedContextCarriesOnlyWhatNamesSomethingInsideIt()
    {
        var zone = new FieldEditorContext(
            Slot("body"),
            "zone-body-control",
            "zone-body-name",
            null,
            Disabled: false,
            ZoneSeverity.Error,
            new ZoneDiagnostics([Diagnostic("zones.body.items[1].properties.headline")], []),
            "zones.body");

        var clean = zone.Nested(
            Slot("headline"), "c", "l", "zones.body.items[0].properties.headline");

        var broken = zone.Nested(
            Slot("headline"), "c", "l", "zones.body.items[1].properties.headline");

        clean.Severity.Should().Be(ZoneSeverity.None);
        clean.AriaInvalid.Should().BeNull("a healthy control omits the attribute entirely");

        broken.Severity.Should().Be(ZoneSeverity.Error);
        broken.AriaInvalid.Should().Be("true");
        broken.Diagnostics!.Errors.Should().ContainSingle();
    }

    [Test]
    public void AContextThatNamesNoPathStillKnowsWhereItsZoneSits()
    {
        // Every construction site that predates nesting — the canvas's, and every test building a
        // context for a leaf editor — has to keep producing the path those slots actually have.
        var context = new FieldEditorContext(
            Slot("hero"), "c", "l", null, Disabled: false, ZoneSeverity.None);

        context.Path.Should().Be("zones.hero");
    }

    private static CapturedSlot Slot(string key) =>
        new(key, key, "plainText", IsRequired: false, SortOrder: 0, Configuration: null);

    private static ApiDiagnostic Diagnostic(string property) =>
        new("content.required", "This has to be filled in before publishing.", property);
}
