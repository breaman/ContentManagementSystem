using ContentManagementSystem.Client.Components.Admin.Canvas;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Tests.Canvas;

/// <summary>
/// The order and grouping of the canvas's zone cards (task P6-05, spec section 14.1).
/// </summary>
/// <remarks>
/// Stated here rather than only through a rendered canvas because the order cards appear in is the
/// one thing about an editing screen people build muscle memory for. A rule expressed only in a
/// Razor loop can only be checked by rendering one and reading the markup.
/// </remarks>
public class CanvasLayoutTests
{
    [Test]
    public void NoZonesIsNoGroupsRatherThanOneEmptyOne()
    {
        CanvasLayout.Build(null).Should().BeEmpty();
        CanvasLayout.Build([]).Should().BeEmpty();
    }

    [Test]
    public void UngroupedZonesAreOneRunWithNoHeading()
    {
        var groups = CanvasLayout.Build([Zone("hero", 0), Zone("body", 1)]);

        var group = groups.Should().ContainSingle().Subject;

        group.Name.Should().BeNull();
        group.Zones.Select(zone => zone.Key).Should().Equal("hero", "body");
    }

    [Test]
    public void ZonesSharingAGroupAreDrawnTogetherEvenWhenTheirSortOrdersAreNot()
    {
        var groups = CanvasLayout.Build(
            [
                Zone("metaTitle", 0, group: "SEO"),
                Zone("body", 1),
                Zone("metaDescription", 2, group: "SEO"),
            ]);

        // The alternative — cards in raw sort order with a heading wherever the group changes —
        // prints "SEO" twice, and an editor scrolling for it stops at the first one.
        groups.Select(candidate => candidate.Name).Should().Equal("SEO", null);
        groups[0].Zones.Select(zone => zone.Key).Should().Equal("metaTitle", "metaDescription");
    }

    [Test]
    public void AnUngroupedZoneStaysWhereItWasNumberedRatherThanJoiningTheOthers()
    {
        var groups = CanvasLayout.Build(
            [
                Zone("hero", 0),
                Zone("metaTitle", 1, group: "SEO"),
                Zone("footer", 2),
            ]);

        // Two headingless runs rather than one: merging them would drag the footer up above the SEO
        // group it was deliberately numbered after, which is a zone moving because of a group it is
        // not even in.
        groups.Select(candidate => candidate.Name).Should().Equal(null, "SEO", null);
        groups[0].Zones.Should().ContainSingle().Which.Key.Should().Be("hero");
        groups[2].Zones.Should().ContainSingle().Which.Key.Should().Be("footer");
    }

    [Test]
    public void ZonesKeepTheirOwnSortOrderInsideAGroup()
    {
        var groups = CanvasLayout.Build(
            [
                Zone("second", 5, group: "SEO"),
                Zone("first", 1, group: "SEO"),
            ]);

        groups.Should().ContainSingle().Which.Zones
            .Select(zone => zone.Key).Should().Equal("first", "second");
    }

    [Test]
    public void TwoZonesWithTheSameSortOrderAreOrderedByKeyRatherThanByChance()
    {
        var groups = CanvasLayout.Build([Zone("b", 0), Zone("a", 0)]);

        // Whatever order they arrive in, the canvas draws the same page twice running.
        groups.Should().ContainSingle().Which.Zones
            .Select(zone => zone.Key).Should().Equal("a", "b");
    }

    [Test]
    public void AGroupNameThatDiffersOnlyInWhitespaceIsOneGroup()
    {
        var groups = CanvasLayout.Build(
            [
                Zone("metaTitle", 0, group: "SEO"),
                Zone("metaDescription", 1, group: " SEO "),
            ]);

        // A template author's stray space is a typo, and drawing two "SEO" headings is the confusing
        // half of the mistake rather than a faithful report of it.
        groups.Should().ContainSingle().Which.Zones.Should().HaveCount(2);
    }

    [Test]
    public void AGroupOfWhitespaceIsNoGroupAtAll()
    {
        var groups = CanvasLayout.Build([Zone("hero", 0, group: "   ")]);

        groups.Should().ContainSingle().Which.Name.Should().BeNull();
    }

    private static CapturedSlot Zone(string key, int sortOrder, string? group = null) =>
        new(key, key, FieldTypeKeys.PlainText, IsRequired: false, sortOrder, Configuration: null,
            Description: null, group);
}
