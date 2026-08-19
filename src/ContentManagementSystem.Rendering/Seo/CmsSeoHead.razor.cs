using ContentManagementSystem.Core.Delivery.Seo;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Rendering.Seo;

/// <summary>
/// The search and social metadata of one page, as document head elements (task P8-01, P8-02, P8-03).
/// </summary>
/// <remarks>
/// Given its metadata rather than resolving any, so that it renders identically wherever it is put
/// and can be asserted on without a database. The one thing it does besides write elements is
/// declare the share image as a cache dependency: a page whose Open Graph image is replaced in the
/// library has to be re-rendered, and nothing else in the render would know that the head named it
/// (spec section 16.2).
/// </remarks>
public partial class CmsSeoHead : ComponentBase
{
    /// <summary>The resolved head, from <see cref="ISeoMetadataBuilder"/>.</summary>
    [Parameter]
    [EditorRequired]
    public SeoMetadata Metadata { get; set; } = SeoMetadata.Empty;

    /// <summary>
    /// The render this head belongs to, whose cache tags the share image is added to.
    /// </summary>
    /// <remarks>
    /// Optional so the component can be rendered on its own in a test. In delivery it is always
    /// supplied, and the missing tag would be a page that keeps a withdrawn image in its card.
    /// </remarks>
    [Parameter]
    public RenderContext? Context { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Metadata.OgImageMediaId is { } mediaId)
        {
            Context?.CacheTags.AddMedia(mediaId);
        }
    }
}
