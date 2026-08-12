using Microsoft.AspNetCore.Components;

namespace S2.DynamicSsr.Cms;

/// <summary>Root component for a delivery response: cascades the render context to the template.</summary>
public partial class DeliveryHost : ComponentBase
{
    [Parameter]
    [EditorRequired]
    public CmsRenderContext Context { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public Type TemplateType { get; set; } = default!;
}
