namespace ContentManagementSystem.Core.Content.Schema;

/// <summary>
/// One slot on its way into a schema snapshot, independent of the table it came from.
/// </summary>
/// <param name="Key">Stable key the payload addresses the value by.</param>
/// <param name="Name">Editor-facing label.</param>
/// <param name="FieldTypeKey">Key of the field type that fills the slot.</param>
/// <param name="ConfigurationJson">Field-type configuration, or null.</param>
/// <param name="IsRequired">Whether an empty value blocks publishing.</param>
/// <remarks>
/// A zone, a block-type property, and a composed property are three tables holding the same six
/// facts. This is those six facts, so that flattening a composition into its host block type does
/// not need a fake entity instance to carry a property that has no row of its own in that block
/// type. Deliberately carries no sort order: <see cref="ContentSchemaSnapshot.WriteSlots"/> takes
/// the sequence order as the effective one.
/// </remarks>
public readonly record struct ContentSlot(
    string Key,
    string Name,
    string FieldTypeKey,
    string? ConfigurationJson,
    bool IsRequired);
