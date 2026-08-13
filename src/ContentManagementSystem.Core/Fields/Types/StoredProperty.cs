using System.Buffers;
using System.Text.Json;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Rewrites one member of a stored property object.
/// </summary>
/// <remarks>
/// <see cref="JsonElement"/> is a read-only window over parsed bytes, so "sanitize this value"
/// necessarily means "write a new object". Every other member is copied through verbatim rather than
/// re-modelled: a stored property may carry members this build knows nothing about — written by a
/// newer deployment, or by a field type's own extension — and dropping them on a save would be
/// silent data loss.
/// </remarks>
internal static class StoredProperty
{
    /// <summary>
    /// Returns a copy of a property object with one string member replaced.
    /// </summary>
    /// <param name="property">The stored property object.</param>
    /// <param name="member">Name of the member to replace.</param>
    /// <param name="value">The replacement text.</param>
    /// <returns>
    /// A detached element, safe to hold after the document it was built from is disposed.
    /// </returns>
    public static JsonElement WithStringMember(JsonElement property, string member, string value)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            var replaced = false;

            foreach (var existing in property.EnumerateObject())
            {
                if (existing.NameEquals(member))
                {
                    writer.WriteString(member, value);
                    replaced = true;
                }
                else
                {
                    existing.WriteTo(writer);
                }
            }

            if (!replaced) writer.WriteString(member, value);

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);

        return document.RootElement.Clone();
    }
}
