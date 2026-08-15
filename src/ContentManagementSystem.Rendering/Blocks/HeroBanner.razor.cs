namespace ContentManagementSystem.Rendering.Blocks;

/// <summary>
/// Reference block type: a full-width banner (task P3-10, spec section 8.2).
/// </summary>
/// <remarks>
/// The block a landing page's <c>hero</c> zone holds. Like a template, it declares placement only —
/// its property definitions are <c>BlockTypeProperty</c> rows a <c>Developer</c> owns, captured into
/// a <c>BlockTypeRevision</c> whenever they change, and a block instance renders against the revision
/// it captured (spec section 8.5).
/// <list type="table">
/// <listheader><term>Property</term><description>Field type</description></listheader>
/// <item><term><c>headline</c></term><description><c>plainText</c></description></item>
/// <item><term><c>standfirst</c></term><description><c>multilineText</c></description></item>
/// <item><term><c>image</c></term><description><c>media</c></description></item>
/// <item><term><c>cta</c></term><description><c>link</c></description></item>
/// <item><term><c>background</c></term><description><c>color</c></description></item>
/// <item><term><c>isFullBleed</c></term><description><c>boolean</c></description></item>
/// </list>
/// <para>
/// A block authored against an older revision is simply missing the properties added since, and each
/// of those renders nothing — which is why nothing in the markup checks whether a property is there
/// before naming it.
/// </para>
/// </remarks>
public partial class HeroBanner : CmsBlockBase;
