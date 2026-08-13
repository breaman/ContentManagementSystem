using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// <c>media</c> and <c>mediaList</c> (task P1-11, spec section 7.1).
/// </summary>
public class MediaFieldTypeTests
{
    private readonly MediaFieldType _media = new();

    private readonly MediaListFieldType _list = new();

    [Fact]
    public async Task APickedItemIsAccepted()
    {
        var result = await _media.ValidateAsync(
            """
            { "type": "media", "mediaId": 812, "altOverride": null,
              "focalPoint": { "x": 0.5, "y": 0.33 },
              "crop": { "x": 0, "y": 0.1, "w": 1, "h": 0.8 } }
            """);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task APropertyWithNoPickedItemIsUnfilledRatherThanMalformed()
    {
        var draft = await _media.ValidateAsync("""{ "type": "media" }""", """{ "required": true }""");
        var publish = await _media.ValidateAsync(
            """{ "type": "media" }""",
            """{ "required": true }""",
            ValidationMode.Publish);

        draft.IsValid.Should().BeTrue();
        publish.Codes().Should().Equal(FieldValidationCodes.Required);
    }

    [Fact]
    public async Task GeometryWithoutAPickedItemIsStillUnfilled()
    {
        var result = await _media.ValidateAsync(
            """{ "type": "media", "mediaId": null, "focalPoint": { "x": 0.5, "y": 0.5 } }""",
            """{ "required": true }""",
            ValidationMode.Publish);

        // Leftover state from a cleared picker. Treating it as filled would publish a property the
        // editor sees as empty.
        result.Codes().Should().Equal(FieldValidationCodes.Required);
    }

    [Fact]
    public async Task AnIdThatCouldNotBelongToAnEntityIsRejected()
    {
        var result = await _media.ValidateAsync("""{ "type": "media", "mediaId": 0 }""");

        result.Codes().Should().Equal(FieldValidationCodes.ReferenceId);
    }

    [Fact]
    public async Task AnIdStoredAsTextIsRejected()
    {
        var result = await _media.ValidateAsync("""{ "type": "media", "mediaId": "812" }""");

        result.Codes().Should().Equal(FieldValidationCodes.ReferenceId);
    }

    [Fact]
    public async Task AFocalPointOutsideTheImageIsRejected()
    {
        var result = await _media.ValidateAsync(
            """{ "type": "media", "mediaId": 812, "focalPoint": { "x": 1.4, "y": 0.5 } }""");

        result.Codes().Should().Equal(FieldValidationCodes.MediaFocalPoint);
        result.Paths().Should().Equal("focalPoint");
    }

    [Fact]
    public async Task ACropRunningOffTheEdgeIsRejected()
    {
        var result = await _media.ValidateAsync(
            """{ "type": "media", "mediaId": 812, "crop": { "x": 0.6, "y": 0, "w": 0.8, "h": 1 } }""");

        // Storable, and there is no sensible thing for the rendition pipeline to do with it later.
        result.Codes().Should().Equal(FieldValidationCodes.MediaCrop);
    }

    [Fact]
    public async Task ACropWithNoAreaIsRejected()
    {
        var result = await _media.ValidateAsync(
            """{ "type": "media", "mediaId": 812, "crop": { "x": 0, "y": 0, "w": 0, "h": 1 } }""");

        result.Codes().Should().Equal(FieldValidationCodes.MediaCrop);
    }

    [Fact]
    public async Task AClearedCropIsNotAMalformedOne()
    {
        var result = await _media.ValidateAsync(
            """{ "type": "media", "mediaId": 812, "crop": null, "focalPoint": null }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AltTextThatIsNotTextIsRejected()
    {
        var result = await _media.ValidateAsync("""{ "type": "media", "mediaId": 812, "altOverride": 7 }""");

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Fact]
    public void APickedItemIsReportedAsAReference()
    {
        var references = _media.ExtractReferences("""{ "type": "media", "mediaId": 812 }""");

        references.Should().ContainSingle()
            .Which.Should().Be(new ContentReference(ContentReferenceTargetType.Media, 812));
    }

    [Fact]
    public void AnEmptyPickerReportsNothing()
    {
        _media.ExtractReferences("""{ "type": "media", "mediaId": null }""").Should().BeEmpty();
    }

    [Fact]
    public async Task AGalleryOfPickedItemsIsAccepted()
    {
        var result = await _list.ValidateAsync(
            """{ "type": "mediaList", "items": [ { "mediaId": 812 }, { "mediaId": 813 } ] }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task TheOffendingGalleryItemIsNamedByItsPosition()
    {
        var result = await _list.ValidateAsync(
            """{ "type": "mediaList", "items": [ { "mediaId": 812 }, { "mediaId": null } ] }""");

        result.Paths().Should().Equal("items[1].mediaId");
    }

    [Fact]
    public async Task AGalleryLongerThanTheMaximumIsRejected()
    {
        var result = await _list.ValidateAsync(
            """{ "type": "mediaList", "items": [ { "mediaId": 1 }, { "mediaId": 2 } ] }""",
            """{ "max": 1 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MaxItems);
    }

    [Fact]
    public async Task AnEmptyGallerySavesAsADraftButDoesNotSatisfyAMinimumOnPublish()
    {
        var draft = await _list.ValidateAsync("""{ "type": "mediaList", "items": [] }""", """{ "min": 1 }""");
        var publish = await _list.ValidateAsync(
            """{ "type": "mediaList", "items": [] }""",
            """{ "min": 1 }""",
            ValidationMode.Publish);

        // "min": 1 is how spec section 7.2 says "this must have at least one"; applying counts only
        // to non-empty lists would let the empty case through the one rule aimed at it.
        draft.IsValid.Should().BeTrue();
        publish.Codes().Should().Equal(FieldValidationCodes.MinItems);
    }

    [Fact]
    public async Task AGalleryThatIsNotAListIsAShapeError()
    {
        var result = await _list.ValidateAsync("""{ "type": "mediaList", "items": { "mediaId": 1 } }""");

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Fact]
    public void EveryGalleryItemIsReportedAtItsOwnPosition()
    {
        var references = _list.ExtractReferences(
            """{ "type": "mediaList", "items": [ { "mediaId": 812 }, { "mediaId": 813 } ] }""");

        references.Should().Equal(
            new ContentReference(ContentReferenceTargetType.Media, 812, "items[0]"),
            new ContentReference(ContentReferenceTargetType.Media, 813, "items[1]"));
    }

    [Fact]
    public void ARepeatedImageIsReportedTwice()
    {
        var references = _list.ExtractReferences(
            """{ "type": "mediaList", "items": [ { "mediaId": 812 }, { "mediaId": 812 } ] }""");

        // Deduplication is the reference indexer's decision, not the field type's: the field type
        // reports what the payload says, at the positions it says it.
        references.Should().HaveCount(2);
    }
}
