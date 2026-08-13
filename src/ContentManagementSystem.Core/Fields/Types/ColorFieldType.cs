using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// A colour, optionally constrained to the design system's palette (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "color", "value": "#1f6feb" }</c>. Configuration keys: <c>required</c> and
/// <c>palette</c>, a list of the hex values a property is allowed to hold.
/// <para>
/// One form only — six hex digits behind a hash. Named colours, <c>rgb()</c>, and the three-digit
/// shorthand are all refused, so that a stored value can be compared, swatched, and emitted into a
/// stylesheet without a parse step that has its own opinions about what is equal to what.
/// </para>
/// <para>
/// A configured palette is a constraint on the content, not styling: it is what stops a brand
/// refresh from having to hunt down one-off colours typed into pages over two years.
/// </para>
/// </remarks>
public sealed class ColorFieldType : FieldTypeBase
{
    /// <summary><c>#RRGGBB</c> — a hash and six hex digits.</summary>
    private const int HexLength = 7;

    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Color;

    /// <inheritdoc />
    public override string DisplayName => "Colour";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.None;

    /// <inheritdoc />
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
        if (value.ValueKind is not JsonValueKind.String || !IsHexColor(value.GetString()))
        {
            return ValidationResult.Error(
                FieldValidationCodes.ColorFormat,
                "Expected a colour written as #RRGGBB.",
                ValueMember);
        }

        var palette = configuration.GetStringArray("palette");

        if (palette.Length == 0) return ValidationResult.Success;

        var color = value.GetString()!;

        // Case-insensitive: #1F6FEB and #1f6feb are the same colour, and which one is stored depends
        // on whether the author used the picker or typed it.
        foreach (var allowed in palette)
        {
            if (string.Equals(allowed, color, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Success;
            }
        }

        return ValidationResult.Error(
            FieldValidationCodes.ColorPalette,
            $"'{color}' is not in this field's colour palette.",
            ValueMember);
    }

    private static bool IsHexColor(string? value)
    {
        if (value is not { Length: HexLength } || value[0] != '#') return false;

        for (var index = 1; index < HexLength; index++)
        {
            if (!char.IsAsciiHexDigit(value[index])) return false;
        }

        return true;
    }
}
