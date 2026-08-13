using System.Text.Json;

namespace ContentManagementSystem.Shared.Contracts.Fields;

/// <summary>
/// The parsed <c>ConfigurationJson</c> of one zone or block-type property, interpreted by its field
/// type (spec section 7.2).
/// </summary>
/// <remarks>
/// Immutable and safe to cache: the underlying element is cloned free of its owning document, so an
/// instance can be held per schema row and reused across every payload validated against it. That
/// caching is not an optimisation detail — re-parsing configuration on each property visit
/// dominated everything else in the S1 spike's measurements.
/// <para>
/// Cache entries are keyed by the schema row and invalidated when its revision changes. Nothing
/// request-scoped belongs on this type, which is why <see cref="ValidationMode"/> is passed
/// separately to <see cref="IFieldType.ValidateAsync"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var maxLength = configuration.GetInt32("maxLength") ?? int.MaxValue;
/// var allowed = configuration.GetStringArray("allowedBlockTypes");
/// </code>
/// </example>
public sealed class FieldConfiguration
{
    /// <summary>Configuration for a property that declares none.</summary>
    public static FieldConfiguration Empty { get; } = new(default);

    private readonly JsonElement _root;

    private FieldConfiguration(JsonElement root) => _root = root;

    /// <summary>
    /// The raw configuration object, for field types whose configuration is too rich for the typed
    /// accessors below.
    /// </summary>
    public JsonElement Root => _root;

    /// <summary>Whether any configuration was supplied at all.</summary>
    public bool IsEmpty => _root.ValueKind is not JsonValueKind.Object;

    /// <summary>
    /// Parses a stored configuration blob.
    /// </summary>
    /// <param name="configurationJson">
    /// The <c>ConfigurationJson</c> column value. Null, empty, or whitespace yields
    /// <see cref="Empty"/>.
    /// </param>
    /// <returns>A cacheable configuration.</returns>
    /// <exception cref="JsonException">The value is not well-formed JSON.</exception>
    public static FieldConfiguration Parse(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson)) return Empty;

        using var document = JsonDocument.Parse(configurationJson);

        // Cloned so the element outlives the document it was parsed from and the instance can be
        // cached without pinning a disposable.
        return new FieldConfiguration(document.RootElement.Clone());
    }

    /// <summary>Looks up a configuration property by name.</summary>
    /// <param name="name">Property name, case-sensitive.</param>
    /// <param name="value">The property value when present.</param>
    /// <returns><see langword="true"/> when the property is present and not null.</returns>
    public bool TryGetValue(string name, out JsonElement value)
    {
        if (_root.ValueKind is JsonValueKind.Object &&
            _root.TryGetProperty(name, out value) &&
            value.ValueKind is not JsonValueKind.Null)
        {
            return true;
        }

        value = default;

        return false;
    }

    /// <summary>Reads an integer setting, such as <c>maxLength</c> or <c>min</c>.</summary>
    /// <param name="name">Property name.</param>
    /// <returns>The value, or null when absent or not a number.</returns>
    public int? GetInt32(string name) =>
        TryGetValue(name, out var value) && value.ValueKind is JsonValueKind.Number &&
        value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    /// <summary>Reads a decimal setting, such as <c>step</c>.</summary>
    /// <param name="name">Property name.</param>
    /// <returns>The value, or null when absent or not a number.</returns>
    public decimal? GetDecimal(string name) =>
        TryGetValue(name, out var value) && value.ValueKind is JsonValueKind.Number &&
        value.TryGetDecimal(out var parsed)
            ? parsed
            : null;

    /// <summary>Reads a string setting, such as <c>pattern</c> or <c>profile</c>.</summary>
    /// <param name="name">Property name.</param>
    /// <returns>The value, or null when absent or not a string.</returns>
    public string? GetString(string name) =>
        TryGetValue(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Reads a boolean setting, such as <c>allowNesting</c>.</summary>
    /// <param name="name">Property name.</param>
    /// <param name="defaultValue">Value returned when the setting is absent.</param>
    /// <returns>The configured value, or <paramref name="defaultValue"/>.</returns>
    public bool GetBoolean(string name, bool defaultValue = false) =>
        TryGetValue(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;

    /// <summary>Reads a string-array setting, such as <c>allowedBlockTypes</c> or <c>options</c>.</summary>
    /// <param name="name">Property name.</param>
    /// <returns>The values, or an empty array when absent. Never null.</returns>
    public string[] GetStringArray(string name)
    {
        if (!TryGetValue(name, out var value) || value.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<string>(value.GetArrayLength());
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind is JsonValueKind.String && element.GetString() is { } text)
            {
                items.Add(text);
            }
        }

        return [.. items];
    }
}
