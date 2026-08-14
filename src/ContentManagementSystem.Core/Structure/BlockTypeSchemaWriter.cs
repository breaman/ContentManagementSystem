using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// Flattens a block type's property set and cuts its revisions.
/// </summary>
/// <remarks>
/// Shared by the block-type service and the composition service, which both change what a block
/// type's captured schema should say. A composition edited in one place has to recut every block
/// type composing it, and the flattening that decides what those revisions contain must be the same
/// code in both directions or the two would disagree about a block type's property order.
/// </remarks>
internal static class BlockTypeSchemaWriter
{
    /// <summary>
    /// Produces the property set an editor sees, in the order they see it.
    /// </summary>
    /// <param name="blockType">
    /// The block type, with <c>Properties</c>, <c>Compositions</c>, and each composition's own
    /// <c>Properties</c> loaded.
    /// </param>
    /// <returns>Own properties first, then each composed group in turn.</returns>
    /// <remarks>
    /// Composed groups follow the block type's own properties rather than interleaving with them
    /// (spec section 6.3). The two sort orders come from different tables and are free to overlap,
    /// so merging on the number would shuffle a composed group into the middle of the host's
    /// properties the first time a developer numbered them alike; an editor's muscle memory for
    /// where a field sits is worth more than that.
    /// </remarks>
    public static List<ContentSlot> Flatten(BlockType blockType)
    {
        var slots = blockType.Properties
            .OrderBy(property => property.SortOrder)
            .ThenBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => new ContentSlot(
                property.Key,
                property.Name,
                property.FieldTypeKey,
                property.ConfigurationJson,
                property.IsRequired))
            .ToList();

        foreach (var composed in Composed(blockType))
        {
            slots.Add(new ContentSlot(
                composed.Property.Key,
                composed.Property.Name,
                composed.Property.FieldTypeKey,
                composed.Property.ConfigurationJson,
                composed.Property.IsRequired));
        }

        return slots;
    }

    /// <summary>
    /// Walks the composed properties in the order they are appended, with the group they came from.
    /// </summary>
    /// <param name="blockType">The block type, with its compositions loaded.</param>
    /// <returns>Each composed property beside its composition.</returns>
    public static IEnumerable<(Composition Composition, CompositionProperty Property)> Composed(
        BlockType blockType)
    {
        foreach (var binding in blockType.Compositions
            .OrderBy(binding => binding.SortOrder)
            .ThenBy(binding => binding.CompositionId))
        {
            if (binding.Composition is null) continue;

            foreach (var property in binding.Composition.Properties
                .OrderBy(property => property.SortOrder)
                .ThenBy(property => property.Key, StringComparer.Ordinal))
            {
                yield return (binding.Composition, property);
            }
        }
    }

    /// <summary>
    /// Appends a revision capturing the property set as it will stand once the change is saved.
    /// </summary>
    /// <param name="blockType">The block type being changed.</param>
    /// <param name="notes">What changed, for the history list.</param>
    /// <returns>
    /// The revision, so a failed save can tell a collision on the revision number from one on a
    /// property key.
    /// </returns>
    /// <remarks>
    /// The snapshot flattens compositions away deliberately. A block instance renders from its
    /// captured snapshot alone, so a composition edited or detached later cannot reach back and
    /// change what an already-published block shows — which is the same guarantee spec section 8.5
    /// gives templates, extended to the indirection compositions add.
    /// </remarks>
    public static BlockTypeRevision Cut(BlockType blockType, string notes)
    {
        var revision = new BlockTypeRevision
        {
            BlockTypeId = blockType.Id,
            RevisionNumber = blockType.CurrentRevision + 1,
            PropertySnapshotJson = ContentSchemaSnapshot.WriteSlots(Flatten(blockType)),
            Notes = notes,
        };

        blockType.Revisions.Add(revision);
        blockType.CurrentRevision = revision.RevisionNumber;

        return revision;
    }
}
