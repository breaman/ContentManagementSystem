using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Content;

/// <summary>
/// Projection of a payload into reference rows (task P1-16, spec sections 6.2 and 7.3).
/// </summary>
public class ReferenceIndexerTests
{
    private const string BlockId = "0f6c8b1e-3a4d-4f2b-9c7e-1d2a3b4c5d6e";

    private const string NestedId = "7a1b2c3d-4e5f-4061-8293-a4b5c6d7e8f9";

    private static IReadOnlyList<ContentReference> Extract(string payloadJson) =>
        ContentEngineHarness.Indexer().Extract(ContentEngineHarness.Payload(payloadJson));

    [Test]
    public void AZoneLevelReferenceIsReportedWithItsZonePath()
    {
        var references = Extract(
            """
            { "schemaVersion": 1, "templateKey": "t", "templateRevision": 1,
              "zones": { "image": { "type": "media", "mediaId": 812 } } }
            """);

        references.Should().Equal(
            new ContentReference(ContentReferenceTargetType.Media, 812, "zones.image"));
    }

    [Test]
    public void EveryReferenceInsideABlockZoneIsReported()
    {
        var references = Extract(
            $$"""
            { "schemaVersion": 1, "templateKey": "t", "templateRevision": 1,
              "zones": { "hero": { "type": "blocks", "items": [
                { "id": "{{BlockId}}", "blockTypeKey": "hero-banner", "blockTypeRevision": 3,
                  "properties": {
                    "image": { "type": "media", "mediaId": 812 },
                    "cta": { "type": "link", "kind": "page", "pageId": 44 } } }
              ] } } }
            """);

        references.Should().Equal(
            new ContentReference(
                ContentReferenceTargetType.Media, 812, "zones.hero.items[0].properties.image"),
            new ContentReference(
                ContentReferenceTargetType.Page, 44, "zones.hero.items[0].properties.cta"));
    }

    [Test]
    public void AReferenceTwoBlockLevelsDownIsReported()
    {
        var references = Extract(
            $$"""
            { "schemaVersion": 1, "templateKey": "t", "templateRevision": 1,
              "zones": { "sidebar": { "type": "blocks", "items": [
                { "id": "{{BlockId}}", "blockTypeKey": "text-columns", "blockTypeRevision": 1,
                  "properties": { "children": { "type": "blocks", "items": [
                    { "id": "{{NestedId}}", "blockTypeKey": "quote", "blockTypeRevision": 1,
                      "properties": { "portrait": { "type": "media", "mediaId": 977 } } }
                  ] } } }
              ] } } }
            """);

        // The failure this whole projection exists to prevent: a page that stops invalidating when
        // the image it shows is replaced, because the reference was never recorded (spec 7.3).
        references.Should().Equal(
            new ContentReference(
                ContentReferenceTargetType.Media,
                977,
                "zones.sidebar.items[0].properties.children.items[0].properties.portrait"));
    }

    [Test]
    public void ThePathOfAnIndexedReferenceReadsAsOneExpression()
    {
        var references = Extract(
            """
            { "schemaVersion": 1, "templateKey": "t", "templateRevision": 1,
              "zones": { "related": { "type": "pageReference", "value": [11, 12] } } }
            """);

        references.Select(reference => reference.Path).Should()
            .Equal("zones.related.value[0]", "zones.related.value[1]");
    }

    [Test]
    public void TheSameTargetReferencedTwiceIsReportedTwice()
    {
        var references = Extract(
            """
            { "schemaVersion": 1, "templateKey": "t", "templateRevision": 1,
              "zones": { "hero": { "type": "media", "mediaId": 812 },
                         "thumbnail": { "type": "media", "mediaId": 812 } } }
            """);

        // Occurrences, not distinct targets. Collapsing them is the projection's business, and
        // knowing both places is what lets the backoffice show an editor where a reference is.
        references.Should().HaveCount(2);
        references.Select(reference => reference.TargetId).Should().AllBeEquivalentTo(812);
    }

    [Test]
    public void AZoneTheTemplateNoLongerDefinesStillReportsItsReferences()
    {
        var references = Extract(
            """
            { "schemaVersion": 1, "templateKey": "t", "templateRevision": 1,
              "zones": { "removedZone": { "type": "reusable", "reusableContentId": 3 } } }
            """);

        // The walk is driven by the payload, not by the schema. Orphaned content is retained
        // (spec section 8.5), and an index that forgot it would under-report where-used — the one
        // direction of error that produces stale pages.
        references.Should().Equal(
            new ContentReference(ContentReferenceTargetType.ReusableContent, 3, "zones.removedZone"));
    }

    [Test]
    public void APayloadWhoseTemplateRevisionIsUnknownStillReportsItsReferences()
    {
        // Nothing here consults a schema catalog at all. A schema-driven walk would find no zones to
        // iterate and would erase the page's reference rows on its next save.
        var references = Extract(
            """
            { "schemaVersion": 1, "templateKey": "deleted-template", "templateRevision": 404,
              "zones": { "image": { "type": "media", "mediaId": 5 } } }
            """);

        references.Should().ContainSingle();
    }

    [Test]
    public void AValueWrittenByAFieldTypeThisBuildNoLongerHasIsSkipped()
    {
        var references = Extract(
            """
            { "schemaVersion": 1, "templateKey": "t", "templateRevision": 1,
              "zones": { "legacy": { "type": "assetPicker", "assetId": 9 } } }
            """);

        // Nothing can read the value's shape, so nothing can be recovered from it until the field
        // type is restored. Throwing on it would take down every save on the site (spec 15.3).
        references.Should().BeEmpty();
    }

    [Test]
    public void AValueThatPointsAtNothingContributesNothing()
    {
        var references = Extract(
            """
            { "schemaVersion": 1, "templateKey": "t", "templateRevision": 1,
              "zones": { "headline": { "type": "plainText", "value": "Ship faster" },
                         "cleared": null,
                         "empty": { "type": "media", "mediaId": null } } }
            """);

        references.Should().BeEmpty();
    }

    [Test]
    public void APayloadWithNoZonesContributesNothing()
    {
        Extract("""{ "schemaVersion": 1, "templateKey": "t", "templateRevision": 1 }""")
            .Should().BeEmpty();
    }

    [Test]
    public void EveryExtractedReferenceCarriesAUsableTarget()
    {
        var references = Extract(
            """
            { "schemaVersion": 1, "templateKey": "t", "templateRevision": 1,
              "zones": { "image": { "type": "media", "mediaId": 812 },
                         "related": { "type": "pageReference", "value": 44 } } }
            """);

        // A row pointing at id 0 is a foreign key that fails on the publish path, in a transaction
        // that has already snapshotted the version (spec section 5.5).
        references.Should().OnlyContain(reference => reference.TargetId > 0);
        references.Should().OnlyContain(reference => reference.Path != null);
    }
}
