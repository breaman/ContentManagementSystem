using System.Text.Json;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>boolean</c> value (spec section 7.1).
/// </summary>
/// <remarks>
/// "Yes" and "No" are literal English, which the deployment's one supported culture makes safe
/// (<c>en-US</c> only, open question Q1). A site wanting other words places the property in a block
/// and branches on it in that block's markup, rather than configuring vocabulary through a field
/// type that declares no setting for it.
/// <para>
/// <c>false</c> renders, and renders differently from absent. The field type treats a deliberate
/// "off" as a filled value, so a renderer that emitted nothing for it would make the two
/// indistinguishable on the page and lose the author's answer.
/// </para>
/// </remarks>
public partial class BooleanRenderer : CmsFieldRendererBase
{
    /// <summary>The stored switch, or null when the value is absent, cleared, or not a boolean.</summary>
    protected bool? State =>
        Member(ValueMember)?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
}
