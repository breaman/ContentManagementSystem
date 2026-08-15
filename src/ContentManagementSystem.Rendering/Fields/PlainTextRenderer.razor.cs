namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>plainText</c> value (spec section 7.1).
/// </summary>
/// <remarks>
/// The simplest renderer there is, and deliberately so. The field type stores the value un-stripped
/// so that an author who types <c>a &lt; b</c> keeps their characters, which leaves HTML encoding
/// here as the only thing between a stored angle bracket and an injected element.
/// </remarks>
public partial class PlainTextRenderer : CmsFieldRendererBase
{
    /// <summary>The stored text; empty when the value is absent, cleared, or is not a string.</summary>
    protected string Text => ValueText ?? string.Empty;
}
