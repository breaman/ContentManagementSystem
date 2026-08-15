using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Marks a link whose target is unpublished (task P3-20, spec section 12.3).
/// </summary>
/// <remarks>
/// Rendered only under <see cref="CmsRenderMode.Preview"/>, because that is the only mode in which
/// an unpublished target resolves at all — the public path resolves it to nothing and the link
/// degrades to plain text. So this component cannot appear on a public page even by mistake: there
/// is no anchor for it to sit inside.
/// <para>
/// A component rather than a string in two renderers, so the wording, the class, and the tooltip are
/// one decision. Two copies would differ within a release of somebody rewording one of them.
/// </para>
/// </remarks>
public partial class CmsDraftBadge : ComponentBase;
