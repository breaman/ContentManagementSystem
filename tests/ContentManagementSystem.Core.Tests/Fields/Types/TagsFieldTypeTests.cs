using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// <c>tags</c> (task P1-11, spec section 7.1).
/// </summary>
public class TagsFieldTypeTests
{
    private readonly TagsFieldType _fieldType = new();

    [Fact]
    public async Task AListOfTagsIsAccepted()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "tags", "value": ["release-notes", "v2"] }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AnEmptyTagIsRejected()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "tags", "value": ["ok", "  "] }""");

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
        result.Paths().Should().Equal("value[1]");
    }

    [Fact]
    public async Task ATagThatIsNotTextIsRejected()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "tags", "value": ["ok", 3] }""");

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Fact]
    public async Task TheSameTagInADifferentCaseIsARepeat()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "tags", "value": ["Release Notes", "release notes"] }""");

        // One tag to everyone except a byte comparison; storing both puts two entries in every tag
        // list on the site.
        result.Codes().Should().Equal(FieldValidationCodes.Duplicate);
    }

    [Fact]
    public async Task ATagLongerThanTheConfiguredLimitIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "tags", "value": ["a-very-long-tag"] }""",
            """{ "maxLength": 5 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MaxLength);
    }

    [Fact]
    public async Task MoreTagsThanTheMaximumAreRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "tags", "value": ["a", "b", "c"] }""",
            """{ "max": 2 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MaxItems);
    }

    [Fact]
    public async Task NoTagsSavesAsADraftButDoesNotPublishWhenRequired()
    {
        var draft = await _fieldType.ValidateAsync(
            """{ "type": "tags", "value": [] }""",
            isRequired: true);
        var publish = await _fieldType.ValidateAsync(
            """{ "type": "tags", "value": [] }""",
            mode: ValidationMode.Publish,
            isRequired: true);

        draft.IsValid.Should().BeTrue();
        publish.Codes().Should().Equal(FieldValidationCodes.Required);
    }

    [Fact]
    public void TagsAreIndexedAsWords()
    {
        var text = _fieldType.ExtractSearchText(FieldTypeTestHarness.Element(
            """{ "type": "tags", "value": ["release-notes", "v2"] }"""));

        text.Should().Be("release-notes v2");
    }

    [Fact]
    public void TagsReportNoReferences()
    {
        // A tag names a concept, not an entity: nothing breaks when one falls out of use, and there
        // is no target type for it in the reference model.
        _fieldType.ExtractReferences("""{ "type": "tags", "value": ["v2"] }""").Should().BeEmpty();
        _fieldType.Capabilities.HasFlag(FieldTypeCapabilities.ReferenceBearing).Should().BeFalse();
    }
}
