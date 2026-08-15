using System.Text.Json;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Base class for a block component: the markup one block type renders as (spec section 8.2).
/// </summary>
/// <remarks>
/// Blocks are rendered by the <c>blocks</c> field renderer, one per item in the stored list, each
/// keyed by its block id so that re-ordering a list moves markup rather than rewriting it.
/// <para>
/// A block's properties arrive as raw JSON for the same reason a zone's value does: a block type's
/// property set is data a developer edits in the backoffice, so there is no CLR type to bind to. A
/// block authored against an older revision of its type may be missing properties the markup reads,
/// and reading one that is not there must render nothing rather than throw — one broken block is
/// isolated by its boundary, but a block that renders nothing at all is better still.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// @attribute [CmsBlockType("quote", "Pull Quote")]
/// @inherits CmsBlockBase
///
/// &lt;figure class="quote"&gt;
///     &lt;blockquote&gt;@Text("quote")&lt;/blockquote&gt;
///     &lt;figcaption&gt;@Text("attribution")&lt;/figcaption&gt;
/// &lt;/figure&gt;
/// </code>
/// </example>
public abstract class CmsBlockBase : ComponentBase
{
    /// <summary>The block's stored <c>properties</c> object, keyed by property key.</summary>
    [Parameter]
    public JsonElement Properties { get; set; }

    /// <summary>The block instance's id, stable across edits and used in render diagnostics.</summary>
    [Parameter]
    public Guid BlockId { get; set; }

    /// <summary>The revision of the block type this instance was authored against.</summary>
    [Parameter]
    public int BlockTypeRevision { get; set; }

    /// <summary>The render context, cascaded by the delivery host.</summary>
    [CascadingParameter]
    public RenderContext Context { get; set; } = default!;

    /// <summary>Reads a property's stored value object.</summary>
    /// <param name="propertyKey">The property key.</param>
    /// <returns>The whole property object, or null when this block does not carry it.</returns>
    protected JsonElement? Property(string propertyKey) =>
        Properties.ValueKind is JsonValueKind.Object &&
        Properties.TryGetProperty(propertyKey, out var property) &&
        property.ValueKind is JsonValueKind.Object
            ? property
            : null;

    /// <summary>Reads a simple text property.</summary>
    /// <param name="propertyKey">The property key.</param>
    /// <returns>
    /// The stored text, or an empty string when the property is absent, cleared, or holds something
    /// other than a string. Empty rather than null so markup can interpolate it directly.
    /// </returns>
    /// <remarks>
    /// A convenience for the plain-text-shaped field types, which store their value under
    /// <c>value</c>. Anything structured — a media reference, a nested block list — is rendered by
    /// its own field renderer rather than read out here by hand.
    /// </remarks>
    protected string Text(string propertyKey) =>
        Property(propertyKey) is { } property &&
        property.TryGetProperty("value", out var value) &&
        value.ValueKind is JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
