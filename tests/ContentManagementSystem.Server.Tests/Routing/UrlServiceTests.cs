using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Routing;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Routing;

/// <summary>
/// Materialized routes, the cascade a rename causes, and the redirects it leaves behind
/// (tasks P3-04 and P3-25, spec sections 10.4 and 10.5).
/// </summary>
/// <remarks>
/// Driven against a real database because the facts under test are database facts: a filtered unique
/// index that lets a draft route sit at a URL a live page already serves, and a subtree rewrite that
/// has to commit whole or not at all.
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class UrlServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private PageWorkbench _bench = null!;

    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Fact]
    public async Task ANewPageGetsADraftRouteAndNoPublishedOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Our Pricing", cancellationToken);

        var routes = await RoutesOfAsync(page.Summary.Id, cancellationToken);

        // The draft route is what lets preview address the page by URL from the moment it exists,
        // rather than from the moment it is first published.
        routes.Should().ContainSingle();
        routes[0].Url.Should().Be("/our-pricing");
        routes[0].IsPublished.Should().BeFalse();
        routes[0].UrlHash.Should().Equal(SiteUrls.Hash("/our-pricing"));
    }

    [Fact]
    public async Task AUrlIsItsAncestorsSlugsJoined()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken);
        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var child = await _bench.AddPageAsync(template, "Widget", cancellationToken, section.Summary.Id);
        var grandchild = await _bench.AddPageAsync(template, "Specifications", cancellationToken, child.Summary.Id);

        var urls = _bench.Resolve<IUrlService>();

        (await urls.ComputeAsync(section.Summary.Id, cancellationToken)).Should().Be("/products");
        (await urls.ComputeAsync(child.Summary.Id, cancellationToken)).Should().Be("/products/widget");
        (await urls.ComputeAsync(grandchild.Summary.Id, cancellationToken))
            .Should().Be("/products/widget/specifications");
    }

    [Fact]
    public async Task AnExplicitUrlIgnoresTheTreeButItsDescendantsStillBuildOnIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken);
        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var child = await _bench.AddPageAsync(template, "Widget", cancellationToken, section.Summary.Id);
        var grandchild = await _bench.AddPageAsync(template, "Specs", cancellationToken, child.Summary.Id);

        await PatchAsync(
            child.Summary.Id,
            new PatchPageMetadataRequest
            {
                UseExplicitUrl = new Patch<bool>(true),
                ExplicitUrl = new Patch<string?>("/shop/the-widget"),
            },
            cancellationToken);

        var urls = _bench.Resolve<IUrlService>();

        (await urls.ComputeAsync(child.Summary.Id, cancellationToken)).Should().Be("/shop/the-widget");

        // The subtree below stays coherent: opting out of the tree relocates the branch rather than
        // detaching the pages under it.
        (await urls.ComputeAsync(grandchild.Summary.Id, cancellationToken))
            .Should().Be("/shop/the-widget/specs");
    }

    [Fact]
    public async Task PublishingMaterializesThePublicRouteAndUnpublishingWithdrawsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "About", cancellationToken);

        var publishing = _bench.Resolve<IPublishingService>();
        var published = await publishing.PublishAsync(page.Summary.Id, true, cancellationToken);
        published.IsSuccess.Should().BeTrue(Because(published));

        var live = await RoutesOfAsync(page.Summary.Id, cancellationToken);
        live.Should().HaveCount(2, "the draft route stays beside the published one");
        live.Should().ContainSingle(route => route.IsPublished).Which.Url.Should().Be("/about");

        var withdrawn = await publishing.UnpublishAsync(page.Summary.Id, cancellationToken);
        withdrawn.IsSuccess.Should().BeTrue(Because(withdrawn));

        var afterwards = await RoutesOfAsync(page.Summary.Id, cancellationToken);

        // The public URL is gone; the draft route is not, because an editor still has to find the
        // page they just took down.
        afterwards.Should().ContainSingle().Which.IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task ChangingASlugMovesThePageAndEveryDescendantAndLeavesRedirectsBehind()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var child = await _bench.AddPageAsync(template, "Widget", cancellationToken, section.Summary.Id);
        var grandchild = await _bench.AddPageAsync(template, "Specs", cancellationToken, child.Summary.Id);

        var publishing = _bench.Resolve<IPublishingService>();

        foreach (var id in new[] { section.Summary.Id, child.Summary.Id, grandchild.Summary.Id })
        {
            (await publishing.PublishAsync(id, true, cancellationToken)).IsSuccess.Should().BeTrue();
        }

        await PatchAsync(
            section.Summary.Id,
            new PatchPageMetadataRequest { Slug = new Patch<string>("catalogue") },
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        // Acceptance criterion P3 #4: the page and all descendants.
        (await PublishedUrlAsync(section.Summary.Id, cancellationToken)).Should().Be("/catalogue");
        (await PublishedUrlAsync(child.Summary.Id, cancellationToken)).Should().Be("/catalogue/widget");
        (await PublishedUrlAsync(grandchild.Summary.Id, cancellationToken))
            .Should().Be("/catalogue/widget/specs");

        var resolver = _bench.Resolve<IRouteResolver>();

        foreach (var (from, to) in new[]
                 {
                     ("/products", "/catalogue"),
                     ("/products/widget", "/catalogue/widget"),
                     ("/products/widget/specs", "/catalogue/widget/specs"),
                 })
        {
            var resolution = await resolver.ResolveAsync(from, cancellationToken);

            resolution.Kind.Should().Be(RouteResolutionKind.Redirect, $"'{from}' was vacated");
            resolution.TargetUrl.Should().Be(to);
            resolution.StatusCode.Should().Be(301);
        }
    }

    [Fact]
    public async Task ARedirectPointingAtAPageFollowsThatPageWhenItMovesAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);

        (await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        await PatchAsync(
            page.Summary.Id,
            new PatchPageMetadataRequest { Slug = new Patch<string>("plans") },
            cancellationToken);

        await PatchAsync(
            page.Summary.Id,
            new PatchPageMetadataRequest { Slug = new Patch<string>("cost") },
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var resolver = _bench.Resolve<IRouteResolver>();

        // Both vacated URLs land on the current one in a single hop. The first is stored as a page
        // reference so it followed the second move for free; the second was created by that move.
        (await resolver.ResolveAsync("/pricing", cancellationToken)).TargetUrl.Should().Be("/cost");
        (await resolver.ResolveAsync("/plans", cancellationToken)).TargetUrl.Should().Be("/cost");
    }

    [Fact]
    public async Task ALivePageAtAUrlOutranksARedirectWithTheSameSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var original = await _bench.AddPageAsync(template, "Offers", cancellationToken);

        var publishing = _bench.Resolve<IPublishingService>();
        (await publishing.PublishAsync(original.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        // Move it away, which leaves /offers redirecting to /deals.
        await PatchAsync(
            original.Summary.Id,
            new PatchPageMetadataRequest { Slug = new Patch<string>("deals") },
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        (await _bench.Resolve<IRouteResolver>().ResolveAsync("/offers", cancellationToken))
            .Kind.Should().Be(RouteResolutionKind.Redirect);

        // Now reuse the vacated URL for new content — the case that is impossible if a redirect
        // outranks a page (acceptance criterion P3 #6).
        var replacement = await _bench.AddPageAsync(template, "Offers", cancellationToken);
        (await publishing.PublishAsync(replacement.Summary.Id, true, cancellationToken))
            .IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        var resolution = await _bench.Resolve<IRouteResolver>().ResolveAsync("/offers", cancellationToken);

        resolution.Kind.Should().Be(RouteResolutionKind.Page);
        resolution.PageId.Should().Be(replacement.Summary.Id);
    }

    [Fact]
    public async Task TwoPublishedPagesCannotOccupyOneUrl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var occupant = await _bench.AddPageAsync(template, "Guides", cancellationToken);
        var section = await _bench.AddPageAsync(template, "Products", cancellationToken);
        var child = await _bench.AddPageAsync(template, "Manual", cancellationToken, section.Summary.Id);

        var publishing = _bench.Resolve<IPublishingService>();
        (await publishing.PublishAsync(occupant.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();
        (await publishing.PublishAsync(section.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();
        (await publishing.PublishAsync(child.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        // An explicit URL is how two pages can collide without being siblings, so it is the case the
        // sibling-slug rule cannot see and this check has to. Refused with a diagnostic naming the
        // holder, rather than left to fail on the unique index as a 500.
        var refused = await _bench.Resolve<IPageService>().PatchMetadataAsync(
            child.Summary.Id,
            new PatchPageMetadataRequest
            {
                UseExplicitUrl = new Patch<bool>(true),
                ExplicitUrl = new Patch<string?>("/guides"),
            },
            null,
            cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Conflict);
        refused.Diagnostics.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == RoutingCodes.UrlTaken);

        _bench.Context.ChangeTracker.Clear();

        // And nothing moved: the refusal has to leave the routes exactly as it found them.
        (await PublishedUrlAsync(child.Summary.Id, cancellationToken)).Should().Be("/products/manual");
        (await PublishedUrlAsync(occupant.Summary.Id, cancellationToken)).Should().Be("/guides");
    }

    [Fact]
    public async Task ADraftRouteMaySitAtAUrlALivePageAlreadyServes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var live = await _bench.AddPageAsync(template, "Handbook", cancellationToken);

        (await _bench.Resolve<IPublishingService>()
            .PublishAsync(live.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        // Preparing a replacement at the URL a live page is still serving is ordinary work, not an
        // edge case — the filtered unique index exists precisely so this is allowed. Its draft route
        // is unconstrained; only the published one would collide.
        var replacement = await _bench.AddPageAsync(template, "Handbook 2027", cancellationToken);

        await PatchAsync(
            replacement.Summary.Id,
            new PatchPageMetadataRequest
            {
                UseExplicitUrl = new Patch<bool>(true),
                ExplicitUrl = new Patch<string?>("/handbook"),
            },
            cancellationToken);

        var routes = await _bench.Context.PageRoutes
            .AsNoTracking()
            .Where(route => route.Url == "/handbook")
            .ToListAsync(cancellationToken);

        // Three rows share the URL and only one of them is published: the live page's own draft
        // route sits there too, which is the ordinary state of any published page.
        routes.Should().HaveCount(3);
        routes.Should().ContainSingle(route => route.IsPublished)
            .Which.PageId.Should().Be(live.Summary.Id);
        routes.Should().ContainSingle(route => !route.IsPublished && route.PageId == replacement.Summary.Id);
    }

    [Fact]
    public async Task RecyclingAPageWithdrawsItsPublicUrlAndRestoringDoesNotBringItBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var section = await _bench.AddPageAsync(template, "Archive", cancellationToken);
        var child = await _bench.AddPageAsync(template, "2025", cancellationToken, section.Summary.Id);

        var publishing = _bench.Resolve<IPublishingService>();
        (await publishing.PublishAsync(section.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();
        (await publishing.PublishAsync(child.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        (await _bench.Resolve<IRecycleBinService>().DeleteAsync(section.Summary.Id, cancellationToken))
            .IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        var resolver = _bench.Resolve<IRouteResolver>();
        (await resolver.ResolveAsync("/archive", cancellationToken)).Kind.Should().Be(RouteResolutionKind.NotFound);
        (await resolver.ResolveAsync("/archive/2025", cancellationToken))
            .Kind.Should().Be(RouteResolutionKind.NotFound);

        (await _bench.Resolve<IRecycleBinService>().RestoreAsync(section.Summary.Id, cancellationToken))
            .IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        // Restored as drafts, so the public URLs stay 404 until somebody publishes them again — the
        // routes must not resurrect content nobody has looked at (spec section 14.10).
        (await resolver.ResolveAsync("/archive", cancellationToken)).Kind.Should().Be(RouteResolutionKind.NotFound);
        (await RoutesOfAsync(section.Summary.Id, cancellationToken))
            .Should().ContainSingle().Which.IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task ANonCanonicalSpellingResolvesToThePageAndAsksForARedirectToTheCanonicalUrl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Contact", cancellationToken);

        (await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        var resolver = _bench.Resolve<IRouteResolver>();

        var exact = await resolver.ResolveAsync("/contact", cancellationToken);
        exact.Kind.Should().Be(RouteResolutionKind.Page);
        exact.CanonicalUrl.Should().BeNull("the request already used the canonical form");

        var sloppy = await resolver.ResolveAsync("/Contact/", cancellationToken);
        sloppy.Kind.Should().Be(RouteResolutionKind.Page);
        sloppy.CanonicalUrl.Should().Be("/contact", "the same content must not answer at two addresses");
    }

    /// <summary>Applies a metadata patch and fails the test loudly if it was refused.</summary>
    private async Task PatchAsync(
        int pageId,
        PatchPageMetadataRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _bench.Resolve<IPageService>().PatchMetadataAsync(
            pageId,
            request,
            null,
            cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));

        _bench.Context.ChangeTracker.Clear();
    }

    /// <summary>Reads a page's stored route rows.</summary>
    private Task<List<Data.Models.Cms.PageRoute>> RoutesOfAsync(
        int pageId,
        CancellationToken cancellationToken) =>
        _bench.Context.PageRoutes
            .AsNoTracking()
            .Where(route => route.PageId == pageId)
            .OrderBy(route => route.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Reads the URL a page is publicly served at, or null when it is not.</summary>
    private Task<string?> PublishedUrlAsync(int pageId, CancellationToken cancellationToken) =>
        _bench.Context.PageRoutes
            .AsNoTracking()
            .Where(route => route.PageId == pageId && route.IsPublished)
            .Select(route => route.Url)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Renders a refusal's diagnostics into an assertion message.</summary>
    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
}
