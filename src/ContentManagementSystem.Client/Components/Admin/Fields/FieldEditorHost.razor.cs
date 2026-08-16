using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields;

/// <summary>
/// Draws whichever editor the catalog maps a field type to (ADR-0014, tasks P6-06 to P6-15).
/// </summary>
/// <remarks>
/// The one place a field type key becomes a component. Both surfaces that host field values go
/// through it — the editing canvas for a zone, the block list for a block's property — so the answer
/// to "what fills a <c>richText</c>" cannot differ between them, and a field type registered by a
/// deployment gets its editor in both places from a single registration.
/// <para>
/// A field type with no editor falls back rather than rendering nothing. The stored value shown
/// read-only is not much, but it is enough for an author to see what is there and for a developer to
/// recognise the omission — and the alternative, an empty card, is indistinguishable from an empty
/// value.
/// </para>
/// </remarks>
public partial class FieldEditorHost : ComponentBase
{
    /// <summary>Maps the field type key to the component that fills it in.</summary>
    [Inject]
    private IFieldEditorCatalog Catalog { get; set; } = default!;

    /// <summary>The slot, and the ids the frame wants the control to carry.</summary>
    [Parameter]
    [EditorRequired]
    public FieldEditorContext Field { get; set; } = default!;

    /// <summary>The stored value as JSON text, empty when nothing has been authored.</summary>
    [Parameter]
    public string Value { get; set; } = string.Empty;

    /// <summary>Raised with the rewritten JSON whenever the value changes.</summary>
    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    /// <summary>The component drawing this field type.</summary>
    private Type Editor => Catalog.EditorFor(Field.Slot.FieldTypeKey);

    /// <summary>
    /// The three parameters of <see cref="FieldEditorBase"/>, by name.
    /// </summary>
    /// <remarks>
    /// Rebuilt on every render rather than cached. <c>DynamicComponent</c> compares the dictionary by
    /// reference to decide whether the child's parameters changed, so a cached instance mutated in
    /// place would leave the editor showing the previous value after an external write — a reload,
    /// or a conflict resolution taking theirs (P6-19).
    /// </remarks>
    private Dictionary<string, object?> Parameters => new(StringComparer.Ordinal)
    {
        [nameof(FieldEditorBase.Field)] = Field,
        [nameof(FieldEditorBase.Value)] = Value,
        [nameof(FieldEditorBase.ValueChanged)] = ValueChanged,
    };
}
