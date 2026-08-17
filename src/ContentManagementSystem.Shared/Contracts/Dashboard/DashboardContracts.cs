namespace ContentManagementSystem.Shared.Contracts.Dashboard;

/// <summary>
/// The four tiles of the backoffice landing screen (spec section 14.9, tasks P6-24 to P6-27).
/// </summary>
/// <remarks>
/// A closed set rather than free-form keys, because each tile is also a route: the "more" link on a
/// tile opens the same query unabridged, and a route that accepted any string would be a screen that
/// could be asked for a tile nobody wrote.
/// </remarks>
public enum DashboardTile
{
    /// <summary>What the signed-in editor has in progress.</summary>
    MyWork = 0,

    /// <summary>What publishes or expires in the next seven days.</summary>
    Scheduled = 1,

    /// <summary>What has gone wrong quietly and nobody has looked at.</summary>
    NeedsAttention = 2,

    /// <summary>What has been done to content lately.</summary>
    RecentActivity = 3,
}

/// <summary>What a dashboard row points at, which decides where clicking it goes.</summary>
public enum DashboardItemKind
{
    /// <summary>A page. Opens in the editor.</summary>
    Page = 0,

    /// <summary>A media item. Opens in the library.</summary>
    Media = 1,

    /// <summary>A URL nobody serves. Opens the redirect screen, where a rule can be written for it.</summary>
    Url = 2,

    /// <summary>An audit entry. It points at whatever it was about, when that still exists.</summary>
    Activity = 3,
}

/// <summary>
/// One row of a dashboard tile.
/// </summary>
/// <param name="Kind">What the row points at.</param>
/// <param name="Id">Identity of the entity, or null for a row that is not one — a 404'd URL.</param>
/// <param name="Title">What the row is called, phrased as content rather than as an identity.</param>
/// <param name="Detail">
/// The second line: why this row is here. "Review was due 12 days ago" is the whole value of a
/// needs-attention row, and a list of titles without it is a list nobody can prioritise.
/// </param>
/// <param name="When">The instant the row is about, or null when it is not about one.</param>
/// <param name="IsOverdue">
/// Whether this row has passed the moment it should have been dealt with — a scheduled publish whose
/// time came and went, a review date in the past. Drawn differently <em>and</em> said in the detail,
/// never by colour alone (task P6-39).
/// </param>
public sealed record DashboardItem(
    DashboardItemKind Kind,
    int? Id,
    string Title,
    string Detail,
    DateTimeOffset? When = null,
    bool IsOverdue = false);

/// <summary>
/// One list within a tile.
/// </summary>
/// <param name="Key">Stable identifier, used as an element id and in tests.</param>
/// <param name="Title">Heading above the list.</param>
/// <param name="Items">The rows, already trimmed to the requested limit.</param>
/// <param name="TotalCount">
/// How many rows there are altogether. Reported separately so a tile showing five of forty says so —
/// a tile that showed five and implied five is a dashboard that hides the backlog it exists to
/// surface.
/// </param>
/// <param name="EmptyMessage">What to say when there are none. Written per group, because "nothing
/// is overdue" and "you have nothing in progress" are different pieces of good news.</param>
public sealed record DashboardGroup(
    string Key,
    string Title,
    IReadOnlyList<DashboardItem> Items,
    int TotalCount,
    string EmptyMessage);

/// <summary>
/// One tile, and the lists inside it.
/// </summary>
/// <param name="Tile">Which tile this is.</param>
/// <param name="Title">Heading of the tile.</param>
/// <param name="Groups">Its lists, in the order they are shown.</param>
/// <param name="Note">
/// Something true about the tile that its rows cannot say — most usefully that a source of rows has
/// not shipped yet, so an empty list is not mistaken for good news.
/// </param>
public sealed record DashboardTileContent(
    DashboardTile Tile,
    string Title,
    IReadOnlyList<DashboardGroup> Groups,
    string? Note = null);

/// <summary>The whole landing screen.</summary>
/// <param name="Tiles">Every tile, in the order spec section 14.9 lists them.</param>
/// <param name="GeneratedOn">
/// When the numbers were read. Shown, because a dashboard is a snapshot and one left open on a
/// second monitor overnight is a snapshot of yesterday.
/// </param>
public sealed record DashboardContent(
    IReadOnlyList<DashboardTileContent> Tiles,
    DateTimeOffset GeneratedOn);
