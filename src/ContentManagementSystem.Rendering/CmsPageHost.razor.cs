using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// The root component of a page render: resolves the template component and cascades the render
/// context to everything below it (spec section 15.2).
/// </summary>
/// <remarks>
/// Shared by public delivery and preview deliberately. Preview that rendered through a second path
/// would be a preview of something other than the page — the whole point of it is that an editor
/// sees what a visitor will see, so the only difference between the two is the
/// <see cref="RenderContext.Mode"/> on the context and which version was loaded.
/// <para>
/// <strong>Nothing here streams.</strong> Cache tags accumulate while the tree renders, so a
/// response whose headers go out before the render completes carries an incomplete tag set and
/// produces a page that never invalidates. Delivery renders this to a buffer, then sets headers,
/// then writes (S2 spike, consequence 3), and no component beneath it may opt into streaming
/// rendering.
/// </para>
/// </remarks>
public partial class CmsPageHost : ComponentBase
{
    /// <summary>The page version to render, its content, and the tags it accumulates.</summary>
    [Parameter]
    [EditorRequired]
    public RenderContext Context { get; set; } = default!;

    [Inject]
    private ICmsComponentCatalog Components { get; set; } = default!;

    [Inject]
    private ILogger<CmsPageHost> Logger { get; set; } = default!;

    private Type? TemplateType { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        ArgumentNullException.ThrowIfNull(Context);

        if (Components.TryGetTemplate(Context.Page.TemplateKey, out var template))
        {
            TemplateType = template;

            return;
        }

        TemplateType = null;

        // An error rather than a warning, and never an exception: this is a deployment that lost a
        // template component while pages built on it are still live. The reconciler has already
        // marked the row orphaned and the cms-templates health check is already degraded
        // (spec section 8.4); what is left is for the request to survive.
        Logger.LogError(
            "No deployed component declares template key '{TemplateKey}' (page {PageId}, " +
            "version {VersionId}).",
            Context.Page.TemplateKey,
            Context.Page.Id,
            Context.Page.VersionId);
    }
}
