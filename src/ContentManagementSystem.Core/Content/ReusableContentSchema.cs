using ContentManagementSystem.Core.Content.Schema;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Presents a block type revision as the schema a reusable item's payload is checked and rendered
/// against (spec section 9.1).
/// </summary>
/// <remarks>
/// <strong>A zone and a block-type property are the same thing.</strong> Both are a keyed slot with
/// a field type and a captured configuration; both store a value carrying the discriminator of
/// whatever wrote it; both are dispatched by that discriminator rather than by the schema. The two
/// snapshot types say so themselves — <see cref="ContentSchema"/> and <see cref="BlockTypeSchema"/>
/// are both lists of <see cref="ContentPropertySchema"/>, differing only in what they call the list.
/// <para>
/// So a reusable item stores an ordinary content payload whose <c>zones</c> object holds the block's
/// properties, and this turns the block type's captured revision into the schema that walk needs.
/// The alternative — a parallel validator, a parallel indexer, a parallel diff, each aware that a
/// reusable item is shaped like a block — is four more places for the absent-versus-null distinction
/// to be lost, in exchange for a payload envelope whose member names read slightly more naturally.
/// </para>
/// </remarks>
public static class ReusableContentSchema
{
    /// <summary>
    /// Builds the schema for a block type revision, when the deployment still knows it.
    /// </summary>
    /// <param name="catalog">Resolves the captured revisions a payload names.</param>
    /// <param name="blockTypeKey">Key of the block type shaping the item.</param>
    /// <param name="revisionNumber">The revision the payload captured.</param>
    /// <returns>
    /// The schema, or null when the revision is unknown. Null is a rendering and validation
    /// condition rather than an error, for the reason <c>IContentSchemaCatalog</c> gives: a revision
    /// can be missing legitimately, and the payload's own discriminators are enough to read every
    /// value it holds (spec section 15.3).
    /// </returns>
    public static ContentSchema? For(
        IContentSchemaCatalog catalog,
        string blockTypeKey,
        int? revisionNumber)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog.TryGetBlockType(blockTypeKey, revisionNumber, out var blockType)
            ? new ContentSchema(blockTypeKey, blockType.RevisionNumber, blockType.Properties)
            : null;
    }
}
