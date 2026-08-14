using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

// Both the EF entity and the extracted value type are called ContentReference, deliberately: spec
// sections 7 and 23.2 both use the name for what is the same edge at two stages of its life.
using ContentReferenceTargetType = ContentManagementSystem.Shared.Contracts.Fields.ContentReferenceTargetType;

namespace ContentManagementSystem.Data.Tests.Cms;

/// <summary>
/// Asserts the storage guarantees the versioning model depends on (tasks P2-01 to P2-04).
/// </summary>
/// <remarks>
/// Every one of these is a behaviour SQL Server provides and the in-memory provider does not:
/// filtered and unique indexes, <c>rowversion</c> conflicts, cascade behaviour, and the interaction
/// between a soft delete and a global query filter. Asserting them against a fake would be
/// asserting that the fake works.
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class PageSchemaTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task APageAndItsFirstDraftAreInsertedInOneTransaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "home", cancellationToken);

        // Page and PageVersion reference each other, so the draft pointer is set by a second
        // statement inside the same transaction (spec section 23.5). What matters is that the pair
        // is consistent once the transaction commits.
        var stored = await context.Pages
            .Include(p => p.DraftVersion)
            .SingleAsync(p => p.Id == page.Id, cancellationToken);

        stored.DraftVersionId.Should().NotBeNull();
        stored.DraftVersion!.PageId.Should().Be(page.Id);
        stored.DraftVersion.Status.Should().Be(PageVersionStatus.Draft);
        stored.PublishedVersionId.Should().BeNull("a page is not published by being created");
    }

    [Fact]
    public async Task TwoVersionsOfOnePageCannotShareAVersionNumber()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "home", cancellationToken);

        // The number is computed from the current maximum, so two concurrent publishes race for it.
        // The unique index is what turns that race into a failed save rather than a history in
        // which "restore version 2" is ambiguous.
        context.PageVersions.Add(CreateVersion(page, versionNumber: 1, PageVersionStatus.Published));

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TwoPagesCannotShareAPublicIdentifier()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var first = await CreatePageAsync(context, "home", cancellationToken);
        var second = await CreatePageAsync(context, "about", cancellationToken);

        second.PublicId = first.PublicId;

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AStrayRemoveRetiresThePageInsteadOfDestroyingItsHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "home", cancellationToken);

        // Loaded on purpose. EF resolves a severed required relationship the moment Remove is
        // called unless cascades are deferred to SaveChanges, so with the versions in the change
        // tracker the net used to be bypassed entirely — the call threw before any override could
        // rewrite it, and the same call against an unloaded page was caught. A safety net whose
        // behaviour depends on what happens to be loaded is not one.
        await context.Entry(page).Collection(p => p.Versions).LoadAsync(cancellationToken);

        // Nothing in the CMS is supposed to call Remove on a page — RecycleBinService does the
        // subtree walk and sets the flag itself. This is the net under that rule (task P2-04): the
        // version history a soft delete exists to preserve must survive somebody forgetting it.
        context.Pages.Remove(page);
        await context.SaveChangesAsync(cancellationToken);

        var stored = await context.Pages
            .IgnoreQueryFilters()
            .Include(p => p.Versions)
            .SingleAsync(p => p.Id == page.Id, cancellationToken);

        stored.IsDeleted.Should().BeTrue();
        stored.DeletedOn.Should().NotBeNull();
        stored.Versions.Should().ContainSingle("the draft is what a restore has to come back to");
    }

    [Fact]
    public async Task ADeletedPageIsHiddenFromOrdinaryQueriesButItsHistoryStaysReachable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "home", cancellationToken);

        page.IsDeleted = true;
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        (await context.Pages.AnyAsync(p => p.Id == page.Id, cancellationToken))
            .Should().BeFalse("the global query filter excludes deleted pages");

        (await context.Pages.IgnoreQueryFilters().AnyAsync(p => p.Id == page.Id, cancellationToken))
            .Should().BeTrue("the recycle bin asks for them explicitly");

        // PageVersion deliberately carries no filter of its own. A deleted page's history is the
        // thing the recycle bin exists to preserve, so it must not disappear with the page.
        (await context.PageVersions.CountAsync(v => v.PageId == page.Id, cancellationToken))
            .Should().Be(1);
    }

    [Fact]
    public async Task ADraftSavedTwiceFromStaleStateFailsRatherThanOverwriting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databaseName = $"cms_conc_{Guid.NewGuid():N}";
        await using var context = await fixture.CreateDatabaseAsync(databaseName, cancellationToken);

        var page = await CreatePageAsync(context, "home", cancellationToken);

        // Two editors, each having loaded the draft before the other saved.
        await using var elena = fixture.CreateContext(databaseName);
        await using var marcus = fixture.CreateContext(databaseName);

        var elenasCopy = await elena.PageVersions.SingleAsync(v => v.Id == page.DraftVersionId, cancellationToken);
        var marcusCopy = await marcus.PageVersions.SingleAsync(v => v.Id == page.DraftVersionId, cancellationToken);

        elenasCopy.Title = "Elena's title";
        await elena.SaveChangesAsync(cancellationToken);

        marcusCopy.Title = "Marcus's title";

        var save = async () => await marcus.SaveChangesAsync(cancellationToken);

        // The authoritative concurrency layer (spec section 11.8). Edit locks are only advisory, so
        // this has to hold whether or not anybody acquired one.
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task EditLocksAreNotWrittenToTheAuditLog()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "home", cancellationToken);

        var editor = new User { UserName = "elena", NormalizedUserName = "ELENA" };
        context.Users.Add(editor);
        await context.SaveChangesAsync(cancellationToken);

        var auditedBefore = await context.AuditLogs.CountAsync(cancellationToken);

        context.EditLocks.Add(new EditLock
        {
            PageId = page.Id,
            UserId = editor.Id,
            AcquiredOn = DateTimeOffset.UtcNow,
            HeartbeatOn = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        // Written every 30 seconds per open editor. Auditing them would grow the audit table
        // without bound while recording nothing anybody would ever ask about (spec section 23.5).
        // This is the first excluded table to exist, so it is the first chance to assert the
        // exclusion registered back in task P1-05.
        (await context.AuditLogs.CountAsync(cancellationToken)).Should().Be(auditedBefore);
    }

    [Fact]
    public async Task ContentReferencesAreNotWrittenToTheAuditLog()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "home", cancellationToken);
        var auditedBefore = await context.AuditLogs.CountAsync(cancellationToken);

        context.ContentReferences.Add(new ContentReference
        {
            SourceType = ContentSourceType.PageVersion,
            SourceVersionId = page.DraftVersionId!.Value,
            TargetType = Shared.Contracts.Fields.ContentReferenceTargetType.Page,
            TargetId = page.Id,
            ZoneKey = "body",
        });

        await context.SaveChangesAsync(cancellationToken);

        // Deleted and reinserted wholesale on every draft save, which is every twenty seconds per
        // open editor — the same argument that exempts the tables spec section 23.5 lists.
        (await context.AuditLogs.CountAsync(cancellationToken)).Should().Be(auditedBefore);
    }

    [Fact]
    public async Task TheLiveChildrenIndexIsFilteredToUndeletedPages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var filter = await context.Database
            .SqlQuery<string>(
                $"""
                 SELECT i.filter_definition AS Value
                 FROM sys.indexes i
                 JOIN sys.tables t ON t.object_id = i.object_id
                 WHERE t.name = 'Pages' AND i.name = 'IX_Pages_ParentId_SortOrder_Live'
                 """)
            .SingleAsync(cancellationToken);

        // The tree's own query never wants deleted rows, and the filter is what keeps them out of
        // the index rather than merely out of the results.
        filter.Should().Contain("IsDeleted").And.Contain("0");
    }

    /// <summary>
    /// Creates a template, a page, and the page's first draft the way the creating transaction will.
    /// </summary>
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

        var draft = CreateVersion(page, versionNumber: 1, PageVersionStatus.Draft);
        context.PageVersions.Add(draft);
        await context.SaveChangesAsync(cancellationToken);

        page.DraftVersionId = draft.Id;
        await context.SaveChangesAsync(cancellationToken);

        return page;
    }

    private static PageVersion CreateVersion(Page page, int versionNumber, PageVersionStatus status) =>
        new()
        {
            PageId = page.Id,
            VersionNumber = versionNumber,
            Status = status,
            Title = "Home",
            ContentJson = """{"templateKey":"landing","templateRevision":1,"zones":{}}""",
            TemplateId = page.TemplateId,
            TemplateRevision = 1,
        };
}
