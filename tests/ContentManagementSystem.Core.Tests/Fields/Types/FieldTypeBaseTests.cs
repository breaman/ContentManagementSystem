using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// The rules every value field type inherits (task P1-10). Exercised through
/// <see cref="PlainTextFieldType"/>, which adds nothing to them.
/// </summary>
public class FieldTypeBaseTests
{
    private readonly PlainTextFieldType _fieldType = new();

    [Fact]
    public async Task AnAbsentPropertyIsValidWhenNothingIsRequired()
    {
        var result = await _fieldType.ValidateAsync(FieldTypeTestHarness.Absent);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AnExplicitlyClearedPropertyIsValidWhenNothingIsRequired()
    {
        // Null and absent are distinct in storage (spec section 6.2) but neither is filled in, and
        // neither is a shape error.
        var result = await _fieldType.ValidateAsync("null");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AnEmptyRequiredValueSavesAsADraft()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "plainText", "value": "" }""",
            """{ "required": true }""");

        // An editor must be able to save half-finished work (spec section 8.3).
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AnEmptyRequiredValueDoesNotPublish()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "plainText", "value": "" }""",
            """{ "required": true }""",
            ValidationMode.Publish);

        result.Codes().Should().Equal(FieldValidationCodes.Required);
        result.HasErrors.Should().BeTrue();
    }

    [Fact]
    public async Task AnAbsentRequiredPropertyDoesNotPublish()
    {
        var result = await _fieldType.ValidateAsync(
            FieldTypeTestHarness.Absent,
            """{ "required": true }""",
            ValidationMode.Publish);

        result.Codes().Should().Equal(FieldValidationCodes.Required);
    }

    [Fact]
    public async Task WhitespaceDoesNotSatisfyARequiredTextValue()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "plainText", "value": "   " }""",
            """{ "required": true }""",
            ValidationMode.Publish);

        // Otherwise a required heading is satisfied by a space and the page publishes with an empty
        // element.
        result.Codes().Should().Equal(FieldValidationCodes.Required);
    }

    [Fact]
    public async Task AValueStoredByADifferentFieldTypeIsReportedAsAMismatch()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "richText", "value": "hello" }""");

        // Validating it as plain text anyway would report a shape error against the wrong field
        // type and send whoever reads it to the wrong place.
        result.Codes().Should().Equal(FieldValidationCodes.TypeMismatch);
    }

    [Fact]
    public async Task AValueWithNoTypeDiscriminatorIsValidatedAgainstTheSchema()
    {
        // The discriminator is a redundancy in the payload; the schema is what decides which field
        // type validates a property, so a value missing it is checked rather than rejected.
        var result = await _fieldType.ValidateAsync("""{ "value": "hello" }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task APropertyThatIsNotAnObjectIsAShapeError()
    {
        var result = await _fieldType.ValidateAsync("\"hello\"");

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Fact]
    public async Task AnObjectWithNoValueMemberIsTreatedAsUnfilled()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "plainText" }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task SanitizingAValueFieldTypeReturnsItUnchanged()
    {
        var property = FieldTypeTestHarness.Element("""{ "type": "plainText", "value": "a < b" }""");

        var sanitized = await _fieldType.SanitizeAsync(
            property,
            FieldConfiguration.Empty,
            TestContext.Current.CancellationToken);

        // plainText is encoded at render, never stripped at write: an author who types "a < b"
        // keeps it.
        sanitized.GetRawText().Should().Be(property.GetRawText());
    }

    [Fact]
    public void AValueFieldTypeReportsNoReferences()
    {
        var property = FieldTypeTestHarness.Element("""{ "type": "plainText", "value": "hello" }""");

        _fieldType.ExtractReferences(property).Should().BeEmpty();
    }

    [Fact]
    public void ComponentsComeFromTheHostingLayer()
    {
        // Core sits below Rendering and Client in the reference graph, so a built-in field type
        // cannot name its own components (ADR 0014).
        _fieldType.EditorComponent.Should().BeNull();
        _fieldType.RendererComponent.Should().BeNull();
    }

    [Fact]
    public async Task ValidationObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var validate = async () => await _fieldType.ValidateAsync(
            FieldTypeTestHarness.Element("""{ "type": "plainText", "value": "hello" }"""),
            FieldConfiguration.Empty,
            ValidationMode.Draft,
            cancellation.Token);

        await validate.Should().ThrowAsync<OperationCanceledException>();
    }
}
