using System.Globalization;
using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// One or several values picked from a configured list — a layout variant, a set of categories
/// (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "choice", "value": "wide" }</c>, or with an array under the same member
/// when the property is configured for multiple selection. One member name either way: a renderer
/// that has to look in two places for the same thing eventually looks in only one.
/// <para>
/// Configuration keys: <c>required</c>, <c>options</c> (the allowed values), <c>multiple</c>,
/// and <c>min</c> / <c>max</c> counts when <c>multiple</c> is set.
/// </para>
/// <para>
/// Stored values are option <em>keys</em>, never the labels shown to an author. Labels are display
/// configuration and get reworded; a payload storing them would silently stop matching the option
/// list the first time someone fixed a typo.
/// </para>
/// </remarks>
public sealed class ChoiceFieldType : FieldTypeBase
{
    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Choice;

    /// <inheritdoc />
    public override string DisplayName => "Choice";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.None;

    /// <inheritdoc />
    protected override bool IsEmpty(JsonElement value) =>
        base.IsEmpty(value) ||
        value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() == 0,
            _ => false,
        };

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        var options = configuration.GetStringArray("options");

        return configuration.GetBoolean("multiple")
            ? ValidateMany(value, options, configuration)
            : ValidateOne(value, options);
    }

    private static ValidationResult ValidateOne(JsonElement value, string[] options)
    {
        if (value.ValueKind is not JsonValueKind.String)
        {
            return ValidationResult.Error(
                FieldValidationCodes.Shape,
                "Expected a single selected option.",
                ValueMember);
        }

        return IsKnown(value.GetString(), options)
            ? ValidationResult.Success
            : UnknownOption(value.GetString(), ValueMember);
    }

    private static ValidationResult ValidateMany(
        JsonElement value,
        string[] options,
        FieldConfiguration configuration)
    {
        if (value.ValueKind is not JsonValueKind.Array)
        {
            return ValidationResult.Error(
                FieldValidationCodes.Shape,
                "Expected a list of selected options.",
                ValueMember);
        }

        List<ValidationDiagnostic>? diagnostics = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        foreach (var item in value.EnumerateArray())
        {
            var path = string.Create(CultureInfo.InvariantCulture, $"{ValueMember}[{index}]");

            if (item.ValueKind is not JsonValueKind.String)
            {
                Add(ref diagnostics, new ValidationDiagnostic(
                    FieldValidationCodes.Shape,
                    "Expected an option key.",
                    ValidationSeverity.Error,
                    path));
            }
            else
            {
                var selected = item.GetString()!;

                if (!IsKnown(selected, options))
                {
                    Add(ref diagnostics, UnknownDiagnostic(selected, path));
                }

                if (!seen.Add(selected))
                {
                    Add(ref diagnostics, new ValidationDiagnostic(
                        FieldValidationCodes.Duplicate,
                        $"'{selected}' is selected more than once.",
                        ValidationSeverity.Error,
                        path));
                }
            }

            index++;
        }

        if (configuration.GetInt32("min") is { } min && index < min)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.MinItems,
                $"Select at least {min} options.",
                ValidationSeverity.Error,
                ValueMember));
        }

        if (configuration.GetInt32("max") is { } max && index > max)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.MaxItems,
                $"Select at most {max} options.",
                ValidationSeverity.Error,
                ValueMember));
        }

        return Result(diagnostics);
    }

    /// <summary>
    /// Whether a stored key is one the property offers.
    /// </summary>
    /// <param name="selected">The stored option key.</param>
    /// <param name="options">Configured option keys.</param>
    /// <returns><see langword="true"/> when the value is acceptable.</returns>
    /// <remarks>
    /// A property with no configured options accepts anything: spec section 7.1 allows the option
    /// list to come from a lookup provider instead of static configuration, and refusing every value
    /// when the static list is empty would make that unimplementable.
    /// </remarks>
    private static bool IsKnown(string? selected, string[] options) =>
        options.Length == 0 || Array.IndexOf(options, selected) >= 0;

    private static ValidationResult UnknownOption(string? selected, string path) =>
        ValidationResult.From(UnknownDiagnostic(selected, path));

    private static ValidationDiagnostic UnknownDiagnostic(string? selected, string path) =>
        new(
            FieldValidationCodes.ChoiceUnknownOption,
            $"'{selected}' is not one of the available options.",
            ValidationSeverity.Error,
            path);
}
