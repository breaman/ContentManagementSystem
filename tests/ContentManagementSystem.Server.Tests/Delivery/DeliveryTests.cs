using System.Net;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// Anonymous delivery, end to end over HTTP (tasks P3-13 and P3-24).
/// </summary>
/// <remarks>
/// The vertical slice, asserted the way a visitor experiences it: content is arranged through the
/// real page and publishing services, and then requested with an ordinary <c>HttpClient</c> carrying
/// no identity at all. Everything between — route resolution, the published-version filter, the
/// component render, the headers — is the application's own.
/// <para>
/// The template is <c>article</c>, which is a key a deployed component declares. A key nothing
/// declares would render the fallback layout and every assertion about markup below would be
/// asserting about that instead.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class DeliveryTests(SqlServerFixture fixture)
{
    private const string TemplateKey = "article";

    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task APublishedPageIsReachableAtItsUrlByAnAnonymousRequest()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Pricing", "Our best plans yet", cancellationToken);

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync("/pricing", cancellationToken);

        // Acceptance criterion P3 #1.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        html.Should().Contain("<!DOCTYPE html>")
            .And.Contain("<title>Pricing</title>")
            .And.Contain("data-template=\"article\"")
            .And.Contain("Our best plans yet");

        // Static SSR only: no circuit, no WebAssembly bootstrapper, nothing for a reader to download
        // before they can read (spec section 5.3). This is the assertion output caching depends on.
        html.Should().NotContain("blazor.web.js");

        page.Summary.Id.Should().BePositive();
    }

    [Test]
    public async Task AnUnpublishedPageReturnsNotFoundToAnAnonymousRequest()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await TemplateAsync(cancellationToken);
        var page = await _bench.AddPageAsync(template, "Unreleased", cancellationToken);

        await SaveDraftAsync(page.Summary.Id, template.Key, "Not for the public yet", cancellationToken);

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync("/unreleased", cancellationToken);

        // Half of acceptance criterion P3 #2. The page has a draft route so preview can address it,
        // and the published route the resolver looks up does not exist — which is why this is a 404
        // rather than a permission check somebody could forget to write.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        html.Should().NotContain("Not for the public yet");
    }

    [Test]
    public async Task DraftEditsAfterAPublishDoNotChangeTheAnonymousResponse()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Offers", "What the public sees", cancellationToken);

        using var client = _bench.CreateClient();

        var before = await client.GetStringAsync("/offers", cancellationToken);

        for (var edit = 0; edit < 3; edit++)
        {
            await SaveDraftAsync(page.Summary.Id, TemplateKey, $"Draft edit {edit}", cancellationToken);
        }

        var after = await client.GetStringAsync("/offers", cancellationToken);

        // Acceptance criterion P3 #3, and the requirement's central promise. Byte-for-byte rather
        // than "does not contain the draft text": a weaker assertion passes for a response that
        // changed in some other way nobody looked at.
        after.Should().Be(before);
        after.Should().Contain("What the public sees").And.NotContain("Draft edit");
    }

    [Test]
    public async Task AChangedSlugRedirectsTheOldUrlPermanently()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Offers", "Still here", cancellationToken);

        (await _bench.Resolve<IPageService>().PatchMetadataAsync(
            page.Summary.Id,
            new PatchPageMetadataRequest { Slug = new Shared.Contracts.Api.Patch<string>("deals") },
            null,
            cancellationToken)).IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        using var client = _bench.CreateClient(followRedirects: false);
        using var response = await client.GetAsync("/offers", cancellationToken);

        // Acceptance criterion P3 #4, seen from outside. The redirect rows are asserted at the
        // service level by UrlServiceTests; what this adds is that the endpoint actually serves them.
        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.OriginalString.Should().Be("/deals");
    }

    [Test]
    public async Task ANonCanonicalSpellingIsSentToTheCanonicalUrl()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishedPageAsync("Pricing", "Our best plans yet", cancellationToken);

        using var client = _bench.CreateClient(followRedirects: false);
        using var response = await client.GetAsync("/Pricing/", cancellationToken);

        // The same content must not be readable at two addresses, or the canonical tag, the sitemap,
        // and the analytics report describe three different things (spec section 10.3).
        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.OriginalString.Should().Be("/pricing");
    }

    [Test]
    public async Task AnUnresolvedUrlIsRecordedOnceWithAnAccurateHitCount()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = _bench.CreateClient();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            using var response = await client.GetAsync("/legacy/brochure", cancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        _bench.Context.ChangeTracker.Clear();

        var logged = await _bench.Context.NotFoundLogs
            .AsNoTracking()
            .SingleAsync(entry => entry.Url == "/legacy/brochure", cancellationToken);

        // Acceptance criterion P3 #11. One row per URL, not one per request: the point of the report
        // is which dead URLs receive traffic, and a crawler must not be able to make this the
        // largest table on the site (spec section 10.6).
        logged.HitCount.Should().Be(3);
        logged.FirstSeenOn.Should().BeOnOrBefore(logged.LastSeenOn);
    }

    [Test]
    public async Task TheConfiguredCmsPageIsServedForAnUnresolvedUrlWithA404Status()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var notFoundPage = await PublishedPageAsync("Sorry", "We could not find that", cancellationToken);

        var settings = await _bench.Context.SiteSettings.SingleAsync(cancellationToken);
        settings.NotFoundPageId = notFoundPage.Summary.Id;
        await _bench.Context.SaveChangesAsync(cancellationToken);

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync("/nothing/here", cancellationToken);

        // The 404 page is itself a CMS page (spec section 10.6), so an editor owns its words. The
        // status is still 404: a soft 404 that answers 200 is indexed as a real page.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain("We could not find that");
    }

    [Test]
    public async Task AnUnchangedPageRevalidatesToNotModified()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishedPageAsync("Pricing", "Our best plans yet", cancellationToken);

        using var client = _bench.CreateClient();
        using var first = await client.GetAsync("/pricing", cancellationToken);

        var etag = first.Headers.ETag!.ToString();

        client.DefaultRequestHeaders.Add("If-None-Match", etag);

        using var second = await client.GetAsync("/pricing", cancellationToken);

        // Computed over the finished document rather than derived from the version id, so anything
        // that legitimately changes the page without changing its version — a link target moving, a
        // reusable item republished — still breaks the tag (spec section 16.4).
        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        first.Headers.CacheControl!.ToString().Should().Contain("s-maxage=300");
    }

    [Test]
    public async Task A404IsNeverCached()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync("/nothing/here", cancellationToken);

        // A URL that 404s today is very often one somebody is about to publish a page at, and a
        // cached 404 makes that fix invisible for as long as the entry lives.
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    private async Task<Template> TemplateAsync(CancellationToken cancellationToken) =>
        await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

    private async Task<PageDetail> PublishedPageAsync(
        string title,
        string text,
        CancellationToken cancellationToken)
    {
        var template = await TemplateAsync(cancellationToken);
        var page = await _bench.AddPageAsync(template, title, cancellationToken);

        await SaveDraftAsync(page.Summary.Id, template.Key, text, cancellationToken);

        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken);

        published.IsSuccess.Should().BeTrue(Because(published));
        _bench.Context.ChangeTracker.Clear();

        return page;
    }

    private async Task SaveDraftAsync(
        int pageId,
        string templateKey,
        string text,
        CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        // No expected row version: these are fixtures, not a concurrency test, and the optimistic
        // check has its own suite. Passing null is the documented "skip the check" value rather than
        // a way around a failure.
        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            pageId,
            new SaveDraftRequest(Payload(templateKey, text), null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));
        _bench.Context.ChangeTracker.Clear();
    }

    private static string Payload(string templateKey, string text) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{templateKey}}", "templateRevision": 1,
          "zones": { "kicker": { "type": "plainText", "value": "{{text}}" } } }
        """;

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
