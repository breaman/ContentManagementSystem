namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>color</c> value (spec section 7.1).
/// </summary>
/// <remarks>
/// A colour is almost always read by a block's own markup rather than placed as a zone, so this is
/// the fallback rendering: the hex value, carried on a <c>data-color</c> attribute a stylesheet or a
/// small script can act on, with the same text visible so the zone is never silently blank.
/// <para>
/// <strong>No inline <c>style</c> attribute.</strong> Emitting one would put author-controlled text
/// into a CSS context on every page carrying a colour, which is the single place spec section 20.5's
/// content security policy would then have to be relaxed for. The value's shape is checked on write,
/// but the render path is not the place to lean on that.
/// </para>
/// </remarks>
public partial class ColorRenderer : CmsFieldRendererBase
{
    /// <summary>The stored <c>#RRGGBB</c> value; null when absent or not a string.</summary>
    protected string? Color => ValueText;
}
