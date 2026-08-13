using System.Buffers;
using System.Text.Json;

using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Content.Schema;

/// <summary>
/// Reads and writes the schema snapshots carried by <see cref="TemplateRevision.ZoneSnapshotJson"/>
/// and <see cref="BlockTypeRevision.PropertySnapshotJson"/>.
/// </summary>
/// <remarks>
/// The snapshot columns exist so that a page version can be validated and rendered against the
/// structure as it stood when the content was written (spec section 8.5). This type is the single
/// definition of what goes in them:
/// <code>
/// [
///   {
///     "key": "hero",
///     "name": "Hero",
///     "fieldTypeKey": "blocks",
///     "isRequired": true,
///     "sortOrder": 0,
///     "configuration": { "allowedBlockTypes": ["hero-banner"], "max": 3 }
///   }
/// ]
/// </code>
/// One array, in editor order, for zones and for block-type properties alike — the two are the same
/// shape, and giving them one format means a revision reader, a schema exporter (P1-28), and the
/// structure API cannot drift into two.
/// <para>
/// <c>configuration</c> is embedded as an object rather than as an escaped string. A snapshot is read
/// far more often than it is written, and nesting it avoids a second parse per property on every
/// read; it also keeps the column diffable by a human reviewing a structure promotion.
/// </para>
/// </remarks>
public static class ContentSchemaSnapshot
{
    /// <summary>Member holding the stable key.</summary>
    public const string KeyMember = "key";

    /// <summary>Member holding the editor-facing label.</summary>
    public const string NameMember = "name";

    /// <summary>Member holding the field type key.</summary>
    public const string FieldTypeKeyMember = "fieldTypeKey";

    /// <summary>Member holding whether an empty value blocks publishing.</summary>
    public const string IsRequiredMember = "isRequired";

    /// <summary>Member holding the editor sort order.</summary>
    public const string SortOrderMember = "sortOrder";

    /// <summary>Member holding the field-type configuration object.</summary>
    public const string ConfigurationMember = "configuration";

    /// <summary>
    /// Reads a snapshot into schema slots.
    /// </summary>
    /// <param name="snapshotJson">The snapshot column value.</param>
    /// <returns>The slots, in the order the snapshot lists them.</returns>
    /// <exception cref="JsonException">
    /// The snapshot is not well-formed, is not an array, or holds an entry with no usable key or
    /// field type. Thrown rather than skipped: a slot silently dropped here turns into an orphaned-
    /// content warning on every page using the template, which is a great deal harder to trace back
    /// than a structure row that refuses to load.
    /// </exception>
    public static IReadOnlyList<ContentPropertySchema> Read(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson)) return [];

        using var document = JsonDocument.Parse(snapshotJson);

        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            throw new JsonException("A schema snapshot is an array of slot definitions.");
        }

        var slots = new List<ContentPropertySchema>(document.RootElement.GetArrayLength());
        var index = 0;

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            slots.Add(ReadSlot(entry, index));
            index++;
        }

        return slots;
    }

    /// <summary>Reads a template revision's snapshot.</summary>
    /// <param name="templateKey">Key of the template.</param>
    /// <param name="revisionNumber">The revision number.</param>
    /// <param name="snapshotJson">The <c>ZoneSnapshotJson</c> column value.</param>
    /// <returns>The revision's schema.</returns>
    /// <exception cref="JsonException">The snapshot is not readable.</exception>
    public static ContentSchema ReadTemplate(string templateKey, int revisionNumber, string? snapshotJson) =>
        new(templateKey, revisionNumber, Read(snapshotJson));

    /// <summary>Reads a block type revision's snapshot.</summary>
    /// <param name="blockTypeKey">Key of the block type.</param>
    /// <param name="revisionNumber">The revision number.</param>
    /// <param name="snapshotJson">The <c>PropertySnapshotJson</c> column value.</param>
    /// <returns>The revision's schema.</returns>
    /// <exception cref="JsonException">The snapshot is not readable.</exception>
    public static BlockTypeSchema ReadBlockType(string blockTypeKey, int revisionNumber, string? snapshotJson) =>
        new(blockTypeKey, revisionNumber, Read(snapshotJson));

    /// <summary>
    /// Writes the snapshot for a template's zones, as cutting a revision does.
    /// </summary>
    /// <param name="zones">The zone definitions to capture.</param>
    /// <returns>JSON for the <c>ZoneSnapshotJson</c> column.</returns>
    /// <exception cref="JsonException">A zone's <c>ConfigurationJson</c> is not well-formed.</exception>
    public static string WriteZones(IEnumerable<Zone> zones)
    {
        ArgumentNullException.ThrowIfNull(zones);

        return Write(zones
            .OrderBy(zone => zone.SortOrder)
            .ThenBy(zone => zone.Key, StringComparer.Ordinal)
            .Select(zone => (zone.Key, zone.Name, zone.FieldTypeKey, zone.ConfigurationJson,
                zone.IsRequired, zone.SortOrder)));
    }

    /// <summary>
    /// Writes the snapshot for a block type's properties, compositions already resolved.
    /// </summary>
    /// <param name="properties">The property definitions to capture.</param>
    /// <returns>JSON for the <c>PropertySnapshotJson</c> column.</returns>
    /// <exception cref="JsonException">A property's <c>ConfigurationJson</c> is not well-formed.</exception>
    public static string WriteProperties(IEnumerable<BlockTypeProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return Write(properties
            .OrderBy(property => property.SortOrder)
            .ThenBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => (property.Key, property.Name, property.FieldTypeKey,
                property.ConfigurationJson, property.IsRequired, property.SortOrder)));
    }

    private static ContentPropertySchema ReadSlot(JsonElement entry, int index)
    {
        if (entry.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException($"Schema snapshot entry {index} is not an object.");
        }

        if (ReadString(entry, KeyMember) is not { Length: > 0 } key)
        {
            throw new JsonException($"Schema snapshot entry {index} declares no '{KeyMember}'.");
        }

        if (ReadString(entry, FieldTypeKeyMember) is not { Length: > 0 } fieldTypeKey)
        {
            throw new JsonException($"Schema snapshot entry '{key}' declares no '{FieldTypeKeyMember}'.");
        }

        var configuration = entry.TryGetProperty(ConfigurationMember, out var value) ? value : default;

        return ContentPropertySchema.Create(
            key,
            ReadString(entry, NameMember) ?? key,
            fieldTypeKey,
            configuration,
            entry.TryGetProperty(IsRequiredMember, out var required) &&
                required.ValueKind is JsonValueKind.True,
            entry.TryGetProperty(SortOrderMember, out var sortOrder) &&
                sortOrder.ValueKind is JsonValueKind.Number && sortOrder.TryGetInt32(out var parsed)
                    ? parsed
                    : index);
    }

    private static string? ReadString(JsonElement entry, string member) =>
        entry.TryGetProperty(member, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Write(
        IEnumerable<(string Key, string Name, string FieldTypeKey, string? ConfigurationJson,
            bool IsRequired, int SortOrder)> slots)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();

            foreach (var slot in slots)
            {
                writer.WriteStartObject();
                writer.WriteString(KeyMember, slot.Key);
                writer.WriteString(NameMember, slot.Name);
                writer.WriteString(FieldTypeKeyMember, slot.FieldTypeKey);
                writer.WriteBoolean(IsRequiredMember, slot.IsRequired);
                writer.WriteNumber(SortOrderMember, slot.SortOrder);

                if (!string.IsNullOrWhiteSpace(slot.ConfigurationJson))
                {
                    writer.WritePropertyName(ConfigurationMember);
                    // Parsed rather than copied through as text so that a malformed configuration
                    // is caught while cutting the revision, not while validating content against it.
                    using var configuration = JsonDocument.Parse(slot.ConfigurationJson);
                    configuration.RootElement.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
