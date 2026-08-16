using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Reference;

/// <summary>
/// The <c>mediaList</c> editor — an ordered gallery, each item carrying its own usage-scope settings
/// (task P6-15, spec sections 7.1 and 13.4).
/// </summary>
/// <remarks>
/// Each item is drawn by the single-media control rather than by a copy of its markup, which is the
/// same rule the gallery renderer follows: a crop, a focal point, and an alternative text override
/// have to mean the same thing in a gallery as in a single property, and two implementations would
/// eventually disagree about one of them.
/// <para>
/// The same item may legitimately appear twice. A gallery that repeats a picture is unusual but not
/// wrong, and refusing it would be the control overruling the author on a matter of taste — which is
/// exactly what the field type declines to do.
/// </para>
/// </remarks>
public partial class MediaListEditor : FieldEditorBase
{
    /// <summary>The member holding the gallery.</summary>
    private const string ItemsMember = "items";

    /// <summary>Whether the picker is open.</summary>
    private bool IsPicking { get; set; }

    /// <summary>The gallery, in stored order.</summary>
    private IReadOnlyList<JsonObject> Items => StoredValue.ReadItems(Value, ItemsMember);

    /// <summary>Fewest pictures the slot requires.</summary>
    private int? Min => ConfiguredInt32(FieldSettingNames.Min);

    /// <summary>Most pictures the slot allows.</summary>
    private int? Max => ConfiguredInt32(FieldSettingNames.Max);

    /// <summary>Whether the slot will take no more pictures.</summary>
    private bool IsFull => Max is { } max && Items.Count >= max;

    /// <summary>How many pictures the slot wants, said in words as well as enforced at publish.</summary>
    private string? CountRule => (Min, Max) switch
    {
        (null, null) => null,
        ({ } min, { } max) => $"Between {min} and {max} pictures.",
        ({ } min, null) => $"At least {min}.",
        (null, { } max) => $"At most {max}.",
    };

    private string ItemHeadingId(int index) => $"{Field.ControlId}-item-{index}";

    /// <summary>
    /// One item as the single-media control wants it — with the discriminator it expects.
    /// </summary>
    /// <remarks>
    /// A gallery item is the same shape as a <c>media</c> value minus its <c>type</c>, because the
    /// discriminator belongs to the property and the property here is the list. It is added on the
    /// way in and taken off on the way out, so the control gets what it was written for and the
    /// payload keeps what the field type documents.
    /// </remarks>
    private string ItemJson(int index)
    {
        if (index >= Items.Count) return string.Empty;

        var item = (JsonObject)Items[index].DeepClone();

        item[ContentPayloadMembers.Type] = FieldTypeKeys.Media;

        return item.ToJsonString();
    }

    /// <summary>Writes one item back, dropping the discriminator the list owns.</summary>
    /// <remarks>
    /// An emptied item — the control's way of saying "nothing is picked" — removes the entry rather
    /// than leaving a gallery slot pointing at nothing.
    /// </remarks>
    private Task OnItemChangedAsync(int index, string json)
    {
        var items = Clone();

        if (index >= items.Count) return Task.CompletedTask;

        if (StoredValue.Parse(json) is not { } item)
        {
            items.RemoveAt(index);

            return WriteItemsAsync(items);
        }

        item.Remove(ContentPayloadMembers.Type);
        items[index] = item;

        return WriteItemsAsync(items);
    }

    private Task OnPickedAsync(MediaDetail picked)
    {
        IsPicking = false;

        var items = Clone();

        items.Add(new JsonObject { [MediaValueMembers.MediaId] = picked.Id });

        return WriteItemsAsync(items);
    }

    private Task RemoveAsync(int index)
    {
        var items = Clone();

        if (index >= items.Count) return Task.CompletedTask;

        items.RemoveAt(index);

        return WriteItemsAsync(items);
    }

    /// <summary>Moves one picture by a step, clamped to the ends of the gallery.</summary>
    private Task MoveAsync(int index, int step)
    {
        var items = Clone();
        var target = index + step;

        if (index >= items.Count || target < 0 || target >= items.Count) return Task.CompletedTask;

        (items[index], items[target]) = (items[target], items[index]);

        return WriteItemsAsync(items);
    }

    /// <summary>
    /// A detached copy of the gallery, so it can be rebuilt without mutating what is rendered.
    /// </summary>
    /// <remarks>
    /// <c>JsonNode</c> instances carry a parent, and a node cannot be added to a second array while
    /// it belongs to the first. Cloning is what makes reordering an ordinary list operation instead
    /// of a dance of removals and reinsertions.
    /// </remarks>
    private List<JsonObject> Clone() => [.. Items.Select(item => (JsonObject)item.DeepClone())];

    private Task WriteItemsAsync(IReadOnlyList<JsonObject> items)
    {
        if (items.Count == 0) return WriteAsync(string.Empty);

        return WriteAsync(StoredValue.Write(Value, FieldTypeKey, stored =>
            stored[ItemsMember] = new JsonArray([.. items.Select(item => (JsonNode?)item)])));
    }
}

/// <summary>Member names of one stored media placement (spec section 13.4).</summary>
/// <remarks>
/// Mirrors <c>MediaValue</c> in <c>Core</c>, which the backoffice cannot reference. Only the member
/// this component writes is here; the rest are the single-media control's, and it holds its own.
/// </remarks>
internal static class MediaValueMembers
{
    /// <summary>The library item the placement points at.</summary>
    public const string MediaId = "mediaId";
}
