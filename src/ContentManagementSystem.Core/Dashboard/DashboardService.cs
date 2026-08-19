using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Dashboard;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Dashboard;

/// <summary>
/// The backoffice landing screen's four tiles (spec section 14.9, tasks P6-24 to P6-27).
/// </summary>
/// <remarks>
/// <strong>This is where the housekeeping reports become something anybody acts on.</strong> Every
/// number here is already knowable from a query somebody could have written; what the dashboard adds
/// is that nobody has to think of writing it. A nightly job that produces a report nobody opens is
/// wasted work, and content rot is the failure mode this defends against.
/// <para>
/// Read-only and per-request. Nothing is cached: the tiles are a handful of indexed counts, and a
/// cached dashboard is one that tells an editor their overdue review is still overdue after they
/// have just done it.
/// </para>
/// </remarks>
public interface IDashboardService
{
    /// <summary>
    /// Reads every tile, trimmed for the landing screen.
    /// </summary>
    /// <param name="limit">How many rows each list shows.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The tiles, or a forbidden result.</returns>
    Task<CmsResult<DashboardContent>> GetAsync(int limit = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one tile at length, for the list its "show all" link opens.
    /// </summary>
    /// <param name="tile">Which tile.</param>
    /// <param name="limit">How many rows each of its lists shows.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The tile, or a forbidden result.</returns>
    /// <remarks>
    /// The same queries as <see cref="GetAsync"/> with a larger limit, which is what makes the tile
    /// and the list it links to agree (acceptance criterion P6 #8). A separate implementation would
    /// be a second definition of "needs attention", and the first time they drifted the tile would be
    /// advertising a list that does not contain what it promised.
    /// </remarks>
    Task<CmsResult<DashboardTileContent>> GetTileAsync(
        DashboardTile tile,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IDashboardService" />
/// <param name="context">The application database context.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="acl">Where in the tree the caller may read (task P7-06, spec section 21.2).</param>
/// <param name="users">Who the caller is, which is what makes "my work" theirs.</param>
/// <param name="clock">Source of the current time.</param>
public sealed class DashboardService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    IAclService acl,
    IUserService users,
    TimeProvider clock) : IDashboardService
{
    /// <summary>How far ahead the scheduled tile looks.</summary>
    /// <remarks>Spec section 14.9's window. A week is what an editor plans in.</remarks>
    public const int ScheduleWindowDays = 7;

    /// <summary>The largest number of rows one list will return, whatever was asked for.</summary>
    private const int MaxLimit = 200;

    /// <inheritdoc />
    public async Task<CmsResult<DashboardContent>> GetAsync(
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<DashboardContent>.Forbidden(
                "Reading content is not permitted.",
                PageCodes.Forbidden);
        }

        var tiles = new List<DashboardTileContent>();

        foreach (var tile in Enum.GetValues<DashboardTile>())
        {
            tiles.Add(await BuildAsync(tile, Clamp(limit), cancellationToken));
        }

        return CmsResult<DashboardContent>.Success(new DashboardContent(tiles, clock.GetUtcNow()));
    }

    /// <inheritdoc />
    public async Task<CmsResult<DashboardTileContent>> GetTileAsync(
        DashboardTile tile,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<DashboardTileContent>.Forbidden(
                "Reading content is not permitted.",
                PageCodes.Forbidden);
        }

        return CmsResult<DashboardTileContent>.Success(
            await BuildAsync(tile, Clamp(limit), cancellationToken));
    }

    /// <summary>Builds one tile, with anything the caller may not read taken back out of it.</summary>
    private async Task<DashboardTileContent> BuildAsync(
        DashboardTile tile,
        int limit,
        CancellationToken cancellationToken)
    {
        var content = await (tile switch
        {
            DashboardTile.MyWork => MyWorkAsync(limit, cancellationToken),
            DashboardTile.Scheduled => ScheduledAsync(limit, cancellationToken),
            DashboardTile.NeedsAttention => NeedsAttentionAsync(limit, cancellationToken),
            _ => RecentActivityAsync(limit, cancellationToken),
        });

        return await RedactAsync(content, cancellationToken);
    }

    /// <summary>
    /// Removes the rows naming pages the caller may not read (task P7-06, criterion P7 #6).
    /// </summary>
    /// <param name="content">The tile as its query built it.</param>
    /// <param name="cancellationToken">Token observed while resolving page positions.</param>
    /// <returns>The tile with hidden pages gone and the counts adjusted to match.</returns>
    /// <remarks>
    /// Applied once, after every tile, rather than woven into the eight queries behind them. A
    /// dashboard is the one screen that reads across the whole site, so it is the most likely place
    /// for a hidden branch to reappear as a title in a list — and a filter that has to be remembered
    /// in eight places is a filter that will be forgotten in one.
    /// <para>
    /// <c>TotalCount</c> is reduced by what was removed rather than left alone, because a group
    /// saying "showing 5 of 40" while holding three rows would be the hidden branch leaking as a
    /// number instead of as a title.
    /// </para>
    /// </remarks>
    private async Task<DashboardTileContent> RedactAsync(
        DashboardTileContent content,
        CancellationToken cancellationToken)
    {
        var readable = await acl.GetFilterAsync(CmsPermissions.ContentRead, cancellationToken);

        if (readable.IsUnrestricted) return content;

        var pageIds = content.Groups
            .SelectMany(group => group.Items)
            .Where(item => item.Kind == DashboardItemKind.Page && item.Id is not null)
            .Select(item => item.Id!.Value)
            .Distinct()
            .ToList();

        if (pageIds.Count == 0) return content;

        var paths = await context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => pageIds.Contains(page.Id))
            .Select(page => new { page.Id, page.Path })
            .ToDictionaryAsync(row => row.Id, row => row.Path, cancellationToken);

        var groups = new List<DashboardGroup>(content.Groups.Count);

        foreach (var group in content.Groups)
        {
            var kept = group.Items
                .Where(item => item.Kind != DashboardItemKind.Page
                    || item.Id is not { } id
                    || !paths.TryGetValue(id, out var path)
                    || readable.Allows(id, path))
                .ToList();

            groups.Add(kept.Count == group.Items.Count
                ? group
                : group with
                {
                    Items = kept,
                    TotalCount = Math.Max(0, group.TotalCount - (group.Items.Count - kept.Count)),
                });
        }

        return content with { Groups = groups };
    }

    /// <summary>
    /// What the signed-in editor has in progress (task P6-24).
    /// </summary>
    /// <remarks>
    /// "Mine" is deliberately two things: pages I own, and pages I was the last to touch. Ownership
    /// alone would leave a new editor's own unfinished draft off their own dashboard, which is the
    /// one row they were looking for.
    /// <para>
    /// The review-assignment list spec section 14.9 asks for is <em>not</em> here, and the tile says
    /// so rather than showing an empty list: assignment arrives with the workflow in Phase 7, and an
    /// empty "assigned to you" reads as "nothing is waiting on you" rather than as "this has not
    /// shipped". What can be reported honestly today is what the version statuses already record —
    /// content sitting in review, and content sent back.
    /// </para>
    /// </remarks>
    private async Task<DashboardTileContent> MyWorkAsync(int limit, CancellationToken cancellationToken)
    {
        var me = users.UserId;

        var mine = await Drafts()
            .Where(version =>
                version.Page.PublishedVersionId != null &&
                version.Page.PublishedVersionId != version.Id &&
                (version.Page.OwnerUserId == me || version.ModifiedBy == me))
            .OrderByDescending(version => version.ModifiedOn)
            .ToListAsync(cancellationToken);

        var never = await Drafts()
            .Where(version =>
                version.Page.PublishedVersionId == null &&
                (version.Page.OwnerUserId == me || version.ModifiedBy == me))
            .OrderByDescending(version => version.ModifiedOn)
            .ToListAsync(cancellationToken);

        var inReview = await context.PageVersions
            .AsNoTracking()
            .Include(version => version.Page)
            .Where(version => version.Status == PageVersionStatus.InReview)
            .OrderByDescending(version => version.ModifiedOn)
            .ToListAsync(cancellationToken);

        var rejected = await context.PageVersions
            .AsNoTracking()
            .Include(version => version.Page)
            .Where(version => version.Status == PageVersionStatus.Rejected)
            .OrderByDescending(version => version.ModifiedOn)
            .ToListAsync(cancellationToken);

        return new DashboardTileContent(
            DashboardTile.MyWork,
            "My work",
            [
                Group(
                    "unpublished-changes",
                    "Drafts with unpublished changes",
                    mine.Select(version => Row(
                        version,
                        $"Draft v{version.VersionNumber} is ahead of what the site is serving")),
                    limit,
                    "Nothing you are working on differs from what is published."),
                Group(
                    "never-published",
                    "Never published",
                    never.Select(version => Row(version, "This page has never been on the public site")),
                    limit,
                    "Everything you own has been published at least once."),
                Group(
                    "in-review",
                    "Waiting for review",
                    inReview.Select(version => Row(version, "Submitted and waiting for a decision")),
                    limit,
                    "Nothing is waiting for a review decision."),
                Group(
                    "rejected",
                    "Sent back",
                    rejected.Select(version => Row(version, "Rejected — its comments are on the version", overdue: true)),
                    limit,
                    "Nothing has been sent back."),
            ],
            "Review assignments arrive with the approval workflow in Phase 7. Until then these two " +
            "lists are everything in review or sent back, not only what is assigned to you.");
    }

    /// <summary>
    /// What publishes or expires in the next week (task P6-25).
    /// </summary>
    /// <remarks>
    /// The overdue rows are the reason the tile exists. A scheduled publish whose moment has passed
    /// while the page is still unpublished is a failed job — the background publisher did not run, or
    /// ran and refused — and it is invisible everywhere else in the backoffice, because the page
    /// looks exactly like an ordinary draft.
    /// </remarks>
    private async Task<DashboardTileContent> ScheduledAsync(int limit, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var horizon = now.AddDays(ScheduleWindowDays);

        var publishing = await context.PageVersions
            .AsNoTracking()
            .Include(version => version.Page)
            .Where(version => version.PublishOn != null && version.PublishOn <= horizon)
            .Where(version => version.Page.PublishedVersionId != version.Id)
            .OrderBy(version => version.PublishOn)
            .ToListAsync(cancellationToken);

        var expiring = await context.PageVersions
            .AsNoTracking()
            .Include(version => version.Page)
            .Where(version => version.UnpublishOn != null &&
                version.UnpublishOn >= now &&
                version.UnpublishOn <= horizon)
            .Where(version => version.Page.PublishedVersionId == version.Id)
            .OrderBy(version => version.UnpublishOn)
            .ToListAsync(cancellationToken);

        return new DashboardTileContent(
            DashboardTile.Scheduled,
            "Scheduled",
            [
                Group(
                    "publishing",
                    $"Publishing in the next {ScheduleWindowDays} days",
                    publishing.Select(version => version.PublishOn < now
                        ? Row(
                            version,
                            $"Was due to publish {Ago(version.PublishOn!.Value, now)} and has not",
                            overdue: true,
                            when: version.PublishOn)
                        : Row(
                            version,
                            $"Publishes {In(version.PublishOn!.Value, now)}",
                            when: version.PublishOn)),
                    limit,
                    "Nothing is waiting to go live."),
                Group(
                    "expiring",
                    $"Expiring in the next {ScheduleWindowDays} days",
                    expiring.Select(version => Row(
                        version,
                        $"Stops being served {In(version.UnpublishOn!.Value, now)}",
                        when: version.UnpublishOn)),
                    limit,
                    "Nothing live is due to expire."),
            ]);
    }

    /// <summary>
    /// What has quietly gone wrong (task P6-26).
    /// </summary>
    /// <remarks>
    /// Four lists, and each one is a thing nobody would think to look for. A review date passes in
    /// silence; a reference breaks when somebody deletes the far end; a picture without alternative
    /// text is invisible to the reader who needs it and to everybody who does not; and the top 404s
    /// are the highest-value artefact of a site migration, sorted by the traffic still arriving at
    /// them (spec section 10.6).
    /// </remarks>
    private async Task<DashboardTileContent> NeedsAttentionAsync(int limit, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var overdue = await context.Pages
            .AsNoTracking()
            .Include(page => page.DraftVersion)
            .Where(page => page.ReviewByDate != null && page.ReviewByDate < today)
            .OrderBy(page => page.ReviewByDate)
            .ToListAsync(cancellationToken);

        var broken = await BrokenReferencesAsync(cancellationToken);

        var undescribed = await context.MediaItems
            .AsNoTracking()
            .Where(item => item.MediaKind == MediaKind.Image &&
                !item.IsDecorative &&
                (item.AltText == null || item.AltText == ""))
            .OrderByDescending(item => item.CreatedOn)
            .ToListAsync(cancellationToken);

        var notFound = await context.NotFoundLogs
            .AsNoTracking()
            .OrderByDescending(entry => entry.HitCount)
            .Take(MaxLimit)
            .ToListAsync(cancellationToken);

        return new DashboardTileContent(
            DashboardTile.NeedsAttention,
            "Needs attention",
            [
                Group(
                    "overdue-review",
                    "Past its review date",
                    overdue.Select(page => new DashboardItem(
                        DashboardItemKind.Page,
                        page.Id,
                        page.DraftVersion?.Title ?? page.Slug,
                        $"Review was due {page.ReviewByDate:d MMMM yyyy}",
                        IsOverdue: true)),
                    limit,
                    "No content is past its review date."),
                Group(
                    "broken-references",
                    "Broken references",
                    broken,
                    limit,
                    "Every reference resolves."),
                Group(
                    "missing-alt-text",
                    "Images with no alternative text",
                    undescribed.Select(item => new DashboardItem(
                        DashboardItemKind.Media,
                        item.Id,
                        item.Title ?? item.FileName,
                        "No alternative text, and not marked decorative",
                        item.CreatedOn,
                        IsOverdue: true)),
                    limit,
                    "Every image is described or marked decorative."),
                Group(
                    "not-found",
                    "Most-requested URLs that do not exist",
                    notFound.Select(entry => new DashboardItem(
                        DashboardItemKind.Url,
                        null,
                        entry.Url,
                        $"{entry.HitCount} request(s), most recently {Ago(entry.LastSeenOn, clock.GetUtcNow())}",
                        entry.LastSeenOn)),
                    limit,
                    "Nothing has asked for a URL this site does not serve."),
            ]);
    }

    /// <summary>
    /// What has been done to content lately (task P6-27).
    /// </summary>
    /// <remarks>
    /// Filtered to the content tables rather than showing the whole audit log: this is an editorial
    /// feed, and an identity table's rows are neither interesting here nor safe to show to everyone
    /// who may read content. Permission-filtering beyond that is the tile's whole entry condition —
    /// <c>Content.Read</c> — since v1 has no per-page permissions to filter by; those arrive with
    /// Phase 7, and this query narrows with them rather than being rewritten.
    /// </remarks>
    private async Task<DashboardTileContent> RecentActivityAsync(int limit, CancellationToken cancellationToken)
    {
        string[] tables = [nameof(Page), nameof(PageVersion), nameof(MediaItem), nameof(ReusableContent)];

        var entries = await context.AuditLogs
            .AsNoTracking()
            .Where(entry => tables.Contains(entry.TableName))
            .OrderByDescending(entry => entry.DateTime)
            .Take(MaxLimit)
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();

        return new DashboardTileContent(
            DashboardTile.RecentActivity,
            "Recent activity",
            [
                Group(
                    "recent-activity",
                    "Latest changes",
                    entries.Select(entry => new DashboardItem(
                        DashboardItemKind.Activity,
                        null,
                        $"{entry.Type} {Readable(entry.TableName)}",
                        $"by user {entry.UserId}, {Ago(entry.DateTime, now)}",
                        entry.DateTime)),
                    limit,
                    "Nothing has been changed yet."),
            ],
            "Who did it is shown by identity until the user directory arrives with Phase 7.");
    }

    /// <summary>
    /// Finds published content whose references point at something that is gone.
    /// </summary>
    /// <remarks>
    /// Only the <em>published</em> versions, deliberately. A draft pointing at a page somebody has
    /// not created yet is work in progress; a live page pointing at a deleted one is a broken link a
    /// visitor is meeting right now, and mixing the two would bury the second in the first.
    /// <para>
    /// Reusable targets are checked alongside pages and media because all three ends can be deleted
    /// independently. The rows over-report by design (spec section 7.3), so this list is occasionally
    /// pessimistic — which is the direction to be wrong in when the alternative is silence.
    /// </para>
    /// </remarks>
    private async Task<List<DashboardItem>> BrokenReferencesAsync(CancellationToken cancellationToken)
    {
        var published = await context.Pages
            .AsNoTracking()
            .Where(page => page.PublishedVersionId != null)
            .Select(page => new { page.Id, VersionId = page.PublishedVersionId!.Value, page.DraftVersion!.Title })
            .ToListAsync(cancellationToken);

        if (published.Count == 0) return [];

        var versionIds = published.Select(page => page.VersionId).ToList();

        var references = await context.ContentReferences
            .AsNoTracking()
            .Where(row => row.SourceType == ContentSourceType.PageVersion &&
                versionIds.Contains(row.SourceVersionId))
            .Select(row => new Edge(row.SourceVersionId, row.TargetType, row.TargetId, row.ZoneKey))
            .ToListAsync(cancellationToken);

        if (references.Count == 0) return [];

        // Three id sets, one query each, rather than a join per reference. The global query filters
        // do the work that matters here: a soft-deleted target simply is not in the answer, which is
        // exactly what "points at something that is gone" means to a visitor.
        var livePages = await LiveIdsAsync(
            context.Pages,
            Targets(references, ContentReferenceTargetType.Page),
            cancellationToken);

        var liveMedia = await LiveIdsAsync(
            context.MediaItems,
            Targets(references, ContentReferenceTargetType.Media),
            cancellationToken);

        var liveReusable = await LiveIdsAsync(
            context.ReusableContents,
            Targets(references, ContentReferenceTargetType.ReusableContent),
            cancellationToken);

        var broken = new List<DashboardItem>();
        var seen = new HashSet<(int Source, ContentReferenceTargetType Type, int Target)>();

        foreach (var reference in references)
        {
            var alive = reference.TargetType switch
            {
                ContentReferenceTargetType.Page => livePages.Contains(reference.TargetId),
                ContentReferenceTargetType.Media => liveMedia.Contains(reference.TargetId),
                _ => liveReusable.Contains(reference.TargetId),
            };

            if (alive) continue;

            if (!seen.Add((reference.SourceVersionId, reference.TargetType, reference.TargetId))) continue;

            var source = published.First(page => page.VersionId == reference.SourceVersionId);

            broken.Add(new DashboardItem(
                DashboardItemKind.Page,
                source.Id,
                source.Title,
                $"Its published content points at {Readable(reference.TargetType)} " +
                $"{reference.TargetId}, which no longer exists" +
                (reference.ZoneKey is null ? string.Empty : $" (zone “{reference.ZoneKey}”)"),
                IsOverdue: true));
        }

        return broken;
    }

    /// <summary>The distinct ids one target kind is referenced by.</summary>
    private static List<int> Targets(IEnumerable<Edge> references, ContentReferenceTargetType targetType) =>
        [.. references.Where(edge => edge.TargetType == targetType)
            .Select(edge => edge.TargetId)
            .Distinct()];

    /// <summary>Which of a set of ids still exist, as the global query filters see them.</summary>
    private static async Task<HashSet<int>> LiveIdsAsync<T>(
        IQueryable<T> set,
        List<int> ids,
        CancellationToken cancellationToken) where T : EntityBase
    {
        if (ids.Count == 0) return [];

        return [.. await set.AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .Select(row => row.Id)
            .ToListAsync(cancellationToken)];
    }

    /// <summary>One reference out of a published page, as the broken-reference sweep reads it.</summary>
    /// <param name="SourceVersionId">The published version holding it.</param>
    /// <param name="TargetType">Kind of entity referenced.</param>
    /// <param name="TargetId">Identity of the referenced entity.</param>
    /// <param name="ZoneKey">Zone it sits in, so the row can say where to look.</param>
    private sealed record Edge(
        int SourceVersionId,
        ContentReferenceTargetType TargetType,
        int TargetId,
        string? ZoneKey);

    /// <summary>The draft versions of every live page, as a query the callers narrow.</summary>
    private IQueryable<PageVersion> Drafts() =>
        context.PageVersions
            .AsNoTracking()
            .Include(version => version.Page)
            .Where(version => version.Status == PageVersionStatus.Draft);

    /// <summary>Projects a page version onto a dashboard row.</summary>
    private static DashboardItem Row(
        PageVersion version,
        string detail,
        bool overdue = false,
        DateTimeOffset? when = null) =>
        new(
            DashboardItemKind.Page,
            version.PageId,
            version.Title,
            detail,
            when ?? version.ModifiedOn,
            overdue);

    /// <summary>Trims a list to the limit while reporting how long it really is.</summary>
    private static DashboardGroup Group(
        string key,
        string title,
        IEnumerable<DashboardItem> items,
        int limit,
        string emptyMessage)
    {
        var all = items.ToList();

        return new DashboardGroup(key, title, [.. all.Take(limit)], all.Count, emptyMessage);
    }

    /// <summary>Keeps a caller-supplied limit inside something a landing screen can render.</summary>
    private static int Clamp(int limit) => Math.Clamp(limit, 1, MaxLimit);

    /// <summary>"3 days ago", for a moment in the past.</summary>
    private static string Ago(DateTimeOffset moment, DateTimeOffset now) => (now - moment) switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } elapsed => $"{(int)elapsed.TotalMinutes} minute(s) ago",
        { TotalDays: < 1 } elapsed => $"{(int)elapsed.TotalHours} hour(s) ago",
        var elapsed => $"{(int)elapsed.TotalDays} day(s) ago",
    };

    /// <summary>"in 3 days", for a moment in the future.</summary>
    private static string In(DateTimeOffset moment, DateTimeOffset now) => (moment - now) switch
    {
        { TotalMinutes: < 1 } => "in under a minute",
        { TotalHours: < 1 } remaining => $"in {(int)remaining.TotalMinutes} minute(s)",
        { TotalDays: < 1 } remaining => $"in {(int)remaining.TotalHours} hour(s)",
        var remaining => $"in {(int)remaining.TotalDays} day(s)",
    };

    /// <summary>Names a table the way an editor would say it.</summary>
    private static string Readable(string tableName) => tableName switch
    {
        nameof(Page) => "page",
        nameof(PageVersion) => "page version",
        nameof(MediaItem) => "media item",
        nameof(ReusableContent) => "reusable item",
        _ => tableName,
    };

    /// <summary>Names a reference target the way an editor would say it.</summary>
    private static string Readable(ContentReferenceTargetType targetType) => targetType switch
    {
        ContentReferenceTargetType.Page => "page",
        ContentReferenceTargetType.Media => "media item",
        _ => "reusable item",
    };
}
