using ContentManagementSystem.Shared.Contracts.Dashboard;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Feeds the dashboard a fixed set of tiles so the accessibility gate has markup to check
/// (tasks P6-24 to P6-27, P6-36).
/// </summary>
/// <remarks>
/// Deliberately varied, for the reason the other fakes here give: axe has nothing to say about a
/// tile that renders "nothing to do". The fixture therefore includes a row that links somewhere, a
/// row that links nowhere, an overdue row — which is the one drawn differently, and therefore the
/// one a colour-contrast rule has an opinion about — and a group with more rows than it shows.
/// </remarks>
public sealed class FakeDashboardClient : IDashboardClient
{
    /// <inheritdoc />
    public Task<DashboardContent?> GetAsync(int limit = 5, CancellationToken cancellationToken = default) =>
        Task.FromResult<DashboardContent?>(new DashboardContent(
            [.. Enum.GetValues<DashboardTile>().Select(Tile)],
            DateTimeOffset.UtcNow.AddMinutes(-2)));

    /// <inheritdoc />
    public Task<DashboardTileContent?> GetTileAsync(
        DashboardTile tile,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<DashboardTileContent?>(Tile(tile));

    private static DashboardTileContent Tile(DashboardTile tile) => new(
        tile,
        tile switch
        {
            DashboardTile.MyWork => "My work",
            DashboardTile.Scheduled => "Scheduled",
            DashboardTile.NeedsAttention => "Needs attention",
            _ => "Recent activity",
        },
        [
            new DashboardGroup(
                $"{tile}-overdue",
                "Past its review date",
                [
                    new DashboardItem(
                        DashboardItemKind.Page,
                        FakePageClient.Id,
                        "Pricing",
                        "Review was due 5 January 2026",
                        IsOverdue: true),
                    new DashboardItem(
                        DashboardItemKind.Media,
                        FakeMediaClient.PlacedId,
                        "Team photograph",
                        "No alternative text, and not marked decorative",
                        IsOverdue: true),
                ],
                7,
                "No content is past its review date."),
            new DashboardGroup(
                $"{tile}-activity",
                "Latest changes",
                [
                    // A row with no destination, which is a branch of its own: an audit entry
                    // records what happened to something that may since have been deleted.
                    new DashboardItem(
                        DashboardItemKind.Activity,
                        null,
                        "Update page version",
                        "by user 1, 2 hour(s) ago",
                        DateTimeOffset.UtcNow.AddHours(-2)),
                ],
                1,
                "Nothing has been changed yet."),
            // An empty group, so the "good news" branch is in the markup too.
            new DashboardGroup($"{tile}-empty", "Broken references", [], 0, "Every reference resolves."),
        ],
        tile is DashboardTile.MyWork
            ? "Review assignments arrive with the approval workflow in Phase 7."
            : null);
}
