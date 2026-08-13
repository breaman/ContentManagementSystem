namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One typed property on a <see cref="BlockType"/> — the block-level counterpart of a
/// <see cref="Zone"/>.
/// </summary>
public class BlockTypeProperty : FingerPrintEntityBase
{
    /// <summary>Block type this property belongs to.</summary>
    public int BlockTypeId { get; set; }

    /// <summary>Block type this property belongs to.</summary>
    public BlockType BlockType { get; set; } = null!;

    /// <summary>
    /// Stable identifier used as the property key inside a block instance. Immutable after creation.
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>Editor-facing label.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Optional help text shown beneath the editor control.</summary>
    public string? Description { get; set; }

    /// <summary>Key of the registered field type that fills this property.</summary>
    public string FieldTypeKey { get; set; } = null!;

    /// <summary>Field-type-specific configuration (spec section 7.2).</summary>
    public string? ConfigurationJson { get; set; }

    /// <summary>Whether an empty value blocks publishing. Never blocks a draft save.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Optional tab or accordion grouping in the editor.</summary>
    public string? Group { get; set; }

    /// <summary>Order this property appears in the block editor.</summary>
    public int SortOrder { get; set; }
}
