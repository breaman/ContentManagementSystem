namespace ContentManagementSystem.Core.Content.Schema;

/// <summary>
/// One slot on its way into a schema snapshot, independent of the table it came from.
/// </summary>
/// <param name="Key">Stable key the payload addresses the value by.</param>
/// <param name="Name">Editor-facing label.</param>
/// <param name="FieldTypeKey">Key of the field type that fills the slot.</param>
/// <param name="ConfigurationJson">Field-type configuration, or null.</param>
/// <param name="IsRequired">Whether an empty value blocks publishing.</param>
/// <param name="Description">Help text shown under the editor control, or null.</param>
/// <param name="Group">Tab or accordion the editor groups the slot into, or null.</param>
/// <remarks>
/// A zone, a block-type property, and a composed property are three tables holding the same facts.
/// This is those facts, so that flattening a composition into its host block type does not need a
/// fake entity instance to carry a property that has no row of its own in that block type.
/// Deliberately carries no sort order: <see cref="ContentSchemaSnapshot.WriteSlots"/> takes the
/// sequence order as the effective one.
/// <para>
/// <paramref name="Description"/> and <paramref name="Group"/> are optional because they are the
/// editor's business alone and nothing below the editor reads them — but they are captured all the
/// same, since a canvas laid out from live definitions would regroup a page under whoever is editing
/// it (task P6-05).
/// </para>
/// </remarks>
public readonly record struct ContentSlot(
    string Key,
    string Name,
    string FieldTypeKey,
    string? ConfigurationJson,
    bool IsRequired,
    string? Description = null,
    string? Group = null);
