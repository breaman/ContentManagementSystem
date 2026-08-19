using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Data.Tests.Cms;

/// <summary>
/// The storage guarantees navigation, tagging, search, and the outbox depend on
/// (task P8-14, spec sections 10.7, 17.1, and 16.3).
/// </summary>
/// <remarks>
/// Against real SQL Server, because every rule here is a database constraint rather than a check in
/// code — which is the point of writing them as constraints. The check constraint on a navigation
/// item in particular has no in-memory equivalent at all.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class DeliverySchemaTests(SqlServerFixture fixture)
{
    [Test]
    public async Task ANavigationItemMustPointAtEitherAPageOrAUrlAndNotBoth()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "careers", cancellationToken);
        var menu = await CreateMenuAsync(context, "footer", cancellationToken);

        context.NavigationItems.Add(new NavigationItem
        {
            NavigationMenuId = menu.Id,
            Label = "Careers",
            PageId = page.Id,
            ExternalUrl = "https://example.com/jobs",
        });

        // Two answers to "where does this go" is not repairable from the rendered page, so the
        // database refuses the row rather than letting a renderer pick.
        var both = async () => await context.SaveChangesAsync(cancellationToken);
        await both.Should().ThrowAsync<DbUpdateException>();

        context.ChangeTracker.Clear();

        context.NavigationItems.Add(new NavigationItem
        {
            NavigationMenuId = menu.Id,
            Label = "Nowhere",
        });

        var neither = async () => await context.SaveChangesAsync(cancellationToken);
        await neither.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task ANavigationItemMayPointAtEitherTargetOnItsOwn()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "privacy", cancellationToken);
        var menu = await CreateMenuAsync(context, "footer", cancellationToken);

        context.NavigationItems.Add(new NavigationItem
        {
            NavigationMenuId = menu.Id,
            Label = "Privacy",
            PageId = page.Id,
            SortOrder = 1,
        });

        context.NavigationItems.Add(new NavigationItem
        {
            NavigationMenuId = menu.Id,
            Label = "Status",
            ExternalUrl = "https://status.example.com",
            SortOrder = 2,
        });

        await context.SaveChangesAsync(cancellationToken);

        (await context.NavigationItems.CountAsync(cancellationToken)).Should().Be(2);
    }

    [Test]
    public async Task DeletingAMenuTakesItsItemsWithIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var menu = await CreateMenuAsync(context, "utility", cancellationToken);

        context.NavigationItems.Add(new NavigationItem
        {
            NavigationMenuId = menu.Id,
            Label = "Status",
            ExternalUrl = "https://status.example.com",
        });

        await context.SaveChangesAsync(cancellationToken);

        // The items are the menu: leaving them behind would leave rows nothing can reach.
        await context.NavigationMenus.Where(candidate => candidate.Id == menu.Id)
            .ExecuteDeleteAsync(cancellationToken);

        (await context.NavigationItems.CountAsync(cancellationToken)).Should().Be(0);
    }

    [Test]
    public async Task TwoTagsCannotShareASlug()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        context.Tags.Add(new Tag { Name = "Product", Slug = "product" });
        await context.SaveChangesAsync(cancellationToken);

        // Without this, "Product" and "product" are two tags filtering to two different sets of
        // pages that no editor can tell apart in a picker.
        context.Tags.Add(new Tag { Name = "product", Slug = "product" });

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task APageCannotCarryTheSameTagTwice()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "gearboxes", cancellationToken);
        var tag = new Tag { Name = "Product", Slug = "product" };

        context.Tags.Add(tag);
        await context.SaveChangesAsync(cancellationToken);

        context.PageTags.Add(new PageTag { PageId = page.Id, TagId = tag.Id });
        await context.SaveChangesAsync(cancellationToken);

        context.PageTags.Add(new PageTag { PageId = page.Id, TagId = tag.Id });

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task OneThingHasOneSearchDocument()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        context.SearchDocuments.Add(Document(SearchEntityKind.Page, 42, "Gearboxes"));
        await context.SaveChangesAsync(cancellationToken);

        // The uniqueness is what makes the indexer an upsert. Without it a page saved twice is two
        // results for one page, and neither of them is wrong enough to notice.
        context.SearchDocuments.Add(Document(SearchEntityKind.Page, 42, "Gearboxes"));

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();

        context.ChangeTracker.Clear();

        // The same id under a different kind is a different thing entirely.
        context.SearchDocuments.Add(Document(SearchEntityKind.Media, 42, "A photograph"));
        await context.SaveChangesAsync(cancellationToken);

        (await context.SearchDocuments.CountAsync(cancellationToken)).Should().Be(2);
    }

    [Test]
    public async Task AnOutboxMessageIsStoredWithABigIntKeyAndNoProcessedTimestamp()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = "cms.cache.invalidate",
            PayloadJson = """{"tags":["page:1"]}""",
            CreatedOn = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        var pending = await context.OutboxMessages
            .Where(message => message.ProcessedOn == null)
            .SingleAsync(cancellationToken);

        // The filtered index answers exactly this question, and the identity is bigint because this
        // table takes rows per publish and is pruned rather than kept.
        pending.Id.Should().BePositive();
        pending.AttemptCount.Should().Be(0);
    }

    private static SearchDocument Document(SearchEntityKind kind, int entityId, string title) =>
        new()
        {
            EntityType = kind,
            EntityId = entityId,
            Title = title,
            IsPublished = true,
            UpdatedOn = DateTimeOffset.UtcNow,
        };

    private static async Task<NavigationMenu> CreateMenuAsync(
        ApplicationDbContext context,
        string key,
        CancellationToken cancellationToken)
    {
        var menu = new NavigationMenu { Key = key, Name = key };

        context.NavigationMenus.Add(menu);
        await context.SaveChangesAsync(cancellationToken);

        return menu;
    }

    /// <summary>Inserts a page and its first draft, closing the mutual foreign key.</summary>
    private static async Task<Page> CreatePageAsync(
        ApplicationDbContext context,
        string slug,
        CancellationToken cancellationToken)
    {
        var template = new Template
        {
            Key = $"template-{Guid.NewGuid():N}",
            Name = "Landing page",
            CurrentRevision = 1,
        };

        context.Templates.Add(template);
        await context.SaveChangesAsync(cancellationToken);

        var page = new Page
        {
            PublicId = Guid.NewGuid(),
            Slug = slug,
            Path = "/",
            TemplateId = template.Id,
        };

        context.Pages.Add(page);
        await context.SaveChangesAsync(cancellationToken);

        page.Path = $"/{page.Id}/";

        var draft = new PageVersion
        {
            PageId = page.Id,
            VersionNumber = 1,
            Status = PageVersionStatus.Draft,
            Title = slug,
            ContentJson = "{}",
            TemplateId = page.TemplateId,
            TemplateRevision = 1,
        };

        context.PageVersions.Add(draft);
        await context.SaveChangesAsync(cancellationToken);

        page.DraftVersionId = draft.Id;
        await context.SaveChangesAsync(cancellationToken);

        return page;
    }
}
