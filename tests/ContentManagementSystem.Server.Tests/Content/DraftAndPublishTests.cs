using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// Saving drafts and publishing them (tasks P2-10 and P2-11).
/// </summary>
/// <remarks>
/// The requirement's central promise lives here, and the test for it is deliberately literal: a
/// published version's stored bytes are captured, the draft is edited several times, and the bytes
/// are compared again (acceptance criterion P2 #4).
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class DraftAndPublishTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private PageWorkbench _bench = null!;

    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Fact]
    public async Task SavingADraftMutatesItInPlaceAndCreatesNoVersionRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        var result = await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, 1, "hero", "Our best plans yet"), page.RowVersion),
            cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));
        result.Value!.Draft.VersionNumber.Should().Be(1, "a draft keeps its number for its whole life");

        var versions = await _bench.Context.PageVersions
            .AsNoTracking()
            .Where(version => version.PageId == page.Summary.Id)
            .ToListAsync(cancellationToken);

        // Acceptance criterion P2 #2, mechanically: an autosave every twenty seconds would otherwise
        // bury the history an editor reads under rows nobody decided anything about.
        versions.Should().ContainSingle();
        versions[0].ContentJson.Should().Contain("Our best plans yet");
    }

    [Fact]
    public async Task TwoConcurrentDraftSavesLeaveTheSecondWithAConflictAndBothPayloads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        var stale = page.RowVersion;

        var first = await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, 1, "hero", "Elena wrote this"), stale),
            cancellationToken);

        first.IsSuccess.Should().BeTrue(Because(first));

        // The second editor still holds the row version from before the first save, which is exactly
        // the state two people with the page open are in (acceptance criterion P2 #8).
        var second = await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, 1, "hero", "Marcus wrote this"), stale),
            cancellationToken);

        second.Outcome.Should().Be(CmsOutcome.Conflict);
        second.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.ConcurrentChange);

        // The stored copy comes back with the refusal, so the editor can offer keep-mine, take-
        // theirs, or a diff without a second round trip that would race the same way.
        second.Value.Should().NotBeNull();
        second.Value!.Draft.ContentJson.Should().Contain("Elena wrote this");
        second.Value.Draft.ContentJson.Should().NotContain("Marcus wrote this");
    }

    [Fact]
    public async Task ADraftCannotBeSavedAgainstAnotherTemplateOrAnInventedRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        await _bench.AddTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        // The payload declares which schema judges it, which makes both of these a privilege
        // boundary rather than data: a client free to name either can pick rules its content passes.
        var wrongTemplate = await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload("article", 1, "body", "text"), null),
            cancellationToken);

        wrongTemplate.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.TemplateMismatch);

        var wrongRevision = await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, 99, "hero", "text"), null),
            cancellationToken);

        wrongRevision.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.TemplateRevisionInvalid);
    }

    [Fact]
    public async Task ADraftSavesWithARequiredZoneEmptyAndAPublishDoesNot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "landing",
            cancellationToken,
            PageWorkbench.TextZone("hero", required: true));

        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);

        // Half-finished work always saves; an unfilled required zone blocks only the publish
        // (spec section 8.3, acceptance criterion P2 #11).
        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(ContentPayload.CreateEmpty(template.Key, 1).ToJson(), null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));

        var published = await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        published.Outcome.Should().Be(CmsOutcome.Invalid);
        published.Diagnostics.Diagnostics.Should().Contain(diagnostic => diagnostic.RelativePath!.Contains("hero"));
    }

    [Fact]
    public async Task PublishingSnapshotsTheDraftAndLeavesItByteForByteAloneAfterwards()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, 1, "hero", "The live text"), null),
            cancellationToken);

        var publish = await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            cancellationToken: cancellationToken);

        publish.IsSuccess.Should().BeTrue(Because(publish));
        publish.Value!.VersionNumber.Should().Be(2, "the publish snapshots the draft into a new row");
        publish.Value.ArchivedVersionNumber.Should().BeNull("nothing was live before");

        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.PageVersions
            .AsNoTracking()
            .SingleAsync(version => version.Id == publish.Value.VersionId, cancellationToken);

        var publishedBytes = stored.ContentJson;
        var publishedRowVersion = stored.RowVersion;

        // Three further edits to the draft, which is what an editor does the moment a page is live.
        for (var i = 0; i < 3; i++)
        {
            await drafts.SaveAsync(
                page.Summary.Id,
                new SaveDraftRequest(Payload(template.Key, 1, "hero", $"Draft revision {i}"), null),
                cancellationToken);
        }

        _bench.Context.ChangeTracker.Clear();

        var after = await _bench.Context.PageVersions
            .AsNoTracking()
            .SingleAsync(version => version.Id == publish.Value.VersionId, cancellationToken);

        // The requirement's central promise (acceptance criterion P2 #4). Byte-for-byte, and the
        // row version too — a row whose concurrency token moved was written to, whatever it now says.
        after.ContentJson.Should().Be(publishedBytes);
        after.RowVersion.Should().Equal(publishedRowVersion);
        after.Status.Should().Be(PageVersionStatus.Published);

        var draft = await _bench.DraftOfAsync(page.Summary.Id, cancellationToken);
        draft.ContentJson.Should().Contain("Draft revision 2");
    }

    [Fact]
    public async Task PublishingAgainArchivesThePreviousVersionAndRepointsThePage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var publishing = _bench.Resolve<IPublishingService>();
        var drafts = _bench.Resolve<IDraftService>();

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, 1, "hero", "First"), null),
            cancellationToken);
        var first = await publishing.PublishAsync(page.Summary.Id, cancellationToken: cancellationToken);

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, 1, "hero", "Second"), null),
            cancellationToken);
        var second = await publishing.PublishAsync(page.Summary.Id, cancellationToken: cancellationToken);

        second.IsSuccess.Should().BeTrue(Because(second));
        second.Value!.ArchivedVersionNumber.Should().Be(first.Value!.VersionNumber);

        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.Pages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == page.Summary.Id, cancellationToken);

        stored.PublishedVersionId.Should().Be(second.Value.VersionId);

        var superseded = await _bench.Context.PageVersions
            .AsNoTracking()
            .SingleAsync(version => version.Id == first.Value.VersionId, cancellationToken);

        superseded.Status.Should().Be(PageVersionStatus.Archived);
    }

    [Fact]
    public async Task PublishingProjectsTheReferenceRowsOfTheVersionThatIsNowLive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "landing",
            cancellationToken,
            PageWorkbench.PageReferenceZone("related"));

        var target = await _bench.AddPageAsync(template, "Target", cancellationToken);
        var source = await _bench.AddPageAsync(template, "Source", cancellationToken);

        await _bench.Resolve<IDraftService>().SaveAsync(
            source.Summary.Id,
            new SaveDraftRequest(
                $$"""
                { "schemaVersion": 1, "templateKey": "{{template.Key}}", "templateRevision": 1,
                  "zones": { "related": { "type": "pageReference", "value": {{target.Summary.Id}} } } }
                """,
                null),
            cancellationToken);

        var publish = await _bench.Resolve<IPublishingService>().PublishAsync(
            source.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        publish.IsSuccess.Should().BeTrue(Because(publish));
        publish.Value!.ReferenceCount.Should().Be(1);

        var row = await _bench.Context.ContentReferences
            .AsNoTracking()
            .SingleAsync(candidate => candidate.SourceVersionId == publish.Value.VersionId, cancellationToken);

        row.TargetId.Should().Be(target.Summary.Id);

        // The coordinates the editor addresses a reference by, resolved out of the payload path.
        row.ZoneKey.Should().Be("related");
    }

    [Fact]
    public async Task LinkingToAPageThatIsGoneBlocksAPublishAndLinkingToAnUnpublishedOneOnlyWarns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "landing",
            cancellationToken,
            PageWorkbench.PageReferenceZone("related"));

        var target = await _bench.AddPageAsync(template, "Target", cancellationToken);
        var source = await _bench.AddPageAsync(template, "Source", cancellationToken);
        var publishing = _bench.Resolve<IPublishingService>();

        await _bench.Resolve<IDraftService>().SaveAsync(
            source.Summary.Id,
            new SaveDraftRequest(
                $$"""
                { "schemaVersion": 1, "templateKey": "{{template.Key}}", "templateRevision": 1,
                  "zones": { "related": { "type": "pageReference", "value": {{target.Summary.Id}} } } }
                """,
                null),
            cancellationToken);

        // Publishing a section top-down is ordinary work, so a link to a page that is merely not
        // live yet is a warning the publisher can acknowledge.
        var check = await publishing.ValidateAsync(source.Summary.Id, cancellationToken);

        check.Value!.CanPublish.Should().BeTrue();
        check.Value.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.NothingPublished);

        await _bench.Resolve<IRecycleBinService>().DeleteAsync(target.Summary.Id, cancellationToken);
        _bench.Context.ChangeTracker.Clear();

        // A link to a page that is gone is different: publishing it puts a dead link on the site.
        var afterDelete = await publishing.ValidateAsync(source.Summary.Id, cancellationToken);

        afterDelete.Value!.CanPublish.Should().BeFalse();
        afterDelete.Value.Errors.Should().Contain(error => error.Code == PageCodes.NotFound);
    }

    [Fact]
    public async Task DiscardingADraftResetsItToWhatIsPublishedAndUnpublishingRetiresTheVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        var nothingToDiscard = await drafts.DiscardAsync(page.Summary.Id, cancellationToken);
        nothingToDiscard.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.NothingPublished);

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, 1, "hero", "The live text"), null),
            cancellationToken);
        await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            cancellationToken: cancellationToken);

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, 1, "hero", "An experiment"), null),
            cancellationToken);

        var discarded = await drafts.DiscardAsync(page.Summary.Id, cancellationToken);

        discarded.IsSuccess.Should().BeTrue(Because(discarded));
        discarded.Value!.ContentJson.Should().Contain("The live text");
        discarded.Value.VersionNumber.Should().Be(1, "the draft keeps its own row");

        var unpublished = await _bench.Resolve<IPublishingService>().UnpublishAsync(
            page.Summary.Id,
            cancellationToken);

        unpublished.IsSuccess.Should().BeTrue(Because(unpublished));

        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.Pages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == page.Summary.Id, cancellationToken);

        stored.PublishedVersionId.Should().BeNull();
        stored.DraftVersionId.Should().NotBeNull("unpublishing does not touch the draft");
    }

    [Fact]
    public async Task ACheckpointFreezesACopyOfTheDraftWithoutPublishingAnything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var drafts = _bench.Resolve<IDraftService>();

        await drafts.SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(template.Key, 1, "hero", "Before the rewrite"), null),
            cancellationToken);

        var result = await drafts.CheckpointAsync(
            page.Summary.Id,
            "before the big rewrite",
            cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));

        _bench.Context.ChangeTracker.Clear();

        var versions = await _bench.Context.PageVersions
            .AsNoTracking()
            .Where(version => version.PageId == page.Summary.Id)
            .OrderBy(version => version.VersionNumber)
            .ToListAsync(cancellationToken);

        versions.Should().HaveCount(2);
        versions[1].Status.Should().Be(PageVersionStatus.Archived);
        versions[1].Label.Should().Be("before the big rewrite");
        versions[1].ContentJson.Should().Contain("Before the rewrite");

        var page2 = await _bench.Context.Pages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == page.Summary.Id, cancellationToken);

        page2.PublishedVersionId.Should().BeNull("a checkpoint is a bookmark, not a publish");
        page2.DraftVersionId.Should().Be(versions[0].Id);
    }

    [Fact]
    public async Task PublishingNeedsItsOwnPermission()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var editor = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.ContentRead, CmsPermissions.ContentEdit),
            cancellationToken);

        var template = await editor.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await editor.AddPageAsync(template, "Pricing", cancellationToken);

        // An Author edits and cannot publish; the endpoint policy is the door and this is the lock.
        (await editor.Resolve<IPublishingService>().PublishAsync(
                page.Summary.Id,
                cancellationToken: cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);
    }

    /// <summary>Builds a payload with one text zone filled in.</summary>
    private static string Payload(string templateKey, int revision, string zoneKey, string text) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{templateKey}}", "templateRevision": {{revision}},
          "zones": { "{{zoneKey}}": { "type": "plainText", "value": "{{text}}" } } }
        """;

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
