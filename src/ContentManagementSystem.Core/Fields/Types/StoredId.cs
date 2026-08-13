using System.Text.Json;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Reads the entity ids that reference-bearing field types store.
/// </summary>
/// <remarks>
/// Every reference in the content model is a positive integer identity written by the server, so
/// the same three questions are asked of a <c>mediaId</c>, a <c>pageId</c>, and a
/// <c>reusableContentId</c>: is it there, is it a number, and is it a number an entity could
/// actually have. Asking them in one place keeps <c>ExtractReferences</c> and <c>ValidateAsync</c>
/// from drifting apart — a value the validator rejects but the extractor still reports would put a
/// row in <c>ContentReference</c> pointing at nothing.
/// </remarks>
internal static class StoredId
{
    /// <summary>
    /// Reads an id member of a stored value.
    /// </summary>
    /// <param name="owner">The object carrying the member.</param>
    /// <param name="member">The member name, such as <c>mediaId</c>.</param>
    /// <param name="id">The identity read.</param>
    /// <returns><see langword="true"/> when the member is a positive integer.</returns>
    public static bool TryRead(JsonElement owner, string member, out int id)
    {
        if (owner.ValueKind is JsonValueKind.Object &&
            owner.TryGetProperty(member, out var value) &&
            value.ValueKind is JsonValueKind.Number &&
            value.TryGetInt32(out var parsed) &&
            parsed > 0)
        {
            id = parsed;

            return true;
        }

        id = 0;

        return false;
    }

    /// <summary>
    /// Reads an element that is itself an id.
    /// </summary>
    /// <param name="value">The element.</param>
    /// <param name="id">The identity read.</param>
    /// <returns><see langword="true"/> when the element is a positive integer.</returns>
    public static bool TryRead(JsonElement value, out int id)
    {
        if (value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out var parsed) && parsed > 0)
        {
            id = parsed;

            return true;
        }

        id = 0;

        return false;
    }
}
