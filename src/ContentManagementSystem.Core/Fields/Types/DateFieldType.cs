using System.Globalization;
using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// A calendar date with no time of day — a publication date, an event day (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "date", "value": "2026-08-12" }</c>, in the ISO-8601 calendar form and
/// nothing else. Configuration keys: <c>required</c>, <c>min</c>, <c>max</c>, both written the same
/// way.
/// <para>
/// A date is not an instant and is deliberately not stored as one. "The 12th" means the 12th
/// wherever it is read; giving it a time and an offset would move it across a date boundary for
/// readers in other time zones, which is how a "published on" date ends up a day out.
/// </para>
/// </remarks>
public sealed class DateFieldType : FieldTypeBase
{
    /// <summary>The one accepted form. Exact parsing, so <c>08/12/2026</c> is refused rather than guessed at.</summary>
    private const string IsoDateFormat = "yyyy-MM-dd";

    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Date;

    /// <inheritdoc />
    public override string DisplayName => "Date";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.None;

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        if (value.ValueKind is not JsonValueKind.String || !TryParse(value.GetString(), out var date))
        {
            return ValidationResult.Error(
                FieldValidationCodes.DateFormat,
                "Expected a date written as YYYY-MM-DD.",
                ValueMember);
        }

        List<ValidationDiagnostic>? diagnostics = null;

        if (TryParse(configuration.GetString("min"), out var min) && date < min)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.Min,
                $"Choose {Format(min)} or later.",
                ValidationSeverity.Error,
                ValueMember));
        }

        if (TryParse(configuration.GetString("max"), out var max) && date > max)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.Max,
                $"Choose {Format(max)} or earlier.",
                ValidationSeverity.Error,
                ValueMember));
        }

        return Result(diagnostics);
    }

    private static bool TryParse(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, IsoDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string Format(DateOnly date) => date.ToString(IsoDateFormat, CultureInfo.InvariantCulture);
}
