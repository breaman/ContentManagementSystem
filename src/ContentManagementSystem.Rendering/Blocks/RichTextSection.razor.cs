namespace ContentManagementSystem.Rendering.Blocks;

/// <summary>
/// Reference block type: a prose section (task P3-10, spec section 8.2).
/// </summary>
/// <remarks>
/// The workhorse of any body zone, and the block on which the sanitization contract is visible:
/// <c>body</c> is rich text and <c>embed</c> is hand-written HTML, and both are re-sanitized as they
/// render (ADR-0008) rather than trusted because they were sanitized when they were saved. A row
/// that arrived by import, by restore, or by a build that predates a tightening of the allowlist has
/// never been through the current profile.
/// <list type="table">
/// <listheader><term>Property</term><description>Field type</description></listheader>
/// <item><term><c>body</c></term><description><c>richText</c></description></item>
/// <item><term><c>alignment</c></term><description><c>choice</c></description></item>
/// <item><term><c>embed</c></term><description><c>html</c>, restricted to the <c>Developer</c> profile</description></item>
/// <item><term><c>settings</c></term><description><c>json</c>, which renders nothing by design</description></item>
/// </list>
/// <para>
/// The class is <c>RichTextSection</c> rather than <c>RichText</c>: the key is what content stores
/// and the class name is free, and a type called <c>RichText</c> sitting one namespace away from
/// <c>RichTextRenderer</c> is the sort of pair that gets imported by mistake in a <c>.razor</c> file
/// where <c>@using</c> order decides the winner.
/// </para>
/// </remarks>
public partial class RichTextSection : CmsBlockBase;
