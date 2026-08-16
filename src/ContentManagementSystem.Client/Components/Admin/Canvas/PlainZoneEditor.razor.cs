using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Canvas;

/// <summary>
/// The zone body the canvas draws until a field type has an editor of its own (task P6-05).
/// </summary>
/// <remarks>
/// Exactly what <c>PageEditor</c> drew before the canvas existed, moved into a card: a textarea for
/// the field types whose stored value is one string, the media slot editor for a media reference,
/// and the stored JSON read-only for everything else.
/// <para>
/// It stays after P6-06 to P6-15 have shipped their editors, because it is the fallback R13 names —
/// the plain UI that keeps working when the phase is cut to its acceptance criteria — and because a
/// deployment can register a field type this build has never heard of, which has to render as
/// something an editor can at least read.
/// </para>
/// </remarks>
public partial class PlainZoneEditor : ComponentBase
{
    /// <summary>The zone, and the ids the card wants the control to carry.</summary>
    [Parameter]
    [EditorRequired]
    public ZoneEditorContext Field { get; set; } = default!;

    /// <summary>The zone's value, as the plain forms hold it.</summary>
    [Parameter]
    public string Value { get; set; } = string.Empty;

    /// <summary>Raised with the new value whenever the control changes.</summary>
    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Whether the field type's value is one string a textarea can round-trip.</summary>
    private bool IsText => PlainSlotValues.Editable(Field.Zone.FieldTypeKey);

    private Task OnChangedAsync(ChangeEventArgs args) =>
        OnValueAsync(args.Value?.ToString() ?? string.Empty);

    private Task OnValueAsync(string value) => ValueChanged.InvokeAsync(value);
}
