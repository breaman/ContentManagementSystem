using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// Reads and normalises the JSON columns the structure services hand to and take from clients.
/// </summary>
/// <remarks>
/// Shared by the template and zone services so the two cannot disagree about what an unreadable
/// column or an empty configuration means — the same blob is written by one and read back by the
/// other on every round trip through the admin screens.
/// </remarks>
internal static class StructureJson
{
    /// <summary>Stands in for a snapshot or configuration that cannot be read.</summary>
    private static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement.Clone();

    /// <summary>
    /// Reads stored JSON for pass-through to the client.
    /// </summary>
    /// <param name="json">The column value.</param>
    /// <param name="logger">Log to report unreadable JSON to.</param>
    /// <param name="subject">What the JSON belongs to, for the log message.</param>
    /// <returns>The parsed value, or an empty array when it could not be read.</returns>
    /// <remarks>
    /// Unreadable JSON is logged and reported as an empty array rather than thrown. These columns
    /// are written only by this application and validated before they are stored, so a failure here
    /// means corruption — and a corrupt configuration on one zone should not take out the structure
    /// screen an operator would use to find it.
    /// </remarks>
    public static JsonElement Read(string? json, ILogger logger, string subject)
    {
        if (string.IsNullOrWhiteSpace(json)) return EmptyArray;

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Stored JSON for {Subject} could not be read.", subject);

            return EmptyArray;
        }
    }

    /// <summary>
    /// Counts the slots in a schema snapshot without validating them.
    /// </summary>
    /// <param name="snapshotJson">The snapshot column value.</param>
    /// <returns>The number of slots, or zero when the column cannot be read as an array.</returns>
    /// <remarks>
    /// A count is wanted for a history list, where one corrupt row must not fail the whole request.
    /// The revision detail endpoints hand back the snapshot itself for anyone who needs to see why.
    /// </remarks>
    public static int CountSlots(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson)) return 0;

        try
        {
            using var document = JsonDocument.Parse(snapshotJson);

            return document.RootElement.ValueKind is JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Turns a configuration sent by a client into what belongs in the column.
    /// </summary>
    /// <param name="configuration">The configuration as supplied, if any.</param>
    /// <returns>Compact JSON, or null when there is nothing to store.</returns>
    /// <remarks>
    /// An absent configuration and an empty object mean the same thing — this field type is
    /// configured with nothing — and collapsing them here keeps <c>{}</c> out of every revision
    /// snapshot, where it would read as a change on a diff of a structure promotion (P1-28).
    /// Re-serialising rather than taking the raw text drops whatever indentation the client sent,
    /// so two identical configurations compare equal as strings.
    /// </remarks>
    public static string? Normalize(JsonElement? configuration)
    {
        if (configuration is not { } value) return null;

        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;

        if (value.ValueKind is JsonValueKind.Object && !value.EnumerateObject().MoveNext()) return null;

        return JsonSerializer.Serialize(value);
    }
}
