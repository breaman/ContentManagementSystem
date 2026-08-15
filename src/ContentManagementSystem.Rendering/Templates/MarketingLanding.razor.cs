namespace ContentManagementSystem.Rendering.Templates;

/// <summary>
/// Reference template: a block-driven campaign page (task P3-10, spec section 8.2).
/// </summary>
/// <remarks>
/// One of the two templates the CMS ships with, and the one that shows what the content model is
/// actually for. It has no logic at all — it names five zones and lays them out, and everything
/// about what those zones contain is data a <c>Developer</c> edits in the backoffice. Changing
/// <c>intro</c> from rich text to a block list is a structure edit here, not a deployment.
/// <para>
/// <strong>The zone definitions are not in this file, and cannot be.</strong> Spec section 8.1 puts
/// them in the database because content-modelling decisions change far more often than layout does,
/// and spec section 27.1 promotes them between environments as JSON. What the table below records is
/// therefore the <em>intended</em> shape — the one the reference zone definitions and the rendering
/// tests are written against — not a contract this component enforces.
/// </para>
/// <list type="table">
/// <listheader><term>Zone</term><description>Field type</description></listheader>
/// <item><term><c>hero</c></term><description><c>blocks</c>, usually one <c>hero-banner</c></description></item>
/// <item><term><c>intro</c></term><description><c>richText</c></description></item>
/// <item><term><c>body</c></term><description><c>blocks</c></description></item>
/// <item><term><c>accent</c></term><description><c>color</c></description></item>
/// <item><term><c>cta</c></term><description><c>link</c></description></item>
/// <item><term><c>footer</c></term><description><c>reusable</c></description></item>
/// </list>
/// <para>
/// Between this template, <see cref="Article"/>, and the three reference block types, every field
/// type in spec section 7.1 has a placement — which is the point of shipping reference content at
/// all. A field type nothing renders is a field type whose renderer nobody has ever seen run.
/// </para>
/// </remarks>
public partial class MarketingLanding : CmsTemplateBase;
