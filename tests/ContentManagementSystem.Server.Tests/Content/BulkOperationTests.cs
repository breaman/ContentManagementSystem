using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// One operation over many pages (task P6-29, spec section 14.11).
/// </summary>
/// <remarks>
/// Two promises get most of the attention, because they are the two a naive implementation breaks.
/// A selection is resolved <em>server-side</em> before anybody confirms anything, so the number in
/// the dialog is the number that happens; and a failure is one item's, not the batch's, so thirty-
/// nine pages publishing is not undone by the fortieth having an empty required zone.
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class BulkOperationTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private PageWorkbench _bench = null!;

    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Fact]
    public async Task TheImpactResolvesABranchIntoEveryPageBeneathItAndPublishesNone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("bulk-branch", cancellationToken, PageWorkbench.TextZone("hero"));
        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var child = await _bench.AddPageAsync(template, "Widgets", cancellationToken, section.Summary.Id);
        await _bench.AddPageAsync(template, "Specifications", cancellationToken, child.Summary.Id);
        await _bench.AddPageAsync(template, "About", cancellationToken);

        var impact = await _bench.Resolve<IBulkOperationService>().DescribeAsync(
            new BulkOperationRequest(
                BulkOperation.Publish,
                new BulkSelection([section.Summary.Id], IncludeDescendants: true)),
            cancellationToken);

        impact.IsSuccess.Should().BeTrue(Because(impact));
        impact.Value!.SelectedCount.Should().Be(1);
        impact.Value.ItemCount.Should().Be(3, "the bystander at the root is not beneath the section");

        // The parent first, then down. A child published before its parent is a live page under an
        // unpublished one, which is a URL the site's own navigation cannot reach.
        impact.Value.Items.Select(item => item.Title).Should().Equal("Products", "Widgets", "Specifications");
        impact.Value.Items[0].WasSelected.Should().BeTrue();
        impact.Value.Items[1].WasSelected.Should().BeFalse("it was swept in behind the page that was");
        impact.Value.RunsInBackground.Should().BeFalse("three pages is well under the threshold");

        _bench.Context.ChangeTracker.Clear();

        (await _bench.Context.Pages.CountAsync(page => page.PublishedVersionId != null, cancellationToken))
            .Should().Be(0, "describing a batch must not run any of it");
    }

    [Fact]
    public async Task APartialFailureLeavesTheSuccessfulPagesPublishedAndReportsTheRestIndividually()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync(
            "bulk-partial",
            cancellationToken,
            PageWorkbench.TextZone("hero", required: true));

        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var filled = await _bench.AddPageAsync(template, "Widgets", cancellationToken, section.Summary.Id);
        var empty = await _bench.AddPageAsync(template, "Gadgets", cancellationToken, section.Summary.Id);

        await FillAsync(section.Summary.Id, template.Key, cancellationToken);
        await FillAsync(filled.Summary.Id, template.Key, cancellationToken);

        // "Gadgets" is left with its required zone unfilled, which is the ordinary way one page in a
        // branch cannot be published while the rest can.
        var job = await _bench.Resolve<IBulkOperationService>().StartAsync(
            new BulkOperationRequest(
                BulkOperation.Publish,
                new BulkSelection([section.Summary.Id], IncludeDescendants: true)),
            cancellationToken);

        job.IsSuccess.Should().BeTrue(Because(job));
        job.Value!.IsFinished.Should().BeTrue("a batch this small runs inside the request");
        job.Value.State.Should().Be(
            BulkJobState.Completed,
            "every item was attempted, which is a completed job with failures rather than a failed one");
        job.Value.Succeeded.Should().Be(2);
        job.Value.Failed.Should().Be(1);

        var failure = job.Value.Results.Single(result => !result.Succeeded);

        failure.Title.Should().Be("Gadgets", "a batch report names pages rather than counting them");
        failure.Diagnostics.Should().NotBeEmpty("'1 item failed' is not something an editor can act on");

        _bench.Context.ChangeTracker.Clear();

        var published = await _bench.Context.Pages
            .AsNoTracking()
            .Where(page => page.PublishedVersionId != null)
            .Select(page => page.Id)
            .ToListAsync(cancellationToken);

        published.Should().BeEquivalentTo(
            [section.Summary.Id, filled.Summary.Id],
            "the successful items stay applied — that is what makes this a batch and not a transaction");
        published.Should().NotContain(empty.Summary.Id);
    }

    [Fact]
    public async Task ADeleteShowsTheWholeSubtreeAndQueuesOnlyTheSelectedRoots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("bulk-delete", cancellationToken, PageWorkbench.TextZone("hero"));
        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        await _bench.AddPageAsync(template, "Widgets", cancellationToken, section.Summary.Id);
        await _bench.AddPageAsync(template, "Gadgets", cancellationToken, section.Summary.Id);

        var request = new BulkOperationRequest(BulkOperation.Delete, new BulkSelection([section.Summary.Id]));
        var bulk = _bench.Resolve<IBulkOperationService>();

        var impact = await bulk.DescribeAsync(request, cancellationToken);

        impact.IsSuccess.Should().BeTrue(Because(impact));
        impact.Value!.ItemCount.Should().Be(
            3,
            "a delete always takes the subtree, so a count of one would be a confirmation that lied");

        var job = await bulk.StartAsync(request, cancellationToken);

        job.IsSuccess.Should().BeTrue(Because(job));
        job.Value!.Total.Should().Be(
            1,
            "the recycle bin is subtree-aware already; queueing the descendants would have every one " +
            "of them report 'no such page' for a batch that worked");
        job.Value.Failed.Should().Be(0);

        _bench.Context.ChangeTracker.Clear();

        (await _bench.Context.Pages.CountAsync(cancellationToken)).Should().Be(0, "all three are in the bin");
    }

    [Fact]
    public async Task APageThatHasGoneSinceTheSelectionWasMadeIsWarnedAboutRatherThanFailingTheBatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("bulk-stale", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Products", cancellationToken);

        var job = await _bench.Resolve<IBulkOperationService>().StartAsync(
            new BulkOperationRequest(
                BulkOperation.SetReviewByDate,
                new BulkSelection([page.Summary.Id, 987654]),
                ReviewByDate: new DateOnly(2027, 1, 31)),
            cancellationToken);

        job.IsSuccess.Should().BeTrue(Because(job));
        job.Value!.Total.Should().Be(1, "the page that is gone is left out rather than attempted");
        job.Value.Succeeded.Should().Be(1);

        _bench.Context.ChangeTracker.Clear();

        (await _bench.Context.Pages.AsNoTracking().SingleAsync(cancellationToken)).ReviewByDate
            .Should().Be(new DateOnly(2027, 1, 31));
    }

    [Fact]
    public async Task SettingAReviewDateAcrossASelectionLeavesEverythingElseOnThosePagesAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("bulk-review", cancellationToken, PageWorkbench.TextZone("hero"));
        var first = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var second = await _bench.AddPageAsync(template, "About", cancellationToken);

        await FillAsync(first.Summary.Id, template.Key, cancellationToken);

        var job = await _bench.Resolve<IBulkOperationService>().StartAsync(
            new BulkOperationRequest(
                BulkOperation.SetReviewByDate,
                new BulkSelection([first.Summary.Id, second.Summary.Id]),
                ReviewByDate: new DateOnly(2027, 3, 4)),
            cancellationToken);

        job.IsSuccess.Should().BeTrue(Because(job));
        job.Value!.Succeeded.Should().Be(2);

        _bench.Context.ChangeTracker.Clear();

        var pages = await _bench.Context.Pages.AsNoTracking().ToListAsync(cancellationToken);

        pages.Should().OnlyContain(page => page.ReviewByDate == new DateOnly(2027, 3, 4));

        // The patch names one member, so the batch cannot reinstate its own copy of the other
        // nineteen over whatever somebody changed while it was running.
        (await _bench.DraftOfAsync(first.Summary.Id, cancellationToken)).ContentJson
            .Should().Contain("Our best plans yet");
    }

    [Fact]
    public async Task AnOwnerWhoDoesNotExistFailsEachPageWithTheReasonRatherThanTheBatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("bulk-owner", cancellationToken, PageWorkbench.TextZone("hero"));
        var first = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var second = await _bench.AddPageAsync(template, "About", cancellationToken);

        var job = await _bench.Resolve<IBulkOperationService>().StartAsync(
            new BulkOperationRequest(
                BulkOperation.SetOwner,
                new BulkSelection([first.Summary.Id, second.Summary.Id]),
                OwnerUserId: 987654),
            cancellationToken);

        job.IsSuccess.Should().BeTrue(Because(job));
        job.Value!.Failed.Should().Be(2, "the batch ran; both of its items were refused");

        // Every item goes through the same service a single edit does, so the reason it gives is the
        // same reason — not a generic "that did not work" invented by the batch.
        job.Value.Results.SelectMany(result => result.Diagnostics)
            .Should().OnlyContain(diagnostic => diagnostic.Code == PageCodes.OwnerNotFound);
    }

    [Fact]
    public async Task ABatchOverTheThresholdIsAcceptedAndRunsAfterTheRequestHasBeenAnswered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("bulk-many", cancellationToken, PageWorkbench.TextZone("hero"));
        var ids = new List<int>();

        for (var i = 0; i < BulkLimits.BackgroundThreshold + 1; i++)
        {
            ids.Add((await _bench.AddPageAsync(template, $"Page {i}", cancellationToken)).Summary.Id);
        }

        var bulk = _bench.Resolve<IBulkOperationService>();

        var started = await bulk.StartAsync(
            new BulkOperationRequest(
                BulkOperation.SetReviewByDate,
                new BulkSelection(ids),
                ReviewByDate: new DateOnly(2027, 6, 1)),
            cancellationToken);

        started.IsSuccess.Should().BeTrue(Because(started));
        started.Value!.Total.Should().Be(ids.Count);

        // Polled exactly as a screen polls it. The job may already have finished by the time the
        // assertion runs, which is fine: what is being asserted is that it finishes and reports each
        // item, not that the caller managed to observe it mid-flight.
        var deadline = DateTime.UtcNow.AddSeconds(60);
        var status = started.Value;

        while (!status.IsFinished && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, cancellationToken);

            status = bulk.Get(started.Value.Id).Value!;
        }

        status.IsFinished.Should().BeTrue("a background job that never finishes is a progress bar forever");
        status.State.Should().Be(BulkJobState.Completed);
        status.Succeeded.Should().Be(ids.Count, string.Join(
            "; ",
            status.Results.Where(result => !result.Succeeded)
                .SelectMany(result => result.Diagnostics)
                .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));

        _bench.Context.ChangeTracker.Clear();

        (await _bench.Context.Pages.CountAsync(
                page => page.ReviewByDate == new DateOnly(2027, 6, 1),
                cancellationToken))
            .Should().Be(ids.Count);
    }

    [Fact]
    public async Task PollingAJobThisProcessNeverRanIsANotFoundRatherThanAnEmptyReport()
    {
        var missing = _bench.Resolve<IBulkOperationService>().Get(Guid.NewGuid());

        missing.Outcome.Should().Be(CmsOutcome.NotFound);
        missing.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.JobNotFound);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task AnEditorWhoMayNotPublishIsRefusedOnceRatherThanPerPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var bench = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.ContentRead, CmsPermissions.ContentEdit),
            cancellationToken);

        var refused = await bench.Resolve<IBulkOperationService>().StartAsync(
            new BulkOperationRequest(BulkOperation.Publish, new BulkSelection([1])),
            cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Forbidden);
        refused.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().NotContain(
                CmsPermissions.ContentPublish,
                "a refusal never names what the caller would have needed to hold");
    }

    [Fact]
    public async Task AnEmptySelectionIsRefusedRatherThanRunAsAJobOverNothing()
    {
        var refused = await _bench.Resolve<IBulkOperationService>().StartAsync(
            new BulkOperationRequest(BulkOperation.Publish, new BulkSelection([])),
            TestContext.Current.CancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Invalid);
        refused.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.SelectionEmpty);
    }

    /// <summary>Fills the required zone, so the page is publishable.</summary>
    private async Task FillAsync(int pageId, string templateKey, CancellationToken cancellationToken)
    {
        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            pageId,
            new SaveDraftRequest(
                $$"""
                { "schemaVersion": 1, "templateKey": "{{templateKey}}", "templateRevision": 1,
                  "zones": { "hero": { "type": "plainText", "value": "Our best plans yet" } } }
                """,
                null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));

        _bench.Context.ChangeTracker.Clear();
    }

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
