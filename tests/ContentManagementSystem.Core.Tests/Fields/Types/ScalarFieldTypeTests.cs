using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// <c>number</c>, <c>boolean</c>, <c>color</c>, and <c>json</c> (task P1-10, spec section 7.1).
/// </summary>
public class ScalarFieldTypeTests
{
    private readonly NumberFieldType _number = new();
    private readonly BooleanFieldType _boolean = new();
    private readonly ColorFieldType _color = new();
    private readonly JsonFieldType _json = new();

    [Test]
    public async Task ANumberBelowMinIsRejected()
    {
        var result = await _number.ValidateAsync(
            """{ "type": "number", "value": 2 }""",
            """{ "min": 5 }""");

        result.Codes().Should().Equal(FieldValidationCodes.Min);
    }

    [Test]
    public async Task ANumberAboveMaxIsRejected()
    {
        var result = await _number.ValidateAsync(
            """{ "type": "number", "value": 120 }""",
            """{ "max": 100 }""");

        result.Codes().Should().Equal(FieldValidationCodes.Max);
    }

    [Test]
    public async Task ANumberOnTheBoundaryIsAccepted()
    {
        var result = await _number.ValidateAsync(
            """{ "type": "number", "value": 100 }""",
            """{ "min": 5, "max": 100 }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task AFractionalStepDoesNotSufferFloatingPointDrift()
    {
        var result = await _number.ValidateAsync(
            """{ "type": "number", "value": 0.3 }""",
            """{ "step": 0.1 }""");

        // Read as a double, 0.3 is not a multiple of 0.1 and this rejects a value that is plainly
        // on the step. Decimal is what makes the rule mean what an author reads it to mean.
        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task StepsAreCountedFromMin()
    {
        var offGrid = await _number.ValidateAsync(
            """{ "type": "number", "value": 10 }""",
            """{ "min": 5, "step": 10 }""");
        var onGrid = await _number.ValidateAsync(
            """{ "type": "number", "value": 15 }""",
            """{ "min": 5, "step": 10 }""");

        // Matching how an HTML number input behaves: 5–100 stepping by 10 accepts 15, not 10.
        offGrid.Codes().Should().Equal(FieldValidationCodes.Step);
        onGrid.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task TextStoredWhereANumberBelongsIsAShapeError()
    {
        var result = await _number.ValidateAsync("""{ "type": "number", "value": "12" }""");

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Test]
    public async Task FalseIsAFilledBooleanValue()
    {
        var result = await _boolean.ValidateAsync(
            """{ "type": "boolean", "value": false }""",
            mode: ValidationMode.Publish,
            isRequired: true);

        // Treating false as unfilled would make a required boolean impossible to publish once an
        // author deliberately turned it off.
        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task AStringIsNotABoolean()
    {
        var result = await _boolean.ValidateAsync("""{ "type": "boolean", "value": "true" }""");

        result.Codes().Should().Equal(FieldValidationCodes.Shape);
    }

    [Test]
    [Arguments("#1f6feb")]
    [Arguments("#1F6FEB")]
    [Arguments("#000000")]
    public async Task SixDigitHexColoursAreAccepted(string color)
    {
        var result = await _color.ValidateAsync($$"""{ "type": "color", "value": "{{color}}" }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    [Arguments("#fff")]
    [Arguments("1f6feb")]
    [Arguments("rgb(31, 111, 235)")]
    [Arguments("rebeccapurple")]
    [Arguments("#1f6feb0a")]
    public async Task OtherColourNotationsAreRefused(string color)
    {
        var result = await _color.ValidateAsync($$"""{ "type": "color", "value": "{{color}}" }""");

        // One stored form means a value can be compared, swatched, and written into a stylesheet
        // without a parse step that has its own opinions about what equals what.
        result.Codes().Should().Equal(FieldValidationCodes.ColorFormat);
    }

    [Test]
    public async Task AColourOutsideThePaletteIsRejected()
    {
        var result = await _color.ValidateAsync(
            """{ "type": "color", "value": "#ff00ff" }""",
            """{ "palette": ["#1f6feb", "#0f172a"] }""");

        result.Codes().Should().Equal(FieldValidationCodes.ColorPalette);
    }

    [Test]
    public async Task ThePaletteIsMatchedRegardlessOfCase()
    {
        var result = await _color.ValidateAsync(
            """{ "type": "color", "value": "#1F6FEB" }""",
            """{ "palette": ["#1f6feb"] }""");

        // Which case is stored depends on whether the author used the picker or typed it.
        result.IsValid.Should().BeTrue();
    }

    [Test]
    [Arguments("""{ "type": "json", "value": { "rows": [1, 2, 3] } }""")]
    [Arguments("""{ "type": "json", "value": [] }""")]
    [Arguments("""{ "type": "json", "value": 42 }""")]
    [Arguments("""{ "type": "json", "value": "text" }""")]
    public async Task AnyJsonIsAcceptedByTheEscapeHatch(string property)
    {
        var result = await _json.ValidateAsync(property);

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task AnEmptyObjectIsAFilledJsonValue()
    {
        var result = await _json.ValidateAsync(
            """{ "type": "json", "value": {} }""",
            mode: ValidationMode.Publish,
            isRequired: true);

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task JsonLargerThanMaxBytesIsRejected()
    {
        var result = await _json.ValidateAsync(
            """{ "type": "json", "value": { "rows": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10] } }""",
            """{ "maxBytes": 16 }""");

        result.Codes().Should().Equal(FieldValidationCodes.JsonMaxBytes);
    }

    [Test]
    public void JsonIsRestrictedToDevelopersAndContributesNothingToSearch()
    {
        _json.Capabilities.Should().Be(FieldTypeCapabilities.DeveloperOnly);
        _json.ExtractSearchText(
            FieldTypeTestHarness.Element("""{ "type": "json", "value": { "note": "hidden" } }"""))
            .Should().BeEmpty();
    }
}
