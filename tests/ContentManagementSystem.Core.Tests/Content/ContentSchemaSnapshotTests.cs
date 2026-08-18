using System.Text.Json;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core.Tests.Content;

/// <summary>
/// The captured schema a page version is validated against (task P1-15, spec section 8.5).
/// </summary>
public class ContentSchemaSnapshotTests
{
    [Test]
    public void AnEmptySnapshotIsNoSlotsRatherThanAnError()
    {
        ContentSchemaSnapshot.Read(null).Should().BeEmpty();
        ContentSchemaSnapshot.Read("  ").Should().BeEmpty();
        ContentSchemaSnapshot.Read("[]").Should().BeEmpty();
    }

    [Test]
    public void SlotsKeepTheOrderTheSnapshotListsThemIn()
    {
        var slots = ContentSchemaSnapshot.Read(
            """
            [ { "key": "b", "fieldTypeKey": "plainText", "sortOrder": 1 },
              { "key": "a", "fieldTypeKey": "plainText", "sortOrder": 0 } ]
            """);

        // The order in the file is the editor's order, already resolved when the revision was cut.
        // Re-sorting here would let the snapshot and the editor disagree about what "first" means.
        slots.Select(slot => slot.Key).Should().Equal("b", "a");
    }

    [Test]
    public void ASlotWithNoLabelFallsBackToItsKey()
    {
        var slot = ContentSchemaSnapshot.Read("""[ { "key": "hero", "fieldTypeKey": "blocks" } ]""")
            .Should().ContainSingle().Subject;

        // The label appears in the messages an editor reads, so an empty one is worse than the key.
        slot.Name.Should().Be("hero");
    }

    [Test]
    public void ASnapshotThatIsNotAnArrayIsRefused()
    {
        var read = () => ContentSchemaSnapshot.Read("""{ "key": "hero" }""");

        read.Should().Throw<JsonException>();
    }

    [Test]
    public void ASlotWithNoKeyOrNoFieldTypeIsRefused()
    {
        var noKey = () => ContentSchemaSnapshot.Read("""[ { "fieldTypeKey": "plainText" } ]""");
        var noFieldType = () => ContentSchemaSnapshot.Read("""[ { "key": "hero" } ]""");

        // Skipping it instead would turn one corrupt structure row into an orphaned-content warning
        // on every page using the template, which is far harder to trace back.
        noKey.Should().Throw<JsonException>();
        noFieldType.Should().Throw<JsonException>();
    }

    [Test]
    public void AMalformedConfigurationIsCaughtWhileCuttingTheRevisionNotWhileValidatingContent()
    {
        var write = () => ContentSchemaSnapshot.WriteZones(
            [
                new Zone
                {
                    Key = "hero",
                    Name = "Hero",
                    FieldTypeKey = FieldTypeKeys.Blocks,
                    ConfigurationJson = "{ not json",
                },
            ]);

        write.Should().Throw<JsonException>();
    }

    [Test]
    public void ZonesAreCapturedInEditorOrder()
    {
        var written = ContentSchemaSnapshot.WriteZones(
            [
                new Zone { Key = "b", Name = "B", FieldTypeKey = FieldTypeKeys.PlainText, SortOrder = 2 },
                new Zone { Key = "a", Name = "A", FieldTypeKey = FieldTypeKeys.PlainText, SortOrder = 1 },
            ]);

        ContentSchemaSnapshot.Read(written).Select(slot => slot.Key).Should().Equal("a", "b");
    }

    [Test]
    public void AZonesHelpTextAndGroupingAreCapturedWithIt()
    {
        var written = ContentSchemaSnapshot.WriteZones(
            [
                new Zone
                {
                    Key = "metaTitle",
                    Name = "Meta title",
                    Description = "Overrides the browser tab and the search result.",
                    FieldTypeKey = FieldTypeKeys.PlainText,
                    Group = "SEO",
                },
            ]);

        // Captured rather than read live off the zone, for the reason every other part of a revision
        // is: the editing canvas of task P6-05 lays a draft out from the revision it was authored
        // against, and a template regrouped since would otherwise rearrange the page under whoever
        // is editing it.
        var slot = CapturedSlot.Read(JsonDocument.Parse(written).RootElement)
            .Should().ContainSingle().Subject;

        slot.Description.Should().Be("Overrides the browser tab and the search result.");
        slot.Group.Should().Be("SEO");
    }

    [Test]
    public void ASnapshotCutBeforeGroupingExistedReadsAsUngrouped()
    {
        var slot = CapturedSlot
            .Read(JsonDocument.Parse("""[ { "key": "hero", "fieldTypeKey": "blocks" } ]""").RootElement)
            .Should().ContainSingle().Subject;

        // Which is what it meant. Every zone of an old revision lands in one ungrouped canvas — the
        // layout those pages have always had.
        slot.Group.Should().BeNull();
        slot.Description.Should().BeNull();
    }

    [Test]
    public void BlockTypePropertiesUseTheSameFormatAsZones()
    {
        var written = ContentSchemaSnapshot.WriteProperties(
            [
                new BlockTypeProperty
                {
                    Key = "headline",
                    Name = "Headline",
                    FieldTypeKey = FieldTypeKeys.PlainText,
                    IsRequired = true,
                },
            ]);

        var schema = ContentSchemaSnapshot.ReadBlockType("hero-banner", 3, written);

        schema.BlockTypeKey.Should().Be("hero-banner");
        schema.RevisionNumber.Should().Be(3);
        schema.FindProperty("headline")!.IsRequired.Should().BeTrue();
    }

    [Test]
    public void ATemplateDeclaringOneZoneKeyTwiceIsRefused()
    {
        var build = () => ContentSchemaSnapshot.ReadTemplate(
            "marketing-landing",
            7,
            """
            [ { "key": "hero", "fieldTypeKey": "blocks" },
              { "key": "hero", "fieldTypeKey": "plainText" } ]
            """);

        // Which of the two a payload is checked against would otherwise depend on iteration order.
        build.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ZoneKeysAreMatchedExactly()
    {
        var schema = ContentSchemaSnapshot.ReadTemplate(
            "marketing-landing",
            7,
            """[ { "key": "hero", "fieldTypeKey": "blocks" } ]""");

        // A zone key is an identifier written into stored payloads, so a culture must never get a
        // say in whether 'Hero' and 'hero' are the same zone.
        schema.DeclaresZone("hero").Should().BeTrue();
        schema.DeclaresZone("Hero").Should().BeFalse();
    }

    [Test]
    public void ABlockCarryingNoRevisionResolvesToTheNewestKnownOne()
    {
        var catalog = new ContentSchemaCatalog(
            [],
            [
                ContentEngineHarness.BlockType("quote", 1),
                ContentEngineHarness.BlockType("quote", 4),
                ContentEngineHarness.BlockType("quote", 2),
            ]);

        catalog.TryGetBlockType("quote", null, out var newest).Should().BeTrue();
        newest!.RevisionNumber.Should().Be(4);

        catalog.TryGetBlockType("quote", 2, out var pinned).Should().BeTrue();
        pinned!.RevisionNumber.Should().Be(2);

        catalog.TryGetBlockType("quote", 99, out _).Should().BeFalse();
        catalog.TryGetBlockType("missing", null, out _).Should().BeFalse();
    }

    [Test]
    public void TheSeededBuiltInBlockTypeCarriesAReadableSnapshot()
    {
        var revision = CmsSeedData.RawHtmlBlockTypeRevision;

        var schema = ContentSchemaSnapshot.ReadBlockType(
            CmsSeedData.RawHtmlBlockTypeKey,
            revision.RevisionNumber,
            revision.PropertySnapshotJson);

        // The seed row was written before this format existed and wrapped the array in an object,
        // which reads as malformed — the one block type every deployment ships with would have been
        // the one whose captured schema could not be loaded. Nothing read the column until now, so
        // nothing caught it; this is that check.
        var slot = schema.Properties.Should().ContainSingle().Subject;
        slot.Key.Should().Be(CmsSeedData.RawHtmlContentPropertyKey);
        slot.FieldTypeKey.Should().Be(FieldTypeKeys.Html);
        slot.IsRequired.Should().BeTrue();
    }
}
