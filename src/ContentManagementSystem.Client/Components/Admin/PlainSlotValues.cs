using System.Text.Json;

using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Components.Admin;

/// <summary>
/// Moves values between a payload envelope and the per-slot strings the editing forms hold.
/// </summary>
/// <remarks>
/// <strong>One slot, one stored value, as JSON text.</strong> Every field editor binds to the whole
/// <c>{ "type": …, "value": … }</c> envelope rather than to the text inside it, so this type no
/// longer interprets anything: it reads a slot's stored value out of the payload and writes it back,
/// and what that value means is the business of the component that draws it.
/// <para>
/// That division arrived with the field editors of P6-06 to P6-15 and is the point of them. Before
/// them this class had to know that a <c>richText</c> value keeps its format beside its text and
/// that a <c>media</c> value does not — knowledge that belongs in one component per field type, not
/// in a switch statement shared by every form. What is left here is the envelope, which is the same
/// for all of them.
/// </para>
/// <para>
/// Shared by the page editor and the reusable-content editor because a zone and a block-type
/// property are the same thing to a payload — the same keyed value, the same discriminator, the same
/// absent-versus-null distinction. Two copies would eventually differ about which of those an
/// emptied control produces, and that difference is invisible until a renderer declines a fallback
/// nobody declined.
/// </para>
/// </remarks>
internal static class PlainSlotValues
{
    /// <summary>
    /// Pulls each slot's stored value into the string its editor binds to.
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
                ? Text(stored)
                : string.Empty;
        }

        return values;
    }

    /// <summary>
    /// Folds the editors' values back into a payload envelope.
    /// </summary>
    /// <param name="contentJson">The payload as it stands, so untouched members survive.</param>
    /// <param name="schemaKey">Template key, or the block type key for reusable content.</param>
    /// <param name="revision">The captured revision the payload names.</param>
    /// <param name="slots">The slots the captured revision declares.</param>
    /// <param name="values">What each editor holds, as stored JSON.</param>
    /// <returns>The payload, ready to save.</returns>
    /// <remarks>
    /// An emptied editor <em>removes</em> the slot rather than storing null. Absent means never
    /// authored and null means deliberately cleared, and the payload reader keeps them apart on
    /// purpose (spec section 6.2); writing null for a control somebody simply never filled in would
    /// tell the renderer a fallback was declined.
    /// <para>
    /// A slot whose value is not well-formed JSON is left exactly as it was found rather than being
    /// written through. An editor cannot produce one — every one of them writes through
    /// <c>StoredValue</c> — so this can only be a value the form never touched, and the honest thing
    /// to do with a value nothing understood is not to rewrite it.
    /// </para>
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
            var stored = values.TryGetValue(slot.Key, out var value) ? value : string.Empty;

            if (string.IsNullOrWhiteSpace(stored))
            {
                builder.RemoveZone(slot.Key);

                continue;
            }

            if (!IsWellFormed(stored))
            {
                continue;
            }

            builder.SetZone(slot.Key, stored);
        }

        return builder.BuildJson();
    }

    /// <summary>The stored value as compact JSON text, or empty when there is nothing stored.</summary>
    private static string Text(JsonElement stored) =>
        stored.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? string.Empty
            : stored.GetRawText();

    private static bool IsWellFormed(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
