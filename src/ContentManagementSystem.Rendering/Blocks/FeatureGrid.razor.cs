namespace ContentManagementSystem.Rendering.Blocks;

/// <summary>
/// Reference block type: a titled grid of nested blocks (task P3-10, spec section 8.2).
/// </summary>
/// <remarks>
/// The one reference block that is a <em>container</em>, and it is here for that reason. Its
/// <c>items</c> property is a <c>blocks</c> value, so the render path goes zone → blocks renderer →
/// block → block property → blocks renderer → block, which is the shape that breaks if the block
/// context is cascaded rather than passed, or if a nested block's captured revision is resolved
/// against the outer block's schema.
/// <list type="table">
/// <listheader><term>Property</term><description>Field type</description></listheader>
/// <item><term><c>heading</c></term><description><c>plainText</c></description></item>
/// <item><term><c>columns</c></term><description><c>number</c></description></item>
/// <item><term><c>publishedOn</c></term><description><c>date</c></description></item>
/// <item><term><c>updatedAt</c></term><description><c>dateTime</c></description></item>
/// <item><term><c>items</c></term><description><c>blocks</c></description></item>
/// <item><term><c>gallery</c></term><description><c>mediaList</c></description></item>
/// <item><term><c>tags</c></term><description><c>tags</c></description></item>
/// <item><term><c>related</c></term><description><c>pageReference</c></description></item>
/// <item><term><c>promo</c></term><description><c>reusable</c></description></item>
/// </list>
/// <para>
/// <c>promo</c> and <c>gallery</c> render nothing until Phase 4 and Phase 5 supply the item store and
/// the media library. They are declared now anyway, because their renderers already contribute the
/// <c>ru:{id}</c> and <c>media:{id}</c> cache tags — a page published before those phases would
/// otherwise be invisible to invalidation forever, and nothing goes back and re-renders it.
/// </para>
/// </remarks>
public partial class FeatureGrid : CmsBlockBase;
