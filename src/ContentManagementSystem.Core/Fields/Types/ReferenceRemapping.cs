using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// The small amount of JSON surgery every reference-bearing field type needs to rewrite its own
/// stored ids (spec section 14.12).
/// </summary>
/// <remarks>
/// The rewriting works on <see cref="JsonNode"/> rather than on <see cref="JsonElement"/>, which is
/// immutable. That is the right trade here and the wrong one on the validation path: duplication is
/// a rare, deliberate operation, whereas validation runs on every save and is why the rest of the
/// engine stays on the read-only DOM.
/// </remarks>
internal static class ReferenceRemapping
{
    /// <summary>Takes a mutable copy of a stored value.</summary>
    /// <param name="value">The value as stored.</param>
    /// <returns>The copy, or null when the value is not an object.</returns>
    public static JsonObject? Clone(JsonElement value) =>
        value.ValueKind is JsonValueKind.Object
            ? JsonNode.Parse(value.GetRawText())?.AsObject()
            : null;

    /// <summary>
    /// Rewrites an id member in place.
    /// </summary>
    /// <param name="owner">The object carrying the member.</param>
    /// <param name="member">The member name, such as <c>pageId</c>.</param>
    /// <param name="targetType">What kind of entity the member points at.</param>
    /// <param name="remap">Supplies the replacement.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool RemapMember(
        JsonObject owner,
        string member,
        ContentReferenceTargetType targetType,
        ReferenceRemapper remap)
    {
        if (owner[member] is not JsonValue node ||
            !node.TryGetValue<int>(out var id) ||
            id <= 0)
        {
            return false;
        }

        var replacement = remap(targetType, id);

        if (replacement == id) return false;

        owner[member] = replacement;

        return true;
    }

    /// <summary>
    /// Rewrites a member that holds either a single id or an array of them.
    /// </summary>
    /// <param name="owner">The object carrying the member.</param>
    /// <param name="member">The member name.</param>
    /// <param name="targetType">What kind of entity the ids point at.</param>
    /// <param name="remap">Supplies the replacements.</param>
    /// <returns><see langword="true"/> when anything changed.</returns>
    /// <remarks>
    /// Both shapes, because a field type configured for multiple values stores an array under the
    /// same member rather than inventing a second one — the rule <c>choice</c> set and
    /// <c>pageReference</c> follows.
    /// </remarks>
    public static bool RemapIdOrArray(
        JsonObject owner,
        string member,
        ContentReferenceTargetType targetType,
        ReferenceRemapper remap)
    {
        if (owner[member] is not JsonArray array) return RemapMember(owner, member, targetType, remap);

        var changed = false;

        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonValue node || !node.TryGetValue<int>(out var id) || id <= 0) continue;

            var replacement = remap(targetType, id);

            if (replacement == id) continue;

            array[i] = replacement;
            changed = true;
        }

        return changed;
    }
}
