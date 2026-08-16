namespace ContentManagementSystem.Rendering.Blocks;

/// <summary>
/// The component behind the built-in <c>rawHtml</c> block type (task P4-04, spec section 9.1).
/// </summary>
/// <remarks>
/// Unlike the three reference block types beside it, this one is not a sample: the database seeds a
/// <c>rawHtml</c> block type marked built-in, because reusable content needs a shape and the
/// commonest one — a footer or a banner authored as markup — must not require a developer to define
/// a block type before the CMS is usable at all. Without a component declaring the key, that seeded
/// row is orphaned and every reusable item shaped by it renders nothing.
/// <list type="table">
/// <listheader><term>Property</term><description>Field type</description></listheader>
/// <item><term><c>content</c></term><description><c>html</c>, required</description></item>
/// </list>
/// <para>
/// The class is <c>RawHtmlBlock</c> rather than <c>RawHtml</c>, for the reason
/// <see cref="RichTextSection"/> gives for its own name: a type called <c>RawHtml</c> one namespace
/// away from <c>RawHtmlRenderer</c> is the sort of pair that gets imported by mistake in a
/// <c>.razor</c> file where <c>@using</c> order decides the winner.
/// </para>
/// </remarks>
public partial class RawHtmlBlock : CmsBlockBase;
