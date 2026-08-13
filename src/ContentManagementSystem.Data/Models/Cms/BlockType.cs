namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// The shape of a repeatable content item placed inside a <c>blocks</c> zone — a hero banner, a
/// quote, a set of text columns.
/// </summary>
/// <remarks>
/// A block type is not independently addressable and is never published on its own: it is published
/// with whatever page or reusable item hosts it (spec section 6.3). Its property set may be extended
/// by composing shared <see cref="Composition"/> groups rather than re-declaring the same properties
/// on a dozen block types.
/// </remarks>
public class BlockType : FingerPrintEntityBase
{
    /// <summary>
    /// Stable identifier written into every block instance in a payload. Immutable after creation.
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>Editor-facing display name, shown in the block picker.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Optional help text describing when to reach for this block.</summary>
    public string? Description { get; set; }

    /// <summary>Assembly-qualified name of the Razor component that renders this block.</summary>
    public string? ComponentTypeName { get; set; }

    /// <summary>Icon identifier shown against this block type in the picker.</summary>
    public string? IconKey { get; set; }

    /// <summary>
    /// Token pattern producing the one-line summary shown for a collapsed block, such as
    /// <c>{headline}</c>. Falls back to the block type name when unset.
    /// </summary>
    public string? SummaryTemplate { get; set; }

    /// <summary>
    /// True when the database holds this block type but no code component declares it. Existing
    /// content renders a logged fallback rather than throwing (spec section 15.3).
    /// </summary>
    public bool IsOrphaned { get; set; }

    /// <summary>Revision number of the newest <see cref="BlockTypeRevision"/>.</summary>
    public int CurrentRevision { get; set; }

    /// <summary>
    /// True for block types the system itself depends on, such as the built-in <c>RawHtml</c> type
    /// backing free-form reusable content. Built-ins cannot be deleted.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Property definitions declared directly on this block type.</summary>
    public ICollection<BlockTypeProperty> Properties { get; set; } = [];

    /// <summary>Shared property groups composed into this block type.</summary>
    public ICollection<BlockTypeComposition> Compositions { get; set; } = [];

    /// <summary>Structural revision history.</summary>
    public ICollection<BlockTypeRevision> Revisions { get; set; } = [];
}
