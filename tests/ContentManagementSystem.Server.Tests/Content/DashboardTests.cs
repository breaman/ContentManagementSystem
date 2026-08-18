using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Dashboard;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Dashboard;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// The backoffice landing screen (spec section 14.9, tasks P6-24 to P6-27).
/// </summary>
/// <remarks>
/// The tile worth most of the attention is "needs attention", because everything in it is a failure
/// nobody would go looking for: a review date that passed in silence, a live page pointing at a page
/// somebody deleted, and a picture nobody described.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class DashboardTests(SqlServerFixture fixture)
{
    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task MyWorkListsTheDraftsTheSignedInEditorHasMovedOnFromWhatIsPublished()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("dash-mine", cancellationToken, PageWorkbench.TextZone("hero"));
        var ahead = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var untouched = await _bench.AddPageAsync(template, "About", cancellationToken);

        await _bench.Resolve<IPublishingService>().PublishAsync(
            ahead.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        // An edit after the publish is what "unpublished changes" means: the draft has moved on from
        // the row the public site is reading.
        await SaveAsync(ahead.Summary.Id, template.Key, "Now with a second tier", cancellationToken);

        var tile = await TileAsync(DashboardTile.MyWork, cancellationToken);

        var changed = tile.Groups.Single(group => group.Key == "unpublished-changes");

        changed.Items.Should().ContainSingle().Which.Title.Should().Be("Pricing");
        changed.Items[0].Detail.Should().Contain("ahead of what the site is serving");

        tile.Groups.Single(group => group.Key == "never-published").Items
            .Should().Contain(item => item.Id == untouched.Summary.Id);

        tile.Note.Should().Contain(
            "Phase 7",
            "an empty 'assigned to you' would read as 'nothing is waiting on you' rather than as " +
            "'assignment has not shipped'");
    }

    [Test]
    public async Task TheScheduledTileHighlightsAPublishWhoseMomentCameAndWent()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("dash-sched", cancellationToken, PageWorkbench.TextZone("hero"));
        var late = await _bench.AddPageAsync(template, "Autumn campaign", cancellationToken);
        var soon = await _bench.AddPageAsync(template, "Winter campaign", cancellationToken);

        var now = _bench.Clock.GetUtcNow();

        await ScheduleAsync(late.Summary.Id, now.AddDays(-2), cancellationToken);
        await ScheduleAsync(soon.Summary.Id, now.AddDays(3), cancellationToken);

        var tile = await TileAsync(DashboardTile.Scheduled, cancellationToken);
        var publishing = tile.Groups.Single(group => group.Key == "publishing");

        publishing.Items.Should().HaveCount(2);

        var overdue = publishing.Items.Single(item => item.IsOverdue);

        overdue.Title.Should().Be("Autumn campaign");
        overdue.Detail.Should().Contain(
            "has not",
            "a schedule that silently did not fire looks exactly like an ordinary draft everywhere else");

        publishing.Items.Single(item => !item.IsOverdue).Title.Should().Be("Winter campaign");
    }

    [Test]
    public async Task NeedsAttentionFindsAnOverdueReviewAndAnUndescribedImage()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("dash-rot", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);

        await _bench.Resolve<IPageService>().PatchMetadataAsync(
            page.Summary.Id,
            // Relative to the fake clock, never a literal: the clock starts at the wall clock, so a
            // pinned date would mean something different every day this repository exists.
            new PatchPageMetadataRequest { ReviewByDate = new(Today.AddDays(-30)) },
            cancellationToken: cancellationToken);

        _bench.Context.MediaItems.Add(new MediaItem
        {
            FileName = "team-photo.jpg",
            OriginalFileName = "team-photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 1024,
            Sha256 = new byte[32],
            StorageKey = "2026/08/team-photo.jpg",
            MediaKind = MediaKind.Image,
            Title = "Team photograph",
        });

        _bench.Context.NotFoundLogs.Add(new NotFoundLog
        {
            Url = "/old-pricing",
            UrlHash = new byte[32],
            HitCount = 412,
            FirstSeenOn = _bench.Clock.GetUtcNow().AddDays(-30),
            LastSeenOn = _bench.Clock.GetUtcNow().AddHours(-2),
        });

        await _bench.Context.SaveChangesAsync(cancellationToken);
        _bench.Context.ChangeTracker.Clear();

        var tile = await TileAsync(DashboardTile.NeedsAttention, cancellationToken);

        tile.Groups.Single(group => group.Key == "overdue-review").Items
            .Should().ContainSingle()
            .Which.Should().Match<DashboardItem>(item => item.Title == "Pricing" && item.IsOverdue);

        tile.Groups.Single(group => group.Key == "missing-alt-text").Items
            .Should().ContainSingle()
            .Which.Title.Should().Be("Team photograph");

        tile.Groups.Single(group => group.Key == "not-found").Items
            .Should().ContainSingle()
            .Which.Detail.Should().Contain("412 request(s)");
    }

    [Test]
    public async Task ABrokenReferenceIsReportedOnlyOnceItIsLiveOnThePublicSite()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "dash-refs",
            cancellationToken,
            PageWorkbench.PageReferenceZone("related"));

        var source = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var target = await _bench.AddPageAsync(template, "Enterprise", cancellationToken);

        var payload =
            $$"""
            { "schemaVersion": 1, "templateKey": "{{template.Key}}", "templateRevision": 1,
              "zones": { "related": { "type": "pageReference", "value": {{target.Summary.Id}} } } }
            """;

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            source.Summary.Id,
            new SaveDraftRequest(payload, null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));
        _bench.Context.ChangeTracker.Clear();

        // A draft pointing at something that is gone is work in progress; the sweep is about links a
        // visitor is meeting right now, so nothing is reported until this is published.
        (await TileAsync(DashboardTile.NeedsAttention, cancellationToken))
            .Groups.Single(group => group.Key == "broken-references").Items
            .Should().BeEmpty();

        await _bench.Resolve<IPublishingService>().PublishAsync(
            source.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        await _bench.Resolve<IRecycleBinService>().DeleteAsync(target.Summary.Id, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var broken = (await TileAsync(DashboardTile.NeedsAttention, cancellationToken))
            .Groups.Single(group => group.Key == "broken-references");

        broken.Items.Should().ContainSingle()
            .Which.Should().Match<DashboardItem>(item =>
                item.Id == source.Summary.Id && item.IsOverdue);

        broken.Items[0].Detail.Should().Contain("no longer exists").And.Contain("related");
    }

    [Test]
    public async Task EveryTileTrimsToTheLimitWhileSayingHowManyThereReallyAre()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("dash-many", cancellationToken, PageWorkbench.TextZone("hero"));

        for (var i = 0; i < 8; i++)
        {
            await _bench.AddPageAsync(template, $"Page {i}", cancellationToken);
        }

        var dashboard = await _bench.Resolve<IDashboardService>().GetAsync(limit: 3, cancellationToken);

        dashboard.IsSuccess.Should().BeTrue(Because(dashboard));

        var never = dashboard.Value!.Tiles
            .Single(tile => tile.Tile == DashboardTile.MyWork)
            .Groups.Single(group => group.Key == "never-published");

        never.Items.Should().HaveCount(3);
        never.TotalCount.Should().Be(
            8,
            "a tile that showed the first three and implied three would hide the backlog it exists " +
            "to surface");
    }

    [Test]
    public async Task AViewerWhoMayNotReadContentGetsARefusalRatherThanAnEmptyDashboard()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var bench = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.MediaUpload),
            cancellationToken);

        var refused = await bench.Resolve<IDashboardService>().GetAsync(cancellationToken: cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Forbidden);
    }

    /// <summary>Today, as the clock under test sees it.</summary>
    private DateOnly Today => DateOnly.FromDateTime(_bench.Clock.GetUtcNow().UtcDateTime);

    /// <summary>Reads one tile at the length its own screen shows.</summary>
    private async Task<DashboardTileContent> TileAsync(DashboardTile tile, CancellationToken cancellationToken)
    {
        var result = await _bench.Resolve<IDashboardService>().GetTileAsync(
            tile,
            cancellationToken: cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));

        return result.Value!;
    }

    /// <summary>Writes a draft payload through the real service.</summary>
    private async Task SaveAsync(int pageId, string templateKey, string text, CancellationToken cancellationToken)
    {
        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            pageId,
            new SaveDraftRequest(
                $$"""
                { "schemaVersion": 1, "templateKey": "{{templateKey}}", "templateRevision": 1,
                  "zones": { "hero": { "type": "plainText", "value": "{{text}}" } } }
                """,
                null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));

        _bench.Context.ChangeTracker.Clear();
    }

    /// <summary>Puts a publish time on a page's draft, which is what the scheduled tile reads.</summary>
    private async Task ScheduleAsync(int pageId, DateTimeOffset publishOn, CancellationToken cancellationToken)
    {
        var draft = await _bench.Context.PageVersions
            .Where(version => version.PageId == pageId && version.Status == PageVersionStatus.Draft)
            .SingleAsync(cancellationToken);

        draft.PublishOn = publishOn;

        await _bench.Context.SaveChangesAsync(cancellationToken);

        _bench.Context.ChangeTracker.Clear();
    }

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
