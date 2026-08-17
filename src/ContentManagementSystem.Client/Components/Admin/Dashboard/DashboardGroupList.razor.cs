using System.Globalization;

using ContentManagementSystem.Shared.Contracts.Dashboard;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Dashboard;

/// <summary>
/// One list within a dashboard tile (tasks P6-24 to P6-27).
/// </summary>
/// <remarks>
/// Every row is a link to the thing it is about, which is the difference between a dashboard and a
/// report. A row that says "Pricing is 12 days past its review date" and cannot be clicked has told
/// an editor about a job and then asked them to go and find it.
/// <para>
/// The count beside the heading is the total, not the number shown, so a tile displaying five of
/// forty says forty. A dashboard that quietly showed the first five would be hiding exactly the
/// backlog it exists to surface.
/// </para>
/// </remarks>
public partial class DashboardGroupList : ComponentBase
{
    /// <summary>The list to draw.</summary>
    [Parameter]
    [EditorRequired]
    public DashboardGroup Group { get; set; } = default!;

    /// <summary>
    /// Which heading level the group's title is.
    /// </summary>
    /// <remarks>
    /// A parameter because the same list appears under two different depths of heading: on the
    /// landing screen it sits inside a tile whose own title is an <c>h2</c>, and on a tile's own
    /// screen the page title is the <c>h1</c> and there is nothing between. Hard-coding either one
    /// makes the other skip a level, which is what axe's heading-order rule reports and what a
    /// screen reader's heading navigation actually stumbles over.
    /// </remarks>
    [Parameter]
    public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// Where a row goes when it is clicked, or null for one that goes nowhere.
    /// </summary>
    /// <remarks>
    /// Two kinds of row deliberately go nowhere. An audit entry records what happened to something
    /// that may since have been deleted, and a link to a page that is gone is worse than no link. A
    /// 404'd URL would want the redirect screen, and the redirect <em>API</em> shipped in Phase 3
    /// while its screen has not — a link to a route nothing serves would be the dashboard's own
    /// broken reference.
    /// </remarks>
    private static string? Link(DashboardItem item) => item switch
    {
        { Kind: DashboardItemKind.Page, Id: { } id } =>
            $"/admin/pages/{id.ToString(CultureInfo.InvariantCulture)}",
        { Kind: DashboardItemKind.Media, Id: { } id } =>
            $"/admin/media/{id.ToString(CultureInfo.InvariantCulture)}",
        _ => null,
    };
}
