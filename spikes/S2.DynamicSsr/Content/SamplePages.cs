using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace S2.DynamicSsr.Content;

public sealed record SamplePage(long Id, long VersionId, string TemplateKey, string PayloadJson)
{
    public JsonDocument Parse() => JsonDocument.Parse(PayloadJson);
}

/// <summary>
/// One page per row of the spec §15.3 fallback matrix, plus a happy path and a large page for timing.
/// </summary>
public static class SamplePages
{
    public static IReadOnlyDictionary<long, SamplePage> All { get; } = new[]
    {
        new SamplePage(1, 1001, "marketing-landing", Healthy),
        new SamplePage(2, 1002, "no-such-template", Healthy),
        new SamplePage(3, 1003, "marketing-landing", UnknownFieldType),
        new SamplePage(4, 1004, "marketing-landing", BlockThrowsInLifecycle),
        new SamplePage(5, 1005, "marketing-landing", BlockThrowsInRender),
        new SamplePage(6, 1006, "marketing-landing", BlockThrowsAsync),
        new SamplePage(7, 1007, "marketing-landing", MissingMediaAndUnpublishedReusable),
        new SamplePage(8, 1008, "marketing-landing", UnknownBlockType),
        new SamplePage(9, 1009, "marketing-landing", Large(50)),
        // Page 1 with the artificially slow block removed, so timings measure rendering rather than
        // a Task.Delay.
        new SamplePage(10, 1010, "marketing-landing", HealthyWithoutDelay),
    }.ToDictionary(p => p.Id);

    private const string HeroAndFooter = """
        "footer": { "type": "reusable", "reusableContentId": 3 }
        """;

    private static string HealthyWithoutDelay
    {
        get
        {
            var payload = JsonNode.Parse(Healthy)!;
            var items = payload["zones"]!["hero"]!["items"]!.AsArray();

            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i]!["blockTypeKey"]!.GetValue<string>() == "slow")
                {
                    items.RemoveAt(i);
                }
            }

            return payload.ToJsonString();
        }
    }

    private static string Healthy => $$"""
        {
          "schemaVersion": 1,
          "templateKey": "marketing-landing",
          "templateRevision": 7,
          "zones": {
            "hero": {
              "type": "blocks",
              "items": [
                {
                  "id": "0f6c1f2e-6f31-4b0a-9f6a-1b8a2c3d4e5f",
                  "blockTypeKey": "hero-banner",
                  "blockTypeRevision": 3,
                  "properties": {
                    "headline": { "type": "plainText", "value": "Ship faster" },
                    "body": { "type": "plainText", "value": "We help teams ship." },
                    "image": { "type": "media", "mediaId": 812 }
                  }
                },
                {
                  "id": "b21d8f44-2a5c-4d1e-8f0b-77c2e9a10d33",
                  "blockTypeKey": "quote",
                  "blockTypeRevision": 1,
                  "properties": {
                    "quote": { "type": "plainText", "value": "It cut our publish cycle in half." },
                    "attribution": { "type": "plainText", "value": "Head of Content, Northwind" }
                  }
                },
                {
                  "id": "cc11cc11-cc11-4c11-8c11-cc11cc11cc11",
                  "blockTypeKey": "slow",
                  "blockTypeRevision": 1,
                  "properties": { "body": { "type": "plainText", "value": "Resolved after an await." } }
                }
              ]
            },
            "body": { "type": "richText", "format": "markdown", "value": "Why teams choose us" },
            {{HeroAndFooter}}
          }
        }
        """;

    private static string UnknownFieldType => """
        {
          "schemaVersion": 1,
          "templateKey": "marketing-landing",
          "templateRevision": 7,
          "zones": {
            "hero": { "type": "sparkline", "value": [1, 2, 3] },
            "body": { "type": "richText", "format": "markdown", "value": "The rest of the page still renders." }
          }
        }
        """;

    private static string BlockThrowsInLifecycle => WithHeroBlock("throws-in-lifecycle");

    private static string BlockThrowsInRender => WithHeroBlock("throws-in-render");

    private static string BlockThrowsAsync => WithHeroBlock("throws-async");

    private static string UnknownBlockType => WithHeroBlock("carousel");

    private static string MissingMediaAndUnpublishedReusable => """
        {
          "schemaVersion": 1,
          "templateKey": "marketing-landing",
          "templateRevision": 7,
          "zones": {
            "hero": {
              "type": "blocks",
              "items": [
                {
                  "id": "aa11aa11-aa11-4a11-8a11-aa11aa11aa11",
                  "blockTypeKey": "hero-banner",
                  "blockTypeRevision": 3,
                  "properties": {
                    "headline": { "type": "plainText", "value": "Still renders" },
                    "image": { "type": "media", "mediaId": 404, "altOverride": "A diagram that went missing" }
                  }
                }
              ]
            },
            "body": { "type": "richText", "format": "markdown", "value": "Body survives both failures." },
            "footer": { "type": "reusable", "reusableContentId": 9 }
          }
        }
        """;

    /// <summary>A failing block surrounded by healthy siblings — isolation only means something here.</summary>
    private static string WithHeroBlock(string blockTypeKey) => $$"""
        {
          "schemaVersion": 1,
          "templateKey": "marketing-landing",
          "templateRevision": 7,
          "zones": {
            "hero": {
              "type": "blocks",
              "items": [
                {
                  "id": "11111111-1111-4111-8111-111111111111",
                  "blockTypeKey": "quote",
                  "blockTypeRevision": 1,
                  "properties": {
                    "quote": { "type": "plainText", "value": "Sibling before the failure." },
                    "attribution": { "type": "plainText", "value": "Before" }
                  }
                },
                {
                  "id": "22222222-2222-4222-8222-222222222222",
                  "blockTypeKey": "{{blockTypeKey}}",
                  "blockTypeRevision": 1,
                  "properties": { "body": { "type": "plainText", "value": "boom" } }
                },
                {
                  "id": "33333333-3333-4333-8333-333333333333",
                  "blockTypeKey": "quote",
                  "blockTypeRevision": 1,
                  "properties": {
                    "quote": { "type": "plainText", "value": "Sibling after the failure." },
                    "attribution": { "type": "plainText", "value": "After" }
                  }
                }
              ]
            },
            "body": { "type": "richText", "format": "markdown", "value": "Body after a failing block." },
            "footer": { "type": "reusable", "reusableContentId": 3 }
          }
        }
        """;

    private static string Large(int blockCount)
    {
        var builder = new StringBuilder();
        builder.Append("""{"schemaVersion":1,"templateKey":"marketing-landing","templateRevision":7,"zones":{"hero":{"type":"blocks","items":[""");

        for (var i = 0; i < blockCount; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var id = new Guid(i + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

            builder.Append("{\"id\":\"").Append(id).Append("\",\"blockTypeRevision\":1,");

            if (i % 2 == 0)
            {
                builder.Append("\"blockTypeKey\":\"hero-banner\",\"properties\":{")
                    .Append("\"headline\":{\"type\":\"plainText\",\"value\":\"Headline ").Append(i).Append("\"},")
                    .Append("\"body\":{\"type\":\"plainText\",\"value\":\"A paragraph of representative prose.\"},")
                    .Append("\"image\":{\"type\":\"media\",\"mediaId\":812}}}");
            }
            else
            {
                builder.Append("\"blockTypeKey\":\"quote\",\"properties\":{")
                    .Append("\"quote\":{\"type\":\"plainText\",\"value\":\"Pull quote ").Append(i).Append("\"},")
                    .Append("\"attribution\":{\"type\":\"plainText\",\"value\":\"Someone, Somewhere\"}}}");
            }
        }

        builder.Append("""]},"body":{"type":"richText","format":"markdown","value":"Body"},"footer":{"type":"reusable","reusableContentId":3}}}""");

        return builder.ToString();
    }
}
