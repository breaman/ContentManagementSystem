namespace ContentManagementSystem.Rendering.Templates;

/// <summary>
/// Reference template: a long-form editorial page (task P3-10, spec section 8.2).
/// </summary>
/// <remarks>
/// The second of the two reference templates, and the one that carries the value-shaped field types.
/// <see cref="MarketingLanding"/> is almost entirely block lists; this one is almost entirely single
/// values, so between them every renderer in <c>Rendering/Fields</c> has a zone it is reached
/// through.
/// <para>
/// As on the landing page, the table below records the <em>intended</em> zone definitions rather
/// than a contract this component enforces — zone definitions are database rows a <c>Developer</c>
/// owns (spec section 8.1), and this markup only declares placement.
/// </para>
/// <list type="table">
/// <listheader><term>Zone</term><description>Field type</description></listheader>
/// <item><term><c>kicker</c></term><description><c>plainText</c></description></item>
/// <item><term><c>standfirst</c></term><description><c>multilineText</c></description></item>
/// <item><term><c>publishedAt</c></term><description><c>dateTime</c></description></item>
/// <item><term><c>reviewedOn</c></term><description><c>date</c></description></item>
/// <item><term><c>readingMinutes</c></term><description><c>number</c></description></item>
/// <item><term><c>isFeatured</c></term><description><c>boolean</c></description></item>
/// <item><term><c>layout</c></term><description><c>choice</c></description></item>
/// <item><term><c>poster</c></term><description><c>media</c></description></item>
/// <item><term><c>body</c></term><description><c>blocks</c></description></item>
/// <item><term><c>embed</c></term><description><c>html</c></description></item>
/// <item><term><c>gallery</c></term><description><c>mediaList</c></description></item>
/// <item><term><c>tags</c></term><description><c>tags</c></description></item>
/// <item><term><c>related</c></term><description><c>pageReference</c></description></item>
/// <item><term><c>analytics</c></term><description><c>json</c></description></item>
/// </list>
/// </remarks>
public partial class Article : CmsTemplateBase;
