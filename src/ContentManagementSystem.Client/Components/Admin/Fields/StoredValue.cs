using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Content;

namespace ContentManagementSystem.Client.Components.Admin.Fields;

/// <summary>
/// Reads and rewrites the <c>{ "type": …, "value": … }</c> envelope every stored field value carries
/// (spec section 6.2), for the editors that fill it in.
/// </summary>
/// <remarks>
/// <strong>Every field editor binds to the stored value as JSON text, never to a parsed model.</strong>
/// The payload is runtime-shaped data with no CLR type, and an editor that deserialized it into one
/// would have to decide what to do with members it did not recognise — the answer being "lose them",
/// which is how a crop written by the media screen disappears when somebody edits the alt text. Every
/// helper here rewrites the members it was asked about and leaves the rest of the object exactly as
/// it was found.
/// <para>
/// Nothing here throws on a malformed value. A value an editor cannot parse is one the validator has
/// already complained about against the same property, and a second complaint from the control would
/// put two messages on one defect; the editor shows an empty control instead, and what the author
/// types replaces it.
/// </para>
/// <para>
/// The empty string means <em>no value at all</em>, which the surrounding form turns into a removed
/// slot. Absent and null are different facts about a zone and the payload reader keeps them apart on
/// purpose — writing null for a control somebody simply never filled in would tell the renderer a
/// fallback had been declined.
/// </para>
/// </remarks>
public static class StoredValue
{
    /// <summary>The member every field type stores its value under, where it stores just one.</summary>
    public const string ValueMember = "value";

    /// <summary>
    /// Parses a stored value into an object that can be rewritten.
    /// </summary>
    /// <param name="json">The stored value as JSON text, or empty when nothing is authored.</param>
    /// <returns>The stored object, or null when there is nothing readable.</returns>
    public static JsonObject? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a stored value, or starts a fresh envelope for the field type.
    /// </summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <param name="fieldTypeKey">The field type writing the value.</param>
    /// <returns>An object carrying at least the discriminator.</returns>
    public static JsonObject ParseOrNew(string? json, string fieldTypeKey)
    {
        var stored = Parse(json) ?? [];

        stored[ContentPayloadMembers.Type] = fieldTypeKey;

        return stored;
    }

    /// <summary>The <c>value</c> member as text, or null when it is absent or not a string.</summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <returns>The authored text.</returns>
    public static string? ReadText(string? json) => ReadText(json, ValueMember);

    /// <summary>A string member of the stored value, or null when it is absent or not a string.</summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <param name="member">The member to read.</param>
    /// <returns>The member's text.</returns>
    public static string? ReadText(string? json, string member) =>
        Parse(json) is { } stored && stored[member]?.GetValueKind() is JsonValueKind.String
            ? stored[member]!.GetValue<string>()
            : null;

    /// <summary>A number member of the stored value, or null when it is absent or not a number.</summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <param name="member">The member to read.</param>
    /// <returns>The member's value.</returns>
    public static decimal? ReadNumber(string? json, string member = ValueMember) =>
        Parse(json) is { } stored && stored[member]?.GetValueKind() is JsonValueKind.Number
            ? stored[member]!.GetValue<decimal>()
            : null;

    /// <summary>An integer member of the stored value, or null when it is absent or not a number.</summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <param name="member">The member to read.</param>
    /// <returns>The member's value.</returns>
    public static int? ReadInt32(string? json, string member = ValueMember) =>
        Parse(json) is { } stored &&
        stored[member]?.GetValueKind() is JsonValueKind.Number &&
        stored[member]!.GetValue<decimal>() is var number &&
        number == decimal.Truncate(number) &&
        number is >= int.MinValue and <= int.MaxValue
            ? (int)number
            : null;

    /// <summary>A boolean member of the stored value, or null when it is absent or not a boolean.</summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <param name="member">The member to read.</param>
    /// <returns>The member's value.</returns>
    public static bool? ReadBoolean(string? json, string member = ValueMember) =>
        Parse(json) is { } stored && stored[member]?.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? stored[member]!.GetValue<bool>()
            : null;

    /// <summary>
    /// The strings in an array member, tolerating a single value written without the array.
    /// </summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <param name="member">The member holding the list.</param>
    /// <returns>The strings, in stored order.</returns>
    /// <remarks>
    /// <c>choice</c> stores one value or an array under the same member depending on its
    /// configuration, so a control whose property was switched to multiple has to be able to read
    /// what was written before the switch.
    /// </remarks>
    public static IReadOnlyList<string> ReadTextList(string? json, string member = ValueMember)
    {
        if (Parse(json) is not { } stored || stored[member] is not { } node) return [];

        return node switch
        {
            JsonArray array =>
            [
                .. array
                    .Where(entry => entry?.GetValueKind() is JsonValueKind.String)
                    .Select(entry => entry!.GetValue<string>()),
            ],
            _ when node.GetValueKind() is JsonValueKind.String => [node.GetValue<string>()],
            _ => [],
        };
    }

    /// <summary>The objects in an array member, in stored order.</summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <param name="member">The member holding the list.</param>
    /// <returns>The items, skipping anything that is not an object.</returns>
    public static IReadOnlyList<JsonObject> ReadItems(string? json, string member)
    {
        if (Parse(json) is not { } stored || stored[member] is not JsonArray array) return [];

        return [.. array.OfType<JsonObject>()];
    }

    /// <summary>
    /// Rewrites the <c>value</c> member, keeping every other member of the envelope.
    /// </summary>
    /// <param name="json">The stored value as it stands.</param>
    /// <param name="fieldTypeKey">The field type writing the value.</param>
    /// <param name="value">The new value, or null to remove the whole envelope.</param>
    /// <returns>The rewritten JSON, or empty when the value is nothing.</returns>
    public static string Write(string? json, string fieldTypeKey, JsonNode? value)
    {
        if (value is null) return string.Empty;

        var stored = ParseOrNew(json, fieldTypeKey);

        stored[ValueMember] = value;

        return stored.ToJsonString();
    }

    /// <summary>
    /// Rewrites the envelope through a callback, keeping every member the callback does not touch.
    /// </summary>
    /// <param name="json">The stored value as it stands.</param>
    /// <param name="fieldTypeKey">The field type writing the value.</param>
    /// <param name="write">Sets the members this field type owns.</param>
    /// <returns>The rewritten JSON.</returns>
    public static string Write(string? json, string fieldTypeKey, Action<JsonObject> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var stored = ParseOrNew(json, fieldTypeKey);

        write(stored);

        return stored.ToJsonString();
    }

    /// <summary>
    /// Rewrites the <c>value</c> member from text, removing the envelope when the text is empty.
    /// </summary>
    /// <param name="json">The stored value as it stands.</param>
    /// <param name="fieldTypeKey">The field type writing the value.</param>
    /// <param name="text">What the control holds.</param>
    /// <returns>The rewritten JSON, or empty when the control holds nothing.</returns>
    /// <remarks>
    /// Empty removes rather than storing <c>""</c>, which is the rule the plain forms already follow:
    /// a box nobody filled in is an unauthored slot, and an empty string is a value an author chose.
    /// The two are different to a renderer deciding whether to use its fallback.
    /// </remarks>
    public static string WriteText(string? json, string fieldTypeKey, string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : Write(json, fieldTypeKey, JsonValue.Create(text));

    /// <summary>Builds a JSON array of strings, or null when the list is empty.</summary>
    /// <param name="values">The strings to store.</param>
    /// <returns>The array, or null so the caller can remove the value.</returns>
    public static JsonArray? TextList(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var array = new JsonArray([.. values.Select(value => (JsonNode?)JsonValue.Create(value))]);

        return array.Count > 0 ? array : null;
    }

    /// <summary>Formats a stored value for a human to read, indented.</summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <returns>The indented JSON, or the input unchanged when it cannot be parsed.</returns>
    /// <remarks>
    /// The only thing to do with a value nothing can edit is read it, and one line of minified JSON
    /// is not reading.
    /// </remarks>
    public static string Indent(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(json);

            return JsonSerializer.Serialize(document.RootElement, IndentedJson);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
}
