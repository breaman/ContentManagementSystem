using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.BlockList;

/// <summary>
/// One block in a block list: its summary, its controls, and its properties
/// (tasks P6-06 and P6-07, spec section 14.3).
/// </summary>
/// <remarks>
/// <strong>Every action is a button; drag is added on top.</strong> Move up, move down, duplicate,
/// delete, and collapse are all reachable by Tab and pressed with Enter or Space, which is
/// acceptance criterion P6 #4 in full. The grip is <c>aria-hidden</c> and takes no Tab stop
/// precisely because it can do nothing the buttons cannot — a handle a keyboard user could focus and
/// not use would be worse than no handle.
/// <para>
/// The properties are drawn through <see cref="FieldEditorHost"/>, the same dispatcher the editing
/// canvas uses, so the control for a field type is identical inside a block and inside a zone
/// (ADR-0014).
/// </para>
/// <para>
/// Diagnostics are narrowed twice: to this block, and then to each property inside it. A twelve-block
/// zone that could only say "3 problems" would leave an author opening all twelve.
/// </para>
/// </remarks>
public partial class BlockCard : ComponentBase
{
    /// <summary>The block being drawn.</summary>
    [Parameter]
    [EditorRequired]
    public StoredBlock Block { get; set; } = default!;

    /// <summary>Where the block sits in the list, from zero.</summary>
    [Parameter]
    public int Position { get; set; }

    /// <summary>How many blocks there are, so the card can say "3 of 12".</summary>
    [Parameter]
    public int Count { get; set; }

    /// <summary>The zone this list fills, for ids, the read-only flag, and diagnostics.</summary>
    [Parameter]
    [EditorRequired]
    public FieldEditorContext Field { get; set; } = default!;

    /// <summary>The block type, or null when this deployment has none by that key.</summary>
    [Parameter]
    public BlockTypeSummary? BlockType { get; set; }

    /// <summary>The properties the captured revision declared, in editor order.</summary>
    [Parameter]
    public IReadOnlyList<CapturedSlot> Schema { get; set; } = [];

    /// <summary>Whether the schema is still being fetched.</summary>
    [Parameter]
    public bool IsSchemaLoading { get; set; }

    /// <summary>Whether the body is hidden.</summary>
    [Parameter]
    public bool IsCollapsed { get; set; }

    /// <summary>Whether the list will take another block, which is what duplicating needs.</summary>
    [Parameter]
    public bool CanAdd { get; set; } = true;

    /// <summary>Raised when the disclosure is pressed.</summary>
    [Parameter]
    public EventCallback OnToggleCollapse { get; set; }

    /// <summary>Raised with a step of -1 or 1.</summary>
    [Parameter]
    public EventCallback<int> OnMove { get; set; }

    /// <summary>Raised when the copy button is pressed.</summary>
    [Parameter]
    public EventCallback OnDuplicate { get; set; }

    /// <summary>Raised when the remove button is pressed.</summary>
    [Parameter]
    public EventCallback OnDelete { get; set; }

    /// <summary>Raised when one of this block's properties changes.</summary>
    [Parameter]
    public EventCallback<BlockPropertyChange> OnPropertyChanged { get; set; }

    /// <summary>Raised when a drag of this card begins, with its position.</summary>
    [Parameter]
    public EventCallback<int> OnDragStart { get; set; }

    /// <summary>Raised when a drag is dropped on this card, with its position.</summary>
    [Parameter]
    public EventCallback<int> OnDropOn { get; set; }

    /// <summary>Whether a drag is currently hovering this card.</summary>
    private bool IsDropTarget { get; set; }

    /// <summary>Where this block sits in the payload, which is the prefix its diagnostics carry.</summary>
    private string Path => $"{Field.Path}.{BlockMembers.Items}[{Position}]";

    /// <summary>Everything validation said about this block or anything inside it.</summary>
    private ZoneDiagnostics Diagnostics => (Field.Diagnostics ?? ZoneDiagnostics.Empty).Within(Path);

    /// <summary>What was said about the block itself rather than about one of its properties.</summary>
    /// <remarks>
    /// Everything under this block, minus everything under one of its property rows. A diagnostic
    /// about the block's id or its type has no property row to land on, and bucketing it under one
    /// would put it beside a control that is not what is wrong.
    /// </remarks>
    private ZoneDiagnostics BlockDiagnostics
    {
        get
        {
            var propertyPath = $"{Path}.{BlockMembers.Properties}";

            return new ZoneDiagnostics(
                [.. Diagnostics.Errors.Where(d => !ZoneDiagnostics.Covers(propertyPath, d.Property))],
                [.. Diagnostics.Warnings.Where(d => !ZoneDiagnostics.Covers(propertyPath, d.Property))]);
        }
    }

    /// <summary>What to call the block type, falling back to the key a payload carries.</summary>
    private string TypeName => BlockType?.Name ?? Block.BlockTypeKey;

    /// <summary>The one-line summary shown when the block is collapsed.</summary>
    private string Summary => Block.Summarize(BlockType?.SummaryTemplate, TypeName, Schema);

    /// <summary>
    /// The card's element id, which is what a deep link from the publish dialog targets.
    /// </summary>
    /// <remarks>
    /// Keyed on the block's own id rather than on its position, so a link followed after a reorder
    /// still lands on the block it named.
    /// </remarks>
    private string CardId => $"block-{Block.Id?.ToString("n") ?? Position.ToString()}";

    private string BodyId => $"{CardId}-body";

    private string SeverityClass => Diagnostics.Severity switch
    {
        ZoneSeverity.Error => "cms-block--error",
        ZoneSeverity.Warning => "cms-block--warning",
        _ => IsDropTarget ? "cms-block--drop" : string.Empty,
    };

    private string BadgeClass =>
        Diagnostics.Severity is ZoneSeverity.Error ? "text-bg-danger" : "text-bg-warning";

    private string BadgeText => Diagnostics.Severity is ZoneSeverity.Error
        ? Plural(Diagnostics.Errors.Count, "problem")
        : Plural(Diagnostics.Warnings.Count, "warning");

    private string LabelId(string key) => $"{CardId}-{key}-name";

    private string HelpId(string key) => $"{CardId}-{key}-help";

    /// <summary>The id a deep link to one property of this block targets.</summary>
    private string PropertyAnchor(string key) => $"{CardId}-{key}";

    /// <summary>Builds the context one property's editor is drawn with.</summary>
    private FieldEditorContext ContextFor(CapturedSlot property) => Field.Nested(
        property,
        $"{PropertyAnchor(property.Key)}-control",
        LabelId(property.Key),
        $"{Path}.{BlockMembers.Properties}.{property.Key}",
        property.Description is { Length: > 0 } ? HelpId(property.Key) : null);

    /// <summary>What was said about one property of this block.</summary>
    private ZoneDiagnostics DiagnosticsFor(string key) =>
        Diagnostics.Within($"{Path}.{BlockMembers.Properties}.{key}");

    private static string Plural(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>Completes a drag by telling the list where it landed.</summary>
    private async Task OnDropAsync()
    {
        IsDropTarget = false;

        await OnDropOn.InvokeAsync(Position);
    }
}
