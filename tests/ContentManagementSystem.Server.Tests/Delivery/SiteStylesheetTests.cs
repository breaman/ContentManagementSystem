using System.Net;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Appearance;
using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Delivery.Appearance;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// The administrator-authored site stylesheet, end to end over HTTP (tasks P10-14 and P10-16).
/// </summary>
/// <remarks>
/// The central assertion is the one pages already make, restated about styling: <strong>saving the
/// draft changes nothing an anonymous visitor receives, and publishing does</strong> (criterion
/// P10 #2). It is asserted over HTTP against an anonymous client rather than against the service,
/// because "what a visitor gets" is a property of the response and not of the row.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class SiteStylesheetTests(SqlServerFixture fixture)
{
    private const string TemplateKey = "stylesheet-subject";
    private const string DraftCss = ".cms-page { --draft-marker: 1; }";
    private const string PublishedCss = ".cms-page { --published-marker: 1; }";

    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(
            fixture,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task NothingIsServedAndNothingIsLinkedUntilAStylesheetIsPublished()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishedPageAsync("Pricing", cancellationToken);

        using var client = _bench.CreateClient();

        var stylesheet = await client.GetAsync(SiteStylesheetEndpoint.Path, cancellationToken);

        // A 404 rather than an empty 200: the document does not link this file in that state, so a
        // request for it is a stale cache or an old page, and both should act on "there is nothing
        // here" (spec section 30.4).
        stylesheet.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var page = await client.GetStringAsync("/pricing", cancellationToken);

        page.Should().NotContain(
            SiteStylesheetEndpoint.Path,
            "a deployment that never uses the feature should pay nothing for it");
        page.Should().Contain("/css/site.css", "the shipped stylesheet is always linked");
    }

    [Test]
    public async Task SavingTheDraftChangesNothingAnAnonymousVisitorReceives()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishedPageAsync("Pricing", cancellationToken);
        await PublishStylesheetAsync(PublishedCss, cancellationToken);
        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient();

        (await client.GetStringAsync(SiteStylesheetEndpoint.Path, cancellationToken))
            .Should().Be(PublishedCss);

        await SaveDraftAsync(DraftCss, cancellationToken);
        await DispatchAsync(cancellationToken);

        // Criterion P10 #2, the half that is easy to get wrong: an editing screen that wrote
        // straight through would pass every other test in this file.
        var served = await client.GetStringAsync(SiteStylesheetEndpoint.Path, cancellationToken);

        served.Should().Be(PublishedCss);
        served.Should().NotContain("draft-marker");
    }

    [Test]
    public async Task PublishingReachesTheNextRequest()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishedPageAsync("Pricing", cancellationToken);
        await PublishStylesheetAsync(PublishedCss, cancellationToken);
        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient();

        await client.GetStringAsync(SiteStylesheetEndpoint.Path, cancellationToken);

        await SaveDraftAsync(DraftCss, cancellationToken);
        await PublishStylesheetAsync(null, cancellationToken);
        await DispatchAsync(cancellationToken);

        (await client.GetStringAsync(SiteStylesheetEndpoint.Path, cancellationToken))
            .Should().Be(DraftCss);
    }

    [Test]
    public async Task PublishingEnqueuesTheStylesheetTagAndNotTheWholeSite()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        // Published once already, so this publish is a change of content rather than a change of
        // whether the link exists — the case where every cached page still has the right markup.
        await PublishStylesheetAsync(PublishedCss, cancellationToken);
        await DispatchAsync(cancellationToken);

        await SaveDraftAsync(DraftCss, cancellationToken);
        await PublishStylesheetAsync(null, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var enqueued = await PendingInvalidationsAsync(cancellationToken);

        enqueued.Should().ContainSingle();
        enqueued[0].Should().Contain(CacheTags.SiteStylesheet);
        enqueued[0].Should().NotContain(
            $"\"{CacheTags.All}\"",
            "the stylesheet's URL is stable, so a page's markup did not change");
    }

    [Test]
    public async Task TheFirstPublishEvictsEverySiteBecauseEveryPageGainsALink()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await SaveDraftAsync(PublishedCss, cancellationToken);
        await PublishStylesheetAsync(null, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var enqueued = await PendingInvalidationsAsync(cancellationToken);

        // The transition, and only the transition. A page cached before the first publish has no
        // <link> in it at all and would go on having none until its hour was up.
        enqueued.Should().ContainSingle();
        enqueued[0].Should().Contain(CacheTags.SiteStylesheet).And.Contain(CacheTags.All);
    }

    [Test]
    public async Task APublishedStylesheetIsLinkedAfterTheShippedOne()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishedPageAsync("Pricing", cancellationToken);
        await PublishStylesheetAsync(PublishedCss, cancellationToken);
        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient();

        var html = await client.GetStringAsync("/pricing", cancellationToken);

        var shipped = html.IndexOf("/css/site.css", StringComparison.Ordinal);
        var custom = html.IndexOf(SiteStylesheetEndpoint.Path, StringComparison.Ordinal);

        shipped.Should().BeGreaterThanOrEqualTo(0);
        custom.Should().BeGreaterThan(
            shipped,
            "later rules of equal specificity win, which is the whole mechanism (spec section 30.1)");
    }

    [Test]
    public async Task TheResponseIsPinnedToTextCssAndRevalidatedRatherThanFingerprinted()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishStylesheetAsync(PublishedCss, cancellationToken);
        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient();

        var response = await client.GetAsync(SiteStylesheetEndpoint.Path, cancellationToken);

        response.Content.Headers.ContentType?.MediaType.Should().Be("text/css");
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.CacheControl?.MustRevalidate.Should().BeTrue();

        // The conditional request the stable URL is paid for with. Without a 304 here, every page
        // load would re-download the whole stylesheet.
        using var conditional = new HttpRequestMessage(HttpMethod.Get, SiteStylesheetEndpoint.Path);

        conditional.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag!.ToString());

        var second = await client.SendAsync(conditional, cancellationToken);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Test]
    public async Task TheBackofficeDocumentLinksNoAdministratorAuthoredStylesheet()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishStylesheetAsync(PublishedCss, cancellationToken);
        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient(followRedirects: true, CmsRoles.Administrator);

        var html = await client.GetStringAsync("/admin", cancellationToken);

        // Criterion P10 #4, asserted against the rendered HTML rather than against App.razor, so a
        // future edit cannot quietly hand a stylesheet the run of the admin screens.
        html.Should().NotContain(SiteStylesheetEndpoint.Path);
        html.Should().NotContain(PublishedCss);
    }

    [Test]
    public async Task RevertingToNothingPutsTheSiteBackToTheDesignItShipsWith()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishedPageAsync("Pricing", cancellationToken);
        await PublishStylesheetAsync(PublishedCss, cancellationToken);
        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient();

        (await client.GetAsync(SiteStylesheetEndpoint.Path, cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = _bench.NewScope())
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<ISiteStylesheetService>()
                .RevertAsync(revisionId: null, copyToDraft: false, cancellationToken);

            result.IsSuccess.Should().BeTrue(Because(result));
        }

        await DispatchAsync(cancellationToken);

        (await client.GetAsync(SiteStylesheetEndpoint.Path, cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.GetStringAsync("/pricing", cancellationToken))
            .Should().NotContain(SiteStylesheetEndpoint.Path);
    }

    [Test]
    public async Task ARefusedConstructIsRefusedOnSaveAndTheLivestylesheetKeepsBeingServed()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishStylesheetAsync(PublishedCss, cancellationToken);
        await DispatchAsync(cancellationToken);

        await using (var scope = _bench.NewScope())
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<ISiteStylesheetService>()
                .SaveDraftAsync("@import 'https://cdn.example.com/theme.css';", null, cancellationToken);

            result.Outcome.Should().Be(CmsOutcome.Invalid);
            result.Diagnostics.Diagnostics.Should().Contain(
                diagnostic => diagnostic.RelativePath != null && diagnostic.RelativePath.StartsWith("line ", StringComparison.Ordinal),
                "a refusal names where it is");
        }

        using var client = _bench.CreateClient();

        // Criterion P10 #3's second half: a refused save leaves the live site exactly as it was.
        (await client.GetStringAsync(SiteStylesheetEndpoint.Path, cancellationToken))
            .Should().Be(PublishedCss);
    }

    [Test]
    public async Task ACallerWithoutAppearanceEditIsRefusedByTheServiceItself()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        // A workbench holding every permission except this one. The endpoint policy would refuse
        // first in a real request; this asserts the service refuses on its own, which is the check
        // that still runs when something else calls it (criterion P10 #5).
        await using var restricted = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.ContentRead, CmsPermissions.SettingsEdit),
            cancellationToken);

        var stylesheet = restricted.Resolve<ISiteStylesheetService>();

        (await stylesheet.GetAsync(cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);
        (await stylesheet.SaveDraftAsync(DraftCss, null, cancellationToken)).Outcome
            .Should().Be(CmsOutcome.Forbidden);
        (await stylesheet.PublishAsync(null, cancellationToken)).Outcome
            .Should().Be(CmsOutcome.Forbidden);
        (await stylesheet.RevertAsync(null, false, cancellationToken)).Outcome
            .Should().Be(CmsOutcome.Forbidden);
    }

    [Test]
    public async Task ALostRaceHandsBackTheStylesheetThatWon()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var first = await _bench.Resolve<ISiteStylesheetService>().GetAsync(cancellationToken);
        var stale = first.Value!.RowVersion;

        await SaveDraftAsync(PublishedCss, cancellationToken);

        await using var scope = _bench.NewScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ISiteStylesheetService>()
            .SaveDraftAsync(DraftCss, stale, cancellationToken);

        result.Outcome.Should().Be(CmsOutcome.Conflict);
        result.Value.Should().NotBeNull();
        result.Value!.DraftCss.Should().Be(
            PublishedCss,
            "the losing editor needs the draft that won in order to choose (spec section 11.8)");
    }

    [Test]
    public async Task PreviewServesTheDraftToSomebodyWhoMayEditIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishStylesheetAsync(PublishedCss, cancellationToken);
        await SaveDraftAsync(DraftCss, cancellationToken);
        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient(followRedirects: false, CmsRoles.Administrator);

        var response = await client.GetAsync(SiteStylesheetEndpoint.PreviewHref, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(cancellationToken)).Should().Be(DraftCss);

        // Never cached anywhere. A draft stylesheet in a shared cache is a redesign nobody approved
        // being served to the public.
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
    }

    [Test]
    public async Task PreviewServesThePublishedCopyToSomebodyWhoMayNot()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        // A workbench without `Appearance.Edit`, which is what a shared preview link resolves to:
        // those are opened by approvers and clients with no account (spec section 12.2). The
        // permission has to be withheld here rather than by using an anonymous client, because this
        // workbench installs one permissive `ICmsAuthorization` for the whole application and every
        // request through it — anonymous ones included — resolves that.
        await using var restricted = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.ContentRead, CmsPermissions.ContentPublish),
            cancellationToken);

        await using (var scope = restricted.NewScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Arranged in the table rather than through the service, which would refuse this caller.
            await context.SiteStylesheets
                .Where(row => row.Id == SiteStylesheet.SingletonId)
                .ExecuteUpdateAsync(
                    updates => updates
                        .SetProperty(row => row.DraftCss, DraftCss)
                        .SetProperty(row => row.PublishedCss, PublishedCss)
                        .SetProperty(row => row.PublishedHash, PublishedHash())
                        .SetProperty(row => row.PublishedOn, DateTimeOffset.UnixEpoch),
                    cancellationToken);
        }

        using var client = restricted.CreateClient(followRedirects: false, CmsRoles.Viewer);

        var response = await client.GetAsync(SiteStylesheetEndpoint.PreviewHref, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // The site as it looks, not a page with no styling — and not the design nobody has published.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be(PublishedCss);
        body.Should().NotContain("draft-marker");
    }

    [Test]
    public async Task PublishRefusesADraftThatReachedTheDatabaseByAnotherPath()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishStylesheetAsync(PublishedCss, cancellationToken);

        // A restore, an environment promotion, or a hand-written UPDATE: the save this service
        // guards was never run, and publish is the last point before every visitor sees it.
        await _bench.Context.SiteStylesheets
            .Where(sheet => sheet.Id == SiteStylesheet.SingletonId)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(
                    sheet => sheet.DraftCss,
                    "@import url('https://cdn.example.com/theme.css');"),
                cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        await using var scope = _bench.NewScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ISiteStylesheetService>()
            .PublishAsync(null, cancellationToken);

        result.Outcome.Should().Be(CmsOutcome.Invalid);

        using var client = _bench.CreateClient();

        (await client.GetStringAsync(SiteStylesheetEndpoint.Path, cancellationToken))
            .Should().Be(PublishedCss);
    }

    [Test]
    public async Task RevertingToAnEarlierRevisionPublishesItAndLeavesTheDraftAlone()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishStylesheetAsync(PublishedCss, cancellationToken);
        await PublishStylesheetAsync(DraftCss, cancellationToken);
        await DispatchAsync(cancellationToken);

        var stylesheet = _bench.Resolve<ISiteStylesheetService>();

        var revisions = await stylesheet.ListRevisionsAsync(cancellationToken);
        var earlier = revisions.Value!.Single(revision => !revision.IsCurrent);

        await using (var scope = _bench.NewScope())
        {
            var reverted = await scope.ServiceProvider
                .GetRequiredService<ISiteStylesheetService>()
                .RevertAsync(earlier.Id, copyToDraft: false, cancellationToken);

            reverted.IsSuccess.Should().BeTrue(Because(reverted));

            // The half that makes revert safe to reach for: the work that broke the site is still
            // in the draft, so recovering costs nothing (criterion P10 #6).
            reverted.Value!.DraftCss.Should().Be(DraftCss);
        }

        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient();

        (await client.GetStringAsync(SiteStylesheetEndpoint.Path, cancellationToken))
            .Should().Be(PublishedCss);
    }

    /// <summary>The published copy's hash, since the fixture writes the column directly.</summary>
    private static byte[] PublishedHash() =>
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PublishedCss));

    private async Task<List<string>> PendingInvalidationsAsync(CancellationToken cancellationToken) =>
        await _bench.Context.OutboxMessages
            .AsNoTracking()
            .Where(message =>
                message.ProcessedOn == null &&
                message.Type == CacheInvalidationMessage.MessageType)
            .Select(message => message.PayloadJson)
            .ToListAsync(cancellationToken);

    private async Task SaveDraftAsync(string css, CancellationToken cancellationToken)
    {
        await using var scope = _bench.NewScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ISiteStylesheetService>()
            .SaveDraftAsync(css, null, cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));

        _bench.Context.ChangeTracker.Clear();
    }

    /// <summary>Publishes the draft, optionally replacing it first.</summary>
    private async Task PublishStylesheetAsync(string? css, CancellationToken cancellationToken)
    {
        if (css is not null) await SaveDraftAsync(css, cancellationToken);

        await using var scope = _bench.NewScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ISiteStylesheetService>()
            .PublishAsync(null, cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));

        _bench.Context.ChangeTracker.Clear();
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _bench.NewScope();

        await scope.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(cancellationToken);
    }

    private async Task<PageDetail> PublishedPageAsync(string title, CancellationToken cancellationToken)
    {
        var template = await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

        var page = await _bench.AddPageAsync(template, title, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(title), null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));
        _bench.Context.ChangeTracker.Clear();

        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken);

        published.IsSuccess.Should().BeTrue(Because(published));
        _bench.Context.ChangeTracker.Clear();

        return page;
    }

    private static string Payload(string text) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{TemplateKey}}", "templateRevision": 1,
          "zones": { "kicker": { "type": "plainText", "value": "{{text}}" } } }
        """;

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
