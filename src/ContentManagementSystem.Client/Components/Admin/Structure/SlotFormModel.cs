using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Components.Admin.Structure;

/// <summary>
/// What the zone and property forms bind to.
/// </summary>
/// <remarks>
/// One model for both, because a zone and a block-type property are the same thing at validation
/// time and the forms would otherwise be two copies of one screen.
/// <para>
/// Client-side validation here is deliberately thin — required-ness and length, the things a form
/// can answer instantly. Everything that decides whether a definition is <em>legal</em> (the key
/// shape, whether it is taken, whether the configuration is one the field type can honour) is a
/// server rule and is reported back as diagnostics. Duplicating those here would produce a second
/// definition of the content model that drifts.
/// </para>
/// </remarks>
public sealed class SlotFormModel
{
    /// <summary>Stable key. Immutable once the slot exists.</summary>
    [Required(ErrorMessage = "A key is required.")]
    [StringLength(100, ErrorMessage = "A key may be at most 100 characters.")]
    public string? Key { get; set; }

    /// <summary>Editor-facing label.</summary>
    [Required(ErrorMessage = "A display name is required.")]
    [StringLength(200, ErrorMessage = "A display name may be at most 200 characters.")]
    public string? Name { get; set; }

    /// <summary>Optional help text shown beneath the editor control.</summary>
    [StringLength(500, ErrorMessage = "A description may be at most 500 characters.")]
    public string? Description { get; set; }

    /// <summary>Key of the field type that fills the slot.</summary>
    [Required(ErrorMessage = "Choose a field type.")]
    public string? FieldTypeKey { get; set; }

    /// <summary>Field-type configuration, as the developer typed it.</summary>
    public string? ConfigurationJson { get; set; }

    /// <summary>Whether an empty value blocks publishing.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Whether the zone participates in in-context editing. Zones only.</summary>
    public bool IsInlineEditable { get; set; }

    /// <summary>Optional tab or accordion grouping in the editor.</summary>
    [StringLength(100, ErrorMessage = "A group name may be at most 100 characters.")]
    public string? Group { get; set; }

    /// <summary>Order the slot appears in the editor.</summary>
    public int SortOrder { get; set; }

    /// <summary>Database identity when editing, or null when adding.</summary>
    public int? Id { get; set; }

    /// <summary>Whether this form is adding a slot rather than editing one.</summary>
    public bool IsNew => Id is null;

    /// <summary>Builds a blank form for adding a slot.</summary>
    /// <param name="sortOrder">Where the new slot goes, usually after the existing ones.</param>
    public static SlotFormModel ForNew(int sortOrder) => new() { SortOrder = sortOrder };

    /// <summary>Fills the form from an existing zone.</summary>
    /// <param name="zone">The zone to edit.</param>
    public static SlotFormModel From(ZoneDefinition zone) => new()
    {
        Id = zone.Id,
        Key = zone.Key,
        Name = zone.Name,
        Description = zone.Description,
        FieldTypeKey = zone.FieldTypeKey,
        ConfigurationJson = Write(zone.Configuration),
        IsRequired = zone.IsRequired,
        IsInlineEditable = zone.IsInlineEditable,
        Group = zone.Group,
        SortOrder = zone.SortOrder,
    };

    /// <summary>Fills the form from an existing property.</summary>
    /// <param name="property">The property to edit.</param>
    public static SlotFormModel From(PropertyDefinition property) => new()
    {
        Id = property.Id,
        Key = property.Key,
        Name = property.Name,
        Description = property.Description,
        FieldTypeKey = property.FieldTypeKey,
        ConfigurationJson = Write(property.Configuration),
        IsRequired = property.IsRequired,
        Group = property.Group,
        SortOrder = property.SortOrder,
    };

    /// <summary>
    /// Parses the typed configuration.
    /// </summary>
    /// <param name="configuration">The parsed value, or null when the box was left empty.</param>
    /// <param name="error">Why it could not be parsed, when it could not.</param>
    /// <returns>Whether the box holds something sendable.</returns>
    /// <remarks>
    /// Caught here rather than sent, because malformed JSON is the one configuration problem the
    /// server cannot describe usefully — it would come back as "not valid JSON" with an offset into
    /// a string the developer cannot see. Everything the server <em>can</em> explain is left to it.
    /// </remarks>
    public bool TryReadConfiguration(out JsonElement? configuration, out string? error)
    {
        configuration = null;
        error = null;

        if (string.IsNullOrWhiteSpace(ConfigurationJson)) return true;

        try
        {
            using var document = JsonDocument.Parse(ConfigurationJson);

            configuration = document.RootElement.Clone();

            return true;
        }
        catch (JsonException exception)
        {
            error = exception.Message;

            return false;
        }
    }

    /// <summary>Renders a stored configuration back into the textarea, indented to be editable.</summary>
    private static string? Write(JsonElement? configuration) =>
        configuration is { } value
            ? JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })
            : null;
}
