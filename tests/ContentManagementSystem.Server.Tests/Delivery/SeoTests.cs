using System.Net;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Delivery.Seo;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Delivery.Seo;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// What a crawler sees: the document head, <c>sitemap.xml</c>, and <c>robots.txt</c>
/// (tasks P8-01 to P8-05).
/// </summary>
/// <remarks>
/// Asserted over HTTP against the real application rather than against the metadata builder alone,
/// because most of what can go wrong here is a wiring mistake — a head that is built and not
/// rendered, a sitemap route the catch-all shadows, an editable robots body that the environment
/// override never sees.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class SeoTests(SqlServerFixture fixture)
{
    private const string TemplateKey = "article";

    private PageWorkbench _bench = null!;
    private Template? _template;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task APageEmitsItsTitleDescriptionCanonicalAndRobotsDirective()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Pricing", "Our best plans yet", cancellationToken);

        await PatchSeoAsync(
            page.Summary.Id,
            cancellationToken,
            request => request with
            {
                MetaTitle = new Patch<string?>("Pricing: every plan"),
                MetaDescription = new Patch<string?>("What each plan costs and what it includes."),
            });

        var html = await PublishAndFetchAsync(page.Summary.Id, "/pricing", cancellationToken);

        // Acceptance criterion P8 #1, the four elements every page carries.
        html.Should().Contain("<title>Pricing: every plan</title>")
            .And.Contain("""<meta name="description" content="What each plan costs and what it includes." />""")
            .And.Contain("""<link rel="canonical" href="http://localhost/pricing" />""")
            .And.Contain("""<meta name="robots" content="index, follow" />""");
    }

    [Test]
    public async Task APageEmitsOpenGraphAndTwitterCardTags()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Field notes", "A long read", cancellationToken);

        await PatchSeoAsync(
            page.Summary.Id,
            cancellationToken,
            request => request with
            {
                MetaDescription = new Patch<string?>("Notes from the field."),
                OgTitle = new Patch<string?>("Field notes, in full"),
                OgType = new Patch<string?>("article"),
            });

        var html = await PublishAndFetchAsync(page.Summary.Id, "/field-notes", cancellationToken);

        // Open Graph is RDFa and spells its key as property; Twitter's cards use name. Several
        // validators reject the wrong one, so both attribute forms are asserted.
        html.Should().Contain("""<meta property="og:type" content="article" />""")
            .And.Contain("""<meta property="og:title" content="Field notes, in full" />""")
            .And.Contain("""<meta property="og:url" content="http://localhost/field-notes" />""")
            .And.Contain("""<meta property="og:description" content="Notes from the field." />""")
            .And.Contain("""<meta name="twitter:card" content="summary" />""")
            .And.Contain("""<meta name="twitter:title" content="Field notes, in full" />""");

        // An article carries the timestamp a card renders a date from.
        html.Should().Contain("""<meta property="article:published_time""");
    }

    [Test]
    public async Task TheOpenGraphImageIsRenderedThroughA1200x630Crop()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Launch", "We shipped it", cancellationToken);
        var image = await _bench.AddImageAsync(1600, 1200, cancellationToken, "A photograph of the team");

        var draft = await _bench.Context.PageVersions.SingleAsync(
            version => version.PageId == page.Summary.Id && version.Status == PageVersionStatus.Draft,
            cancellationToken);

        draft.OgImageMediaId = image.Id;
        await _bench.Context.SaveChangesAsync(cancellationToken);
        _bench.Context.ChangeTracker.Clear();

        var html = await PublishAndFetchAsync(page.Summary.Id, "/launch", cancellationToken);

        // The fixed crop of spec section 18.2 rather than the image's own ratio: every network
        // crops a share image for itself otherwise, and each of them differently.
        html.Should().Contain("1200x630/crop/")
            .And.Contain("""<meta property="og:image:width" content="1200" />""")
            .And.Contain("""<meta property="og:image:height" content="630" />""")
            .And.Contain("""<meta property="og:image:alt" content="A photograph of the team" />""")
            .And.Contain("""<meta name="twitter:card" content="summary_large_image" />""");

        // Absolute, unlike every other image the site emits: a crawler fetching it has only the tag
        // and no document to resolve a relative URL against.
        var url = WebUtility.HtmlDecode(Between(html, "<meta property=\"og:image\" content=\"", "\" />"));
        url.Should().StartWith("http://localhost/media/");

        using var client = _bench.CreateClient();
        using var fetched = await client.GetAsync(url, cancellationToken);

        // The URL is not merely well formed: it is signed, and the endpoint serves it.
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TheHomePageCarriesWebSiteAndOrganizationJsonLd()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Home", "Welcome", cancellationToken);

        var settings = await _bench.Context.SiteSettings.SingleAsync(cancellationToken);
        settings.HomePageId = page.Summary.Id;
        settings.SiteName = "Northwind";
        await _bench.Context.SaveChangesAsync(cancellationToken);
        _bench.Context.ChangeTracker.Clear();

        using var client = _bench.CreateClient();
        var html = await client.GetStringAsync("/home", cancellationToken);

        html.Should().Contain("""<script type="application/ld+json">""")
            .And.Contain("\"@type\":\"WebSite\"")
            .And.Contain("\"@type\":\"Organization\"")
            .And.Contain("\"name\":\"Northwind\"");
    }

    [Test]
    public async Task AChildPageEmitsABreadcrumbListOfItsPublishedAncestors()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await TemplateAsync(cancellationToken);
        var parent = await _bench.AddPageAsync(template, "Support", cancellationToken);

        await SaveDraftAsync(parent.Summary.Id, template.Key, "How to reach us", cancellationToken);
        await PublishAsync(parent.Summary.Id, cancellationToken);

        var child = await _bench.AddPageAsync(template, "Contact", cancellationToken, parent.Summary.Id);

        await SaveDraftAsync(child.Summary.Id, template.Key, "Send us a note", cancellationToken);
        await PublishAsync(child.Summary.Id, cancellationToken);

        using var client = _bench.CreateClient();
        var html = await client.GetStringAsync("/support/contact", cancellationToken);

        html.Should().Contain("\"@type\":\"BreadcrumbList\"")
            .And.Contain("\"position\":1,\"name\":\"Support\",\"item\":\"http://localhost/support\"")
            .And.Contain("\"position\":2,\"name\":\"Contact\",\"item\":\"http://localhost/support/contact\"");
    }

    [Test]
    public async Task AHandAuthoredStructuredDataDocumentReplacesTheGeneratedOnes()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Recipes", "Dinner tonight", cancellationToken);

        await PatchSeoAsync(
            page.Summary.Id,
            cancellationToken,
            request => request with
            {
                StructuredDataJson = new Patch<string?>("""{"@context":"https://schema.org","@type":"Recipe","name":"Soup"}"""),
            });

        var html = await PublishAndFetchAsync(page.Summary.Id, "/recipes", cancellationToken);

        // One description of a page, not two that disagree: an editor who filled this in did so
        // because the generated answer was wrong (spec section 18.2).
        html.Should().Contain("\"@type\":\"Recipe\"").And.NotContain("\"@type\":\"WebPage\"");
    }

    [Test]
    public async Task ANoIndexPageSaysSoAndIsLeftOutOfTheSitemap()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Internal", "Not for search", cancellationToken);

        await PatchSeoAsync(
            page.Summary.Id,
            cancellationToken,
            request => request with
            {
                RobotsIndex = new Patch<bool>(false),
                RobotsFollow = new Patch<bool>(false),
            });

        var html = await PublishAndFetchAsync(page.Summary.Id, "/internal", cancellationToken);

        html.Should().Contain("""<meta name="robots" content="noindex, nofollow" />""");

        using var client = _bench.CreateClient();
        var sitemap = await client.GetStringAsync("/sitemap.xml", cancellationToken);

        // Acceptance criterion P8 #2: exactly the published, indexable pages.
        sitemap.Should().NotContain("/internal");
    }

    [Test]
    public async Task TheSitemapListsPublishedPagesWithTheirPublishDate()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishedPageAsync("Pricing", "Our best plans yet", cancellationToken);

        var template = await TemplateAsync(cancellationToken);
        var unpublished = await _bench.AddPageAsync(template, "Draft only", cancellationToken);

        await SaveDraftAsync(unpublished.Summary.Id, template.Key, "Not live", cancellationToken);

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync("/sitemap.xml", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");

        var sitemap = await response.Content.ReadAsStringAsync(cancellationToken);

        sitemap.Should().Contain("""<?xml version="1.0" encoding="UTF-8"?>""")
            .And.Contain("<loc>http://localhost/pricing</loc>")
            .And.Contain("<changefreq>weekly</changefreq>")
            .And.Contain("<priority>0.5</priority>")
            .And.Contain($"<lastmod>{DateTime.UtcNow:yyyy-MM-dd}</lastmod>")
            .And.NotContain("/draft-only");
    }

    [Test]
    public async Task TheSitemapBecomesAnIndexAboveTheConfiguredPageSize()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var bench = await PageWorkbench.CreateAsync(
            fixture,
            cancellationToken: cancellationToken,
            configure: services => services.Configure<SeoOptions>(options => options.SitemapPageSize = 1));

        var template = await bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

        foreach (var title in new[] { "First", "Second" })
        {
            var page = await bench.AddPageAsync(template, title, cancellationToken);

            bench.Context.ChangeTracker.Clear();

            (await bench.Resolve<IDraftService>().SaveAsync(
                page.Summary.Id,
                new SaveDraftRequest(Payload(template.Key, title), null),
                cancellationToken)).IsSuccess.Should().BeTrue();

            bench.Context.ChangeTracker.Clear();

            (await bench.Resolve<IPublishingService>()
                .PublishAsync(page.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

            bench.Context.ChangeTracker.Clear();
        }

        using var client = bench.CreateClient();

        var index = await client.GetStringAsync("/sitemap.xml", cancellationToken);

        // Above the threshold the response describes the files rather than the pages
        // (spec section 18.3).
        index.Should().Contain("<sitemapindex")
            .And.Contain("<loc>http://localhost/sitemap-1.xml</loc>")
            .And.Contain("<loc>http://localhost/sitemap-2.xml</loc>");

        var first = await client.GetStringAsync("/sitemap-1.xml", cancellationToken);

        first.Should().Contain("<urlset").And.Contain("<loc>http://localhost/first</loc>")
            .And.NotContain("/second");

        using var past = await client.GetAsync("/sitemap-3.xml", cancellationToken);

        // A file past the end is a 404 rather than an empty urlset, which would tell a crawler the
        // site has no pages.
        past.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task RobotsTxtOutsideProductionDisallowsEverythingWhateverIsConfigured()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var settings = await _bench.Context.SiteSettings.SingleAsync(cancellationToken);
        settings.RobotsTxt = "User-agent: *\nAllow: /";
        await _bench.Context.SaveChangesAsync(cancellationToken);

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync("/robots.txt", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");

        // Acceptance criterion P8 #3. The test host runs as Development, and the configured body
        // says the opposite of what is served — which is the whole point of the override.
        (await response.Content.ReadAsStringAsync(cancellationToken))
            .Should().Be(RobotsEndpoint.DisallowAll);
    }

    private static string Between(string html, string prefix, string suffix)
    {
        var start = html.IndexOf(prefix, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"the document should contain {prefix}");
        start += prefix.Length;

        var end = html.IndexOf(suffix, start, StringComparison.Ordinal);

        end.Should().BeGreaterThan(start);

        return html[start..end];
    }

    private async Task<string> PublishAndFetchAsync(int pageId, string url, CancellationToken cancellationToken)
    {
        await PublishAsync(pageId, cancellationToken);

        using var client = _bench.CreateClient();

        return await client.GetStringAsync(url, cancellationToken);
    }

    private async Task PublishAsync(int pageId, CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(pageId, true, cancellationToken);

        published.IsSuccess.Should().BeTrue(Because(published));
        _bench.Context.ChangeTracker.Clear();
    }

    private async Task PatchSeoAsync(
        int pageId,
        CancellationToken cancellationToken,
        Func<PatchPageMetadataRequest, PatchPageMetadataRequest> configure)
    {
        _bench.Context.ChangeTracker.Clear();

        var patched = await _bench.Resolve<IPageService>().PatchMetadataAsync(
            pageId,
            configure(new PatchPageMetadataRequest()),
            null,
            cancellationToken);

        patched.IsSuccess.Should().BeTrue(Because(patched));
        _bench.Context.ChangeTracker.Clear();
    }

    /// <summary>
    /// The template these fixtures publish against, given its zones once.
    /// </summary>
    /// <remarks>
    /// Cached per test rather than per call: <c>UseTemplateAsync</c> adds the zone it is given, and
    /// a second call collides on the unique index over template and zone key.
    /// </remarks>
    private async Task<Template> TemplateAsync(CancellationToken cancellationToken) =>
        _template ??= await _bench.UseTemplateAsync(
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
        await PublishAsync(page.Summary.Id, cancellationToken);

        return page;
    }

    private async Task SaveDraftAsync(
        int pageId,
        string templateKey,
        string text,
        CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

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

/// <summary>
/// The <c>robots.txt</c> body itself, away from the environment override that hides it in a test
/// host (task P8-05).
/// </summary>
public class RobotsBodyTests
{
    [Test]
    public void TheDefaultBodyDisallowsTheApplicationPrefixesAndNamesTheSitemap()
    {
        var body = RobotsEndpoint.Body(null, "https://example.com/sitemap.xml");

        body.Should().Contain("User-agent: *")
            .And.Contain("Disallow: /admin")
            .And.Contain("Disallow: /api")
            .And.Contain("Disallow: /preview")
            .And.Contain("Sitemap: https://example.com/sitemap.xml");
    }

    [Test]
    public void AnEditedBodyKeepsItsOwnRulesAndGainsTheSitemapLine()
    {
        var body = RobotsEndpoint.Body("User-agent: *\nDisallow: /private", "https://example.com/sitemap.xml");

        // The sitemap line is the one whose absence is silent — a crawler simply never learns the
        // file exists — so it is added rather than assumed.
        body.Should().Contain("Disallow: /private").And.EndWith("Sitemap: https://example.com/sitemap.xml\n");
    }

    [Test]
    public void ABodyThatAlreadyNamesASitemapIsLeftAlone()
    {
        var body = RobotsEndpoint.Body(
            "User-agent: *\nDisallow:\nSitemap: https://cdn.example.com/sitemap.xml",
            "https://example.com/sitemap.xml");

        body.Should().NotContain("https://example.com/sitemap.xml");
    }
}
