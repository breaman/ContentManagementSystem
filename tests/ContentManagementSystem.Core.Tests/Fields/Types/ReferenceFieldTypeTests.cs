using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// <c>pageReference</c> and <c>reusable</c> (task P1-11, spec sections 7.1 and 9).
/// </summary>
public class ReferenceFieldTypeTests
{
    private const string Multiple = """{ "multiple": true }""";

    private readonly PageReferenceFieldType _pages = new();

    private readonly ReusableFieldType _reusable = new();

    [Fact]
    public async Task ASinglePageIsAccepted()
    {
        var result = await _pages.ValidateAsync("""{ "type": "pageReference", "value": 44 }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AListIsRefusedWhereASinglePageIsConfigured()
    {
        var result = await _pages.ValidateAsync("""{ "type": "pageReference", "value": [44] }""");

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Fact]
    public async Task ASinglePageIsRefusedWhereAListIsConfigured()
    {
        var result = await _pages.ValidateAsync("""{ "type": "pageReference", "value": 44 }""", Multiple);

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Fact]
    public async Task AValueThatIsNotAnIdentityIsRejected()
    {
        var result = await _pages.ValidateAsync("""{ "type": "pageReference", "value": "/about" }""");

        // Storing a URL instead of an identity is the mistake decision D6 exists to prevent, and it
        // has to fail here rather than render as a broken link later.
        result.Codes().Should().Equal(FieldValidationCodes.ReferenceId);
    }

    [Fact]
    public async Task TheSamePageTwiceInAListIsRejected()
    {
        var result = await _pages.ValidateAsync("""{ "type": "pageReference", "value": [44, 44] }""", Multiple);

        result.Codes().Should().Equal(FieldValidationCodes.Duplicate);
        result.Paths().Should().Equal("value[1]");
    }

    [Fact]
    public async Task MorePagesThanTheMaximumAreRejected()
    {
        var result = await _pages.ValidateAsync(
            """{ "type": "pageReference", "value": [1, 2, 3] }""",
            """{ "multiple": true, "max": 2 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MaxItems);
    }

    [Fact]
    public async Task AnEmptyListDoesNotSatisfyAMinimumOnPublish()
    {
        var result = await _pages.ValidateAsync(
            """{ "type": "pageReference", "value": [] }""",
            """{ "multiple": true, "min": 2 }""",
            ValidationMode.Publish);

        result.Codes().Should().Equal(FieldValidationCodes.MinItems);
    }

    [Fact]
    public void ASinglePageIsReportedAsOneReference()
    {
        var references = _pages.ExtractReferences("""{ "type": "pageReference", "value": 44 }""");

        references.Should().ContainSingle()
            .Which.Should().Be(new ContentReference(ContentReferenceTargetType.Page, 44));
    }

    [Fact]
    public void EveryPageInAListIsReportedAtItsOwnPosition()
    {
        var references = _pages.ExtractReferences("""{ "type": "pageReference", "value": [44, 45] }""");

        references.Should().Equal(
            new ContentReference(ContentReferenceTargetType.Page, 44, "value[0]"),
            new ContentReference(ContentReferenceTargetType.Page, 45, "value[1]"));
    }

    [Fact]
    public async Task APlacementOfReusableContentIsAccepted()
    {
        var result = await _reusable.ValidateAsync(
            """{ "type": "reusable", "reusableContentId": 3, "pinnedVersionId": null }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task APinnedPlacementIsAccepted()
    {
        var result = await _reusable.ValidateAsync(
            """{ "type": "reusable", "reusableContentId": 3, "pinnedVersionId": 7 }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task APinnedVersionThatIsNotAVersionIsRejected()
    {
        var result = await _reusable.ValidateAsync(
            """{ "type": "reusable", "reusableContentId": 3, "pinnedVersionId": "latest" }""");

        result.Codes().Should().Equal(FieldValidationCodes.ReferenceId);
        result.Paths().Should().Equal("pinnedVersionId");
    }

    [Fact]
    public async Task APlacementWithNothingPickedIsUnfilledRatherThanMalformed()
    {
        var draft = await _reusable.ValidateAsync("""{ "type": "reusable" }""", """{ "required": true }""");
        var publish = await _reusable.ValidateAsync(
            """{ "type": "reusable" }""",
            """{ "required": true }""",
            ValidationMode.Publish);

        draft.IsValid.Should().BeTrue();
        publish.Codes().Should().Equal(FieldValidationCodes.Required);
    }

    [Fact]
    public void APinnedPlacementStillReportsTheItemItIsPinnedTo()
    {
        var references = _reusable.ExtractReferences(
            """{ "type": "reusable", "reusableContentId": 3, "pinnedVersionId": 7 }""");

        // One row, for the item, pinned or not. Recording the version instead would drop the page
        // out of the where-used list the delete guard runs (spec section 9.4).
        references.Should().ContainSingle()
            .Which.Should().Be(new ContentReference(ContentReferenceTargetType.ReusableContent, 3));
    }
}
