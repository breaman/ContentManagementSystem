using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Renders one property of the block being rendered — the block-level <see cref="CmsZone"/>
/// (spec section 8.2).
/// </summary>
/// <remarks>
/// A block type's property set is data a developer edits in the backoffice, so a block component
/// cannot bind its properties to a CLR type. <see cref="CmsBlockBase.Text"/> covers the text-shaped
/// ones; everything structured — an image, a link, a nested block list, rich text that has to go
/// through the sanitizer — is rendered by the field type's own renderer, which is what this
/// resolves.
/// <para>
/// The same four conditions render nothing as on a zone, and every one of them is ordinary rather
/// than exceptional (spec section 15.3): the property was never authored, an editor cleared it, the
/// stored value carries no field type discriminator, or no renderer is registered for the field type
/// it names. The last two are logged, because both mean the deployment and the content disagree.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// @attribute [CmsBlockType("hero-banner", "Hero Banner")]
/// @inherits CmsBlockBase
///
/// &lt;section class="hero"&gt;
///     &lt;h2&gt;@Text("headline")&lt;/h2&gt;
///     &lt;CmsBlockProperty Name="image" /&gt;
/// &lt;/section&gt;
/// </code>
/// </example>
public partial class CmsBlockProperty : ComponentBase
{
    /// <summary>The property key, as declared by the block type and stored in the payload.</summary>
    [Parameter]
    [EditorRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>The block being rendered, cascaded by the <c>blocks</c> field renderer.</summary>
    [CascadingParameter]
    public BlockRenderContext? Block { get; set; }

    /// <summary>The render context, cascaded by the delivery host.</summary>
    [CascadingParameter]
    public RenderContext Context { get; set; } = default!;

    [Inject]
    private IFieldRendererCatalog Renderers { get; set; } = default!;

    [Inject]
    private ILogger<CmsBlockProperty> Logger { get; set; } = default!;

    private Type? RendererType { get; set; }

    private Dictionary<string, object?> RendererParameters { get; set; } = [];

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        RendererType = null;
        RendererParameters = [];

        if (Block is null)
        {
            // Only reachable from a block component rendered outside the blocks renderer — a
            // developer previewing their markup in isolation, most likely. Logged rather than
            // thrown, because nothing about a misplaced component justifies taking a page down.
            Logger.LogWarning(
                "'{Component}' for property '{PropertyKey}' rendered with no cascading {ContextType}.",
                nameof(CmsBlockProperty),
                Name,
                nameof(BlockRenderContext));

            return;
        }

        if (Block.Property(Name) is not { } value) return;

        if (!FieldValueDispatch.TryGetFieldTypeKey(value, out var fieldTypeKey))
        {
            Logger.LogWarning(
                "Property '{PropertyKey}' of block {BlockId} ('{BlockTypeKey}') on page {PageId} " +
                "version {VersionId} stores no field type discriminator.",
                Name,
                Block.BlockId,
                Block.BlockTypeKey,
                Context?.Page.Id,
                Context?.Page.VersionId);

            return;
        }

        if (!Renderers.TryGetRenderer(fieldTypeKey, out var renderer))
        {
            Logger.LogWarning(
                "No renderer is registered for field type '{FieldTypeKey}' (property '{PropertyKey}' " +
                "of block {BlockId} '{BlockTypeKey}', page {PageId}, version {VersionId}); the " +
                "property renders nothing.",
                fieldTypeKey,
                Name,
                Block.BlockId,
                Block.BlockTypeKey,
                Context?.Page.Id,
                Context?.Page.VersionId);

            return;
        }

        RendererType = renderer;
        RendererParameters = FieldValueDispatch.Parameters(
            value,
            Name,
            FieldValueDispatch.Configuration(Block.Schema?.FindProperty(Name), fieldTypeKey));
    }
}
