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
public class FieldConfiguration
{
    /// <summary>Configuration for a property that declares none.</summary>
    public static FieldConfiguration Empty { get; } = new(default, false);

    private readonly JsonElement _root;

    /// <summary>
    /// Builds a configuration over a parsed blob.
    /// </summary>
    /// <param name="root">The configuration object, or a default element when there is none.</param>
    /// <param name="isRequired">Whether an empty value blocks publishing.</param>
    /// <remarks>
    /// Protected rather than private only so the field-type configuration contract test can derive
    /// a subclass that records which settings a field type actually reads. A setting read but never
    /// declared is one <see cref="FieldConfigurationSchema"/> refuses to store, so the two halves
    /// have to be checked against each other by something.
    /// </remarks>
    protected FieldConfiguration(JsonElement root, bool isRequired)
    {
        _root = root;
        IsRequired = isRequired;
    }

    /// <summary>
    /// The raw configuration object, for field types whose configuration is too rich for the typed
    /// accessors below.
    /// </summary>
    public JsonElement Root => _root;

    /// <summary>Whether any configuration was supplied at all.</summary>
    public bool IsEmpty => _root.ValueKind is not JsonValueKind.Object;

    /// <summary>
    /// Whether an empty value blocks publishing.
    /// </summary>
    /// <remarks>
    /// Deliberately not a configuration setting. It is carried by the <c>IsRequired</c> column on
    /// <c>Zone</c> and <c>BlockTypeProperty</c>, which is what the structure admin screens write and
    /// what the editor reads to mark a field; a second copy inside <c>ConfigurationJson</c> would be
    /// free to disagree with the column, and nothing in the model would say which one won.
    /// <see cref="FieldConfigurationSchema"/> therefore refuses to declare a setting by that name,
    /// and configuration validation refuses to store one.
    /// </remarks>
    public bool IsRequired { get; }

    /// <summary>
    /// Parses a stored configuration blob.
    /// </summary>
    /// <param name="configurationJson">
    /// The <c>ConfigurationJson</c> column value. Null, empty, or whitespace yields an empty
    /// configuration.
    /// </param>
    /// <param name="isRequired">
    /// The <c>IsRequired</c> column value of the zone or block-type property being validated.
    /// </param>
    /// <returns>A cacheable configuration.</returns>
    /// <exception cref="JsonException">The value is not well-formed JSON.</exception>
    public static FieldConfiguration Parse(string? configurationJson, bool isRequired = false)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return isRequired ? new FieldConfiguration(default, true) : Empty;
        }

        using var document = JsonDocument.Parse(configurationJson);

        // Cloned so the element outlives the document it was parsed from and the instance can be
        // cached without pinning a disposable.
        return new FieldConfiguration(document.RootElement.Clone(), isRequired);
    }

    /// <summary>
    /// Builds a configuration over an already-parsed object.
    /// </summary>
    /// <param name="configuration">
    /// The configuration object. Anything that is not an object — including an undefined element —
    /// yields an empty configuration, which is what a property declaring none looks like.
    /// </param>
    /// <param name="isRequired">
    /// The <c>IsRequired</c> column value of the zone or block-type property being validated.
    /// </param>
    /// <returns>A cacheable configuration.</returns>
    /// <remarks>
    /// The counterpart to <see cref="Parse(string?, bool)"/> for callers whose configuration arrives
    /// embedded in a larger document rather than as its own column — a template revision's zone
    /// snapshot, for instance. Re-serialising it just to parse it again would be the only
    /// alternative.
    /// </remarks>
    public static FieldConfiguration FromElement(JsonElement configuration, bool isRequired = false)
    {
        if (configuration.ValueKind is not JsonValueKind.Object)
        {
            return isRequired ? new FieldConfiguration(default, true) : Empty;
        }

        // Cloned for the same reason Parse clones: the instance is cached per schema row and must
        // not pin the document it was read out of.
        return new FieldConfiguration(configuration.Clone(), isRequired);
    }

    /// <summary>Looks up a configuration property by name.</summary>
    /// <param name="name">Property name, case-sensitive.</param>
    /// <param name="value">The property value when present.</param>
    /// <returns><see langword="true"/> when the property is present and not null.</returns>
    public virtual bool TryGetValue(string name, out JsonElement value)
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
