using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Content;

/// <summary>
/// The payload walk (task P1-15, spec sections 6.2, 8.5 and 15.3).
/// </summary>
public class ContentSchemaValidatorTests
{
    private const string BlockId = "0f6c8b1e-3a4d-4f2b-9c7e-1d2a3b4c5d6e";

    private static readonly Core.Content.Schema.ContentSchema Template =
        ContentEngineHarness.Template(
            "marketing-landing",
            7,
            ContentEngineHarness.Slot("headline", FieldTypeKeys.PlainText, """{ "maxLength": 20 }"""),
            ContentEngineHarness.Slot("body", FieldTypeKeys.MultilineText, isRequired: true),
            ContentEngineHarness.Slot("hero", FieldTypeKeys.Blocks));

    private static readonly Core.Content.Schema.BlockTypeSchema HeroBanner =
        ContentEngineHarness.BlockType(
            "hero-banner",
            3,
            ContentEngineHarness.Slot("headline", FieldTypeKeys.PlainText, """{ "maxLength": 10 }"""),
            ContentEngineHarness.Slot("image", FieldTypeKeys.Media, isRequired: true));

    private static readonly Core.Content.Schema.ContentSchemaCatalog Catalog =
        ContentEngineHarness.Catalog(Template, HeroBanner);

    [Test]
    public async Task AValidPayloadIsAccepted()
    {
        var report = await Catalog.ValidateAsync(
            $$"""
            {
              "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": {
                "headline": { "type": "plainText", "value": "Ship faster" },
                "body": { "type": "multilineText", "value": "Two\nlines" },
                "hero": { "type": "blocks", "items": [
                  { "id": "{{BlockId}}", "blockTypeKey": "hero-banner", "blockTypeRevision": 3,
                    "properties": {
                      "headline": { "type": "plainText", "value": "Ship" },
                      "image": { "type": "media", "mediaId": 812 }
                    } }
                ] }
              }
            }
            """,
            ValidationMode.Publish);

        report.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task APayloadThatIsNotAnObjectIsRejected()
    {
        var report = await Catalog.ValidateAsync("[]");

        report.Codes().Should().Equal(ContentValidationCodes.PayloadShape);
    }

    [Test]
    public async Task AnEnvelopeWithNoSchemaVersionIsRejected()
    {
        var report = await Catalog.ValidateAsync("""{ "templateKey": "marketing-landing" }""");

        report.Codes().Should().Equal(ContentValidationCodes.SchemaVersionMissing);
    }

    [Test]
    public async Task ASchemaVersionThisBuildCannotReadIsRejected()
    {
        var report = await Catalog.ValidateAsync(
            """{ "schemaVersion": 2, "templateKey": "marketing-landing", "templateRevision": 7, "zones": {} }""");

        // Including a version from the future: a node mid-rollback must refuse content it cannot
        // read rather than check it against assumptions that no longer hold.
        report.Codes().Should().Equal(ContentValidationCodes.SchemaVersionUnsupported);
    }

    [Test]
    public async Task AnEnvelopeNamingNoTemplateIsRejected()
    {
        var report = await Catalog.ValidateAsync("""{ "schemaVersion": 1, "zones": {} }""");

        report.Codes().Should().Equal(ContentValidationCodes.TemplateMissing);
    }

    [Test]
    public async Task ATemplateRevisionThisDeploymentDoesNotKnowIsRejected()
    {
        var report = await Catalog.ValidateAsync(
            """{ "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 99, "zones": {} }""");

        // Nothing below can be checked without a schema, and publishing content nobody could check
        // is how a page reaches the public site broken.
        report.Codes().Should().Equal(ContentValidationCodes.TemplateUnknown);
        report.HasErrors.Should().BeTrue();
    }

    [Test]
    public async Task AnEnvelopeWithNoZonesObjectIsRejected()
    {
        var report = await Catalog.ValidateAsync(
            """{ "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7 }""");

        report.Codes().Should().Equal(ContentValidationCodes.ZonesMissing);
    }

    [Test]
    public async Task AZoneValueIsCheckedByItsFieldTypeAndAddressedByTheWalk()
    {
        var report = await Catalog.ValidateAsync(
            """
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": { "headline": { "type": "plainText", "value": "Far too long to fit in twenty" } } }
            """);

        report.Codes().Should().Equal(FieldValidationCodes.MaxLength);
        report.Paths().Should().Equal("zones.headline.value");

        var diagnostic = report.Diagnostics[0];
        diagnostic.ZoneKey.Should().Be("headline");
        diagnostic.PropertyKey.Should().Be("headline");
        diagnostic.BlockId.Should().BeNull();
    }

    [Test]
    public async Task AnUnfilledRequiredZoneBlocksPublishingButNotADraftSave()
    {
        const string Payload =
            """
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": { "headline": { "type": "plainText", "value": "Ship" } } }
            """;

        // An editor must always be able to save half-finished work (spec section 8.3).
        (await Catalog.ValidateAsync(Payload)).IsValid.Should().BeTrue();

        var publish = await Catalog.ValidateAsync(Payload, ValidationMode.Publish);

        publish.Codes().Should().Equal(FieldValidationCodes.Required);
        publish.Paths().Should().Equal("zones.body");
    }

    [Test]
    public async Task AZoneClearedOnPurposeIsStillUnfilledWhenPublishing()
    {
        var report = await Catalog.ValidateAsync(
            """
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": { "body": null } }
            """,
            ValidationMode.Publish);

        report.Codes().Should().Equal(FieldValidationCodes.Required);
    }

    [Test]
    public async Task AZoneTheTemplateNoLongerDefinesIsAWarningAndItsDataIsUntouched()
    {
        var payloadJson =
            """
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": {
                "body": { "type": "multilineText", "value": "kept" },
                "sidebar": { "type": "plainText", "value": "orphaned but retained" } } }
            """;

        var report = await Catalog.ValidateAsync(payloadJson, ValidationMode.Publish);

        // Removing a zone must not destroy content, which is only implementable if the diagnostic is
        // a warning: an error here would make the page unpublishable until someone discarded it.
        report.Codes().Should().Equal(ContentValidationCodes.ZoneOrphaned);
        report.Paths().Should().Equal("zones.sidebar");
        report.HasErrors.Should().BeFalse();
        report.Diagnostics[0].Severity.Should().Be(ValidationSeverity.Warning);

        ContentPayload.Parse(payloadJson).GetZone("sidebar").GetProperty("value").GetString()
            .Should().Be("orphaned but retained");
    }

    [Test]
    public async Task AZoneDefinedAgainstAFieldTypeThisBuildNoLongerHasIsAWarning()
    {
        var schema = ContentEngineHarness.Template(
            "marketing-landing",
            7,
            ContentEngineHarness.Slot("legacy", "markdownTable", isRequired: true));

        var report = await schema.ValidateAsync(
            """
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": { "legacy": { "type": "markdownTable", "value": "…" } } }
            """,
            ValidationMode.Publish);

        // Spec section 15.3: an unknown field type key renders nothing and logs. Erroring would make
        // removing a field type from a build unsaveable rather than merely degraded.
        report.Codes().Should().Equal(ContentValidationCodes.FieldTypeUnknown);
        report.HasErrors.Should().BeFalse();
    }

    [Test]
    public async Task AValueWrittenByADifferentFieldTypeThanTheSchemaDeclaresIsRejected()
    {
        var report = await Catalog.ValidateAsync(
            """
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": { "headline": { "type": "number", "value": 3 } } }
            """);

        // A field type change needs an explicit converter (spec section 8.5); until one has run, the
        // stored bytes are not the shape the schema claims.
        report.Codes().Should().Equal(FieldValidationCodes.TypeMismatch);
    }

    [Test]
    public async Task ABlockPropertyIsCheckedAgainstItsBlockTypeRevision()
    {
        var report = await Catalog.ValidateAsync(
            $$"""
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": { "hero": { "type": "blocks", "items": [
                { "id": "{{BlockId}}", "blockTypeKey": "hero-banner", "blockTypeRevision": 3,
                  "properties": { "headline": { "type": "plainText", "value": "Rather too long" } } }
              ] } } }
            """);

        report.Codes().Should().Equal(FieldValidationCodes.MaxLength);
        report.Paths().Should().Equal("zones.hero.items[0].properties.headline.value");
    }

    [Test]
    public async Task ABlockDiagnosticNamesTheZoneTheBlockAndTheProperty()
    {
        var report = await Catalog.ValidateAsync(
            $$"""
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": {
                "body": { "type": "multilineText", "value": "filled" },
                "hero": { "type": "blocks", "items": [
                  { "id": "{{BlockId}}", "blockTypeKey": "hero-banner", "blockTypeRevision": 3,
                    "properties": { "headline": { "type": "plainText", "value": "Ship" } } }
                ] } } }
            """,
            ValidationMode.Publish);

        // Acceptance criterion P1 #2: the exact zone, block id, and property. The block id rather
        // than its index, because that is how the backoffice addresses a block.
        var diagnostic = report.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be(FieldValidationCodes.Required);
        diagnostic.ZoneKey.Should().Be("hero");
        diagnostic.BlockId.Should().Be(Guid.Parse(BlockId));
        diagnostic.PropertyKey.Should().Be("image");
        diagnostic.Path.Should().Be("zones.hero.items[0].properties.image");
    }

    [Test]
    public async Task ABlockWithNoPropertiesAtAllStillFailsItsRequiredProperties()
    {
        var report = await Catalog.ValidateAsync(
            $$"""
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": {
                "body": { "type": "multilineText", "value": "filled" },
                "hero": { "type": "blocks", "items": [
                  { "id": "{{BlockId}}", "blockTypeKey": "hero-banner", "blockTypeRevision": 3 }
                ] } } }
            """,
            ValidationMode.Publish);

        report.Codes().Should().Equal(FieldValidationCodes.Required);
        report.Paths().Should().Equal("zones.hero.items[0].properties.image");
    }

    [Test]
    public async Task ABlockTypeRevisionThisDeploymentDoesNotKnowIsAWarning()
    {
        var report = await Catalog.ValidateAsync(
            $$"""
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": {
                "body": { "type": "multilineText", "value": "filled" },
                "hero": { "type": "blocks", "items": [
                  { "id": "{{BlockId}}", "blockTypeKey": "hero-banner", "blockTypeRevision": 99,
                    "properties": {} }
                ] } } }
            """,
            ValidationMode.Publish);

        // Its properties cannot be checked, but refusing the save would strand every page holding a
        // block whose type was removed, leaving an editor no way to delete it.
        report.Codes().Should().Equal(ContentValidationCodes.BlockTypeUnknown);
        report.Paths().Should().Equal("zones.hero.items[0]");
        report.HasErrors.Should().BeFalse();
        report.Diagnostics[0].BlockId.Should().Be(Guid.Parse(BlockId));
    }

    [Test]
    public async Task ABlockCarryingNoRevisionFallsBackToTheNewestKnownOne()
    {
        var report = await Catalog.ValidateAsync(
            $$"""
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": { "hero": { "type": "blocks", "items": [
                { "id": "{{BlockId}}", "blockTypeKey": "hero-banner",
                  "properties": { "image": { "type": "media", "mediaId": 1 },
                                  "headline": { "type": "plainText", "value": "Rather too long" } } }
              ] } } }
            """);

        // A payload written before revisions were captured is validated, not written off.
        report.Codes().Should().Equal(FieldValidationCodes.MaxLength);
    }

    [Test]
    public async Task ABlockPropertyTheBlockTypeNoLongerDefinesIsAWarning()
    {
        var report = await Catalog.ValidateAsync(
            $$"""
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": {
                "body": { "type": "multilineText", "value": "filled" },
                "hero": { "type": "blocks", "items": [
                  { "id": "{{BlockId}}", "blockTypeKey": "hero-banner", "blockTypeRevision": 3,
                    "properties": { "image": { "type": "media", "mediaId": 812 },
                                    "subtitle": { "type": "plainText", "value": "retained" } } }
                ] } } }
            """,
            ValidationMode.Publish);

        report.Codes().Should().Equal(ContentValidationCodes.PropertyOrphaned);
        report.Paths().Should().Equal("zones.hero.items[0].properties.subtitle");
        report.HasErrors.Should().BeFalse();
    }

    [Test]
    public async Task APropertyNestedAWholeBlockLevelDownIsStillChecked()
    {
        var nestedId = "7a1b2c3d-4e5f-4061-8293-a4b5c6d7e8f9";
        var container = ContentEngineHarness.BlockType(
            "cards",
            1,
            ContentEngineHarness.Slot("children", FieldTypeKeys.Blocks, """{ "allowNesting": true }"""));
        var quote = ContentEngineHarness.BlockType(
            "quote",
            1,
            ContentEngineHarness.Slot("text", FieldTypeKeys.PlainText, """{ "maxLength": 5 }"""));
        var catalog = ContentEngineHarness.Catalog(
            ContentEngineHarness.Template(
                "marketing-landing",
                7,
                ContentEngineHarness.Slot("hero", FieldTypeKeys.Blocks, """{ "allowNesting": true }""")),
            container,
            quote);

        var report = await catalog.ValidateAsync(
            $$"""
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": { "hero": { "type": "blocks", "items": [
                { "id": "{{BlockId}}", "blockTypeKey": "cards", "blockTypeRevision": 1, "properties": {
                  "children": { "type": "blocks", "items": [
                    { "id": "{{nestedId}}", "blockTypeKey": "quote", "blockTypeRevision": 1,
                      "properties": { "text": { "type": "plainText", "value": "too long" } } }
                  ] } } }
              ] } } }
            """);

        // The gap S1 found one level down, in the walk rather than in reference extraction: a
        // container that is not descended into validates nothing inside it.
        report.Codes().Should().Equal(FieldValidationCodes.MaxLength);
        report.Paths().Should().Equal(
            "zones.hero.items[0].properties.children.items[0].properties.text.value");
        report.Diagnostics[0].BlockId.Should().Be(Guid.Parse(nestedId));
    }

    [Test]
    public async Task ARemovedZoneAndANewlyRequiredOneBehaveAsTemplateEvolutionPromises()
    {
        // The S1 scenario: content authored against revision 7 checked against revision 8, which
        // removes 'sidebar' and adds a required 'announcement' (spec section 8.5).
        var revision8 = ContentEngineHarness.Template(
            "marketing-landing",
            8,
            ContentEngineHarness.Slot("headline", FieldTypeKeys.PlainText),
            ContentEngineHarness.Slot("announcement", FieldTypeKeys.PlainText, isRequired: true));

        const string Payload =
            """
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": { "headline": { "type": "plainText", "value": "Ship" },
                         "sidebar": { "type": "plainText", "value": "kept" } } }
            """;

        var draft = await revision8.ValidateAsync(Payload);

        draft.HasErrors.Should().BeFalse();
        draft.Codes().Should().Equal(ContentValidationCodes.ZoneOrphaned);

        var publish = await revision8.ValidateAsync(Payload, ValidationMode.Publish);

        publish.HasErrors.Should().BeTrue();
        publish.Errors.Should().ContainSingle()
            .Which.Should().Match<ContentValidationDiagnostic>(diagnostic =>
                diagnostic.Code == FieldValidationCodes.Required &&
                diagnostic.Path == "zones.announcement");
    }

    [Test]
    public async Task EveryDiagnosticInABrokenPayloadIsReportedAtOnce()
    {
        var report = await Catalog.ValidateAsync(
            $$"""
            { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
              "zones": {
                "headline": { "type": "plainText", "value": "Far too long to fit in twenty" },
                "hero": { "type": "blocks", "items": [
                  { "id": "{{BlockId}}", "blockTypeKey": "hero-banner", "blockTypeRevision": 3,
                    "properties": { "headline": { "type": "plainText", "value": "Also far too long" } } }
                ] },
                "sidebar": { "type": "plainText", "value": "orphaned" } } }
            """,
            ValidationMode.Publish);

        // One walk, everything found, in document order — an editor fixing one problem at a time
        // because the validator stopped at the first is the behaviour this is written to avoid.
        report.Codes().Should().Equal(
            FieldValidationCodes.MaxLength,
            FieldValidationCodes.Required,
            FieldValidationCodes.MaxLength,
            FieldValidationCodes.Required,
            ContentValidationCodes.ZoneOrphaned);
    }

    [Test]
    public async Task ACancelledWalkStops()
    {
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        var validate = async () => await ContentEngineHarness.Validator(Catalog).ValidateAsync(
            ContentEngineHarness.Payload(
                """
                { "schemaVersion": 1, "templateKey": "marketing-landing", "templateRevision": 7,
                  "zones": {} }
                """),
            ValidationMode.Publish,
            cancellation.Token);

        await validate.Should().ThrowAsync<OperationCanceledException>();
    }
}
