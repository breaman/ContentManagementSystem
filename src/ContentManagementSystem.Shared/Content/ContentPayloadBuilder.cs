using System.Buffers;
using System.Text.Json;

namespace ContentManagementSystem.Shared.Content;

/// <summary>
/// Writes a content payload envelope (spec section 6.2).
/// </summary>
/// <remarks>
/// The write half of <see cref="ContentPayload"/>, and the only supported way to produce one:
/// assembling the envelope by hand in a caller is how a payload ends up missing its
/// <c>schemaVersion</c> or carrying zones in an order that churns the diff.
/// <para>
/// Zones keep insertion order, and a builder started from an existing payload keeps that payload's
/// order and every envelope member this build does not recognise. Both matter for the same reason:
/// a draft save that reorders or drops members turns a one-property edit into a whole-document
/// change in the version diff (spec section 11.4), or into silent data loss when the member came
/// from a newer deployment.
/// </para>
/// <para>
/// Not thread-safe. A builder is a short-lived, single-caller object; the payload it produces is the
/// shareable one.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var payload = new ContentPayloadBuilder(draft)
///     .SetZone("headline", authoredValue)
///     .ClearZone("subtitle")
///     .Build();
/// </code>
/// </example>
public sealed class ContentPayloadBuilder
{
    private static readonly JsonElement NullElement = ParseNull();

    private readonly List<KeyValuePair<string, JsonElement>> _zones = [];
    private readonly ContentPayload? _source;

    /// <summary>
    /// Starts a payload for content authored against a template revision.
    /// </summary>
    /// <param name="templateKey">Key of the template.</param>
    /// <param name="templateRevision">The template revision being captured.</param>
    public ContentPayloadBuilder(string templateKey, int templateRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        TemplateKey = templateKey;
        TemplateRevision = templateRevision;
        SchemaVersion = ContentPayload.CurrentSchemaVersion;
    }

    /// <summary>
    /// Starts from an existing payload, so an edit rewrites what it touches and nothing else.
    /// </summary>
    /// <param name="payload">The payload to copy.</param>
    /// <remarks>
    /// The envelope is taken as it stands, including a <c>schemaVersion</c> older than this build's:
    /// re-stamping it would claim a shape the zones do not have. Use
    /// <see cref="WithSchemaVersion"/> when a migration has actually rewritten them.
    /// </remarks>
    public ContentPayloadBuilder(ContentPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        _source = payload;
        SchemaVersion = payload.SchemaVersion ?? ContentPayload.CurrentSchemaVersion;
        TemplateKey = payload.TemplateKey ?? string.Empty;
        TemplateRevision = payload.TemplateRevision ?? 0;

        if (payload.HasZones)
        {
            foreach (var zone in payload.Zones.EnumerateObject())
            {
                _zones.Add(new KeyValuePair<string, JsonElement>(zone.Name, zone.Value));
            }
        }
    }

    /// <summary>The envelope version that will be written.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>The template key that will be written.</summary>
    public string TemplateKey { get; private set; }

    /// <summary>The template revision that will be written.</summary>
    public int TemplateRevision { get; private set; }

    /// <summary>Sets the envelope version.</summary>
    /// <param name="schemaVersion">The version to write.</param>
    /// <returns>This builder, for chaining.</returns>
    public ContentPayloadBuilder WithSchemaVersion(int schemaVersion)
    {
        SchemaVersion = schemaVersion;

        return this;
    }

    /// <summary>
    /// Captures a template revision, as opening a page against a newer revision does.
    /// </summary>
    /// <param name="templateKey">Key of the template.</param>
    /// <param name="templateRevision">The revision to capture.</param>
    /// <returns>This builder, for chaining.</returns>
    public ContentPayloadBuilder WithTemplate(string templateKey, int templateRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        TemplateKey = templateKey;
        TemplateRevision = templateRevision;

        return this;
    }

    /// <summary>Sets a zone's stored value.</summary>
    /// <param name="zoneKey">The zone key.</param>
    /// <param name="value">The value as the zone's field type stores it.</param>
    /// <returns>This builder, for chaining.</returns>
    public ContentPayloadBuilder SetZone(string zoneKey, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneKey);

        var index = IndexOf(zoneKey);
        var entry = new KeyValuePair<string, JsonElement>(zoneKey, value);

        if (index < 0)
        {
            _zones.Add(entry);
        }
        else
        {
            // Replaced in place: a value that moves to the end of the object reads as a removal plus
            // an addition to anything comparing two payloads.
            _zones[index] = entry;
        }

        return this;
    }

    /// <summary>Sets a zone's stored value from JSON text.</summary>
    /// <param name="zoneKey">The zone key.</param>
    /// <param name="valueJson">The value as JSON.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="JsonException">The text is not well-formed JSON.</exception>
    public ContentPayloadBuilder SetZone(string zoneKey, string valueJson)
    {
        ArgumentNullException.ThrowIfNull(valueJson);

        using var document = JsonDocument.Parse(valueJson);

        return SetZone(zoneKey, document.RootElement.Clone());
    }

    /// <summary>
    /// Marks a zone as explicitly cleared, writing <c>null</c>.
    /// </summary>
    /// <param name="zoneKey">The zone key.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Not the same as <see cref="RemoveZone"/>. Cleared says an editor emptied the zone; absent says
    /// it was never authored (spec section 6.2).
    /// </remarks>
    public ContentPayloadBuilder ClearZone(string zoneKey) => SetZone(zoneKey, NullElement);

    /// <summary>
    /// Removes a zone from the payload entirely, as though it had never been authored.
    /// </summary>
    /// <param name="zoneKey">The zone key.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Reserved for the one case that means it: an editor discarding orphaned content from the
    /// "obsolete content" panel (spec section 8.5). Removing a zone that is still in the template
    /// loses the fact that it was cleared on purpose.
    /// </remarks>
    public ContentPayloadBuilder RemoveZone(string zoneKey)
    {
        var index = IndexOf(zoneKey);

        if (index >= 0) _zones.RemoveAt(index);

        return this;
    }

    /// <summary>Builds the payload.</summary>
    /// <returns>The payload.</returns>
    public ContentPayload Build()
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);

        return ContentPayload.FromElement(document.RootElement);
    }

    /// <summary>Builds the payload as the JSON text that is stored.</summary>
    /// <returns>Compact JSON.</returns>
    public string BuildJson() => Build().ToJson();

    private static JsonElement ParseNull()
    {
        using var document = JsonDocument.Parse("null");

        return document.RootElement.Clone();
    }

    private int IndexOf(string zoneKey)
    {
        // Linear: a template has zones in the dozens, and keeping one list is what keeps the order
        // stable. A dictionary alongside it would buy nothing measurable and could disagree with it.
        for (var i = 0; i < _zones.Count; i++)
        {
            if (string.Equals(_zones[i].Key, zoneKey, StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    private void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteNumber(ContentPayloadMembers.SchemaVersion, SchemaVersion);
        writer.WriteString(ContentPayloadMembers.TemplateKey, TemplateKey);
        writer.WriteNumber(ContentPayloadMembers.TemplateRevision, TemplateRevision);

        WriteUnknownEnvelopeMembers(writer);

        writer.WritePropertyName(ContentPayloadMembers.Zones);
        writer.WriteStartObject();

        foreach (var zone in _zones)
        {
            writer.WritePropertyName(zone.Key);
            zone.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Copies through any envelope member this build does not know about.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <remarks>
    /// A payload may carry members written by a newer deployment — the reverse of the situation
    /// <see cref="ContentPayload.CurrentSchemaVersion"/> guards. Dropping them on a save would be
    /// silent data loss of exactly the kind a rolling deployment produces and nobody notices until
    /// the newer nodes are back.
    /// </remarks>
    private void WriteUnknownEnvelopeMembers(Utf8JsonWriter writer)
    {
        if (_source is not { IsObject: true }) return;

        foreach (var member in _source.Root.EnumerateObject())
        {
            if (member.NameEquals(ContentPayloadMembers.SchemaVersion) ||
                member.NameEquals(ContentPayloadMembers.TemplateKey) ||
                member.NameEquals(ContentPayloadMembers.TemplateRevision) ||
                member.NameEquals(ContentPayloadMembers.Zones))
            {
                continue;
            }

            member.WriteTo(writer);
        }
    }
}
