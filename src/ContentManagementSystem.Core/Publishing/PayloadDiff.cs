using System.Text;
using System.Text.Json;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Publishing;

/// <summary>
/// Compares two content payloads, zone by zone and block by block (task P2-14, spec section 11.4).
/// </summary>
/// <remarks>
/// The whole of the comparison, and none of the database. <see cref="ContentDiffService"/> loads the
/// two versions, checks the caller's permission, and compares their metadata; everything that
/// decides <em>what changed inside the content</em> lives here, over two parsed payloads and the
/// field type registry.
/// <para>
/// Split out so the algorithm can be exercised directly (task P2-25). Reorder, insert, delete, and a
/// change nested inside a block are four distinct outcomes that a positional comparison collapses
/// into two, and asserting that through a page, a template, and a publish would test the plumbing
/// far more thoroughly than the rule.
/// </para>
/// </remarks>
/// <param name="registry">The field types, which is what renders a stored value comparably.</param>
public sealed class PayloadDiff(IFieldTypeRegistry registry)
{
    /// <summary>
    /// Compares the zones of two payloads.
    /// </summary>
    /// <param name="before">The earlier payload, or null when it could not be parsed.</param>
    /// <param name="after">The later payload, or null when it could not be parsed.</param>
    /// <returns>One entry per zone that differs, in the later version's order.</returns>
    /// <remarks>
    /// An unparseable payload is compared as though it held no zones rather than throwing. A version
    /// whose stored document a later build cannot read is exactly when somebody opens the diff.
    /// </remarks>
    public IReadOnlyList<ZoneChange> Compare(ContentPayload? before, ContentPayload? after)
    {
        var changes = new List<ZoneChange>();

        // The later version's order first, then anything only the earlier one had. A removed zone
        // has no position in the new document, and appending it is the only honest place to put it.
        var keys = new List<string>(after?.ZoneKeys ?? []);
        keys.AddRange((before?.ZoneKeys ?? []).Where(key => !keys.Contains(key, StringComparer.Ordinal)));

        foreach (var key in keys)
        {
            var leftState = before?.GetZoneState(key) ?? ContentValueState.Absent;
            var rightState = after?.GetZoneState(key) ?? ContentValueState.Absent;
            var left = before?.GetZone(key) ?? default;
            var right = after?.GetZone(key) ?? default;

            if (leftState is ContentValueState.Absent && rightState is ContentValueState.Absent) continue;

            if (leftState is ContentValueState.Absent)
            {
                changes.Add(Zone(key, ContentChangeKind.Added, left, right));

                continue;
            }

            if (rightState is ContentValueState.Absent)
            {
                changes.Add(Zone(key, ContentChangeKind.Removed, left, right));

                continue;
            }

            if (SameJson(left, right)) continue;

            changes.Add(Zone(key, ContentChangeKind.Changed, left, right));
        }

        return changes;
    }

    private ZoneChange Zone(string key, ContentChangeKind kind, JsonElement left, JsonElement right)
    {
        var fieldTypeKey = TypeKey(right) ?? TypeKey(left);
        var blocks = HasItems(left) || HasItems(right)
            ? CompareBlocks(left, right)
            : [];

        // A container reports its changes block by block, and rendering it as text as well would put
        // the whole zone's words beside a precise account of what actually moved.
        if (blocks.Count > 0 || HasItems(left) || HasItems(right))
        {
            return new ZoneChange(key, fieldTypeKey, kind, null, null, [], blocks);
        }

        var before = Render(left);
        var after = Render(right);

        return new ZoneChange(
            key,
            fieldTypeKey,
            kind,
            before,
            after,
            IsReferenceBearing(fieldTypeKey) ? [] : WordDiff.Compute(before, after),
            []);
    }

    /// <summary>
    /// Matches block instances by their stable GUID and compares the ones that appear in both.
    /// </summary>
    /// <remarks>
    /// The identity is the <c>id</c> member the <c>blocks</c> field type writes, not the position.
    /// That is what turns a drag-and-drop reorder into one <see cref="ContentChangeKind.Moved"/>
    /// entry instead of a wall of removals and additions (acceptance criterion P2 #6).
    /// </remarks>
    private List<BlockChange> CompareBlocks(JsonElement left, JsonElement right)
    {
        var before = ReadBlocks(left);
        var after = ReadBlocks(right);
        var changes = new List<BlockChange>();

        foreach (var (id, block) in after)
        {
            if (!before.TryGetValue(id, out var was))
            {
                changes.Add(new BlockChange(
                    id, BlockTypeKey(block.Element), ContentChangeKind.Added, null, block.Index, []));

                continue;
            }

            var properties = CompareProperties(was.Element, block.Element);

            if (properties.Count > 0)
            {
                changes.Add(new BlockChange(
                    id,
                    BlockTypeKey(block.Element),
                    ContentChangeKind.Changed,
                    was.Index,
                    block.Index,
                    properties));

                continue;
            }

            if (was.Index != block.Index)
            {
                changes.Add(new BlockChange(
                    id, BlockTypeKey(block.Element), ContentChangeKind.Moved, was.Index, block.Index, []));
            }
        }

        foreach (var (id, block) in before)
        {
            if (!after.ContainsKey(id))
            {
                changes.Add(new BlockChange(
                    id, BlockTypeKey(block.Element), ContentChangeKind.Removed, block.Index, null, []));
            }
        }

        return [.. changes.OrderBy(change => change.AfterIndex ?? change.BeforeIndex ?? 0)];
    }

    private List<PropertyChange> CompareProperties(JsonElement before, JsonElement after)
    {
        var left = Member(before, BlocksFieldType.PropertiesMember);
        var right = Member(after, BlocksFieldType.PropertiesMember);
        var changes = new List<PropertyChange>();

        var keys = new List<string>();
        if (right.ValueKind is JsonValueKind.Object)
        {
            keys.AddRange(right.EnumerateObject().Select(property => property.Name));
        }

        if (left.ValueKind is JsonValueKind.Object)
        {
            keys.AddRange(left.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => !keys.Contains(name, StringComparer.Ordinal)));
        }

        foreach (var key in keys)
        {
            var hasLeft = left.ValueKind is JsonValueKind.Object && left.TryGetProperty(key, out _);
            var hasRight = right.ValueKind is JsonValueKind.Object && right.TryGetProperty(key, out _);
            var leftValue = Member(left, key);
            var rightValue = Member(right, key);

            if (hasLeft && hasRight && SameJson(leftValue, rightValue)) continue;

            var kind = (hasLeft, hasRight) switch
            {
                (false, true) => ContentChangeKind.Added,
                (true, false) => ContentChangeKind.Removed,
                _ => ContentChangeKind.Changed,
            };

            var fieldTypeKey = TypeKey(rightValue) ?? TypeKey(leftValue);
            var renderedBefore = Render(leftValue);
            var renderedAfter = Render(rightValue);

            changes.Add(new PropertyChange(
                key,
                fieldTypeKey,
                kind,
                renderedBefore,
                renderedAfter,
                IsReferenceBearing(fieldTypeKey) ? [] : WordDiff.Compute(renderedBefore, renderedAfter)));
        }

        return changes;
    }

    /// <summary>
    /// Renders a stored value as the text a person compares.
    /// </summary>
    /// <remarks>
    /// Delegated to the field type that wrote it, by the stored discriminator — a value has to be
    /// read by whatever wrote it. A reference-bearing value renders as the identities it points at
    /// instead, because "Media 12 → Media 15" is the change, and the alt text beside it is not.
    /// The human labels those ids resolve to arrive with the media library in Phase 5.
    /// </remarks>
    private string? Render(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined) return null;
        if (value.ValueKind is JsonValueKind.Null) return null;

        if (TypeKey(value) is not { } key || registry.Find(key) is not { } fieldType)
        {
            // No field type to ask, which happens for content written by a build that had one. The
            // raw document is worse than a rendering and far better than reporting no change.
            return value.ToString();
        }

        if (fieldType.Capabilities.HasFlag(FieldTypeCapabilities.ReferenceBearing))
        {
            var targets = fieldType.ExtractReferences(value)
                .Select(reference => $"{reference.TargetType} {reference.TargetId}")
                .ToList();

            return targets.Count == 0 ? null : string.Join(", ", targets);
        }

        var text = fieldType.ExtractSearchText(value);

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private bool IsReferenceBearing(string? fieldTypeKey) =>
        fieldTypeKey is not null &&
        registry.Find(fieldTypeKey) is { } fieldType &&
        fieldType.Capabilities.HasFlag(FieldTypeCapabilities.ReferenceBearing);

    /// <summary>Reads a container's items into a map from block id to element and position.</summary>
    private static Dictionary<Guid, (JsonElement Element, int Index)> ReadBlocks(JsonElement value)
    {
        var blocks = new Dictionary<Guid, (JsonElement, int)>();
        var items = Member(value, BlocksFieldType.ItemsMember);

        if (items.ValueKind is not JsonValueKind.Array) return blocks;

        var index = 0;

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind is JsonValueKind.Object &&
                item.TryGetProperty(BlocksFieldType.IdMember, out var id) &&
                id.ValueKind is JsonValueKind.String &&
                Guid.TryParse(id.GetString(), out var parsed))
            {
                // A duplicate id is a malformed payload the blocks field type already reports on.
                // Keeping the first occurrence means the diff still renders rather than throwing.
                blocks.TryAdd(parsed, (item, index));
            }

            index++;
        }

        return blocks;
    }

    private static bool HasItems(JsonElement value) =>
        Member(value, BlocksFieldType.ItemsMember).ValueKind is JsonValueKind.Array;

    private static string? BlockTypeKey(JsonElement block) =>
        block.TryGetProperty(BlocksFieldType.BlockTypeKeyMember, out var key) &&
        key.ValueKind is JsonValueKind.String
            ? key.GetString()
            : null;

    private static string? TypeKey(JsonElement value) =>
        value.ValueKind is JsonValueKind.Object &&
        value.TryGetProperty(ContentPayloadMembers.Type, out var type) &&
        type.ValueKind is JsonValueKind.String
            ? type.GetString()
            : null;

    private static JsonElement Member(JsonElement owner, string name) =>
        owner.ValueKind is JsonValueKind.Object && owner.TryGetProperty(name, out var value)
            ? value
            : default;

    /// <summary>
    /// Whether two values are the same content.
    /// </summary>
    /// <remarks>
    /// Compared as canonical text rather than by walking the two documents. Member order is not
    /// meaningful inside a stored value — <c>ContentPayloadBuilder</c> preserves zone order for the
    /// diff's benefit, but nothing preserves it inside a block's properties — so the raw text is
    /// re-serialised before comparison.
    /// </remarks>
    private static bool SameJson(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;

        return string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.Ordinal);
    }

    private static string Canonicalize(JsonElement value)
    {
        if (value.ValueKind is not JsonValueKind.Object && value.ValueKind is not JsonValueKind.Array)
        {
            return value.ToString();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(value, writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }

                writer.WriteEndObject();

                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in value.EnumerateArray())
                {
                    Write(item, writer);
                }

                writer.WriteEndArray();

                break;

            default:
                value.WriteTo(writer);

                break;
        }
    }
}
