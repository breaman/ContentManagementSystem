using Bunit;

using ContentManagementSystem.Client.Components.Admin.Dashboard;
using ContentManagementSystem.Shared.Contracts.Dashboard;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ContentManagementSystem.Client.Tests.Dashboard;

/// <summary>
/// The backoffice landing screen (tasks P6-24 to P6-27, acceptance criterion P6 #8).
/// </summary>
/// <remarks>
/// The criterion has two halves and both are pinned here: the tiles surface the signed-in editor's
/// work and what is overdue, and <em>every tile deep-links into a correctly filtered list</em> —
/// which is the same server query at a larger limit rather than a screen that resembles it.
/// </remarks>
public class DashboardScreenTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    private readonly StubDashboardClient _client = new();

    public DashboardScreenTests()
    {
        _bunit.JSInterop.Mode = JSRuntimeMode.Loose;

        _bunit.Services.AddSingleton<IDashboardClient>(_client);
        _bunit.Services.AddSingleton<TimeProvider>(
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero)));

        _bunit.AddAuthorization().SetAuthorized("Elena").SetRoles(CmsRoles.Editor);
    }

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void EveryTileIsDrawnAndLinksToItsOwnUnabridgedList()
    {
        var screen = _bunit.Render<DashboardScreen>();

        var links = screen.FindAll("section.card a")
            .Select(link => link.GetAttribute("href"))
            .ToList();

        links.Should().Contain(
            [
                "/admin/dashboard/MyWork",
                "/admin/dashboard/Scheduled",
                "/admin/dashboard/NeedsAttention",
                "/admin/dashboard/RecentActivity",
            ],
            "a tile that cannot be opened is a report, not a dashboard");
    }

    [Test]
    public void ARowLinksToThePageItIsAboutAndSaysWhyItIsListed()
    {
        var screen = _bunit.Render<DashboardScreen>();

        var row = screen.Find(".cms-dashboard__item");

        row.QuerySelector("a")!.GetAttribute("href").Should().Be("/admin/pages/7");
        row.TextContent.Should().Contain(
            "Review was due",
            "a list of titles with no reason beside them is a list nobody can prioritise");
    }

    [Test]
    public void AnOverdueRowSaysSoInWordsRatherThanOnlyInColour()
    {
        var screen = _bunit.Render<DashboardScreen>();

        var row = screen.Find(".cms-dashboard__item--overdue");

        row.TextContent.Should().Contain(
            "Overdue",
            "a row that is only red is one half the people reading it cannot tell from the rest (P6-39)");
    }

    [Test]
    public void ATileShowingSomeOfManySaysHowManyThereAre()
    {
        var screen = _bunit.Render<DashboardScreen>();

        screen.Markup.Should().Contain("Showing 1 of 12");
    }

    [Test]
    public void TheTileScreenLoadsTheTileTheRouteNames()
    {
        var screen = _bunit.Render<DashboardTileScreen>(parameters => parameters
            .Add(component => component.Tile, "NeedsAttention"));

        _client.Requested.Should().Equal(DashboardTile.NeedsAttention);
        screen.Find("h1").TextContent.Should().Be("Needs attention");
    }

    [Test]
    public void ATileNameNothingWroteIsSaidRatherThanShownAsAnEmptyList()
    {
        var screen = _bunit.Render<DashboardTileScreen>(parameters => parameters
            .Add(component => component.Tile, "everything"));

        screen.Markup.Should().Contain(
            "No such tile",
            "an empty screen would read as 'nothing needs attention', which is the opposite of true");
        _client.Requested.Should().BeEmpty();
    }

    /// <summary>One tile's worth of rows, including an overdue one and a trimmed list.</summary>
    private sealed class StubDashboardClient : IDashboardClient
    {
        /// <summary>Every tile asked for by name.</summary>
        public List<DashboardTile> Requested { get; } = [];

        /// <inheritdoc />
        public Task<DashboardContent?> GetAsync(int limit = 5, CancellationToken cancellationToken = default) =>
            Task.FromResult<DashboardContent?>(new DashboardContent(
                [.. Enum.GetValues<DashboardTile>().Select(Tile)],
                new DateTimeOffset(2026, 8, 16, 11, 59, 0, TimeSpan.Zero)));

        /// <inheritdoc />
        public Task<DashboardTileContent?> GetTileAsync(
            DashboardTile tile,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            Requested.Add(tile);

            return Task.FromResult<DashboardTileContent?>(Tile(tile));
        }

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
                    $"{tile}-group",
                    "Past its review date",
                    [
                        new DashboardItem(
                            DashboardItemKind.Page,
                            7,
                            "Pricing",
                            "Review was due 5 January 2026",
                            IsOverdue: true),
                    ],
                    12,
                    "Nothing to do."),
            ]);
    }
}
