namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One typed property on a <see cref="Composition"/>. Identical in shape to
/// <see cref="BlockTypeProperty"/>, because a composed property must behave exactly like a directly
/// declared one once it is flattened into a block type.
/// </summary>
public class CompositionProperty : FingerPrintEntityBase
{
    /// <summary>Composition this property belongs to.</summary>
    public int CompositionId { get; set; }

    /// <summary>Composition this property belongs to.</summary>
    public Composition Composition { get; set; } = null!;

    /// <summary>
    /// Stable identifier used as the property key inside a block instance. Immutable after creation.
    /// </summary>
    /// <remarks>
    /// Composed keys share a namespace with the host block type's own property keys, so a
    /// composition whose key collides with a directly declared one is refused at save time.
    /// </remarks>
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

    /// <summary>Order this property appears within the composed group.</summary>
    public int SortOrder { get; set; }
}
