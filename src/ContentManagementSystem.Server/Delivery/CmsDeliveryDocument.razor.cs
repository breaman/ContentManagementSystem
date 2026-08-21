using ContentManagementSystem.Core.Delivery.Seo;
using ContentManagementSystem.Rendering;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Server.Delivery;

/// <summary>
/// The document a public page response is (spec section 15.4, task P3-13).
/// </summary>
/// <remarks>
/// The head is <c>CmsSeoHead</c>'s, built from metadata this document is handed rather than resolved
/// here: the fallbacks of spec section 18.1, the Open Graph and Twitter tags, and the JSON-LD are
/// one component's job and one service's rules, so that preview and delivery cannot emit different
/// heads for the same version.
/// <para>
/// <strong>Nothing here may stream.</strong> Cache tags accumulate while the body renders, and a
/// response whose headers went out first would carry an incomplete set and produce a page that never
/// invalidates. Delivery renders this whole document to a string, then sets headers, then writes
/// (S2 spike, consequence 3) — which is why it is rendered through <c>HtmlRenderer</c> rather than
/// returned as a <c>RazorComponentResult</c>: the string path cannot regress into streaming by
/// somebody adding an attribute.
/// </para>
/// </remarks>
public partial class CmsDeliveryDocument : ComponentBase
{
    /// <summary>The render context for the body, carrying the cache tags it will accumulate.</summary>
    [Parameter]
    [EditorRequired]
    public RenderContext Context { get; set; } = default!;

    /// <summary>The resolved document head (tasks P8-01 to P8-03).</summary>
    [Parameter]
    [EditorRequired]
    public SeoMetadata Seo { get; set; } = SeoMetadata.Empty;

    /// <summary>
    /// Where to find the administrator-authored stylesheet, or null when there is none to link
    /// (task P10-07, spec section 30.1).
    /// </summary>
    /// <remarks>
    /// Supplied by the renderer rather than resolved here, for the reason the head is: a component
    /// that queried while rendering would make what the document says depend on when it happened to
    /// run. The renderer also decides <em>which</em> stylesheet — the published one for a live
    /// render, the draft for a preview — so this document has one link and no branch, and preview
    /// cannot drift into showing what is live.
    /// </remarks>
    [Parameter]
    public string? CustomStylesheetHref { get; set; }
}
