using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.BlockList;

/// <summary>What one property of one block changed to.</summary>
/// <param name="Key">The property alias.</param>
/// <param name="Json">Its new stored value as JSON text, or empty to remove it.</param>
public sealed record BlockPropertyChange(string Key, string Json);

/// <summary>A block that was deleted and can still be put back.</summary>
/// <param name="Node">The block, exactly as it was.</param>
/// <param name="Index">Where it was, so undo restores the order and not only the block.</param>
/// <param name="Summary">What it was called, so the offer to undo names it.</param>
public sealed record RemovedBlock(JsonObject Node, int Index, string Summary);

/// <summary>
/// The <c>blocks</c> editor — the field type that turns a zone into a page builder
/// (tasks P6-06 and P6-07, spec section 14.3).
/// </summary>
/// <remarks>
/// <strong>Every block is drawn from the revision it was authored against</strong>, not from the
/// block type's current one (spec section 8.5). Two blocks of the same type in one zone can
/// therefore be laid out differently, and that is correct rather than a defect: a block added before
/// a property existed has no value for it, and drawing a control for that property would quietly
/// invite an author to fill in a schema their content is not being judged by.
/// <para>
/// The property controls come from the same catalog the canvas uses (ADR-0014), so a rich-text
/// editor inside a hero banner is the identical component to one filling a zone. That is what keeps
/// the block list a container rather than a second editing surface.
/// </para>
/// <para>
/// <strong>Everything here is reachable from the keyboard (P6-07).</strong> Add, reorder, duplicate,
/// collapse, and delete are all buttons; drag is added on top of them by <see cref="BlockCard"/>
/// rather than being the way any of it is done. Acceptance criterion P6 #4 is the whole of the
/// reason, and it is also why deletion is undoable — a keyboard user tabbing through a list of
/// remove buttons is exactly who presses the wrong one.
/// </para>
/// </remarks>
public partial class BlockListEditor : FieldEditorBase
{
    [Inject]
    private IStructureClient Structure { get; set; } = default!;

    [Inject]
    private IReusableClient BlockTypes { get; set; } = default!;

    /// <summary>Every block type this deployment has, keyed by its stable key.</summary>
    private Dictionary<string, BlockTypeSummary> Types { get; } = new(StringComparer.Ordinal);

    /// <summary>Captured property schemas, keyed by block type key and revision.</summary>
    private Dictionary<string, IReadOnlyList<CapturedSlot>> Schemas { get; } = new(StringComparer.Ordinal);

    /// <summary>Schemas currently being fetched, so one is not fetched twice.</summary>
    private HashSet<string> Loading { get; } = new(StringComparer.Ordinal);

    /// <summary>Blocks the author has collapsed, by id.</summary>
    private HashSet<Guid> Collapsed { get; } = [];

    /// <summary>The block type picker's state.</summary>
    private bool IsPicking { get; set; }

    /// <summary>Whether the block type list is still being fetched.</summary>
    private bool IsLoading { get; set; } = true;

    /// <summary>The last deleted block, until the next change retires the offer to undo.</summary>
    private RemovedBlock? Removed { get; set; }

    /// <summary>Where a drag in progress started, or null when nothing is being dragged.</summary>
    private int? Dragging { get; set; }

    /// <summary>The blocks, in stored order.</summary>
    private IReadOnlyList<StoredBlock> Blocks =>
        [.. StoredValue.ReadItems(Value, BlockMembers.Items).Select(StoredBlock.Read)];

    /// <summary>Fewest blocks the slot requires.</summary>
    private int? Min => ConfiguredInt32(FieldSettingNames.Min);

    /// <summary>Most blocks the slot allows.</summary>
    private int? Max => ConfiguredInt32(FieldSettingNames.Max);

    /// <summary>Whether the slot will take no more blocks.</summary>
    private bool IsFull => Max is { } max && Blocks.Count >= max;

    /// <summary>The block type keys the slot allows, empty when it allows any registered one.</summary>
    private IReadOnlyList<string> AllowedKeys => ConfiguredTextList(FieldSettingNames.AllowedBlockTypes);

    /// <summary>
    /// The block types an author may add here.
    /// </summary>
    /// <remarks>
    /// The allowlist intersected with what is registered, in the allowlist's own order when it names
    /// one — a template author listing <c>hero-banner</c> first meant it to be first. An orphaned
    /// block type is dropped: it has no component to render it, so adding one would author content
    /// the public site draws as nothing.
    /// </remarks>
    private IReadOnlyList<BlockTypeSummary> Addable =>
        AllowedKeys.Count > 0
            ? [.. AllowedKeys.Select(key => Types.GetValueOrDefault(key))
                .OfType<BlockTypeSummary>()
                .Where(type => !type.IsOrphaned)]
            : [.. Types.Values.Where(type => !type.IsOrphaned).OrderBy(type => type.Name, StringComparer.CurrentCulture)];

    private string RuleId => $"{Field.ControlId}-rule";

    private string DescribedBy => string.Join(
        ' ',
        new[] { Field.DescribedBy, CountRule is { Length: > 0 } ? RuleId : null }
            .Where(id => !string.IsNullOrEmpty(id)));

    /// <summary>How many blocks the slot wants, said in words as well as enforced at publish.</summary>
    private string? CountRule => (Min, Max) switch
    {
        (null, null) => null,
        ({ } min, { } max) when min == max => $"Exactly {Plural(min)}.",
        ({ } min, { } max) => $"Between {min} and {Plural(max)}.",
        ({ } min, null) => $"At least {Plural(min)}.",
        (null, { } max) => $"At most {Plural(max)}.",
    };

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        foreach (var type in await Structure.GetBlockTypesAsync())
        {
            Types[type.Key] = type;
        }

        IsLoading = false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Schemas are fetched per distinct block type and revision the payload actually names, not per
    /// block. A zone holding twelve cards of one type costs one request rather than twelve.
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        foreach (var block in Blocks)
        {
            await EnsureSchemaAsync(block);
        }
    }

    /// <summary>The block type of a block, or null when this deployment has none by that key.</summary>
    private BlockTypeSummary? TypeOf(StoredBlock block) =>
        Types.GetValueOrDefault(block.BlockTypeKey);

    /// <summary>The captured properties a block is drawn from, empty until they arrive.</summary>
    private IReadOnlyList<CapturedSlot> SchemaOf(StoredBlock block) =>
        Schemas.GetValueOrDefault(CacheKey(block)) ?? [];

    /// <summary>Whether a block's schema is still being fetched.</summary>
    private bool IsSchemaLoading(StoredBlock block) =>
        !Schemas.ContainsKey(CacheKey(block)) && Loading.Contains(CacheKey(block));

    private bool IsCollapsed(StoredBlock block) => block.Id is { } id && Collapsed.Contains(id);

    /// <summary>Fetches the revision snapshot a block was authored against, once.</summary>
    /// <remarks>
    /// A block naming a type this build does not carry, or carrying no revision, resolves to an
    /// empty schema — which <see cref="BlockCard"/> draws as an unrecognised block that can still be
    /// moved and removed. Refusing to draw it would strand the zone (spec section 15.3).
    /// </remarks>
    private async Task EnsureSchemaAsync(StoredBlock block)
    {
        var key = CacheKey(block);

        if (Schemas.ContainsKey(key) || !Loading.Add(key)) return;

        if (TypeOf(block) is not { } type)
        {
            Schemas[key] = [];

            return;
        }

        // A payload written before revisions were captured falls back to the block type's current
        // revision, which is the only schema there is to judge it by.
        var revision = block.Revision ?? type.CurrentRevision;

        Schemas[key] = await BlockTypes.GetPropertiesAsync(type.Id, revision);

        StateHasChanged();
    }

    private static string CacheKey(StoredBlock block) =>
        $"{block.BlockTypeKey}@{block.Revision?.ToString() ?? "current"}";

    private static string Plural(int count) => count == 1 ? "1 block" : $"{count} blocks";

    private Task ToggleAsync(StoredBlock block)
    {
        if (block.Id is not { } id) return Task.CompletedTask;

        if (!Collapsed.Add(id)) Collapsed.Remove(id);

        return Task.CompletedTask;
    }

    /// <summary>Adds a block of the chosen type, capturing its current revision.</summary>
    private Task OnBlockTypePickedAsync(BlockTypeSummary type)
    {
        IsPicking = false;

        var items = Clone();

        items.Add(StoredBlock.Create(type.Key, type.CurrentRevision));

        return WriteItemsAsync(items);
    }

    /// <summary>Moves one block by a step, clamped to the ends of the list.</summary>
    /// <remarks>
    /// The block keeps its id, which is what lets the version diff report this as a move rather than
    /// as a delete and an add somewhere else.
    /// </remarks>
    private Task MoveAsync(int index, int step)
    {
        var items = Clone();
        var target = index + step;

        if (index >= items.Count || target < 0 || target >= items.Count) return Task.CompletedTask;

        (items[index], items[target]) = (items[target], items[index]);

        return WriteItemsAsync(items);
    }

    /// <summary>
    /// Completes a drag by moving the dragged block to where it was dropped.
    /// </summary>
    /// <param name="to">The position the block was dropped on.</param>
    /// <remarks>
    /// <strong>An enhancement over the move buttons, never a replacement for them</strong> (P6-07).
    /// It ends up in the same <see cref="WriteItemsAsync"/> the arrow buttons do, so a list reordered
    /// by dragging and one reordered by keyboard produce the same payload — including keeping every
    /// block's id, which is what makes the version diff say "moved".
    /// <para>
    /// Removed and reinserted rather than swapped, because a drag across four blocks means "put it
    /// here" and a swap would mean "trade places with", which is a different list.
    /// </para>
    /// </remarks>
    private Task DropAsync(int to)
    {
        if (Dragging is not { } from) return Task.CompletedTask;

        Dragging = null;

        if (from == to) return Task.CompletedTask;

        var items = Clone();

        if (from >= items.Count || to >= items.Count) return Task.CompletedTask;

        var moved = items[from];

        items.RemoveAt(from);
        items.Insert(to, moved);

        return WriteItemsAsync(items);
    }

    /// <summary>Copies a block in beneath the original, with its own identity.</summary>
    private Task DuplicateAsync(int index)
    {
        var items = Clone();

        if (index >= items.Count || IsFull) return Task.CompletedTask;

        items.Insert(index + 1, StoredBlock.Duplicate(items[index]));

        return WriteItemsAsync(items);
    }

    /// <summary>Removes a block, keeping it and its place so the removal can be taken back.</summary>
    private Task DeleteAsync(int index)
    {
        var items = Clone();

        if (index >= items.Count) return Task.CompletedTask;

        var block = StoredBlock.Read(items[index]);
        var type = TypeOf(block);

        Removed = new RemovedBlock(
            items[index],
            index,
            block.Summarize(type?.SummaryTemplate, type?.Name ?? block.BlockTypeKey, SchemaOf(block)));

        if (block.Id is { } id) Collapsed.Remove(id);

        items.RemoveAt(index);

        return WriteItemsAsync(items, keepUndo: true);
    }

    /// <summary>Puts the last removed block back where it was.</summary>
    private Task UndoAsync()
    {
        if (Removed is not { } removed) return Task.CompletedTask;

        var items = Clone();

        items.Insert(Math.Min(removed.Index, items.Count), removed.Node);

        return WriteItemsAsync(items);
    }

    /// <summary>Writes one property of one block, leaving every other member of it alone.</summary>
    private Task OnPropertyChangedAsync(int index, BlockPropertyChange change)
    {
        var items = Clone();

        if (index >= items.Count) return Task.CompletedTask;

        var properties = items[index][BlockMembers.Properties] as JsonObject;

        if (properties is null)
        {
            properties = [];
            items[index][BlockMembers.Properties] = properties;
        }

        if (string.IsNullOrEmpty(change.Json))
        {
            // Removed rather than set to null, following the rule the whole payload layer follows:
            // absent means never authored and null means deliberately cleared, and a renderer reads
            // them differently when deciding whether to use a fallback.
            properties.Remove(change.Key);
        }
        else
        {
            properties[change.Key] = JsonNode.Parse(change.Json);
        }

        return WriteItemsAsync(items);
    }

    /// <summary>
    /// A detached copy of the list, so it can be rebuilt without mutating what is rendered.
    /// </summary>
    /// <remarks>
    /// A <c>JsonNode</c> carries a parent and cannot be added to a second array while it belongs to
    /// the first, which makes reordering a dance of removals and reinsertions unless the list is
    /// cloned first.
    /// </remarks>
    private List<JsonObject> Clone() =>
        [.. StoredValue.ReadItems(Value, BlockMembers.Items).Select(item => (JsonObject)item.DeepClone())];

    /// <summary>Writes the list back, retiring the offer to undo unless this write created it.</summary>
    private Task WriteItemsAsync(IReadOnlyList<JsonObject> items, bool keepUndo = false)
    {
        if (!keepUndo) Removed = null;

        if (items.Count == 0) return WriteAsync(string.Empty);

        return WriteAsync(StoredValue.Write(Value, FieldTypeKey, stored =>
            stored[BlockMembers.Items] = new JsonArray([.. items.Select(item => (JsonNode?)item)])));
    }
}
