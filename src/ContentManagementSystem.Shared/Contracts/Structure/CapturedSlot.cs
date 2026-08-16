using System.Text.Json;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// One zone or block property as a revision snapshot recorded it (spec section 8.5).
/// </summary>
/// <param name="Key">Payload key the value is stored under.</param>
/// <param name="Name">Editor-facing label as it stood when the revision was cut.</param>
/// <param name="FieldTypeKey">Field type that fills the slot.</param>
/// <param name="IsRequired">Whether an empty value blocks publishing.</param>
/// <param name="SortOrder">Order the slot appears in the editor.</param>
/// <param name="Configuration">Field-type-specific configuration, or null when there is none.</param>
/// <param name="Description">
/// Help text as it stood when the revision was cut, or null when the slot has none.
/// </param>
/// <param name="Group">
/// The card group the editing canvas lays the slot out under, or null when it is ungrouped
/// (task P6-05). Snapshots cut before grouping was captured answer null for every slot, which is
/// one ungrouped canvas — the layout those pages have always had.
/// </param>
/// <remarks>
/// Distinct from <see cref="ZoneDefinition"/>, and the difference is the whole point of revisions: a
/// zone definition is what the template says <em>now</em> and carries a database identity, while this
/// is what a stored payload is judged against and may describe a zone that no longer exists. An
/// editor form built from the live definitions would show a control for a zone the page has no value
/// for and silently drop one it does.
/// </remarks>
public sealed record CapturedSlot(
    string Key,
    string Name,
    string FieldTypeKey,
    bool IsRequired,
    int SortOrder,
    JsonElement? Configuration,
    string? Description = null,
    string? Group = null)
{
    /// <summary>
    /// Reads the snapshot array a revision endpoint returns.
    /// </summary>
    /// <param name="snapshot">The <c>zones</c> element of a revision detail.</param>
    /// <returns>The slots in editor order, or empty when the snapshot is not an array.</returns>
    /// <remarks>
    /// Forgiving on purpose. The snapshot is stored data written by an earlier build, and a screen
    /// that threw on an entry it did not recognise would make one unfamiliar slot take the whole
    /// editor down; an entry with no key or no field type is skipped, and the rest of the page is
    /// still editable (spec section 15.3 applies the same rule to delivery).
    /// </remarks>
    public static IReadOnlyList<CapturedSlot> Read(JsonElement snapshot)
    {
        if (snapshot.ValueKind is not JsonValueKind.Array) return [];

        var slots = new List<CapturedSlot>();

        foreach (var entry in snapshot.EnumerateArray())
        {
            if (entry.ValueKind is not JsonValueKind.Object) continue;

            var key = Text(entry, "key");
            var fieldTypeKey = Text(entry, "fieldTypeKey");

            if (key is null || fieldTypeKey is null) continue;

            slots.Add(new CapturedSlot(
                key,
                Text(entry, "name") ?? key,
                fieldTypeKey,
                entry.TryGetProperty("isRequired", out var required) &&
                    required.ValueKind is JsonValueKind.True,
                entry.TryGetProperty("sortOrder", out var order) && order.TryGetInt32(out var sortOrder)
                    ? sortOrder
                    : 0,
                entry.TryGetProperty("configuration", out var configuration)
                    ? configuration.Clone()
                    : null,
                Text(entry, "description"),
                Text(entry, "group")));
        }

        return [.. slots.OrderBy(slot => slot.SortOrder).ThenBy(slot => slot.Key, StringComparer.Ordinal)];
    }

    private static string? Text(JsonElement entry, string member) =>
        entry.TryGetProperty(member, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
