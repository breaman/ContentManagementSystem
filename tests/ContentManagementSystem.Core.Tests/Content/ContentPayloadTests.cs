using System.Text.Json;

using ContentManagementSystem.Shared.Content;

namespace ContentManagementSystem.Core.Tests.Content;

/// <summary>
/// The payload envelope and its absent-versus-null semantics (task P1-14, spec section 6.2).
/// </summary>
public class ContentPayloadTests
{
    private const string Envelope =
        """
        {
          "schemaVersion": 1,
          "templateKey": "marketing-landing",
          "templateRevision": 7,
          "zones": {
            "headline": { "type": "plainText", "value": "Ship faster" },
            "subtitle": null
          }
        }
        """;

    [Test]
    public void TheEnvelopeIsReadable()
    {
        var payload = ContentPayload.Parse(Envelope);

        payload.SchemaVersion.Should().Be(1);
        payload.TemplateKey.Should().Be("marketing-landing");
        payload.TemplateRevision.Should().Be(7);
        payload.HasZones.Should().BeTrue();
    }

    [Test]
    public void AZoneThatWasNeverAuthoredIsAbsent()
    {
        var payload = ContentPayload.Parse(Envelope);

        payload.GetZoneState("body").Should().Be(ContentValueState.Absent);
        payload.TryGetZone("body", out _).Should().BeFalse();
        payload.GetZone("body").ValueKind.Should().Be(JsonValueKind.Undefined);
    }

    [Test]
    public void AZoneThatWasExplicitlyClearedIsNotAbsent()
    {
        var payload = ContentPayload.Parse(Envelope);

        // The distinction spec section 6.2 turns on: a zone added to the template after this was
        // written reads as absent and renders empty, while this one was emptied on purpose.
        payload.GetZoneState("subtitle").Should().Be(ContentValueState.Cleared);
        payload.TryGetZone("subtitle", out var cleared).Should().BeTrue();
        cleared.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public void AZoneHoldingAValueIsPresent()
    {
        var payload = ContentPayload.Parse(Envelope);

        payload.GetZoneState("headline").Should().Be(ContentValueState.Present);
    }

    [Test]
    public void ZoneKeysAreReportedInDocumentOrder()
    {
        var payload = ContentPayload.Parse(Envelope);

        payload.ZoneKeys.Should().Equal("headline", "subtitle");
    }

    [Test]
    public void AMalformedEnvelopeStillParses()
    {
        // Nothing about a missing member is this type's to refuse: the validator needs a readable
        // object to report the envelope's problems against.
        var payload = ContentPayload.Parse("""{ "templateKey": "marketing-landing" }""");

        payload.SchemaVersion.Should().BeNull();
        payload.TemplateRevision.Should().BeNull();
        payload.HasZones.Should().BeFalse();
        payload.ZoneKeys.Should().BeEmpty();
    }

    [Test]
    public void APayloadThatIsNotAnObjectParsesAndSaysSo()
    {
        var payload = ContentPayload.Parse("[]");

        payload.IsObject.Should().BeFalse();
        payload.TemplateKey.Should().BeNull();
    }

    [Test]
    public void TextThatIsNotJsonIsRefused()
    {
        var parse = () => ContentPayload.Parse("{ not json");

        parse.Should().Throw<JsonException>();
        ContentPayload.TryParse("{ not json", out _).Should().BeFalse();
        ContentPayload.TryParse(null, out _).Should().BeFalse();
        ContentPayload.TryParse(Envelope, out var payload).Should().BeTrue();
        payload!.TemplateKey.Should().Be("marketing-landing");
    }

    [Test]
    public void AParsedPayloadOutlivesTheDocumentItCameFrom()
    {
        var payload = ContentPayload.Parse(Envelope);

        GC.Collect();

        // The reason this type holds a detached clone rather than an IDisposable document: it is
        // cached for fifteen minutes by the published-content cache (spec section 16.1), where a
        // disposed backing document would be a use-after-free with a very long fuse.
        payload.GetZone("headline").GetProperty("value").GetString().Should().Be("Ship faster");
    }

    [Test]
    public void RoundTrippingPreservesEverythingThatCarriesMeaning()
    {
        var payload = ContentPayload.Parse(Envelope);

        var reparsed = ContentPayload.Parse(payload.ToJson());

        reparsed.ZoneKeys.Should().Equal("headline", "subtitle");
        reparsed.GetZoneState("subtitle").Should().Be(ContentValueState.Cleared);
        reparsed.GetZoneState("body").Should().Be(ContentValueState.Absent);
    }

    [Test]
    public void AnEmptyPayloadCarriesTheEnvelopeAndNoZones()
    {
        var payload = ContentPayload.CreateEmpty("marketing-landing", 7);

        payload.SchemaVersion.Should().Be(ContentPayload.CurrentSchemaVersion);
        payload.TemplateKey.Should().Be("marketing-landing");
        payload.TemplateRevision.Should().Be(7);
        payload.HasZones.Should().BeTrue();
        // Absent, not null: a page that has just been created has authored nothing, and a zone full
        // of explicit nulls would claim an editor cleared them.
        payload.ZoneKeys.Should().BeEmpty();
    }

    [Test]
    public void TheBuilderWritesTheZonesItIsGiven()
    {
        var payload = new ContentPayloadBuilder("marketing-landing", 7)
            .SetZone("headline", """{ "type": "plainText", "value": "Ship faster" }""")
            .ClearZone("subtitle")
            .Build();

        payload.GetZoneState("headline").Should().Be(ContentValueState.Present);
        payload.GetZoneState("subtitle").Should().Be(ContentValueState.Cleared);
    }

    [Test]
    public void EditingAZoneLeavesEveryOtherZoneWhereItWas()
    {
        var payload = new ContentPayloadBuilder(ContentPayload.Parse(Envelope))
            .SetZone("headline", """{ "type": "plainText", "value": "Ship sooner" }""")
            .Build();

        // Order is not cosmetic here. A save that moves an edited zone to the end of the object
        // reads to the version diff as a removal plus an addition (spec section 11.4).
        payload.ZoneKeys.Should().Equal("headline", "subtitle");
        payload.GetZone("headline").GetProperty("value").GetString().Should().Be("Ship sooner");
    }

    [Test]
    public void RemovingAZoneIsNotTheSameAsClearingIt()
    {
        var source = ContentPayload.Parse(Envelope);

        new ContentPayloadBuilder(source).ClearZone("headline").Build()
            .GetZoneState("headline").Should().Be(ContentValueState.Cleared);

        new ContentPayloadBuilder(source).RemoveZone("headline").Build()
            .GetZoneState("headline").Should().Be(ContentValueState.Absent);
    }

    [Test]
    public void AnEnvelopeMemberThisBuildDoesNotKnowSurvivesASave()
    {
        var source = ContentPayload.Parse(
            """
            { "schemaVersion": 1, "templateKey": "t", "templateRevision": 1,
              "experimentBucket": "b", "zones": {} }
            """);

        var saved = new ContentPayloadBuilder(source).Build();

        // Written by a newer node mid-rollout. Dropping it here is silent data loss that only shows
        // up once the newer nodes are back.
        saved.Root.TryGetProperty("experimentBucket", out var bucket).Should().BeTrue();
        bucket.GetString().Should().Be("b");
    }

    [Test]
    public void ASchemaVersionOlderThanThisBuildIsNotRestamped()
    {
        var source = ContentPayload.Parse("""{ "schemaVersion": 0, "templateKey": "t", "zones": {} }""");

        // Claiming a version the zones do not have would hide the one thing that says they need
        // migrating.
        new ContentPayloadBuilder(source).Build().SchemaVersion.Should().Be(0);
    }

    [Test]
    public void ThePayloadSurvivesASystemTextJsonRoundTrip()
    {
        var payload = ContentPayload.Parse(Envelope);

        var carrier = JsonSerializer.Deserialize<Carrier>(
            JsonSerializer.Serialize(new Carrier(payload)));

        // Without the converter the serializer reflects over the accessors and the document that
        // comes back out is not the one that went in.
        carrier!.Payload.TemplateKey.Should().Be("marketing-landing");
        carrier.Payload.GetZoneState("subtitle").Should().Be(ContentValueState.Cleared);
        carrier.Payload.GetZoneState("body").Should().Be(ContentValueState.Absent);
    }

    private sealed record Carrier(ContentPayload Payload);
}
