namespace ContentManagementSystem.Shared.Contracts.Fields;

/// <summary>
/// Stable diagnostic codes emitted by the built-in field types.
/// </summary>
/// <remarks>
/// A <see cref="ValidationDiagnostic.Code"/> is the part of a diagnostic clients are allowed to
/// switch on — the backoffice maps codes to field-level messages, and the API contract surfaces them
/// in its <c>errors</c> array (spec section 22.2). Messages may be reworded freely; codes may not
/// change once shipped.
/// <para>
/// Codes are namespaced <c>field.*</c> for rules any field type can raise and
/// <c>field.&lt;key&gt;.*</c> for rules specific to one.
/// </para>
/// </remarks>
public static class FieldValidationCodes
{
    /// <summary>A value marked <c>required</c> was empty when publishing.</summary>
    public const string Required = "field.required";

    /// <summary>The stored value is not the JSON shape the field type writes.</summary>
    public const string Shape = "field.shape";

    /// <summary>The payload's <c>type</c> discriminator names a different field type than the schema does.</summary>
    public const string TypeMismatch = "field.typeMismatch";

    /// <summary>Text is longer than the configured <c>maxLength</c>.</summary>
    public const string MaxLength = "field.maxLength";

    /// <summary>Text is shorter than the configured <c>minLength</c>.</summary>
    public const string MinLength = "field.minLength";

    /// <summary>Text does not match the configured <c>pattern</c>.</summary>
    public const string Pattern = "field.pattern";

    /// <summary>The configured <c>pattern</c> is not a usable regular expression.</summary>
    public const string PatternInvalid = "field.pattern.invalid";

    /// <summary>The value is below the configured <c>min</c>.</summary>
    public const string Min = "field.min";

    /// <summary>The value is above the configured <c>max</c>.</summary>
    public const string Max = "field.max";

    /// <summary>The value is not a multiple of the configured <c>step</c>.</summary>
    public const string Step = "field.step";

    /// <summary>Fewer items than the configured <c>min</c> count.</summary>
    public const string MinItems = "field.minItems";

    /// <summary>More items than the configured <c>max</c> count.</summary>
    public const string MaxItems = "field.maxItems";

    /// <summary>The same item appears more than once in a list that does not allow repeats.</summary>
    public const string Duplicate = "field.duplicate";

    /// <summary>A <c>plainText</c> value contains a line break.</summary>
    public const string PlainTextLineBreak = "field.plainText.lineBreak";

    /// <summary>A <c>richText</c> value declares no format, or one that is neither markdown nor HTML.</summary>
    public const string RichTextFormat = "field.richText.format";

    /// <summary>A <c>richText</c> value names a sanitization profile that does not exist.</summary>
    public const string RichTextProfile = "field.richText.profile";

    /// <summary>A <c>date</c> value is not an ISO-8601 calendar date.</summary>
    public const string DateFormat = "field.date.format";

    /// <summary>A <c>dateTime</c> value is not an ISO-8601 instant.</summary>
    public const string DateTimeFormat = "field.dateTime.format";

    /// <summary>A <c>dateTime</c> value carries no UTC offset, so the instant it names is ambiguous.</summary>
    public const string DateTimeOffset = "field.dateTime.offset";

    /// <summary>A <c>choice</c> value is not among the configured options.</summary>
    public const string ChoiceUnknownOption = "field.choice.unknownOption";

    /// <summary>A <c>color</c> value is not an <c>#RRGGBB</c> triplet.</summary>
    public const string ColorFormat = "field.color.format";

    /// <summary>A <c>color</c> value is not in the configured palette.</summary>
    public const string ColorPalette = "field.color.palette";

    /// <summary>A <c>json</c> value serializes to more bytes than the configured <c>maxBytes</c>.</summary>
    public const string JsonMaxBytes = "field.json.maxBytes";
}
