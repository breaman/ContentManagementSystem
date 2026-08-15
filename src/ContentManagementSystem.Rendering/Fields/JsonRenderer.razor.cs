namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>json</c> value: nothing at all (spec section 7.1).
/// </summary>
/// <remarks>
/// <strong>The empty render is the feature.</strong> <c>json</c> is the developer-only escape hatch
/// — configuration for a widget, a shape no other field type expresses — and it is read by the block
/// component that asked for it. Printing it would put internal structure onto a public page, and
/// printing it inside a <c>&lt;script&gt;</c> block, which is what a "useful" default would grow
/// into, would hand authored data to a JavaScript parser.
/// <para>
/// It renders nothing <em>silently</em>, unlike the conditions in spec section 15.3 that also render
/// nothing. Those are the deployment and the content disagreeing and are logged as such; this is the
/// intended outcome, and logging it once per property per cache miss would train an operator to
/// ignore the warnings that matter.
/// </para>
/// <para>
/// It still exists, rather than being left out of the catalog, because "no renderer registered" is
/// a different fact that the startup check reports as a deployment defect.
/// </para>
/// </remarks>
public partial class JsonRenderer : CmsFieldRendererBase
{
}
