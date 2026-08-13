using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Base class for a field type whose payload member holds an ordered list — <c>blocks</c>,
/// <c>mediaList</c>, <c>tags</c>.
/// </summary>
/// <remarks>
/// Three rules are the same whatever the items are: the member has to be an array, an empty list is
/// unfilled, and <c>min</c> / <c>max</c> bound the count. The subclass is left with the only part
/// that differs, which is what an item has to look like.
/// <para>
/// The <c>min</c> rule is deliberately also applied to the empty list. Counting only non-empty lists
/// would make <c>"min": 1</c> — the way spec section 7.2 says "this zone must have at least one
/// block" — the single count rule an empty zone slips past, and it would say something different
/// from <c>required</c> only by accident.
/// </para>
/// </remarks>
public abstract class ListFieldTypeBase : FieldTypeBase
{
    /// <summary>What the items are, for the messages an editor reads: "blocks", "images", "tags".</summary>
    protected abstract string ItemNoun { get; }

    /// <inheritdoc />
    protected override bool IsEmpty(JsonElement value) =>
        base.IsEmpty(value) ||
        (value.ValueKind is JsonValueKind.Array && value.GetArrayLength() == 0);

    /// <inheritdoc />
    protected override ValidationResult ValidateEmpty(FieldConfiguration configuration, ValidationMode mode)
    {
        if (mode is ValidationMode.Publish && configuration.GetInt32("min") is { } min && min > 0)
        {
            return ValidationResult.Error(
                FieldValidationCodes.MinItems,
                $"Add at least {min} {ItemNoun}.",
                PayloadMember);
        }

        return base.ValidateEmpty(configuration, mode);
    }

    /// <inheritdoc />
    protected sealed override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        if (value.ValueKind is not JsonValueKind.Array)
        {
            return ValidationResult.Error(
                FieldValidationCodes.Shape,
                $"Expected a list of {ItemNoun}.",
                PayloadMember);
        }

        List<ValidationDiagnostic>? diagnostics = null;

        ValidateItems(value, configuration, mode, ref diagnostics);
        ValidateCount(value.GetArrayLength(), configuration, ref diagnostics);

        return Result(diagnostics);
    }

    /// <summary>
    /// Checks the items of a non-empty list.
    /// </summary>
    /// <param name="items">The list, guaranteed to be a non-empty array.</param>
    /// <param name="configuration">Parsed configuration for the property being validated.</param>
    /// <param name="mode">Whether a draft is being saved or content is being published.</param>
    /// <param name="diagnostics">Collected diagnostics, allocated on first use.</param>
    /// <remarks>
    /// Handed the whole list rather than one item at a time, because the rules that matter most here
    /// are the ones that span items: a repeated block GUID, a repeated tag.
    /// </remarks>
    protected abstract void ValidateItems(
        JsonElement items,
        FieldConfiguration configuration,
        ValidationMode mode,
        ref List<ValidationDiagnostic>? diagnostics);

    /// <summary>Builds the path of one item, for a diagnostic or an extracted reference.</summary>
    /// <param name="index">Zero-based position in the list.</param>
    /// <returns>A path relative to the value, such as <c>items[2]</c>.</returns>
    protected string ItemPath(int index) => RelativePaths.Index(PayloadMember, index);

    private void ValidateCount(
        int count,
        FieldConfiguration configuration,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        if (configuration.GetInt32("min") is { } min && count < min)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.MinItems,
                $"Add at least {min} {ItemNoun}; there are {count}.",
                ValidationSeverity.Error,
                PayloadMember));
        }

        if (configuration.GetInt32("max") is { } max && count > max)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.MaxItems,
                $"Use at most {max} {ItemNoun}; there are {count}.",
                ValidationSeverity.Error,
                PayloadMember));
        }
    }
}
