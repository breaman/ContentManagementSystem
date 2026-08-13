using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// <c>plainText</c> and <c>multilineText</c> (task P1-10, spec section 7.1).
/// </summary>
public class TextFieldTypeTests
{
    private readonly PlainTextFieldType _plainText = new();
    private readonly MultilineTextFieldType _multilineText = new();

    [Fact]
    public async Task TextLongerThanMaxLengthIsRejected()
    {
        var result = await _plainText.ValidateAsync(
            """{ "type": "plainText", "value": "abcdef" }""",
            """{ "maxLength": 5 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MaxLength);
    }

    [Fact]
    public async Task TextExactlyAtMaxLengthIsAccepted()
    {
        var result = await _plainText.ValidateAsync(
            """{ "type": "plainText", "value": "abcde" }""",
            """{ "maxLength": 5 }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task TextShorterThanMinLengthIsRejected()
    {
        var result = await _plainText.ValidateAsync(
            """{ "type": "plainText", "value": "ab" }""",
            """{ "minLength": 3 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MinLength);
    }

    [Fact]
    public async Task EveryBrokenRuleIsReported()
    {
        var result = await _plainText.ValidateAsync(
            """{ "type": "plainText", "value": "one\ntwo" }""",
            """{ "maxLength": 3, "pattern": "^[a-z]+$" }""");

        // An editor fixing one problem at a time, told about the next one only after saving again,
        // is the experience this avoids.
        result.Codes().Should().BeEquivalentTo(
            FieldValidationCodes.PlainTextLineBreak,
            FieldValidationCodes.MaxLength,
            FieldValidationCodes.Pattern);
    }

    [Fact]
    public async Task PlainTextRefusesLineBreaks()
    {
        var result = await _plainText.ValidateAsync("""{ "type": "plainText", "value": "one\ntwo" }""");

        result.Codes().Should().Equal(FieldValidationCodes.PlainTextLineBreak);
    }

    [Fact]
    public async Task MultilineTextKeepsLineBreaks()
    {
        var result = await _multilineText.ValidateAsync(
            """{ "type": "multilineText", "value": "one\r\ntwo" }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AValueMatchingThePatternIsAccepted()
    {
        var result = await _plainText.ValidateAsync(
            """{ "type": "plainText", "value": "AB-1234" }""",
            """{ "pattern": "^[A-Z]{2}-[0-9]{4}$" }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AValueMissingThePatternCanCarryTheConfiguredMessage()
    {
        var result = await _plainText.ValidateAsync(
            """{ "type": "plainText", "value": "nope" }""",
            """{ "pattern": "^[A-Z]{2}-[0-9]{4}$", "patternMessage": "Use a product code such as AB-1234." }""");

        result.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().Be("Use a product code such as AB-1234.");
    }

    [Fact]
    public async Task AnUnusablePatternWarnsAndDoesNotBlockTheSave()
    {
        var result = await _plainText.ValidateAsync(
            """{ "type": "plainText", "value": "anything" }""",
            """{ "pattern": "([unclosed" }""");

        // The content is fine and the author cannot fix the template's pattern. Blocking here would
        // strand every page on that template until a developer noticed.
        result.Codes().Should().Equal(FieldValidationCodes.PatternInvalid);
        result.HasErrors.Should().BeFalse();
    }

    [Fact]
    public async Task ANumberStoredWhereTextBelongsIsAShapeError()
    {
        var result = await _plainText.ValidateAsync("""{ "type": "plainText", "value": 42 }""");

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Fact]
    public void SearchTextIsTheStoredValueWithWhitespaceCollapsed()
    {
        var property = FieldTypeTestHarness.Element(
            """{ "type": "multilineText", "value": "  Ship\n\n  faster  " }""");

        _multilineText.ExtractSearchText(property).Should().Be("Ship faster");
    }

    [Fact]
    public void SearchTextIsEmptyForAnUnfilledValue()
    {
        var property = FieldTypeTestHarness.Element("""{ "type": "plainText", "value": null }""");

        _plainText.ExtractSearchText(property).Should().BeEmpty();
    }

    [Fact]
    public void TextFieldTypesAreSearchable()
    {
        _plainText.Capabilities.Should().Be(FieldTypeCapabilities.Searchable);
        _multilineText.Capabilities.Should().Be(FieldTypeCapabilities.Searchable);
    }
}
