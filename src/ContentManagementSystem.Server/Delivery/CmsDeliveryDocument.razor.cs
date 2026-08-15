using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Rendering;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Server.Delivery;

/// <summary>
/// The document a public page response is (spec section 15.4, task P3-13).
/// </summary>
/// <remarks>
/// The head is built from the version's own metadata, with the fallbacks spec section 18.1 states:
/// the document title falls back to the page title, and the canonical link falls back to the page's
/// own URL, so both are right for a page nobody has filled the SEO panel in for.
/// <para>
/// <strong>Nothing here may stream.</strong> Cache tags accumulate while the body renders, and a
/// response whose headers went out first would carry an incomplete set and produce a page that never
/// invalidates. Delivery renders this whole document to a string, then sets headers, then writes
/// (S2 spike, consequence 3) — which is why it is rendered through <c>HtmlRenderer</c> rather than
/// returned as a <c>RazorComponentResult</c>: the string path cannot regress into streaming by
/// somebody adding an attribute.
/// </para>
/// <para>
/// Open Graph, Twitter cards, and JSON-LD are Phase 8 (spec section 18.2). They are absent rather
/// than half-built here, because a page emitting an <c>og:image</c> pointing at a media library that
/// does not exist yet is worse than one emitting none.
/// </para>
/// </remarks>
public partial class CmsDeliveryDocument : ComponentBase
{
    /// <summary>The loaded version, for the document head.</summary>
    [Parameter]
    [EditorRequired]
    public PublishedContent Content { get; set; } = default!;

    /// <summary>The render context for the body, carrying the cache tags it will accumulate.</summary>
    [Parameter]
    [EditorRequired]
    public RenderContext Context { get; set; } = default!;

    /// <summary>
    /// The <c>robots</c> directive pair, as the meta element spells it.
    /// </summary>
    /// <remarks>
    /// Always emitted, including the permissive <c>index, follow</c> case. An absent meta element and
    /// an explicit "yes" mean the same thing to a crawler but not to a person auditing why a page is
    /// missing from the index, and the cost of saying so is thirty bytes.
    /// </remarks>
    protected string Robots =>
        $"{(Content.RobotsIndex ? "index" : "noindex")}, {(Content.RobotsFollow ? "follow" : "nofollow")}";
}
