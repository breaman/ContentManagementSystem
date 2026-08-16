using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Scalar;

/// <summary>
/// The <c>tags</c> editor — free-form tag text, added one at a time (spec section 7.1).
/// </summary>
/// <remarks>
/// Chips with a remove button each, and a text box that commits on Enter, on a comma, or on the
/// button beside it. Three ways in on purpose: Enter is what a practised author reaches for, a comma
/// is what everybody else types, and the button is what somebody who has been given neither
/// instruction can see.
/// <para>
/// <strong>No autocomplete against the existing vocabulary yet.</strong> That needs the <c>Tag</c>
/// projection and the taxonomy screens, which are P8's; the field type's own documentation says the
/// same. Until then this stores exactly the tag text an author typed, which is what the projection
/// will be built from.
/// </para>
/// <para>
/// A tag already on the page is not added twice. The field type does not forbid duplicates, but a
/// list showing the same chip twice is a defect nobody would attribute to their own typing.
/// </para>
/// </remarks>
public partial class TagsEditor : FieldEditorBase
{
    /// <summary>What is in the entry box but not yet committed.</summary>
    private string Draft { get; set; } = string.Empty;

    /// <summary>The stored tags, in the order they were added.</summary>
    private IReadOnlyList<string> Tags => StoredValue.ReadTextList(Value);

    /// <summary>Fewest tags the slot requires.</summary>
    private int? Min => ConfiguredInt32(FieldSettingNames.Min);

    /// <summary>Most tags the slot allows.</summary>
    private int? Max => ConfiguredInt32(FieldSettingNames.Max);

    /// <summary>Most characters one tag may hold.</summary>
    private int? MaxLength => ConfiguredInt32(FieldSettingNames.MaxLength);

    /// <summary>Whether the slot will take no more tags.</summary>
    private bool IsFull => Max is { } max && Tags.Count >= max;

    private string HintId => $"{Field.ControlId}-hint";

    private string DescribedBy => string.Join(
        ' ',
        new[] { Field.DescribedBy, HintId }.Where(id => !string.IsNullOrEmpty(id)));

    private string InvalidClass => Field.Severity is ZoneSeverity.Error ? "is-invalid" : string.Empty;

    /// <summary>How many tags the slot wants, said in words as well as enforced at publish.</summary>
    private string? CountRule => (Min, Max) switch
    {
        (null, null) => null,
        ({ } min, { } max) => $"Between {min} and {max} of them.",
        ({ } min, null) => $"At least {min}.",
        (null, { } max) => $"At most {max}.",
    };

    /// <summary>Commits the draft on Enter or on a comma.</summary>
    /// <remarks>
    /// A comma is intercepted on key-down so it never reaches the box. Letting it through and
    /// stripping it afterwards leaves the character visible for a frame, which reads as the control
    /// fighting the typing.
    /// </remarks>
    private Task OnKeyDownAsync(KeyboardEventArgs args) =>
        args.Key is "Enter" or "," ? AddAsync(Draft) : Task.CompletedTask;

    /// <summary>Adds a tag, unless it is empty, a duplicate, or one too many.</summary>
    private Task AddAsync(string text)
    {
        var tag = text.Trim().TrimEnd(',').Trim();

        if (tag.Length == 0 || IsFull) return Task.CompletedTask;

        Draft = string.Empty;

        if (Tags.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        return WriteAsync(StoredValue.Write(Value, FieldTypeKey, StoredValue.TextList([.. Tags, tag])));
    }

    /// <summary>Removes a tag, clearing the slot when it was the last one.</summary>
    private Task RemoveAsync(string tag)
    {
        var remaining = Tags.Where(existing => !string.Equals(existing, tag, StringComparison.Ordinal)).ToList();

        return WriteAsync(remaining.Count == 0
            ? string.Empty
            : StoredValue.Write(Value, FieldTypeKey, StoredValue.TextList(remaining)));
    }
}
