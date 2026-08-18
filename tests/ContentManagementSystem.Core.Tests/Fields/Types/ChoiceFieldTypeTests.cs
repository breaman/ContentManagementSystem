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

    [Test]
    public async Task AConfiguredOptionIsAccepted()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "choice", "value": "wide" }""", Options);

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task AValueOutsideTheOptionListIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": "enormous" }""",
            Options);

        result.Codes().Should().Equal(FieldValidationCodes.ChoiceUnknownOption);
    }

    [Test]
    public async Task OptionKeysAreMatchedExactly()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "choice", "value": "Wide" }""", Options);

        // Keys are matched byte-for-byte against stored payloads, as they are everywhere else in
        // the content model.
        result.Codes().Should().Equal(FieldValidationCodes.ChoiceUnknownOption);
    }

    [Test]
    public async Task AnyValueIsAcceptedWhenNoOptionsAreConfigured()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "choice", "value": "from-a-lookup" }""");

        // Spec section 7.1 allows the option list to come from a lookup provider rather than static
        // configuration; refusing everything when the static list is empty would rule that out.
        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task AListIsRefusedWhereASingleValueIsConfigured()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "choice", "value": ["wide"] }""", Options);

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Test]
    public async Task ASingleValueIsRefusedWhereAListIsConfigured()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": "wide" }""",
            MultipleOptions);

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Test]
    public async Task SeveralConfiguredOptionsAreAccepted()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": ["wide", "full"] }""",
            MultipleOptions);

        result.IsValid.Should().BeTrue();
    }

    [Test]
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

    [Test]
    public async Task RepeatingASelectionIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": ["wide", "wide"] }""",
            MultipleOptions);

        result.Codes().Should().Equal(FieldValidationCodes.Duplicate);
    }

    [Test]
    public async Task FewerSelectionsThanTheMinimumAreRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": ["wide"] }""",
            """{ "options": ["wide", "narrow", "full"], "multiple": true, "min": 2 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MinItems);
    }

    [Test]
    public async Task MoreSelectionsThanTheMaximumAreRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": ["wide", "narrow", "full"] }""",
            """{ "options": ["wide", "narrow", "full"], "multiple": true, "max": 2 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MaxItems);
    }

    [Test]
    public async Task AnEmptySelectionListSavesAsADraftButDoesNotPublishWhenRequired()
    {
        var draft = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": [] }""",
            """{ "multiple": true }""",
            isRequired: true);
        var publish = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": [] }""",
            """{ "multiple": true }""",
            ValidationMode.Publish,
            isRequired: true);

        draft.IsValid.Should().BeTrue();
        publish.Codes().Should().Equal(FieldValidationCodes.Required);
    }

    [Test]
    public async Task ANumberInTheSelectionListIsAShapeError()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "choice", "value": ["wide", 3] }""",
            MultipleOptions);

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }
}
