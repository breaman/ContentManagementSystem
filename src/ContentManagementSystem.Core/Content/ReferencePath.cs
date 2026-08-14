using System.Text.Json;

using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Content;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Where in a payload a reference was found, resolved from the path the indexer reported.
/// </summary>
/// <param name="ZoneKey">Zone the reference sits in, or null when the path names none.</param>
/// <param name="BlockId">
/// Stable GUID of the block instance holding it, or null when the zone holds it directly.
/// </param>
/// <param name="PropertyKey">Property within that block, or null.</param>
public readonly record struct ReferenceLocation(string? ZoneKey, Guid? BlockId, string? PropertyKey);

/// <summary>
/// Turns a payload path such as <c>zones.body.items[1].properties.image</c> into the coordinates the
/// <c>ContentReference</c> table stores.
/// </summary>
/// <remarks>
/// The columns exist so the backoffice can show an editor <em>where</em> a reference is, and so the
/// row survives a reorder. The path alone cannot do the second job — it carries the block's index,
/// and an index changes the moment somebody drags a block — so the block's stable GUID is read back
/// out of the payload at the position the path names (spec section 11.4).
/// <para>
/// Everything here degrades to null rather than throwing. These coordinates are a convenience for
/// the editor; the reference row's job is to record the edge, and a row with no zone key is far
/// better than a save that failed because a path had a shape this parser did not expect.
/// </para>
/// </remarks>
public static class ReferencePath
{
    /// <summary>
    /// Resolves a path against the payload it was produced from.
    /// </summary>
    /// <param name="path">The absolute payload path, or null.</param>
    /// <param name="payload">The payload the path came from.</param>
    /// <returns>The coordinates, with anything the path does not name left null.</returns>
    public static ReferenceLocation Parse(string? path, ContentPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrEmpty(path)) return default;

        var segments = Split(path);

        // Every path the indexer builds starts "zones.<key>"; anything else came from somewhere this
        // parser does not know about, and guessing at it would put a wrong zone key on the row.
        if (segments.Count < 2 || segments[0].Name != ContentPayloadMembers.Zones) return default;

        var zoneKey = segments[1].Name;
        var value = payload.GetZone(zoneKey);

        Guid? blockId = null;
        string? propertyKey = null;

        for (var i = 2; i < segments.Count && value.ValueKind is not JsonValueKind.Undefined; i++)
        {
            var segment = segments[i];

            value = segment.Index is { } index
                ? Element(value, segment.Name, index)
                : Member(value, segment.Name);

            // An element of an "items" array is a block instance, and its id is the identity the
            // diff and the editor both address it by. A nested blocks property overwrites the outer
            // one on purpose: the innermost block is the one that actually holds the reference.
            if (segment.Index is not null &&
                segment.Name == BlocksFieldType.ItemsMember &&
                value.ValueKind is JsonValueKind.Object &&
                value.TryGetProperty(BlocksFieldType.IdMember, out var id) &&
                id.ValueKind is JsonValueKind.String &&
                Guid.TryParse(id.GetString(), out var parsed))
            {
                blockId = parsed;
                propertyKey = null;
            }

            // The member immediately after "properties" is the property key; anything deeper is
            // inside that property's own value and does not rename it.
            if (segment.Index is null &&
                i > 2 &&
                segments[i - 1].Name == BlocksFieldType.PropertiesMember &&
                segments[i - 1].Index is null)
            {
                propertyKey = segment.Name;
            }
        }

        return new ReferenceLocation(zoneKey, blockId, propertyKey);
    }

    /// <summary>One step of a path: a member name, optionally indexed.</summary>
    private readonly record struct PathSegment(string Name, int? Index);

    /// <summary>
    /// Breaks a path into its segments.
    /// </summary>
    /// <remarks>
    /// <c>items[1]</c> is one segment carrying an index rather than two, because the array and the
    /// element it holds are the same step as far as the coordinates are concerned.
    /// </remarks>
    private static List<PathSegment> Split(string path)
    {
        var segments = new List<PathSegment>();

        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bracket = part.IndexOf('[', StringComparison.Ordinal);

            if (bracket < 0 || !part.EndsWith(']'))
            {
                segments.Add(new PathSegment(part, null));

                continue;
            }

            var inner = part[(bracket + 1)..^1];

            segments.Add(int.TryParse(inner, out var index)
                ? new PathSegment(part[..bracket], index)
                : new PathSegment(part, null));
        }

        return segments;
    }

    private static JsonElement Member(JsonElement owner, string name) =>
        owner.ValueKind is JsonValueKind.Object && owner.TryGetProperty(name, out var value)
            ? value
            : default;

    private static JsonElement Element(JsonElement owner, string name, int index)
    {
        var array = string.IsNullOrEmpty(name) ? owner : Member(owner, name);

        if (array.ValueKind is not JsonValueKind.Array || index < 0 || index >= array.GetArrayLength())
        {
            return default;
        }

        return array[index];
    }
}
