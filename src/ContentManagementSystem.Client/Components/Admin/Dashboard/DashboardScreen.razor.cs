using ContentManagementSystem.Shared.Contracts.Dashboard;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Dashboard;

/// <summary>
/// The backoffice landing screen of spec section 14.9 (tasks P6-24 to P6-27).
/// </summary>
/// <remarks>
/// Four tiles, each a few rows deep, each with a link into the same query unabridged. The tiles are
/// short on purpose: a landing screen is read at a glance, and the value is in noticing that
/// something is overdue rather than in reading all forty overdue things from here.
/// <para>
/// It is also the answer to "what is the CMS's front door". Until now <c>/admin</c> was not a route
/// at all, and an editor arriving at the backoffice landed on whichever list they had bookmarked.
/// </para>
/// </remarks>
public partial class DashboardScreen : ComponentBase
{
    /// <summary>How many rows each list shows on the landing screen.</summary>
    public const int TileLimit = 5;

    /// <summary>Reads the tiles.</summary>
    [Inject]
    private IDashboardClient Client { get; set; } = default!;

    /// <summary>Says how long ago the snapshot was taken.</summary>
    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    /// <summary>The tiles, or null while they are still loading.</summary>
    [PersistentState]
    public DashboardContent? Content { get; set; }

    /// <summary>
    /// How long ago the numbers were read.
    /// </summary>
    /// <remarks>
    /// Said rather than implied. A dashboard is a snapshot, and one left open on a second monitor
    /// overnight is a snapshot of yesterday being read as though it were now.
    /// </remarks>
    private string Generated
    {
        get
        {
            if (Content is null) return string.Empty;

            var elapsed = Clock.GetUtcNow() - Content.GeneratedOn;

            return elapsed switch
            {
                { TotalMinutes: < 1 } => "just now",
                { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes} minute(s) ago",
                _ => $"{(int)elapsed.TotalHours} hour(s) ago",
            };
        }
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync() =>
        Content ??= await Client.GetAsync(TileLimit);
}
