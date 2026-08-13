using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Shared behaviour of the field types that store a single unmarked string.
/// </summary>
/// <remarks>
/// Configuration keys: <c>minLength</c>, <c>maxLength</c>, <c>pattern</c>, <c>patternMessage</c>.
/// <para>
/// Lengths are counted in UTF-16 code units, matching <c>nvarchar</c> and therefore matching what
/// the database would refuse. Counting text elements instead would read more naturally to an author
/// but would let a value pass validation and then fail to store, which is the worse surprise.
/// </para>
/// </remarks>
public abstract class TextFieldTypeBase : FieldTypeBase
{
    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.Searchable;
    /// <inheritdoc />
    public override FieldConfigurationSchema ConfigurationSchema { get; } = new(
        [
            FieldConfigurationSetting.Integer(
                "minLength",
                "Fewest characters a value may contain.",
                minimum: 0),
            FieldConfigurationSetting.Integer(
                "maxLength",
                "Most characters a value may contain, counted in UTF-16 code units as nvarchar counts them.",
                minimum: 1),
            FieldConfigurationSetting.Text(
                "pattern",
                "Regular expression every value must match.",
                FieldSettingFormat.RegularExpression),
            FieldConfigurationSetting.Text(
                "patternMessage",
                "What to tell an editor when a value does not match the pattern. The default says only that the format is wrong, which a pattern of any complexity makes useless."),
        ],
        [new FieldSettingRange("minLength", "maxLength")]);


    /// <summary>Whether a stored value may contain line breaks.</summary>
    protected abstract bool AllowsLineBreaks { get; }

    /// <inheritdoc />
    /// <remarks>
    /// A string of nothing but whitespace is unfilled. Otherwise an author could satisfy a required
    /// heading with a space, and the page would publish with an empty <c>&lt;h1&gt;</c>.
    /// </remarks>
    protected override bool IsEmpty(JsonElement value) =>
        base.IsEmpty(value) ||
        (value.ValueKind is JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        if (value.ValueKind is not JsonValueKind.String)
        {
            return ValidationResult.Error(
                FieldValidationCodes.Shape,
                "Expected a text value.",
                ValueMember);
        }

        var text = value.GetString()!;
        List<ValidationDiagnostic>? diagnostics = null;

        if (!AllowsLineBreaks && text.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.PlainTextLineBreak,
                "This field holds a single line of text; remove the line breaks.",
                ValidationSeverity.Error,
                ValueMember));
        }

        if (configuration.GetInt32("maxLength") is { } maxLength && text.Length > maxLength)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.MaxLength,
                $"Use at most {maxLength} characters; this is {text.Length}.",
                ValidationSeverity.Error,
                ValueMember));
        }

        if (configuration.GetInt32("minLength") is { } minLength && text.Length < minLength)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.MinLength,
                $"Use at least {minLength} characters; this is {text.Length}.",
                ValidationSeverity.Error,
                ValueMember));
        }

        if (configuration.GetString("pattern") is { Length: > 0 } pattern)
        {
            switch (FieldPatterns.Evaluate(pattern, text))
            {
                case PatternOutcome.NoMatch:
                    Add(ref diagnostics, new ValidationDiagnostic(
                        FieldValidationCodes.Pattern,
                        configuration.GetString("patternMessage") ?? "This value is not in the expected format.",
                        ValidationSeverity.Error,
                        ValueMember));
                    break;

                case PatternOutcome.Unusable:
                    // A warning, not an error: the content is fine and the author cannot fix the
                    // template's pattern. Blocking the save would strand every page on that
                    // template until a developer noticed.
                    Add(ref diagnostics, new ValidationDiagnostic(
                        FieldValidationCodes.PatternInvalid,
                        "This field's validation pattern could not be applied and was skipped.",
                        ValidationSeverity.Warning,
                        ValueMember));
                    break;
            }
        }

        return Result(diagnostics);
    }

    /// <inheritdoc />
    public override string ExtractSearchText(JsonElement value) => SearchText.Collapse(GetStringValue(value));
}
