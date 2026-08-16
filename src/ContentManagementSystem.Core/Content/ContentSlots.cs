using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Shared.Content;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Finds the schema slot a value was authored against, wherever in a payload it sits.
/// </summary>
/// <remarks>
/// A zone and a block-type property are the same thing to every reader of a payload, and this is
/// where that stops being a slogan: given a payload path, it hands back the
/// <see cref="ContentPropertySchema"/> that governs it, whether the value is a zone or is four
/// levels down inside nested blocks.
/// <para>
/// It exists because some rules cannot be enforced by a field type. A field type is a stateless
/// singleton with no database, so <c>allowedTemplates</c> ("this property accepts only article
/// pages") and <c>allowedTypes</c> ("this property accepts only banner-shaped items") can only be
/// checked where both the stored value and the entity it points at are in reach — which is the
/// publish check. What the publish check does not have on its own is the configuration those
/// settings live in, and reaching it means resolving the block's own captured revision.
/// </para>
/// <para>
/// Every failure is null. A path this cannot follow, a revision this deployment no longer knows, a
/// block whose type has been retired: all mean the same thing to a caller — no configured
/// restriction can be read, so none is enforced. Guessing instead would refuse a publish over a
/// rule nobody actually wrote.
/// </para>
/// </remarks>
public static class ContentSlots
{
    /// <summary>
    /// Resolves the slot at a payload path.
    /// </summary>
    /// <param name="path">The absolute payload path a reference was found at.</param>
    /// <param name="payload">The payload the path came from.</param>
    /// <param name="templateSchema">
    /// The captured schema of the template revision the payload names, or null when it could not be
    /// resolved.
    /// </param>
    /// <param name="catalog">Resolves the block type revisions nested blocks captured.</param>
    /// <returns>The slot, or null when it cannot be reached.</returns>
    public static ContentPropertySchema? Resolve(
        string? path,
        ContentPayload payload,
        ContentSchema? templateSchema,
        IContentSchemaCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(catalog);

        var location = ReferencePath.Parse(path, payload);

        if (location.ZoneKey is not { } zoneKey) return null;

        // Directly in a zone: the template revision governs it.
        if (location.BlockId is null || location.PropertyKey is null)
        {
            return templateSchema?.FindZone(zoneKey);
        }

        if (location.BlockTypeKey is not { Length: > 0 } blockTypeKey) return null;

        // The block's own captured revision, never the block type's current one. A block published
        // last year is governed by the property set as it stood then, which is the whole of spec
        // section 8.5 — enforcing today's restriction against it would refuse a publish of content
        // that was legal when it was written.
        return catalog.TryGetBlockType(blockTypeKey, location.BlockTypeRevision, out var blockSchema)
            ? blockSchema.FindProperty(location.PropertyKey)
            : null;
    }
}
