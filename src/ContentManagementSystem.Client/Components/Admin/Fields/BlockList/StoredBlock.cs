using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Components.Admin.Fields.BlockList;

/// <summary>
/// Member names of a stored block instance (spec section 6.2).
/// </summary>
/// <remarks>
/// Mirrors the constants on <c>BlocksFieldType</c>, which the backoffice cannot reference —
/// <c>Core</c> is not loaded in the browser. They are the on-disk contract either way.
/// </remarks>
public static class BlockMembers
{
    /// <summary>The member holding the ordered block instances.</summary>
    public const string Items = "items";

    /// <summary>The stable GUID identifying one block across edits and versions.</summary>
    public const string Id = "id";

    /// <summary>The block type the instance was authored against.</summary>
    public const string BlockTypeKey = "blockTypeKey";

    /// <summary>The block type revision whose schema was captured at write time.</summary>
    public const string BlockTypeRevision = "blockTypeRevision";

    /// <summary>The block's own property values, keyed by property alias.</summary>
    public const string Properties = "properties";
}

/// <summary>
/// One block instance, read into the facts the editor draws it from.
/// </summary>
/// <param name="Node">The stored object, so a write can keep every member it does not touch.</param>
/// <param name="Id">The block's stable identity, or null when the payload carries none.</param>
/// <param name="BlockTypeKey">Key of the block type the instance was authored against.</param>
/// <param name="Revision">The captured revision, or null when the payload predates capturing.</param>
/// <remarks>
/// <strong>The id is what makes reordering visible as a move.</strong> It is generated when a block
/// is added and preserved for the life of that block, so a version diff reports a block that moved
/// as <em>moved</em> rather than as removed-plus-added — and so the editor can address the right one
/// when two blocks of the same type sit side by side.
/// </remarks>
public sealed record StoredBlock(JsonObject Node, Guid? Id, string BlockTypeKey, int? Revision)
{
    /// <summary>Reads a stored block, tolerating a payload that is missing pieces.</summary>
    /// <param name="node">The stored object.</param>
    /// <returns>The block.</returns>
    /// <remarks>
    /// Nothing is rejected here. A block with no id or no type key is one the publish check will
    /// complain about with a payload path an editor can act on; refusing to draw it would leave them
    /// looking at a gap in a list with no way to remove it.
    /// </remarks>
    public static StoredBlock Read(JsonObject node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new StoredBlock(
            node,
            node[BlockMembers.Id]?.GetValueKind() is JsonValueKind.String &&
            Guid.TryParse(node[BlockMembers.Id]!.GetValue<string>(), out var id)
                ? id
                : null,
            node[BlockMembers.BlockTypeKey]?.GetValueKind() is JsonValueKind.String
                ? node[BlockMembers.BlockTypeKey]!.GetValue<string>()
                : string.Empty,
            node[BlockMembers.BlockTypeRevision]?.GetValueKind() is JsonValueKind.Number
                ? node[BlockMembers.BlockTypeRevision]!.GetValue<int>()
                : null);
    }

    /// <summary>Builds a new, empty block of a type.</summary>
    /// <param name="blockTypeKey">Key of the block type.</param>
    /// <param name="revision">The block type's current revision, captured now.</param>
    /// <returns>The stored object.</returns>
    /// <remarks>
    /// The revision is captured at the moment of adding, which is what spec section 8.5 means by a
    /// payload being judged against the schema it was authored against. A block added today and
    /// edited next year is still laid out by the properties that existed today.
    /// </remarks>
    public static JsonObject Create(string blockTypeKey, int revision) => new()
    {
        [BlockMembers.Id] = Guid.NewGuid().ToString(),
        [BlockMembers.BlockTypeKey] = blockTypeKey,
        [BlockMembers.BlockTypeRevision] = revision,
        [BlockMembers.Properties] = new JsonObject(),
    };

    /// <summary>Copies a block, giving the copy its own identity.</summary>
    /// <param name="node">The block to duplicate.</param>
    /// <returns>The copy.</returns>
    /// <remarks>
    /// A new id, always. Duplicating a block and keeping its id would put two blocks with one
    /// identity in the same list — which the validator refuses outright, because it makes the diff
    /// ambiguous and makes the editor address the wrong one.
    /// </remarks>
    public static JsonObject Duplicate(JsonObject node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var copy = (JsonObject)node.DeepClone();

        copy[BlockMembers.Id] = Guid.NewGuid().ToString();

        return copy;
    }

    /// <summary>The block's property values, or an empty object when it has none yet.</summary>
    public JsonObject Properties =>
        Node[BlockMembers.Properties] as JsonObject ?? [];

    /// <summary>One property's stored value as JSON text, empty when it is unauthored.</summary>
    /// <param name="key">The property alias.</param>
    /// <returns>The value a field editor binds to.</returns>
    public string ValueOf(string key) =>
        Properties[key] is { } value ? value.ToJsonString() : string.Empty;

    /// <summary>
    /// The one-line summary shown when the block is collapsed.
    /// </summary>
    /// <param name="template">The block type's token pattern, such as <c>{headline}</c>.</param>
    /// <param name="fallback">What to show when the pattern produces nothing — the type's name.</param>
    /// <param name="schema">The captured properties, for reading a token's value.</param>
    /// <returns>Text an author can recognise this block by in a collapsed list.</returns>
    /// <remarks>
    /// <strong>A collapsed block that says only "Hero banner" is a collapsed block nobody dares
    /// collapse.</strong> The whole point of the summary is that a twelve-block page can be read as a
    /// list — which needs the block's own content in it, not its type.
    /// <para>
    /// A token naming a property that is empty, or that the revision does not declare, resolves to
    /// nothing rather than to its own name in braces. A summary reading "{headline}" tells an author
    /// about a template they did not write and cannot fix.
    /// </para>
    /// </remarks>
    public string Summarize(string? template, string fallback, IReadOnlyList<CapturedSlot> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (template is not { Length: > 0 }) return fallback;

        var text = new StringBuilder();
        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);

            if (open < 0)
            {
                text.Append(template.AsSpan(index));

                break;
            }

            var close = template.IndexOf('}', open);

            if (close < 0)
            {
                // An unclosed brace is a pattern somebody is halfway through writing. The rest goes
                // through as literal text rather than being swallowed.
                text.Append(template.AsSpan(index));

                break;
            }

            text.Append(template.AsSpan(index, open - index));
            text.Append(TokenValue(template[(open + 1)..close], schema));

            index = close + 1;
        }

        var summary = text.ToString().Trim();

        return summary.Length > 0 ? summary : fallback;
    }

    /// <summary>Reads one token's value out of the block's properties, as plain text.</summary>
    /// <remarks>
    /// Only values that are one readable string resolve. A token pointing at a media reference or a
    /// nested block list has no sensible one-line form, and printing its JSON in a summary line
    /// would make the collapsed list less readable than the type name it replaced.
    /// </remarks>
    private string TokenValue(string key, IReadOnlyList<CapturedSlot> schema)
    {
        if (schema.All(slot => slot.Key != key)) return string.Empty;

        if (Properties[key] is not JsonObject stored) return string.Empty;

        return stored["value"]?.GetValueKind() is JsonValueKind.String
            ? stored["value"]!.GetValue<string>()
            : string.Empty;
    }
}
