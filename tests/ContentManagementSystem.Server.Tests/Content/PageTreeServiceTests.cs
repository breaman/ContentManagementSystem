using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// The materialized path and the rules that keep it honest (task P2-05).
/// </summary>
/// <remarks>
/// Driven against a real database because the thing under test is a denormalisation: what makes it
/// correct is that a prefix query over the stored column returns the same subtree the parent
/// pointers describe, and there is no way to observe that without both.
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class PageTreeServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task AttachingBuildsThePathFromTheParentAndTheNewIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tree = scope.ServiceProvider.GetRequiredService<IPageTreeService>();

        var root = await AddPageAsync(context, tree, parentId: null, "home", cancellationToken);
        var child = await AddPageAsync(context, tree, root.Id, "about", cancellationToken);
        var grandchild = await AddPageAsync(context, tree, child.Id, "team", cancellationToken);

        root.Path.Should().Be($"/{root.Id}/");
        root.Depth.Should().Be(0);
        grandchild.Path.Should().Be($"/{root.Id}/{child.Id}/{grandchild.Id}/");
        grandchild.Depth.Should().Be(2);
    }

    [Fact]
    public async Task APathOnlyMatchesWholeAncestors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tree = scope.ServiceProvider.GetRequiredService<IPageTreeService>();

        var first = await AddPageAsync(context, tree, parentId: null, "one", cancellationToken);
        var child = await AddPageAsync(context, tree, first.Id, "one-child", cancellationToken);

        // The trailing slash is the whole reason the format has one. Without it, page 1's prefix
        // would match pages 10 through 19 as well, and a move would drag half the site with it.
        var lookalikes = await context.Pages
            .Where(p => p.Path.StartsWith(first.Path) && p.Id != first.Id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        lookalikes.Should().Equal(child.Id);
    }

    [Fact]
    public async Task MovingASubtreeRewritesEveryDescendantPathAndDepth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tree = scope.ServiceProvider.GetRequiredService<IPageTreeService>();

        var products = await AddPageAsync(context, tree, parentId: null, "products", cancellationToken);
        var archive = await AddPageAsync(context, tree, parentId: null, "archive", cancellationToken);
        var widget = await AddPageAsync(context, tree, products.Id, "widget", cancellationToken);
        var spec = await AddPageAsync(context, tree, widget.Id, "spec", cancellationToken);

        var outcome = await tree.MoveAsync(widget, archive.Id, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        outcome.Should().Be(PageMoveOutcome.Moved);
        widget.ParentId.Should().Be(archive.Id);
        widget.Path.Should().Be($"/{archive.Id}/{widget.Id}/");

        // The descendant is the point: its ancestry prefix changed, its own shape did not.
        spec.Path.Should().Be($"/{archive.Id}/{widget.Id}/{spec.Id}/");
        spec.Depth.Should().Be(2);
    }

    [Fact]
    public async Task ADeletedDescendantMovesWithItsSubtree()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tree = scope.ServiceProvider.GetRequiredService<IPageTreeService>();

        var products = await AddPageAsync(context, tree, parentId: null, "products", cancellationToken);
        var archive = await AddPageAsync(context, tree, parentId: null, "archive", cancellationToken);
        var widget = await AddPageAsync(context, tree, products.Id, "widget", cancellationToken);
        var retired = await AddPageAsync(context, tree, widget.Id, "retired", cancellationToken);

        retired.IsDeleted = true;
        await context.SaveChangesAsync(cancellationToken);

        await tree.MoveAsync(widget, archive.Id, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        var stored = await context.Pages
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Id == retired.Id, cancellationToken);

        // Leaving it behind would mean restoring it later put it back into a branch that no longer
        // exists — the recycle bin would hand back a page nothing can reach (spec section 14.10).
        stored.Path.Should().Be($"/{archive.Id}/{widget.Id}/{retired.Id}/");
    }

    [Fact]
    public async Task APageCannotBeMovedUnderItsOwnDescendant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tree = scope.ServiceProvider.GetRequiredService<IPageTreeService>();

        var products = await AddPageAsync(context, tree, parentId: null, "products", cancellationToken);
        var widget = await AddPageAsync(context, tree, products.Id, "widget", cancellationToken);

        // The failure this prevents is silent: the subtree would still be in the table, reachable
        // from nothing, and no query would report it missing.
        (await tree.MoveAsync(products, widget.Id, cancellationToken))
            .Should().Be(PageMoveOutcome.WouldCreateCycle);

        (await tree.MoveAsync(products, products.Id, cancellationToken))
            .Should().Be(PageMoveOutcome.WouldCreateCycle);

        products.ParentId.Should().BeNull("a refused move changes nothing");
    }

    [Fact]
    public async Task MovingUnderAPageThatIsNotThereIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tree = scope.ServiceProvider.GetRequiredService<IPageTreeService>();

        var page = await AddPageAsync(context, tree, parentId: null, "home", cancellationToken);
        var deleted = await AddPageAsync(context, tree, parentId: null, "old-section", cancellationToken);

        deleted.IsDeleted = true;
        await context.SaveChangesAsync(cancellationToken);

        (await tree.MoveAsync(page, 999_999, cancellationToken))
            .Should().Be(PageMoveOutcome.ParentNotFound);

        // A deleted page is not an available parent either: adopting one would put a live subtree
        // under a page sitting in the recycle bin.
        (await tree.MoveAsync(page, deleted.Id, cancellationToken))
            .Should().Be(PageMoveOutcome.ParentNotFound);
    }

    /// <summary>
    /// Inserts a page and attaches it to the tree the way the creating transaction will.
    /// </summary>
    private static async Task<Page> AddPageAsync(
        ApplicationDbContext context,
        IPageTreeService tree,
        int? parentId,
        string slug,
        CancellationToken cancellationToken)
    {
        var templateId = await context.Templates
            .Select(template => template.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (templateId == 0)
        {
            var template = new Template { Key = "landing", Name = "Landing page", CurrentRevision = 1 };
            context.Templates.Add(template);
            await context.SaveChangesAsync(cancellationToken);
            templateId = template.Id;
        }

        var page = new Page
        {
            PublicId = Guid.NewGuid(),
            Slug = slug,
            Path = string.Empty,
            ParentId = parentId,
            TemplateId = templateId,
        };

        context.Pages.Add(page);
        await context.SaveChangesAsync(cancellationToken);

        // The path contains the page's own id, so it is set in a second statement — the same shape
        // as the draft pointer (spec section 23.5).
        await tree.AttachAsync(page, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return page;
    }
}
