using System.Globalization;
using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// A number — counts, prices, column widths (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "number", "value": 12.5 }</c>, as a JSON number rather than a string, so
/// the payload stays comparable and a diff can tell 10 from "10".
/// <para>
/// Configuration keys: <c>required</c>, <c>min</c>, <c>max</c>, <c>step</c>.
/// </para>
/// <para>
/// Values are read as <see cref="decimal"/>. Binary floating point would make a step of <c>0.1</c>
/// reject values that are exactly on it, which is a maddening bug to be told about by an author.
/// </para>
/// </remarks>
public sealed class NumberFieldType : FieldTypeBase
{
    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Number;

    /// <inheritdoc />
    public override string DisplayName => "Number";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.None;

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        if (value.ValueKind is not JsonValueKind.Number || !value.TryGetDecimal(out var number))
        {
            return ValidationResult.Error(
                FieldValidationCodes.Shape,
                "Expected a number.",
                ValueMember);
        }

        List<ValidationDiagnostic>? diagnostics = null;
        var min = configuration.GetDecimal("min");
        var max = configuration.GetDecimal("max");

        if (min is { } lower && number < lower)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.Min,
                $"Enter {Format(lower)} or more.",
                ValidationSeverity.Error,
                ValueMember));
        }

        if (max is { } upper && number > upper)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.Max,
                $"Enter {Format(upper)} or less.",
                ValidationSeverity.Error,
                ValueMember));
        }

        if (configuration.GetDecimal("step") is { } step && step > 0)
        {
            // Steps are counted from min when there is one, matching how an HTML number input
            // behaves. A range of 5–100 stepping by 10 accepts 15, not 10.
            var offset = number - (min ?? 0m);

            if (offset % step != 0m)
            {
                Add(ref diagnostics, new ValidationDiagnostic(
                    FieldValidationCodes.Step,
                    min is { } origin
                        ? $"Enter a value {Format(origin)} plus a multiple of {Format(step)}."
                        : $"Enter a multiple of {Format(step)}.",
                    ValidationSeverity.Error,
                    ValueMember));
            }
        }

        return Result(diagnostics);
    }

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
