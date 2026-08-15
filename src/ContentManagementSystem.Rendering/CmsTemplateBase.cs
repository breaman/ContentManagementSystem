using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Base class for a template component: the markup one page shape is laid out with
/// (spec section 8.2).
/// </summary>
/// <remarks>
/// A template declares <em>placement</em> and nothing else. It names its zones with
/// <c>&lt;CmsZone Name="hero" /&gt;</c> and never learns what field type fills one, which is what
/// lets a developer change a zone from rich text to a block list in the backoffice without touching
/// the markup (spec section 8.1).
/// <para>
/// A zone the template names but the payload has never held renders empty, so adding a zone cannot
/// break already-published pages.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// @attribute [CmsTemplate("marketing-landing", "Marketing Landing Page")]
/// @inherits CmsTemplateBase
///
/// &lt;article class="landing"&gt;
///     &lt;h1&gt;@Page.Title&lt;/h1&gt;
///     &lt;header&gt;&lt;CmsZone Name="hero" /&gt;&lt;/header&gt;
///     &lt;div class="container"&gt;&lt;CmsZone Name="body" /&gt;&lt;/div&gt;
/// &lt;/article&gt;
/// </code>
/// </example>
public abstract class CmsTemplateBase : ComponentBase
{
    /// <summary>The render context, cascaded by the delivery host.</summary>
    [CascadingParameter]
    public RenderContext Context { get; set; } = default!;

    /// <summary>The page version being rendered.</summary>
    protected RenderPage Page => Context.Page;

    /// <summary>Whether an editor is previewing rather than a visitor reading the live site.</summary>
    protected bool IsPreview => Context.IsPreview;
}
