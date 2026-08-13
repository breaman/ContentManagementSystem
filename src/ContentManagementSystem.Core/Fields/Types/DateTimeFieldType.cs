using System.Globalization;
using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// A point in time — an embargo, a countdown target, an event start (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "dateTime", "value": "2026-08-12T09:30:00Z" }</c>. Configuration keys:
/// <c>required</c>, <c>min</c>, <c>max</c>, both written the same way.
/// <para>
/// The offset is mandatory. <c>2026-08-12T09:30:00</c> names no instant on its own — it means one
/// thing to the browser that submitted it, another to the server that stores it, and a third to the
/// scheduler that acts on it. Rejecting it is the only reading that does not quietly pick one.
/// </para>
/// </remarks>
public sealed class DateTimeFieldType : FieldTypeBase
{
    private static readonly char[] TimeSeparators = ['T', 't', ' '];

    /// <inheritdoc />
    public override string Key => FieldTypeKeys.DateTime;

    /// <inheritdoc />
    public override string DisplayName => "Date and time";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.None;

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
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out instant);

    /// <summary>
    /// Whether the text carries a time zone designator of its own.
    /// </summary>
    /// <param name="value">The stored text, already known to parse.</param>
    /// <returns><see langword="true"/> when the instant is unambiguous.</returns>
    /// <remarks>
    /// Checked on the text rather than on the parse result, because
    /// <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>
    /// supplies the machine's local offset when the value carries none, leaving nothing in the
    /// parsed value to distinguish an author who wrote <c>+00:00</c> from a server that happens to
    /// run in UTC.
    /// </remarks>
    private static bool HasExplicitOffset(string value)
    {
        var separator = value.IndexOfAny(TimeSeparators);

        if (separator < 0) return false;

        var time = value.AsSpan(separator + 1).TrimEnd();

        // Within the time portion a '+' or '-' can only introduce the offset.
        return time.Length > 0 && (time[^1] is 'Z' or 'z' || time.LastIndexOfAny('+', '-') >= 0);
    }

    private static string Format(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
