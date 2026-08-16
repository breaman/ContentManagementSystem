using System.Globalization;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Scalar;

/// <summary>
/// The <c>number</c> editor (spec section 7.1).
/// </summary>
/// <remarks>
/// Held as <see cref="decimal"/> all the way through, matching the field type. Binary floating point
/// would make a configured step of <c>0.1</c> reject values that are exactly on it, which is a
/// maddening thing to be told by a publish check.
/// <para>
/// Every number crossing the boundary is formatted with the invariant culture, because it is going
/// into a JSON document rather than in front of a reader. What the author types is parsed by the
/// browser's own number input, which handles their locale's decimal separator for them.
/// </para>
/// </remarks>
public partial class NumberEditor : FieldEditorBase
{
    /// <summary>The stored number, or null when nothing is authored.</summary>
    private decimal? Number => StoredValue.ReadNumber(Value);

    /// <summary>The smallest value the slot allows.</summary>
    private decimal? Min => ConfiguredDecimal(FieldSettingNames.Min);

    /// <summary>The largest value the slot allows.</summary>
    private decimal? Max => ConfiguredDecimal(FieldSettingNames.Max);

    /// <summary>The increment the slot requires values to be a multiple of.</summary>
    private decimal? Step => ConfiguredDecimal(FieldSettingNames.Step);

    /// <summary>
    /// The <c>step</c> attribute, defaulting to "any".
    /// </summary>
    /// <remarks>
    /// Not <c>1</c>, which is the HTML default and would have the browser refuse a decimal the field
    /// type is perfectly happy to store. A property that wants whole numbers says so with a
    /// configured step of 1.
    /// </remarks>
    private string StepAttribute => Step is { } step ? Text(step)! : "any";

    private string BoundsId => $"{Field.ControlId}-bounds";

    private string DescribedBy => string.Join(
        ' ',
        new[] { Field.DescribedBy, Bounds is { Length: > 0 } ? BoundsId : null }
            .Where(id => !string.IsNullOrEmpty(id)));

    private string InvalidClass => Field.Severity is ZoneSeverity.Error ? "is-invalid" : string.Empty;

    /// <summary>
    /// What the slot allows, said in words as well as in attributes.
    /// </summary>
    /// <remarks>
    /// The <c>min</c> and <c>max</c> attributes are enforced by the browser but are invisible until
    /// they fire, and what they say when they fire is a tooltip that disappears. A line of help text
    /// is what an author can read before typing.
    /// </remarks>
    private string? Bounds => (Min, Max, Step) switch
    {
        (null, null, null) => null,
        ({ } min, { } max, var step) => $"Between {Text(min)} and {Text(max)}{StepSuffix(step)}.",
        ({ } min, null, var step) => $"{Text(min)} or more{StepSuffix(step)}.",
        (null, { } max, var step) => $"{Text(max)} or less{StepSuffix(step)}.",
        (null, null, { } step) => $"In steps of {Text(step)}.",
    };

    private static string StepSuffix(decimal? step) =>
        step is { } value ? $", in steps of {Text(value)}" : string.Empty;

    /// <summary>Formats a number for an attribute or for help text, invariantly.</summary>
    private static string? Text(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Stores what was typed, removing the slot when the box is emptied.
    /// </summary>
    /// <remarks>
    /// Unparseable input clears rather than throwing: a number box can hold text a browser refuses
    /// to parse, an empty box being the common one mid-edit, and an author who deleted a value meant
    /// to remove it.
    /// </remarks>
    private Task OnChangedAsync(ChangeEventArgs args)
    {
        var typed = args.Value?.ToString();

        if (string.IsNullOrWhiteSpace(typed) ||
            !decimal.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return WriteAsync(string.Empty);
        }

        return WriteAsync(StoredValue.Write(Value, FieldTypeKey, JsonValue.Create(number)));
    }
}
