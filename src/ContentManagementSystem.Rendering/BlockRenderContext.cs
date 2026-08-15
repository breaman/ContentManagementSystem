using System.Text.Json;

using ContentManagementSystem.Core.Content.Schema;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// What one block instance is rendered with: its identity, its stored properties, and the block
/// type revision those properties were authored against (spec section 8.2).
/// </summary>
/// <remarks>
/// Cascaded around each block by the <c>blocks</c> field renderer so that
/// <see cref="CmsBlockProperty"/> can dispatch a property to its renderer without the block
/// component having to pass anything down. A block's markup names a property key and nothing else,
/// exactly as a template's markup names a zone key and nothing else.
/// <para>
/// The block component itself receives the same three facts as ordinary parameters
/// (<see cref="CmsBlockBase"/>), because a component should be renderable from a test that states
/// its inputs outright. This is the ambient copy, for the components nested inside it.
/// </para>
/// </remarks>
/// <param name="BlockId">The block instance's id, stable across edits (spec section 11.4).</param>
/// <param name="BlockTypeKey">Key of the block type the instance names.</param>
/// <param name="BlockTypeRevision">
/// The revision the instance captured, or null for a payload written before revisions were carried.
/// </param>
/// <param name="Properties">The block's stored <c>properties</c> object.</param>
/// <param name="Schema">
/// The captured property definitions of that revision, or null when it could not be resolved. Null
/// is a rendering condition rather than an error, for the same reason a missing template revision
/// is: the payload carries everything needed to read itself, and only the configuration is lost.
/// </param>
public sealed record BlockRenderContext(
    Guid BlockId,
    string BlockTypeKey,
    int? BlockTypeRevision,
    JsonElement Properties,
    BlockTypeSchema? Schema)
{
    /// <summary>Reads a property's stored value object.</summary>
    /// <param name="propertyKey">The property key.</param>
    /// <returns>The whole property object, or null when this block does not carry it.</returns>
    /// <remarks>
    /// A block authored against an older revision of its type is simply missing the properties added
    /// since, which is ordinary and renders nothing — the same fact about a block that an unauthored
    /// zone is about a page.
    /// </remarks>
    public JsonElement? Property(string propertyKey) =>
        Properties.ValueKind is JsonValueKind.Object &&
        Properties.TryGetProperty(propertyKey, out var property) &&
        property.ValueKind is JsonValueKind.Object
            ? property
            : null;
}
