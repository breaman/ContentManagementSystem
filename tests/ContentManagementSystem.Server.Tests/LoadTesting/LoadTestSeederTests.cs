using System.Net;

using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.LoadTesting;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.LoadTesting;

/// <summary>
/// The load-testing dataset (task P9-12, spec section 25 NFR-9).
/// </summary>
/// <remarks>
/// The seeder writes rows with bulk copy instead of going through the content services, so it holds
/// its own opinion about what a published page consists of. That opinion is what these tests check,
/// and they check it the only way that means anything: by asking the running application for the
/// seeded pages over HTTP. A dataset the delivery pipeline cannot serve is worthless whatever the
/// row counts say.
/// <para>
/// Four hundred pages rather than fifty thousand. The shape is derived from the count, so a small
/// run is the same site in miniature — sections, topics, leaves, a deep branch, recycled pages, and
/// the redirects — and it runs in seconds.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class LoadTestSeederTests(SqlServerFixture fixture)
{
    private const int Pages = 400;

    private PageWorkbench _bench = null!;
    private string _manifestPath = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync()
    {
        _bench = await PageWorkbench.CreateAsync(
            fixture,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

        _manifestPath = Path.Combine(Path.GetTempPath(), $"cms-load-test-{Guid.NewGuid():N}.json");
    }

    [After(HookType.Test)]
    public async ValueTask DisposeAsync()
    {
        await _bench.DisposeAsync();

        if (File.Exists(_manifestPath)) File.Delete(_manifestPath);
    }

    [Test]
    public async Task TheSeededSiteIsOneTheDeliveryPipelineServes()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var report = await Seeder().SeedAsync(Options(), cancellationToken: cancellationToken);

        report.AlreadySeeded.Should().BeFalse();
        report.Pages.Should().Be(Pages);
        report.PublishedPages.Should().BeGreaterThan(Pages / 2);
        report.ManifestPath.Should().NotBeNull();

        var manifest = LoadTestManifest.Read(await File.ReadAllTextAsync(_manifestPath, cancellationToken))!;

        manifest.Counts.Pages.Should().Be(Pages);
        manifest.PublishedUrls.Should().NotBeEmpty();

        using var client = _bench.CreateClient(followRedirects: false);

        foreach (var url in manifest.PublishedUrls.Take(5))
        {
            using var response = await client.GetAsync(url, cancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{url} was seeded as published");

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            // The template rendered rather than the fallback, and the picture the payload names
            // resolved to a media row — the two things a hand-written payload most easily gets
            // wrong, and neither of which shows up in a row count.
            html.Should().Contain("class=\"cms-page");
            html.Should().Contain("<img");
        }
    }

    [Test]
    public async Task ALandingPageCarriesTheSharedFooter()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await Seeder().SeedAsync(Options(), cancellationToken: cancellationToken);

        var manifest = LoadTestManifest.Read(await File.ReadAllTextAsync(_manifestPath, cancellationToken))!;

        manifest.LandingUrls.Should().NotBeEmpty("the sections and topics are landing pages");

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync(manifest.LandingUrls[0], cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Late-bound reusable content resolved through the delivery path. This is the fan-out the
        // load test exists to measure: publishing this one item invalidates every page holding it.
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        html.Should().Contain("Seeded load-test footer");
    }

    [Test]
    public async Task TheMissAndRedirectPathsHaveSomethingToServe()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await Seeder().SeedAsync(Options(), cancellationToken: cancellationToken);

        var manifest = LoadTestManifest.Read(await File.ReadAllTextAsync(_manifestPath, cancellationToken))!;

        using var client = _bench.CreateClient(followRedirects: false);

        using var missing = await client.GetAsync(manifest.NotFoundUrls[0], cancellationToken);

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var moved = await client.GetAsync(manifest.RedirectUrls[0], cancellationToken);

        moved.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        moved.Headers.Location.Should().NotBeNull();
    }

    [Test]
    public async Task EveryPageIsPointedAtTheVersionsWrittenForIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await Seeder().SeedAsync(Options(), cancellationToken: cancellationToken);

        var pages = await _bench.Context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

        pages.Should().HaveCount(Pages);
        pages.Should().OnlyContain(page => page.DraftVersionId != null);

        var published = pages.Where(page => page.PublishedVersionId != null).ToList();

        published.Should().NotBeEmpty();
        published.Should().OnlyContain(page => !page.IsDeleted, "a recycled page is not published");

        var live = await _bench.Context.PageRoutes
            .AsNoTracking()
            .CountAsync(route => route.IsPublished, cancellationToken);

        live.Should().Be(published.Count, "a published page has exactly one live route");

        var statuses = await _bench.Context.PageVersions
            .AsNoTracking()
            .Where(version => version.Status == PageVersionStatus.Published)
            .CountAsync(cancellationToken);

        statuses.Should().Be(published.Count);

        // The tree, not a flat list: the deep branch is what the ACL and path-prefix costs are
        // measured against, and a generator that quietly stopped nesting would still count right.
        var deepest = await _bench.Context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .MaxAsync(page => page.Depth, cancellationToken);

        deepest.Should().BeGreaterThanOrEqualTo(10);
    }

    [Test]
    public async Task TheSharedFooterIsFoundByTheWalkThatHasToInvalidateIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var report = await Seeder().SeedAsync(Options(), cancellationToken: cancellationToken);

        var footer = await _bench.Context.ReusableContents
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        // The rows the seeder writes are not extracted from the payloads it wrote them into, so the
        // one thing worth asserting is that the application's own walk finds what the seeder claims
        // to have placed. An empty ContentReferences table would make a load test of publishing the
        // footer report the cost of invalidating nothing (risk R8).
        var impact = await _bench.Resolve<IReferenceQueryService>().WhereUsedAsync(
            ContentReferenceTargetType.ReusableContent,
            footer.Id,
            cancellationToken);

        impact.AffectedPageCount.Should().BeGreaterThan(10, "a fifth of the leaves are landing pages too");
        impact.PinnedPageCount.Should().Be(0, "the footer is placed late-bound, which is what makes it fan out");

        var landingTemplateId = await _bench.Context.Templates
            .AsNoTracking()
            .Where(template => template.Key == "marketing-landing")
            .Select(template => template.Id)
            .SingleAsync(cancellationToken);

        var landingPages = await _bench.Context.Pages
            .AsNoTracking()
            .CountAsync(page => page.TemplateId == landingTemplateId, cancellationToken);

        impact.AffectedPageCount.Should().BeLessThanOrEqualTo(
            landingPages,
            "only landing pages carry the footer");

        report.Pages.Should().Be(Pages);
    }

    [Test]
    public async Task SeedingTwiceLeavesTheFirstDatasetAlone()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await Seeder().SeedAsync(Options(), cancellationToken: cancellationToken);

        var second = await Seeder().SeedAsync(Options(), cancellationToken: cancellationToken);

        second.AlreadySeeded.Should().BeTrue();
        second.Pages.Should().Be(Pages);

        var pages = await _bench.Context.Pages
            .IgnoreQueryFilters()
            .CountAsync(cancellationToken);

        pages.Should().Be(Pages, "the second run wrote nothing");
    }

    [Test]
    public async Task PurgingRemovesTheDatasetAndReseedingRebuildsTheSameOne()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await Seeder().SeedAsync(Options(), cancellationToken: cancellationToken);

        var before = LoadTestManifest.Read(await File.ReadAllTextAsync(_manifestPath, cancellationToken))!;

        (await Seeder().PurgeAsync(Options(), cancellationToken: cancellationToken)).Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        (await _bench.Context.Pages.IgnoreQueryFilters().CountAsync(cancellationToken))
            .Should().Be(0);
        (await _bench.Context.MediaItems.IgnoreQueryFilters().CountAsync(cancellationToken))
            .Should().Be(0);
        (await _bench.Context.SearchDocuments.CountAsync(cancellationToken))
            .Should().Be(0);

        using var client = _bench.CreateClient(followRedirects: false);
        using var gone = await client.GetAsync(before.PublishedUrls[0], cancellationToken);

        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await Seeder().SeedAsync(Options(), cancellationToken: cancellationToken);

        var after = LoadTestManifest.Read(await File.ReadAllTextAsync(_manifestPath, cancellationToken))!;

        // The same seed has to produce the same site, or two load-test runs cannot be compared: a
        // regression and a differently shaped dataset would look identical in the numbers.
        after.PublishedUrls.Should().Equal(before.PublishedUrls);
        after.Counts.Should().Be(before.Counts);
    }

    private ILoadTestSeeder Seeder() => _bench.Resolve<ILoadTestSeeder>();

    private LoadTestSeedOptions Options() => new()
    {
        Pages = Pages,
        MediaItems = 60,

        // Three, because each one is written to the media store and the largest is a four-thousand
        // pixel original. They are content-addressed and drawn deterministically, so a second run of
        // the suite finds them already there and writes nothing.
        DistinctImages = 3,
        Tags = 20,
        Redirects = 10,
        BatchSize = 250,
        ManifestPath = _manifestPath,
        ManifestSampleSize = 50,
    };
}
