namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// An immutable snapshot of a block type's property definitions, mirroring
/// <see cref="TemplateRevision"/>.
/// </summary>
/// <remarks>
/// Block instances record the revision they were authored against, so a property added or removed
/// today cannot change how already-published blocks render.
/// </remarks>
public class BlockTypeRevision : FingerPrintEntityBase
{
    /// <summary>Block type this revision belongs to.</summary>
    public int BlockTypeId { get; set; }

    /// <summary>Block type this revision belongs to.</summary>
    public BlockType BlockType { get; set; } = null!;

    /// <summary>Monotonically increasing number, starting at 1, unique within the block type.</summary>
    public int RevisionNumber { get; set; }

    /// <summary>
    /// Serialised copy of every property definition — including composed ones — as it stood when
    /// the revision was cut.
    /// </summary>
    public string PropertySnapshotJson { get; set; } = null!;

    /// <summary>Optional note explaining what changed and why.</summary>
    public string? Notes { get; set; }
}
