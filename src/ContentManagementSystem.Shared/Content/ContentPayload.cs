using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentManagementSystem.Shared.Content;

/// <summary>
/// One page version's content, as the single JSON document it is stored as (spec section 6.2).
/// </summary>
/// <remarks>
/// A reader over the envelope, deliberately not a deserializer. The zones and block-type properties
/// that give a payload its shape are user-defined data, created by a <c>Developer</c> in the
/// backoffice, so there is no CLR type to bind to; and binding to one anyway is where the
/// absent-versus-null distinction goes to die. Every accessor here therefore hands back a
/// <see cref="JsonElement"/> and leaves interpretation to the field type that wrote it.
/// <para>
/// <strong>Nothing here rejects a malformed envelope.</strong> A payload whose <c>zones</c> member is
/// missing, or whose <c>schemaVersion</c> is from a future build, still parses — the accessors just
/// report what is not there. Diagnosing that is <c>ContentSchemaValidator</c>'s job (P1-15), and it
/// needs a readable object to diagnose it <em>from</em>. Only JSON that is not well-formed at all is
/// an exception.
/// </para>
/// <para>
/// The underlying element is detached from the document it was parsed out of, so an instance is
/// immutable, thread-safe, safe to cache for the fifteen minutes spec section 16.1 gives the
/// published-content cache, and — because it holds no <see cref="IDisposable"/> — impossible to read
/// after something else has disposed it. The cost is one copy of the payload bytes at parse time,
/// which is the right trade for a value that is parsed once and read on every request that renders
/// the page.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var payload = ContentPayload.Parse(version.ContentJson);
///
/// if (payload.GetZoneState("hero") is ContentValueState.Present &amp;&amp;
///     payload.TryGetZone("hero", out var hero))
/// {
///     Render(hero);
/// }
/// </code>
/// </example>
[JsonConverter(typeof(ContentPayloadJsonConverter))]
public sealed class ContentPayload
{
    /// <summary>
    /// The envelope version this build writes.
    /// </summary>
    /// <remarks>
    /// Bumped only for a change no reader of the previous shape could survive. A new optional
    /// envelope member is not one of those: readers ignore members they do not know, and a bump
    /// would strand every row written before it.
    /// </remarks>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonWriterOptions IndentedOptions = new() { Indented = true };

    private readonly JsonElement _root;

    private ContentPayload(JsonElement root) => _root = root;

    /// <summary>The payload document, for readers that need more than the envelope accessors.</summary>
    public JsonElement Root => _root;

    /// <summary>Whether the document is a JSON object, as an envelope must be.</summary>
    public bool IsObject => _root.ValueKind is JsonValueKind.Object;

    /// <summary>
    /// The declared envelope version, or null when the member is missing or is not a number.
    /// </summary>
    /// <remarks>
    /// Nullable rather than defaulted: "this payload does not say" and "this payload says 1" are
    /// different facts, and only the validator is in a position to decide what to do about the first.
    /// </remarks>
    public int? SchemaVersion => GetInt32(ContentPayloadMembers.SchemaVersion);

    /// <summary>The template this content was authored against, or null when it is not declared.</summary>
    public string? TemplateKey => GetString(ContentPayloadMembers.TemplateKey);

    /// <summary>
    /// The template revision whose schema was captured at write time, or null when it is not declared.
    /// </summary>
    /// <remarks>
    /// The rule that makes template evolution safe: a published version renders against the revision
    /// it captured, so a structural change cannot retroactively alter what is live (spec section 8.5).
    /// </remarks>
    public int? TemplateRevision => GetInt32(ContentPayloadMembers.TemplateRevision);

    /// <summary>Whether the envelope carries a <c>zones</c> object.</summary>
    public bool HasZones => Zones.ValueKind is JsonValueKind.Object;

    /// <summary>
    /// The authored content keyed by zone key, or an undefined element when the member is missing or
    /// is not an object.
    /// </summary>
    public JsonElement Zones =>
        _root.ValueKind is JsonValueKind.Object &&
        _root.TryGetProperty(ContentPayloadMembers.Zones, out var zones)
            ? zones
            : default;

    /// <summary>
    /// Every zone key present in the payload, in document order.
    /// </summary>
    /// <remarks>
    /// Document order, not schema order, because this is what the orphan sweep enumerates: a zone
    /// removed from the template is still here, and is precisely the key the schema cannot account
    /// for (spec section 8.5).
    /// </remarks>
    public IEnumerable<string> ZoneKeys
    {
        get
        {
            if (!HasZones) yield break;

            foreach (var zone in Zones.EnumerateObject())
            {
                yield return zone.Name;
            }
        }
    }

    /// <summary>
    /// Parses stored payload JSON.
    /// </summary>
    /// <param name="json">The <c>ContentJson</c> column value.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="JsonException">The text is not well-formed JSON.</exception>
    public static ContentPayload Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);

        // Cloned so the payload outlives the document it came from. The root clone copies the whole
        // buffer, which is what lets this type avoid IDisposable entirely — see the class remarks.
        return new ContentPayload(document.RootElement.Clone());
    }

    /// <summary>
    /// Parses stored payload JSON without throwing on malformed text.
    /// </summary>
    /// <param name="json">The <c>ContentJson</c> column value.</param>
    /// <param name="payload">The payload when the text is well-formed JSON.</param>
    /// <returns><see langword="true"/> when the text parsed.</returns>
    /// <remarks>
    /// The delivery path uses this: content that cannot be parsed at all is a logged, degraded
    /// render, never an exception on a public request (spec section 15.3).
    /// </remarks>
    public static bool TryParse(string? json, [NotNullWhen(true)] out ContentPayload? payload)
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            payload = Parse(json);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Wraps an already-parsed document.
    /// </summary>
    /// <param name="root">The payload document.</param>
    /// <returns>The payload, holding a detached copy of <paramref name="root"/>.</returns>
    public static ContentPayload FromElement(JsonElement root) => new(root.Clone());

    /// <summary>
    /// Builds the payload a page starts life with: the envelope, and no authored zones.
    /// </summary>
    /// <param name="templateKey">Key of the template the page was created from.</param>
    /// <param name="templateRevision">The template revision being captured.</param>
    /// <returns>An empty payload.</returns>
    /// <remarks>
    /// Every zone is <see cref="ContentValueState.Absent"/> rather than present-and-null: nothing has
    /// been authored yet, and "never authored" is what that means. The result is schema-valid for a
    /// draft save whatever the template requires, because a required zone only blocks publishing
    /// (spec section 8.3).
    /// </remarks>
    public static ContentPayload CreateEmpty(string templateKey, int templateRevision) =>
        new ContentPayloadBuilder(templateKey, templateRevision).Build();

    /// <summary>Looks up a zone value.</summary>
    /// <param name="zoneKey">The zone key.</param>
    /// <param name="value">
    /// The stored value when the member is present. A present-but-null zone yields
    /// <see cref="JsonValueKind.Null"/> and <see langword="true"/> — cleared is not absent.
    /// </param>
    /// <returns><see langword="true"/> when the payload carries the member at all.</returns>
    public bool TryGetZone(string zoneKey, out JsonElement value)
    {
        if (HasZones && Zones.TryGetProperty(zoneKey, out value))
        {
            return true;
        }

        value = default;

        return false;
    }

    /// <summary>Reads a zone value, or an undefined element when the zone was never authored.</summary>
    /// <param name="zoneKey">The zone key.</param>
    /// <returns>
    /// The stored value. An undefined element for an absent zone; a null element for a cleared one.
    /// Both are what the field types are written to expect, so this can be passed straight to one.
    /// </returns>
    public JsonElement GetZone(string zoneKey) => TryGetZone(zoneKey, out var value) ? value : default;

    /// <summary>Reports whether a zone was never authored, explicitly cleared, or holds a value.</summary>
    /// <param name="zoneKey">The zone key.</param>
    /// <returns>The zone's state.</returns>
    public ContentValueState GetZoneState(string zoneKey) =>
        !TryGetZone(zoneKey, out var value)
            ? ContentValueState.Absent
            : value.ValueKind is JsonValueKind.Null
                ? ContentValueState.Cleared
                : ContentValueState.Present;

    /// <summary>Writes the payload to a JSON writer, unchanged.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _root.WriteTo(writer);
    }

    /// <summary>
    /// Serializes the payload back to JSON.
    /// </summary>
    /// <param name="indented">Whether to write human-readable JSON. Storage uses the compact form.</param>
    /// <returns>The JSON text.</returns>
    /// <remarks>
    /// Round-tripping is lossless for everything that carries meaning: member order, absent-versus-
    /// null, and members this build knows nothing about are all preserved, because the document is
    /// written back out rather than re-modelled. Insignificant whitespace is not preserved, which is
    /// why the snapshot tests compare canonicalised text.
    /// <para>
    /// The writer's default encoder is kept, so <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c> and non-ASCII
    /// are escaped. A relaxed encoder would store authored HTML more compactly, and the reason not to
    /// is that a payload does not stay in its column: it reaches the editor as an API response and
    /// could reach a page as embedded JSON, and escaping that is safe everywhere beats escaping that
    /// is safe until someone inlines it into a <c>&lt;script&gt;</c> block.
    /// </para>
    /// </remarks>
    public string ToJson(bool indented = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, indented ? IndentedOptions : default))
        {
            _root.WriteTo(writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <inheritdoc />
    public override string ToString() => ToJson();

    private string? GetString(string member) =>
        _root.ValueKind is JsonValueKind.Object &&
        _root.TryGetProperty(member, out var value) &&
        value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private int? GetInt32(string member) =>
        _root.ValueKind is JsonValueKind.Object &&
        _root.TryGetProperty(member, out var value) &&
        value.ValueKind is JsonValueKind.Number &&
        value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}
