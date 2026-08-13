using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Content.Schema;

/// <summary>
/// One typed slot in a content schema: a zone of a template, or a property of a block type.
/// </summary>
/// <remarks>
/// The two are the same thing at validation time — a key, a field type, and that field type's
/// configuration — so they are one type here rather than two identical ones. Where they differ is
/// only in the wording of the diagnostics the walk produces around them.
/// <para>
/// The parsed <see cref="Configuration"/> is the point of this class existing at all. Configuration
/// arrives as a JSON string on the schema row, and re-parsing it on every property visit dominated
/// everything else the S1 spike measured; holding it parsed on an immutable schema object means it
/// is parsed once per revision and shared by every payload validated against it.
/// </para>
/// </remarks>
public sealed class ContentPropertySchema
{
    /// <summary>
    /// Creates a schema slot over already-parsed configuration.
    /// </summary>
    /// <param name="key">Stable key, as it appears in the payload.</param>
    /// <param name="name">Editor-facing label, used in the messages an editor reads.</param>
    /// <param name="fieldTypeKey">Key of the field type that fills this slot.</param>
    /// <param name="configuration">Parsed configuration, carrying the <c>IsRequired</c> column.</param>
    /// <param name="sortOrder">Order this slot appears in the editor.</param>
    public ContentPropertySchema(
        string key,
        string name,
        string fieldTypeKey,
        FieldConfiguration configuration,
        int sortOrder = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldTypeKey);
        ArgumentNullException.ThrowIfNull(configuration);

        Key = key;
        Name = string.IsNullOrWhiteSpace(name) ? key : name;
        FieldTypeKey = fieldTypeKey;
        Configuration = configuration;
        SortOrder = sortOrder;
    }

    /// <summary>Stable key, as it appears in the payload. Immutable once content references it.</summary>
    public string Key { get; }

    /// <summary>Editor-facing label. Falls back to the key when none was captured.</summary>
    public string Name { get; }

    /// <summary>Key of the field type that fills this slot, such as <c>richText</c>.</summary>
    public string FieldTypeKey { get; }

    /// <summary>Parsed configuration, including whether an empty value blocks publishing.</summary>
    public FieldConfiguration Configuration { get; }

    /// <summary>Order this slot appears in the editor.</summary>
    public int SortOrder { get; }

    /// <summary>Whether an empty value blocks publishing.</summary>
    public bool IsRequired => Configuration.IsRequired;

    /// <summary>
    /// Creates a schema slot from a stored configuration blob.
    /// </summary>
    /// <param name="key">Stable key.</param>
    /// <param name="name">Editor-facing label.</param>
    /// <param name="fieldTypeKey">Key of the field type that fills this slot.</param>
    /// <param name="configurationJson">The <c>ConfigurationJson</c> column value, if any.</param>
    /// <param name="isRequired">The <c>IsRequired</c> column value.</param>
    /// <param name="sortOrder">Order this slot appears in the editor.</param>
    /// <returns>The schema slot.</returns>
    /// <exception cref="JsonException"><paramref name="configurationJson"/> is not well-formed.</exception>
    public static ContentPropertySchema Create(
        string key,
        string name,
        string fieldTypeKey,
        string? configurationJson = null,
        bool isRequired = false,
        int sortOrder = 0) =>
        new(key, name, fieldTypeKey, FieldConfiguration.Parse(configurationJson, isRequired), sortOrder);

    /// <summary>
    /// Creates a schema slot from configuration embedded in a larger document.
    /// </summary>
    /// <param name="key">Stable key.</param>
    /// <param name="name">Editor-facing label.</param>
    /// <param name="fieldTypeKey">Key of the field type that fills this slot.</param>
    /// <param name="configuration">The configuration object, or an undefined element.</param>
    /// <param name="isRequired">Whether an empty value blocks publishing.</param>
    /// <param name="sortOrder">Order this slot appears in the editor.</param>
    /// <returns>The schema slot.</returns>
    public static ContentPropertySchema Create(
        string key,
        string name,
        string fieldTypeKey,
        JsonElement configuration,
        bool isRequired = false,
        int sortOrder = 0) =>
        new(key, name, fieldTypeKey, FieldConfiguration.FromElement(configuration, isRequired), sortOrder);
}
