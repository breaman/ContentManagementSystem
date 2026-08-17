using ContentManagementSystem.Shared.Contracts.Dashboard;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Dashboard;

/// <summary>
/// One dashboard tile, unabridged (acceptance criterion P6 #8).
/// </summary>
/// <remarks>
/// What a tile's "show all" link opens, and deliberately the same component tree as the tile itself
/// over the same server queries with a larger limit. A separate screen with its own filters would be
/// a second definition of "needs attention", and the first time the two drifted the tile would be
/// advertising a list that did not contain what it promised.
/// </remarks>
public partial class DashboardTileScreen : ComponentBase
{
    /// <summary>How many rows each list shows here.</summary>
    /// <remarks>
    /// Enough that the list is the whole backlog in every ordinary case, and bounded because a
    /// screen is not a report: the server clamps it too, so a hand-typed limit cannot ask for
    /// everything.
    /// </remarks>
    public const int ListLimit = 100;

    /// <summary>Reads the tile.</summary>
    [Inject]
    private IDashboardClient Client { get; set; } = default!;

    /// <summary>Which tile, as the route spells it.</summary>
    [Parameter]
    public string? Tile { get; set; }

    /// <summary>The tile, or null while it is loading.</summary>
    [PersistentState]
    public DashboardTileContent? Content { get; set; }

    /// <summary>Whether the route named a tile that does not exist.</summary>
    private bool _unknown;

    /// <inheritdoc />
    /// <remarks>
    /// In <c>OnParametersSetAsync</c> rather than <c>OnInitializedAsync</c>, because navigating from
    /// one tile to another reuses the component: initialising once would leave an editor looking at
    /// the previous tile's rows under the new tile's heading.
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        // Parsed rather than trusted. The tile is a route segment, so anything at all can arrive
        // here, and an unrecognised name is a screen that says so rather than an empty one that
        // reads as "nothing needs attention".
        if (!Enum.TryParse<DashboardTile>(Tile, ignoreCase: true, out var tile))
        {
            _unknown = true;
            Content = null;

            return;
        }

        _unknown = false;

        if (Content?.Tile != tile)
        {
            Content = await Client.GetTileAsync(tile, ListLimit);
        }
    }
}
