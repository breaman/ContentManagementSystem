using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// <c>blocks</c> (task P1-11, spec sections 6.2 and 7.1).
/// </summary>
public class BlocksFieldTypeTests
{
    private const string FirstId = "0f6c8b1e-3a4d-4f2b-9c7e-1d2a3b4c5d6e";

    private const string SecondId = "7a1b2c3d-4e5f-4061-8293-a4b5c6d7e8f9";

    private readonly BlocksFieldType _fieldType = FieldTypeTestHarness.Blocks(
        new PlainTextFieldType(),
        new MediaFieldType(),
        new LinkFieldType());

    [Test]
    public async Task AWellFormedBlockListIsAccepted()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "hero-banner", "blockTypeRevision": 3,
                  "properties": { "headline": { "type": "plainText", "value": "Ship faster" } } }
            ] }
            """);

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task ABlockWithNoStableIdIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "blocks", "items": [ { "blockTypeKey": "quote" } ] }""");

        // Without it the version diff cannot follow a block through a reorder, and reports the
        // whole zone as changed (spec section 11.4).
        result.Codes().Should().Equal(FieldValidationCodes.BlockId);
        result.Paths().Should().Equal("items[0].id");
    }

    [Test]
    public async Task AnIdThatIsNotAGuidIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "blocks", "items": [ { "id": "block-1", "blockTypeKey": "quote" } ] }""");

        result.Codes().Should().Equal(FieldValidationCodes.BlockId);
    }

    [Test]
    public async Task TwoBlocksSharingAnIdAreRejected()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "quote" },
                { "id": "{{FirstId}}", "blockTypeKey": "quote" }
            ] }
            """);

        result.Codes().Should().Equal(FieldValidationCodes.Duplicate);
        result.Paths().Should().Equal("items[1].id");
    }

    [Test]
    public async Task ABlockNamingNoBlockTypeIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""{ "type": "blocks", "items": [ { "id": "{{FirstId}}" } ] }""");

        result.Codes().Should().Equal(FieldValidationCodes.BlockTypeKey);
    }

    [Test]
    public async Task ABlockTypeOutsideTheAllowlistIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""{ "type": "blocks", "items": [ { "id": "{{FirstId}}", "blockTypeKey": "carousel" } ] }""",
            """{ "allowedBlockTypes": ["hero-banner", "quote"] }""");

        result.Codes().Should().Equal(FieldValidationCodes.BlockNotAllowed);
        result.Paths().Should().Equal("items[0].blockTypeKey");
    }

    [Test]
    public async Task AnEmptyAllowlistAcceptsAnyBlockType()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""{ "type": "blocks", "items": [ { "id": "{{FirstId}}", "blockTypeKey": "carousel" } ] }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task ABlockTypeThatIsNotRegisteredIsStillSavable()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""{ "type": "blocks", "items": [ { "id": "{{FirstId}}", "blockTypeKey": "retired" } ] }""");

        // Content outlives the code deployed when it was written. Refusing the save would strand
        // the page instead of letting an editor remove the block (spec section 15.3).
        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task ACapturedRevisionThatIsNotARevisionIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "quote", "blockTypeRevision": 0 }
            ] }
            """);

        result.Codes().Should().Equal(FieldValidationCodes.BlockRevision);
    }

    [Test]
    public async Task AnAbsentCapturedRevisionIsTolerated()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""{ "type": "blocks", "items": [ { "id": "{{FirstId}}", "blockTypeKey": "quote" } ] }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task PropertiesThatAreNotAnObjectAreAShapeError()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "quote", "properties": [] }
            ] }
            """);

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
        result.Paths().Should().Equal("items[0].properties");
    }

    [Test]
    public async Task ABlockPropertyValueIsNotCheckedHere()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "quote",
                  "properties": { "headline": { "type": "plainText", "value": 7 } } }
            ] }
            """);

        // Checking it needs the block type's own property definitions and each property's
        // configuration, which is the schema walk's to load (P1-15). This field type holds the
        // configuration for the list, not for anything inside it.
        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task NestedBlocksAreRejectedWhereTheyAreNotAllowed()
    {
        var result = await _fieldType.ValidateAsync(NestedPayload());

        result.Codes().Should().Equal(FieldValidationCodes.BlockNesting);
        result.Paths().Should().Equal("items[0].properties.rows");
    }

    [Test]
    public async Task OneLevelOfNestingIsAcceptedWhenConfigured()
    {
        var result = await _fieldType.ValidateAsync(NestedPayload(), """{ "allowNesting": true }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task ASecondLevelOfNestingIsRefusedEvenWhenNestingIsAllowed()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "grid", "properties": {
                    "rows": { "type": "blocks", "items": [
                        { "id": "{{SecondId}}", "blockTypeKey": "row", "properties": {
                            "cells": { "type": "blocks", "items": [] }
                        } }
                    ] }
                } }
            ] }
            """,
            """{ "allowNesting": true }""");

        // v1 supports one level; deeper is an editor-experience limit rather than a storage one
        // (spec section 7.1).
        result.Codes().Should().Equal(FieldValidationCodes.BlockNesting);
        result.Paths().Should().Equal("items[0].properties.rows.items[0].properties.cells");
    }

    [Test]
    public async Task MoreBlocksThanTheMaximumAreRejected()
    {
        var result = await _fieldType.ValidateAsync(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "quote" },
                { "id": "{{SecondId}}", "blockTypeKey": "quote" }
            ] }
            """,
            """{ "max": 1 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MaxItems);
    }

    [Test]
    public async Task AnEmptyZoneDoesNotSatisfyAMinimumOnPublish()
    {
        var draft = await _fieldType.ValidateAsync("""{ "type": "blocks", "items": [] }""", """{ "min": 1 }""");
        var publish = await _fieldType.ValidateAsync(
            """{ "type": "blocks", "items": [] }""",
            """{ "min": 1 }""",
            ValidationMode.Publish);

        draft.IsValid.Should().BeTrue();
        publish.Codes().Should().Equal(FieldValidationCodes.MinItems);
    }

    [Test]
    public void ReferencesInsideBlocksAreReportedWithTheirPath()
    {
        var references = _fieldType.ExtractReferences(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "hero-banner", "properties": {
                    "image": { "type": "media", "mediaId": 812 },
                    "cta": { "type": "link", "kind": "page", "pageId": 44 }
                } }
            ] }
            """);

        references.Should().BeEquivalentTo(
        [
            new ContentReference(ContentReferenceTargetType.Media, 812, "items[0].properties.image"),
            new ContentReference(ContentReferenceTargetType.Page, 44, "items[0].properties.cta"),
        ]);
    }

    [Test]
    public void ReferencesNestedOneLevelDeeperAreStillReported()
    {
        var references = _fieldType.ExtractReferences(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "grid", "properties": {
                    "rows": { "type": "blocks", "items": [
                        { "id": "{{SecondId}}", "blockTypeKey": "card", "properties": {
                            "image": { "type": "media", "mediaId": 900 }
                        } }
                    ] }
                } }
            ] }
            """);

        // The gap the S1 spike found: a container that reports only its top level drops every
        // reference beneath it, and the symptom is a page that stops invalidating when the image it
        // shows is replaced.
        references.Should().ContainSingle()
            .Which.Should().Be(new ContentReference(
                ContentReferenceTargetType.Media,
                900,
                "items[0].properties.rows.items[0].properties.image"));
    }

    [Test]
    public void APathReportedByANestedFieldTypeIsKept()
    {
        var fieldType = FieldTypeTestHarness.Blocks(new MediaListFieldType());

        var references = fieldType.ExtractReferences(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "gallery", "properties": {
                    "images": { "type": "mediaList", "items": [ { "mediaId": 1 }, { "mediaId": 2 } ] }
                } }
            ] }
            """);

        references.Select(reference => reference.Path).Should().Equal(
            "items[0].properties.images.items[0]",
            "items[0].properties.images.items[1]");
    }

    [Test]
    public void APropertyWrittenByAFieldTypeThisBuildDoesNotHaveIsSkipped()
    {
        var fieldType = FieldTypeTestHarness.Blocks(new MediaFieldType());

        var references = fieldType.ExtractReferences(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "hero", "properties": {
                    "chart": { "type": "someExtension", "value": 1 },
                    "image": { "type": "media", "mediaId": 812 }
                } }
            ] }
            """);

        // An unknown key is never an exception on the public surface (spec section 15.3), and the
        // rest of the block still has to be reported.
        references.Should().ContainSingle()
            .Which.TargetId.Should().Be(812);
    }

    [Test]
    public void APropertyWithNoTypeDiscriminatorIsSkipped()
    {
        var references = _fieldType.ExtractReferences(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "hero",
                  "properties": { "image": { "mediaId": 812 } } }
            ] }
            """);

        // Inside a container the stored discriminator is the only signal there is: the schema of a
        // block's properties belongs to its block type, and this field type does not have it.
        references.Should().BeEmpty();
    }

    [Test]
    public void TextInsideBlocksIsIndexedInDocumentOrder()
    {
        var text = _fieldType.ExtractSearchText(FieldTypeTestHarness.Element(
            $$"""
            { "type": "blocks", "items": [
                { "id": "{{FirstId}}", "blockTypeKey": "hero", "properties": {
                    "headline": { "type": "plainText", "value": "Ship faster" }
                } },
                { "id": "{{SecondId}}", "blockTypeKey": "cta", "properties": {
                    "link": { "type": "link", "kind": "page", "pageId": 44, "text": "Get started" }
                } }
            ] }
            """));

        // Without this every word in a block zone is invisible to search, which on a block-built
        // site is most of the site.
        text.Should().Be("Ship faster Get started");
    }

    [Test]
    public void AnUnfilledZoneReportsNothing()
    {
        _fieldType.ExtractReferences("""{ "type": "blocks" }""").Should().BeEmpty();
        _fieldType.ExtractSearchText(FieldTypeTestHarness.Element("""{ "type": "blocks" }"""))
            .Should().BeEmpty();
    }

    [Test]
    public void APayloadNestedDeeperThanTheWalkGuardIsTruncatedRatherThanThrowing()
    {
        // Twelve levels: past the walk's guard, and still inside the depth System.Text.Json will
        // parse at all, which caps this well before a stack does.
        var payload = HandEditedNesting(depth: 12);

        var extract = () => _fieldType.ExtractReferences(payload);

        // Not reachable through the editor — validation refuses a second level — but a migrated or
        // hand-edited payload must not be able to overflow the stack on a public request. The
        // reference beyond the guard is dropped, which is the same answer delivery gives to any
        // content it cannot interpret (spec section 15.3).
        extract.Should().NotThrow();
        extract().Should().BeEmpty();
    }

    private static string NestedPayload() =>
        $$"""
        { "type": "blocks", "items": [
            { "id": "{{FirstId}}", "blockTypeKey": "grid", "properties": {
                "rows": { "type": "blocks", "items": [
                    { "id": "{{SecondId}}", "blockTypeKey": "row" }
                ] }
            } }
        ] }
        """;

    private static string HandEditedNesting(int depth)
    {
        var payload = """{ "type": "media", "mediaId": 1 }""";

        for (var level = 0; level < depth; level++)
        {
            payload =
                $$"""
                { "type": "blocks", "items": [
                    { "id": "{{FirstId}}", "blockTypeKey": "nested",
                      "properties": { "inner": {{payload}} } }
                ] }
                """;
        }

        return payload;
    }
}
