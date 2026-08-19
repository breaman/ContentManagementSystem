using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Navigation;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ContentManagementSystem.Rendering.Navigation;

/// <summary>
/// Renders one navigation menu, and declares it as a cache dependency (tasks P8-15 to P8-17).
/// </summary>
/// <remarks>
/// The dependency is the point of the component as much as the markup is. A page showing a menu is
/// invalidated when the menu changes, which is what makes "navigation reflects publish state within
/// one cache generation" true of the page rather than of the menu alone (acceptance criterion
/// P8 #9, spec section 16.2).
/// <para>
/// Resolves its own nodes rather than being handed them, because it is placed by a template author
/// who has no way to run a query. That is a database read during a render, which is affordable
/// exactly because the response it becomes part of is cached under the tag this adds.
/// </para>
/// </remarks>
public partial class CmsNavigation : ComponentBase
{
    /// <summary>Which menu to render. Null renders navigation generated from the content tree.</summary>
    [Parameter]
    public string? MenuKey { get; set; }

    /// <summary>How many levels of the tree to include. Ignored for a managed menu.</summary>
    [Parameter]
    public int MaxDepth { get; set; } = 2;

    /// <summary>The accessible name of the landmark.</summary>
    [Parameter]
    public string Label { get; set; } = "Main";

    /// <summary>The render this menu belongs to, whose cache tags it adds to.</summary>
    [Parameter]
    public RenderContext? Context { get; set; }

    /// <summary>The navigation reader.</summary>
    [Inject]
    public INavigationService Navigation { get; set; } = default!;

    /// <summary>The nodes to render, once resolved.</summary>
    protected IReadOnlyList<NavigationNode> Nodes { get; private set; } = [];

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        // Added before the read rather than after it, so a menu that turns out to be empty still
        // leaves the page depending on it — otherwise the first page published into an empty menu
        // would never evict the pages that render it.
        Context?.CacheTags.AddNavigation(MenuKey ?? CacheTags.StructuralMenuKey);

        Nodes = string.IsNullOrWhiteSpace(MenuKey)
            ? await Navigation.GetStructuralAsync(MaxDepth)
            : await Navigation.GetMenuAsync(MenuKey);
    }

    /// <summary>Renders a list of nodes and everything beneath them.</summary>
    /// <param name="nodes">The nodes at this level.</param>
    /// <returns>The fragment.</returns>
    /// <remarks>
    /// Recursion in code rather than a self-referencing component: the nesting is a list inside a
    /// list item, and expressing that through a component boundary makes the markup harder to read
    /// than the loop it replaces.
    /// </remarks>
    protected RenderFragment Render(IReadOnlyList<NavigationNode> nodes) => builder =>
    {
        builder.OpenElement(0, "ul");

        var sequence = 1;

        foreach (var node in nodes)
        {
            builder.OpenElement(sequence++, "li");
            builder.OpenElement(sequence++, "a");
            builder.AddAttribute(sequence++, "href", node.Url);

            if (node.OpenInNewTab)
            {
                builder.AddAttribute(sequence++, "target", "_blank");

                // noopener is not decoration: without it the opened page can reach back through
                // window.opener and navigate this one.
                builder.AddAttribute(sequence++, "rel", "noopener noreferrer");
            }

            builder.AddContent(sequence++, node.Label);
            builder.CloseElement();

            if (node.Children.Count > 0)
            {
                builder.AddContent(sequence++, Render(node.Children));
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    };
}
