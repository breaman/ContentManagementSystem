using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// A point in time — an embargo, a countdown target, an event start (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "dateTime", "value": "2026-08-12T09:30:00Z" }</c>. Configuration keys: <c>min</c> and
/// <c>max</c>, both written the same way.
/// <para>
/// The offset is mandatory. <c>2026-08-12T09:30:00</c> names no instant on its own — it means one
/// thing to the browser that submitted it, another to the server that stores it, and a third to the
/// scheduler that acts on it. Rejecting it is the only reading that does not quietly pick one.
/// </para>
/// </remarks>
public sealed class DateTimeFieldType : FieldTypeBase
{
    /// <inheritdoc />
    public override string Key => FieldTypeKeys.DateTime;

    /// <inheritdoc />
    public override string DisplayName => "Date and time";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.None;
    /// <inheritdoc />
    public override FieldConfigurationSchema ConfigurationSchema { get; } = new(
        [
            FieldConfigurationSetting.Text(
                "min",
                "Earliest instant an editor may choose, with an offset.",
                FieldSettingFormat.DateTime),
            FieldConfigurationSetting.Text(
                "max",
                "Latest instant an editor may choose, with an offset.",
                FieldSettingFormat.DateTime),
        ],
        [new FieldSettingRange("min", "max")]);


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
                FieldValidationCodes.DateTimeFormat,
                "Expected a date and time.",
                ValueMember);
        }

        var text = value.GetString()!;

        if (!TryParse(text, out var instant))
        {
            return ValidationResult.Error(
                FieldValidationCodes.DateTimeFormat,
                "Expected a date and time written as YYYY-MM-DDThh:mm:ssZ.",
                ValueMember);
        }

        if (!HasExplicitOffset(text))
        {
            return ValidationResult.Error(
                FieldValidationCodes.DateTimeOffset,
                "Include a time zone offset, such as a trailing Z for UTC.",
                ValueMember);
        }

        List<ValidationDiagnostic>? diagnostics = null;

        if (TryParse(configuration.GetString("min"), out var min) && instant < min)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.Min,
                $"Choose {Format(min)} or later.",
                ValidationSeverity.Error,
                ValueMember));
        }

        if (TryParse(configuration.GetString("max"), out var max) && instant > max)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.Max,
                $"Choose {Format(max)} or earlier.",
                ValidationSeverity.Error,
                ValueMember));
        }

        return Result(diagnostics);
    }

    private static bool TryParse(string? value, out DateTimeOffset instant) =>
        ValueFormats.TryParseInstant(value, out instant);

    private static bool HasExplicitOffset(string value) => ValueFormats.HasExplicitOffset(value);

    private static string Format(DateTimeOffset instant) => ValueFormats.FormatInstant(instant);
}
