using System.Text;
using System.Text.Json;

using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Components.Admin;

/// <summary>
/// Moves values between a payload envelope and the plain textareas the unstyled admin forms use.
/// </summary>
/// <remarks>
/// <strong>Deliberately plain.</strong> Every slot is a textarea whatever its field type, and the
/// real per-field-type editors — rich text, media pickers, the block canvas — arrive in Phase 6.
/// What these forms exist to prove is the loop underneath them: a captured revision's slots become
/// controls, what is typed into them round-trips through the payload envelope, and publishing
/// snapshots it without disturbing the draft.
/// <para>
/// Shared by the page editor and the reusable-content editor because a zone and a block-type
/// property are the same thing to a payload — the same keyed value, the same discriminator, the same
/// absent-versus-null distinction. Two copies of the rules below would eventually differ in which of
/// those two states an emptied box produces, and that difference is invisible until a renderer
/// declines a fallback nobody declined.
/// </para>
/// </remarks>
internal static class PlainSlotValues
{
    /// <summary>
    /// Field types whose stored value is a single string these forms can safely round-trip.
    /// </summary>
    /// <remarks>
    /// Everything else is shown as stored JSON and written back verbatim. That is the honest
    /// behaviour for a plain editor: inventing a control for a media reference or a block list would
    /// mean inventing a shape for its value, and the first thing a real editor in Phase 6 would have
    /// to do is repair what this one wrote.
    /// </remarks>
    private static readonly HashSet<string> TextFieldTypes = new(StringComparer.Ordinal)
    {
        FieldTypeKeys.PlainText,
        FieldTypeKeys.MultilineText,
        FieldTypeKeys.RichText,
        FieldTypeKeys.Html,
    };

    /// <summary>Formatting for the read-only view of a value these forms cannot edit.</summary>
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <summary>Whether the slot gets a plain editable control rather than a read-only one.</summary>
    /// <param name="fieldTypeKey">The field type filling the slot.</param>
    /// <returns><see langword="true"/> when its value is a single string.</returns>
    public static bool Editable(string fieldTypeKey) => TextFieldTypes.Contains(fieldTypeKey);

    /// <summary>
    /// Pulls each slot's stored value into the string its control binds to.
    /// </summary>
    /// <param name="contentJson">The draft payload, as stored.</param>
    /// <param name="slots">The slots the captured revision declares.</param>
    /// <returns>One entry per slot, empty for one that has never been authored.</returns>
    /// <remarks>
    /// An unparseable payload yields empty controls rather than an exception. The alternative is an
    /// editor who cannot open the one item that needs fixing, which is the failure spec section 15.3
    /// forbids on the delivery side and which is no more acceptable here.
    /// </remarks>
    public static Dictionary<string, string> Read(string contentJson, IReadOnlyList<CapturedSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        ContentPayload.TryParse(contentJson, out var payload);

        foreach (var slot in slots)
        {
            values[slot.Key] = payload?.TryGetZone(slot.Key, out var stored) is true
                ? ReadValue(stored, slot.FieldTypeKey)
                : string.Empty;
        }

        return values;
    }

    /// <summary>
    /// Folds the controls back into a payload envelope.
    /// </summary>
    /// <param name="contentJson">The payload as it stands, so untouched members survive.</param>
    /// <param name="schemaKey">Template key, or the block type key for reusable content.</param>
    /// <param name="revision">The captured revision the payload names.</param>
    /// <param name="slots">The slots the captured revision declares.</param>
    /// <param name="values">What each control holds.</param>
    /// <returns>The payload, ready to save.</returns>
    /// <remarks>
    /// An emptied control <em>removes</em> the slot rather than storing null. Absent means never
    /// authored and null means deliberately cleared, and the payload reader keeps them apart on
    /// purpose (spec section 6.2); writing null for a box somebody simply never filled in would tell
    /// the renderer a fallback was declined.
    /// </remarks>
    public static string Build(
        string contentJson,
        string schemaKey,
        int revision,
        IReadOnlyList<CapturedSlot> slots,
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(values);

        var parsed = ContentPayload.TryParse(contentJson, out var current) && current.IsObject;

        var builder = parsed
            ? new ContentPayloadBuilder(current!)
            : new ContentPayloadBuilder(schemaKey, revision);

        foreach (var slot in slots)
        {
            var typed = values.TryGetValue(slot.Key, out var value) ? value : string.Empty;

            if (string.IsNullOrEmpty(typed))
            {
                builder.RemoveZone(slot.Key);

                continue;
            }

            builder.SetZone(slot.Key, WriteValue(slot, typed, parsed ? current : null));
        }

        return builder.BuildJson();
    }

    private static string ReadValue(JsonElement stored, string fieldTypeKey)
    {
        if (stored.ValueKind is not JsonValueKind.Object) return string.Empty;

        if (Editable(fieldTypeKey))
        {
            return stored.TryGetProperty("value", out var value) && value.ValueKind is JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        // Indented, because the only thing to do with a value these forms cannot render is read it.
        return JsonSerializer.Serialize(stored, IndentedJson);
    }

    /// <summary>Builds one slot's stored value from what its control holds.</summary>
    private static string WriteValue(CapturedSlot slot, string typed, ContentPayload? current)
    {
        if (!Editable(slot.FieldTypeKey))
        {
            // Written back exactly as it was read, so a field type these forms cannot edit survives
            // a save made for some other slot.
            return typed;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString(ContentPayloadMembers.Type, slot.FieldTypeKey);

            if (slot.FieldTypeKey == FieldTypeKeys.RichText)
            {
                // Rich text is uninterpretable without its format, and the stored one is kept so a
                // save from this screen cannot silently reinterpret an author's HTML as markdown.
                writer.WriteString("format", StoredFormat(slot.Key, current));
            }

            writer.WriteString("value", typed);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The rich-text format already stored for a slot, defaulting to markdown.</summary>
    private static string StoredFormat(string key, ContentPayload? current) =>
        current?.TryGetZone(key, out var stored) is true &&
        stored.ValueKind is JsonValueKind.Object &&
        stored.TryGetProperty("format", out var format) &&
        format.ValueKind is JsonValueKind.String &&
        format.GetString() is { Length: > 0 } value
            ? value
            : "markdown";
}
