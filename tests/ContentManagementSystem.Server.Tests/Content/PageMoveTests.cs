using System.Globalization;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// Moving a page in the tree (task P6-03, spec sections 10.4 and 14.2).
/// </summary>
/// <remarks>
/// Against a real database, because everything a move can get wrong is a database fact: a
/// materialized path rewritten for a subtree, a route table rebuilt beneath it, redirects emitted at
/// the vacated addresses, and — the part with no equivalent anywhere else in the codebase — a
/// transaction that is deliberately rolled back.
/// <para>
/// The preview is the case worth the most attention. It is the same code as the move, so the risk is
/// not that it computes the wrong answer; it is that it leaves something behind. Every preview test
/// here therefore asserts the database afterwards as well as the answer.
/// </para>
/// <para>
/// The fixtures publish. A route rebuild reports a change for a page that was <em>serving</em> the
/// old URL, so a tree of unpublished drafts would exercise the half of this that nobody has ever
/// linked to.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class PageMoveTests(SqlServerFixture fixture)
{
    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(
            fixture,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task MovingAPublishedSubtreeRewritesEveryUrlBeneathItAndLeavesRedirects()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var (about, pricing, enterprise) = await TreeAsync(cancellationToken);

        var moved = await Pages.MoveAsync(pricing, new MovePageRequest(about), cancellationToken);

        moved.IsSuccess.Should().BeTrue(Because(moved));

        var changes = moved.Value!.UrlChanges;

        changes.Should().HaveCount(2, "the page and its child both moved");
        changes[0].PageId.Should().Be(pricing, "the subject page is reported first");
        changes[0].OldUrl.Should().Be("/pricing");
        changes[0].NewUrl.Should().Be("/about/pricing");
        changes[0].Title.Should().Be("Pricing", "a confirmation names pages, not identities");

        changes.Should().ContainSingle(change => change.PageId == enterprise)
            .Which.NewUrl.Should().Be("/about/pricing/enterprise");

        moved.Value.RedirectCount.Should().Be(2, "both pages were published at their old addresses");

        _bench.Context.ChangeTracker.Clear();

        var redirects = await _bench.Context.Redirects
            .AsNoTracking()
            .Select(redirect => redirect.FromUrl)
            .ToListAsync(cancellationToken);

        redirects.Should().Contain("/pricing").And.Contain("/pricing/enterprise");
    }

    [Test]
    public async Task APreviewReportsWhatTheMoveWouldDoAndWritesNothing()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var (about, pricing, _) = await TreeAsync(cancellationToken);

        var preview = await Pages.MoveAsync(
            pricing,
            new MovePageRequest(about, Preview: true),
            cancellationToken);

        preview.IsSuccess.Should().BeTrue(Because(preview));
        preview.Value!.IsPreview.Should().BeTrue();
        preview.Value.UrlChanges.Should().HaveCount(2);

        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.Pages
            .AsNoTracking()
            .SingleAsync(page => page.Id == pricing, cancellationToken);

        stored.ParentId.Should().BeNull("a preview is rolled back, not committed");
        stored.Path.Should().Be($"/{pricing}/");

        var redirects = await _bench.Context.Redirects.AsNoTracking().CountAsync(cancellationToken);

        redirects.Should().Be(
            0,
            "a preview that leaves redirects behind has published a move nobody confirmed");

        var routes = await _bench.Context.PageRoutes
            .AsNoTracking()
            .Where(route => route.PageId == pricing && route.IsPublished)
            .Select(route => route.Url)
            .ToListAsync(cancellationToken);

        routes.Should().Equal("/pricing");
    }

    [Test]
    public async Task ThePreviewAndTheMoveAgreeOnWhatWillHappen()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var (about, pricing, _) = await TreeAsync(cancellationToken);

        var preview = await Pages.MoveAsync(
            pricing, new MovePageRequest(about, Preview: true), cancellationToken);
        var moved = await Pages.MoveAsync(
            pricing, new MovePageRequest(about), cancellationToken);

        // The whole reason the preview runs the real move and rolls it back. A confirmation that
        // promised two redirects and delivered twenty would be worse than no confirmation at all.
        moved.Value!.UrlChanges.Should().BeEquivalentTo(preview.Value!.UrlChanges);
    }

    [Test]
    public async Task AReorderAmongSiblingsChangesNoUrlAtAll()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken);

        var first = await _bench.AddPageAsync(template, "First", cancellationToken);
        var second = await _bench.AddPageAsync(template, "Second", cancellationToken);
        var third = await _bench.AddPageAsync(template, "Third", cancellationToken);

        var moved = await Pages.MoveAsync(
            third.Summary.Id,
            new MovePageRequest(ParentId: null, Position: 0),
            cancellationToken);

        moved.IsSuccess.Should().BeTrue(Because(moved));
        moved.Value!.UrlChanges.Should().BeEmpty(
            "a page's URL is built from the tree, not from its order among siblings — so a reorder " +
            "needs no confirmation and creates no redirect");

        _bench.Context.ChangeTracker.Clear();

        var order = await _bench.Context.Pages
            .AsNoTracking()
            .Where(page => page.ParentId == null)
            .OrderBy(page => page.SortOrder)
            .Select(page => page.Id)
            .ToListAsync(cancellationToken);

        order.Should().Equal(third.Summary.Id, first.Summary.Id, second.Summary.Id);
    }

    [Test]
    public async Task APageCannotBeMovedInsideItsOwnSubtree()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken);

        var parent = await _bench.AddPageAsync(template, "Parent", cancellationToken);
        var child = await _bench.AddPageAsync(
            template, "Child", cancellationToken, parent.Summary.Id);

        var refused = await Pages.MoveAsync(
            parent.Summary.Id,
            new MovePageRequest(child.Summary.Id),
            cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Invalid);
        refused.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.MoveWouldCreateCycle);

        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.Pages
            .AsNoTracking()
            .SingleAsync(page => page.Id == parent.Summary.Id, cancellationToken);

        stored.ParentId.Should().BeNull("a refused move leaves the tree exactly as it was");
    }

    [Test]
    public async Task AMoveIsRefusedWhenASiblingAtTheDestinationAlreadyUsesTheSlug()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken);

        var about = await _bench.AddPageAsync(template, "About", cancellationToken);

        // Two pages called Team: one already under About, one at the root wanting to join it.
        await _bench.AddPageAsync(template, "Team", cancellationToken, about.Summary.Id);
        var loose = await _bench.AddPageAsync(template, "Team", cancellationToken);

        var refused = await Pages.MoveAsync(
            loose.Summary.Id,
            new MovePageRequest(about.Summary.Id),
            cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Conflict);
        refused.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.SlugDuplicate);
    }

    [Test]
    public async Task MovingRequiresPermissionToEdit()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken);
        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);

        await using var viewer = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.ContentRead),
            cancellationToken);

        var refused = await viewer.Resolve<IPageService>().MoveAsync(
            page.Summary.Id,
            new MovePageRequest(ParentId: null, Position: 0),
            cancellationToken);

        // The endpoint policy is the door and this is the lock: a move reached from a CLI verb with
        // no HTTP request at all is subject to the same rule (spec section 20.4).
        refused.Outcome.Should().Be(CmsOutcome.Forbidden);
    }

    [Test]
    public async Task TheBackofficeFilterMatchesATitleASlugAndAPageId()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken);

        var enterprise = await _bench.AddPageAsync(template, "Enterprise plans", cancellationToken);
        await _bench.AddPageAsync(template, "About us", cancellationToken);

        (await FindAsync("enterprise", cancellationToken))
            .Should().ContainSingle().Which.Should().Be(enterprise.Summary.Id, "the title matches");

        (await FindAsync("enterprise-plans", cancellationToken))
            .Should().ContainSingle().Which.Should().Be(enterprise.Summary.Id, "the slug matches");

        // The case the tree's filter box exists for: an editor arrives holding an id from a log
        // line, a ticket, or a URL, and a filter that answered "no results" would read as the page
        // being gone (task P6-04).
        (await FindAsync(enterprise.Summary.Id.ToString(CultureInfo.InvariantCulture), cancellationToken))
            .Should().ContainSingle().Which.Should().Be(enterprise.Summary.Id);

        (await FindAsync("nothing-like-this", cancellationToken)).Should().BeEmpty();
    }

    /// <summary>Runs the backoffice filter and returns the identities it matched.</summary>
    private async Task<IReadOnlyList<int>> FindAsync(string term, CancellationToken cancellationToken)
    {
        var found = await Pages.ListAsync(new PageQuery(Search: term), cancellationToken);

        found.IsSuccess.Should().BeTrue(Because(found));

        return [.. found.Value!.Items.Select(page => page.Id)];
    }

    /// <summary>The page service under test, resolved from the real container.</summary>
    private IPageService Pages => _bench.Resolve<IPageService>();

    /// <summary>
    /// Builds <c>/about</c>, <c>/pricing</c>, and <c>/pricing/enterprise</c>, all published.
    /// </summary>
    /// <returns>The identities of About, Pricing, and Enterprise.</returns>
    private async Task<(int About, int Pricing, int Enterprise)> TreeAsync(
        CancellationToken cancellationToken)
    {
        var template = await _bench.AddTemplateAsync("landing", cancellationToken);
        var publishing = _bench.Resolve<IPublishingService>();

        var about = await _bench.AddPageAsync(template, "About", cancellationToken);
        var pricing = await _bench.AddPageAsync(template, "Pricing", cancellationToken);
        var enterprise = await _bench.AddPageAsync(
            template, "Enterprise", cancellationToken, pricing.Summary.Id);

        foreach (var id in new[] { about.Summary.Id, pricing.Summary.Id, enterprise.Summary.Id })
        {
            var published = await publishing.PublishAsync(id, cancellationToken: cancellationToken);

            published.IsSuccess.Should().BeTrue(Because(published));
        }

        return (about.Summary.Id, pricing.Summary.Id, enterprise.Summary.Id);
    }

    /// <summary>Renders a failed result's diagnostics into the assertion message.</summary>
    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(diagnostic => diagnostic.Message));
}
