using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Server.Delivery.Preview;

/// <summary>
/// The document a preview request receives (tasks P3-16 and P3-21, spec sections 12.1 and 12.3).
/// </summary>
/// <remarks>
/// It contains no page content at all — only the toolbar and a frame — and that is the point. The
/// page is fetched by the browser from the content endpoint and rendered by
/// <c>CmsDeliveryDocument</c>, the same component and the same pipeline the public site is served
/// through, so "preview shows what will be published" is a fact about the code rather than a promise
/// somebody has to keep up.
/// </remarks>
public partial class CmsPreviewDocument : ComponentBase
{
    /// <summary>What is on screen, and where the toolbar's links go.</summary>
    [Parameter]
    [EditorRequired]
    public PreviewChrome Chrome { get; set; } = default!;
}
