using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Content;

/// <summary>
/// Pins the stored formats (task P1-17, spec section 6.2).
/// </summary>
/// <remarks>
/// These are not tests of behaviour; they are a tripwire on a storage contract. Every page version
/// ever written is read back through this envelope, so a member that quietly changes name or
/// nesting does not break a test somewhere — it breaks every row in the database, at some later
/// date, on a machine nobody is watching. Failing here is the cheap version of finding that out.
/// </remarks>
public class PayloadEnvelopeSnapshotTests
{
    private const string BlockId = "0f6c8b1e-3a4d-4f2b-9c7e-1d2a3b4c5d6e";

    [Test]
    public void TheEnvelopeAPageStartsLifeWithIsPinned()
    {
        var payload = ContentPayload.CreateEmpty("marketing-landing", 7);

        JsonSnapshot.Match(payload.ToJson(), "empty-page.json");
    }

    [Test]
    public void TheEnvelopeOfAnAuthoredPageIsPinned()
    {
        var payload = new ContentPayloadBuilder("marketing-landing", 7)
            .SetZone(
                "hero",
                $$"""
                {
                  "type": "blocks",
                  "items": [
                    {
                      "id": "{{BlockId}}",
                      "blockTypeKey": "hero-banner",
                      "blockTypeRevision": 3,
                      "properties": {
                        "headline": { "type": "plainText", "value": "Ship faster" },
                        "body": { "type": "richText", "format": "markdown", "value": "We **help** teams…" },
                        "image": {
                          "type": "media", "mediaId": 812, "altOverride": null,
                          "focalPoint": { "x": 0.5, "y": 0.33 },
                          "crop": { "x": 0, "y": 0.1, "w": 1, "h": 0.8 }
                        },
                        "cta": {
                          "type": "link", "kind": "page", "pageId": 44,
                          "text": "Get started", "target": "_self", "rel": null
                        }
                      }
                    }
                  ]
                }
                """)
            .SetZone("body", """{ "type": "richText", "format": "html", "value": "<p>…</p>" }""")
            .SetZone("footer", """{ "type": "reusable", "reusableContentId": 3, "pinnedVersionId": null }""")
            .ClearZone("subtitle")
            .Build();

        // The spec section 6.2 example, plus a zone an editor cleared on purpose. That the cleared
        // zone survives as null rather than disappearing is the part worth pinning.
        JsonSnapshot.Match(payload.ToJson(), "authored-page.json");
    }

    [Test]
    public void TheStoredFormOfAPayloadIsCompact()
    {
        var payload = ContentPayload.CreateEmpty("marketing-landing", 7);

        // What goes in the column. Indented JSON would inflate every version row by roughly a third
        // for no reader's benefit — the editor reads through the API, not the column.
        payload.ToJson().Should().NotContain("\n");
    }

    [Test]
    public void TheSchemaSnapshotFormatIsPinned()
    {
        var zones = new List<Zone>
        {
            new()
            {
                Key = "hero",
                Name = "Hero",
                FieldTypeKey = FieldTypeKeys.Blocks,
                IsRequired = true,
                SortOrder = 0,
                ConfigurationJson = """{ "allowedBlockTypes": ["hero-banner"], "max": 3 }""",
            },
            new()
            {
                Key = "body",
                Name = "Body",
                FieldTypeKey = FieldTypeKeys.RichText,
                SortOrder = 1,
            },
        };

        // The other storage contract this phase introduces: what a template revision captures, and
        // therefore what a published page is validated and rendered against for the rest of its life
        // (spec section 8.5).
        JsonSnapshot.Match(ContentSchemaSnapshot.WriteZones(zones), "zone-snapshot.json");
    }

    [Test]
    public void ASchemaSnapshotRoundTripsBackIntoTheSameDefinitions()
    {
        var written = ContentSchemaSnapshot.WriteZones(
            [
                new Zone
                {
                    Key = "hero",
                    Name = "Hero",
                    FieldTypeKey = FieldTypeKeys.Blocks,
                    IsRequired = true,
                    ConfigurationJson = """{ "max": 3 }""",
                },
            ]);

        var slot = ContentSchemaSnapshot.Read(written).Should().ContainSingle().Subject;

        slot.Key.Should().Be("hero");
        slot.Name.Should().Be("Hero");
        slot.FieldTypeKey.Should().Be(FieldTypeKeys.Blocks);
        slot.IsRequired.Should().BeTrue();
        slot.Configuration.GetInt32("max").Should().Be(3);
    }
}
