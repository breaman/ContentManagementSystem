namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>multilineText</c> value, preserving the author's line breaks (spec section 7.1).
/// </summary>
/// <remarks>
/// <c>&lt;br&gt;</c> rather than <c>white-space: pre-line</c>: the break is part of what was
/// authored, and expressing it in markup means it survives being copied out of the page, read by a
/// screen reader, and styled by a site that resets white space. A CSS rule would also need the site
/// to carry a stylesheet for a field type to work, which nothing else here requires.
/// <para>
/// Both line ending conventions collapse to one break. A value pasted from a Windows editor arrives
/// with <c>\r\n</c> and must not render an extra blank line for it.
/// </para>
/// </remarks>
public partial class MultilineTextRenderer : CmsFieldRendererBase
{
    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];

    /// <summary>The stored text split into lines; empty when there is nothing to render.</summary>
    protected IReadOnlyList<string> Lines { get; private set; } = [];

    /// <inheritdoc />
    protected override void OnParametersSet() =>
        Lines = ValueText is { Length: > 0 } text
            ? text.Split(LineBreaks, StringSplitOptions.None)
            : [];
}
