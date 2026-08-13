namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One named, typed slot in a template that an editor fills with content.
/// </summary>
/// <remarks>
/// A zone declares <em>what kind</em> of data belongs in it via <see cref="FieldTypeKey"/>; the
/// template's Razor component declares <em>where</em> it appears via <c>&lt;CmsZone Name="…" /&gt;</c>.
/// Zone shapes are spec section 8.3.
/// </remarks>
public class Zone : FingerPrintEntityBase
{
    /// <summary>Template this zone belongs to.</summary>
    public int TemplateId { get; set; }

    /// <summary>Template this zone belongs to.</summary>
    public Template Template { get; set; } = null!;

    /// <summary>
    /// Stable identifier used as the payload's zone key. Immutable after creation.
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>Editor-facing label.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Optional help text shown beneath the editor control.</summary>
    public string? Description { get; set; }

    /// <summary>Key of the registered field type that fills this zone, such as <c>richText</c>.</summary>
    public string FieldTypeKey { get; set; } = null!;

    /// <summary>
    /// Field-type-specific configuration, validated against that field type's JSON Schema on save
    /// so a <c>Developer</c> cannot persist a configuration the editor component cannot honour
    /// (spec section 7.2).
    /// </summary>
    public string? ConfigurationJson { get; set; }

    /// <summary>
    /// Whether an empty value blocks publishing. It never blocks a draft save — an editor must
    /// always be able to save half-finished work.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>Whether this zone participates in in-context editing (v2, spec section 14.5).</summary>
    public bool IsInlineEditable { get; set; }

    /// <summary>Optional tab or accordion grouping in the editor.</summary>
    public string? Group { get; set; }

    /// <summary>Order this zone appears in the editor UI.</summary>
    public int SortOrder { get; set; }
}
