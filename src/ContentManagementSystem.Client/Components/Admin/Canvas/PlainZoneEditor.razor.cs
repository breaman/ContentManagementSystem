using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Canvas;

/// <summary>
/// The zone body drawn for a field type that has no editor of its own (task P6-05).
/// </summary>
/// <remarks>
/// A textarea for the field types whose stored value is a single string, and the stored JSON
/// read-only for everything else.
/// <para>
/// It stays after P6-06 to P6-15 shipped their editors, for two reasons. It is
/// <c>IFieldEditorCatalog.FallbackEditor</c> — a deployment can register a field type this build has
/// never heard of, and the honest thing to show for it is what is stored rather than nothing at all.
/// And it is the plain UI R13 names as the fallback if Phase 6 is cut back to its acceptance
/// criteria, which is only a fallback while it still round-trips a value correctly.
/// </para>
/// <para>
/// <strong>Rich text is deliberately not in the editable list any more.</strong> Its stored value
/// carries a format beside its text, and a textarea that wrote the text back without the format
/// would leave the value uninterpretable — the field type treats an absent format as an error rather
/// than as a default. Keeping the format here would mean this class knowing one field type's shape,
/// which is exactly what the editor catalog exists to stop.
/// </para>
/// </remarks>
public partial class PlainZoneEditor : FieldEditorBase
{
    /// <summary>
    /// Field types whose stored value is one plain string this control can safely round-trip.
    /// </summary>
    /// <remarks>
    /// Deliberately short. Inventing a control for a media reference or a block list would mean
    /// inventing a shape for its value, and the first thing the real editor would have to do is
    /// repair what this one wrote.
    /// </remarks>
    private static readonly HashSet<string> TextFieldTypes = new(StringComparer.Ordinal)
    {
        FieldTypeKeys.PlainText,
        FieldTypeKeys.MultilineText,
        FieldTypeKeys.Html,
    };

    /// <summary>Whether the field type's value is one string a textarea can round-trip.</summary>
    private bool IsText => TextFieldTypes.Contains(Field.Slot.FieldTypeKey);

    /// <summary>The authored text, for the editable case.</summary>
    private string Text => StoredValue.ReadText(Value) ?? string.Empty;

    /// <summary>The stored value, indented, for the case nothing can edit.</summary>
    private string Stored => StoredValue.Indent(Value);

    private Task OnChangedAsync(ChangeEventArgs args) =>
        WriteTextAsync(args.Value?.ToString());
}
