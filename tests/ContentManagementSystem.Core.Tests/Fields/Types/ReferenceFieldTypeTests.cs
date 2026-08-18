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

    [Test]
    public async Task ASinglePageIsAccepted()
    {
        var result = await _pages.ValidateAsync("""{ "type": "pageReference", "value": 44 }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task AListIsRefusedWhereASinglePageIsConfigured()
    {
        var result = await _pages.ValidateAsync("""{ "type": "pageReference", "value": [44] }""");

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Test]
    public async Task ASinglePageIsRefusedWhereAListIsConfigured()
    {
        var result = await _pages.ValidateAsync("""{ "type": "pageReference", "value": 44 }""", Multiple);

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Test]
    public async Task AValueThatIsNotAnIdentityIsRejected()
    {
        var result = await _pages.ValidateAsync("""{ "type": "pageReference", "value": "/about" }""");

        // Storing a URL instead of an identity is the mistake decision D6 exists to prevent, and it
        // has to fail here rather than render as a broken link later.
        result.Codes().Should().Equal(FieldValidationCodes.ReferenceId);
    }

    [Test]
    public async Task TheSamePageTwiceInAListIsRejected()
    {
        var result = await _pages.ValidateAsync("""{ "type": "pageReference", "value": [44, 44] }""", Multiple);

        result.Codes().Should().Equal(FieldValidationCodes.Duplicate);
        result.Paths().Should().Equal("value[1]");
    }

    [Test]
    public async Task MorePagesThanTheMaximumAreRejected()
    {
        var result = await _pages.ValidateAsync(
            """{ "type": "pageReference", "value": [1, 2, 3] }""",
            """{ "multiple": true, "max": 2 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MaxItems);
    }

    [Test]
    public async Task AnEmptyListDoesNotSatisfyAMinimumOnPublish()
    {
        var result = await _pages.ValidateAsync(
            """{ "type": "pageReference", "value": [] }""",
            """{ "multiple": true, "min": 2 }""",
            ValidationMode.Publish);

        result.Codes().Should().Equal(FieldValidationCodes.MinItems);
    }

    [Test]
    public void ASinglePageIsReportedAsOneReference()
    {
        var references = _pages.ExtractReferences("""{ "type": "pageReference", "value": 44 }""");

        references.Should().ContainSingle()
            .Which.Should().Be(new ContentReference(ContentReferenceTargetType.Page, 44));
    }

    [Test]
    public void EveryPageInAListIsReportedAtItsOwnPosition()
    {
        var references = _pages.ExtractReferences("""{ "type": "pageReference", "value": [44, 45] }""");

        references.Should().Equal(
            new ContentReference(ContentReferenceTargetType.Page, 44, "value[0]"),
            new ContentReference(ContentReferenceTargetType.Page, 45, "value[1]"));
    }

    [Test]
    public async Task APlacementOfReusableContentIsAccepted()
    {
        var result = await _reusable.ValidateAsync(
            """{ "type": "reusable", "reusableContentId": 3, "pinnedVersionId": null }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task APinnedPlacementIsAccepted()
    {
        var result = await _reusable.ValidateAsync(
            """{ "type": "reusable", "reusableContentId": 3, "pinnedVersionId": 7 }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task APinnedVersionThatIsNotAVersionIsRejected()
    {
        var result = await _reusable.ValidateAsync(
            """{ "type": "reusable", "reusableContentId": 3, "pinnedVersionId": "latest" }""");

        result.Codes().Should().Equal(FieldValidationCodes.ReferenceId);
        result.Paths().Should().Equal("pinnedVersionId");
    }

    [Test]
    public async Task APlacementWithNothingPickedIsUnfilledRatherThanMalformed()
    {
        var draft = await _reusable.ValidateAsync("""{ "type": "reusable" }""", isRequired: true);
        var publish = await _reusable.ValidateAsync(
            """{ "type": "reusable" }""",
            mode: ValidationMode.Publish,
            isRequired: true);

        draft.IsValid.Should().BeTrue();
        publish.Codes().Should().Equal(FieldValidationCodes.Required);
    }

    [Test]
    public void APinnedPlacementStillReportsTheItemItIsPinnedTo()
    {
        var references = _reusable.ExtractReferences(
            """{ "type": "reusable", "reusableContentId": 3, "pinnedVersionId": 7 }""");

        // One row, and its target is the item, pinned or not. Recording the version as the target
        // instead would drop the page out of the where-used list the delete guard runs — while the
        // pin riding along on the row is what lets the publish-impact check split the pages that
        // will change from the ones that will not (spec section 9.4).
        references.Should().ContainSingle()
            .Which.Should().Be(new ContentReference(
                ContentReferenceTargetType.ReusableContent,
                3,
                Path: null,
                IsPinned: true,
                PinnedVersionId: 7));
    }

    [Test]
    public void ALateBoundPlacementReportsNoPin()
    {
        var references = _reusable.ExtractReferences(
            """{ "type": "reusable", "reusableContentId": 3, "pinnedVersionId": null }""");

        // The default, and the one that delivers G4. Asserted beside the pinned case because the
        // two rows differ in nothing an index can see — only in the flag that decides whether a
        // publish of item 3 counts this page as changing.
        references.Should().ContainSingle()
            .Which.Should().Be(new ContentReference(ContentReferenceTargetType.ReusableContent, 3));
    }
}
