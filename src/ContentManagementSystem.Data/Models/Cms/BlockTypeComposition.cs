namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// Joins a <see cref="BlockType"/> to a <see cref="Composition"/> it inherits properties from.
/// </summary>
/// <remarks>
/// Carried as an entity rather than a skip navigation because the ordering matters: composed
/// property groups render in <see cref="SortOrder"/> after the block type's own properties, and an
/// editor's muscle memory for where a field sits is worth keeping stable.
/// </remarks>
public class BlockTypeComposition : FingerPrintEntityBase
{
    /// <summary>Block type receiving the composed properties.</summary>
    public int BlockTypeId { get; set; }

    /// <summary>Block type receiving the composed properties.</summary>
    public BlockType BlockType { get; set; } = null!;

    /// <summary>Composition supplying the properties.</summary>
    public int CompositionId { get; set; }

    /// <summary>Composition supplying the properties.</summary>
    public Composition Composition { get; set; } = null!;

    /// <summary>Order this group appears relative to other composed groups.</summary>
    public int SortOrder { get; set; }
}
