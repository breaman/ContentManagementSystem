using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// <c>choice</c> (task P1-10, spec section 7.1).
/// </summary>
public class ChoiceFieldTypeTests
{
    private const string Options = """{ "options": ["wide", "narrow", "full"] }""";

    private const string MultipleOptions =
        """{ "options": ["wide", "narrow", "full"], "multiple": true }""";

    private readonly ChoiceFieldType _fieldType = new();

    [Fact]
    public async Task AConfiguredOptionIsAccepted()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "choice", "value": "wide" }""", Options);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AValueOutsideTheOptionListIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": "enormous" }""",
            Options);

        result.Codes().Should().Equal(FieldValidationCodes.ChoiceUnknownOption);
    }

    [Fact]
    public async Task OptionKeysAreMatchedExactly()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "choice", "value": "Wide" }""", Options);

        // Keys are matched byte-for-byte against stored payloads, as they are everywhere else in
        // the content model.
        result.Codes().Should().Equal(FieldValidationCodes.ChoiceUnknownOption);
    }

    [Fact]
    public async Task AnyValueIsAcceptedWhenNoOptionsAreConfigured()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "choice", "value": "from-a-lookup" }""");

        // Spec section 7.1 allows the option list to come from a lookup provider rather than static
        // configuration; refusing everything when the static list is empty would rule that out.
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AListIsRefusedWhereASingleValueIsConfigured()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "choice", "value": ["wide"] }""", Options);

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Fact]
    public async Task ASingleValueIsRefusedWhereAListIsConfigured()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": "wide" }""",
            MultipleOptions);

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Fact]
    public async Task SeveralConfiguredOptionsAreAccepted()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": ["wide", "full"] }""",
            MultipleOptions);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task TheOffendingItemIsNamedByItsPosition()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": ["wide", "enormous"] }""",
            MultipleOptions);

        // The schema walk prefixes the absolute payload path, so this relative path is what lets an
        // editor be pointed at the second chip rather than at the field.
        result.Diagnostics.Should().ContainSingle()
            .Which.RelativePath.Should().Be("value[1]");
    }

    [Fact]
    public async Task RepeatingASelectionIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": ["wide", "wide"] }""",
            MultipleOptions);

        result.Codes().Should().Equal(FieldValidationCodes.Duplicate);
    }

    [Fact]
    public async Task FewerSelectionsThanTheMinimumAreRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": ["wide"] }""",
            """{ "options": ["wide", "narrow", "full"], "multiple": true, "min": 2 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MinItems);
    }

    [Fact]
    public async Task MoreSelectionsThanTheMaximumAreRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": ["wide", "narrow", "full"] }""",
            """{ "options": ["wide", "narrow", "full"], "multiple": true, "max": 2 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MaxItems);
    }

    [Fact]
    public async Task AnEmptySelectionListSavesAsADraftButDoesNotPublishWhenRequired()
    {
        var draft = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": [] }""",
            """{ "multiple": true, "required": true }""");
        var publish = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": [] }""",
            """{ "multiple": true, "required": true }""",
            ValidationMode.Publish);

        draft.IsValid.Should().BeTrue();
        publish.Codes().Should().Equal(FieldValidationCodes.Required);
    }

    [Fact]
    public async Task ANumberInTheSelectionListIsAShapeError()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": ["wide", 3] }""",
            MultipleOptions);

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }
}
