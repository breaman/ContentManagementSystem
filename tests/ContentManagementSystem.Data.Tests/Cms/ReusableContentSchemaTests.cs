using ContentManagementSystem.Data.Configurations.Cms;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Data.Tests.Cms;

/// <summary>
/// The storage guarantees reusable content depends on (tasks P4-01 and P4-02).
/// </summary>
/// <remarks>
/// Each of these is a behaviour SQL Server provides and the in-memory provider does not: unique and
/// filtered indexes, <c>rowversion</c> conflicts, restrict-on-delete, and the interaction between a
/// soft delete and a global query filter. Asserting them against a fake would be asserting that the
/// fake works.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class ReusableContentSchemaTests(SqlServerFixture fixture)
{
    [Test]
    public async Task AnItemAndItsFirstDraftAreInsertedInOneTransaction()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var item = await CreateItemAsync(context, "site-footer", cancellationToken);

        // The item and its version reference each other, so the draft pointer is set by a second
        // statement inside the same transaction. What matters is that the pair is consistent once it
        // commits (spec section 23.5).
        var stored = await context.ReusableContents
            .Include(candidate => candidate.DraftVersion)
            .SingleAsync(candidate => candidate.Id == item.Id, cancellationToken);

        stored.DraftVersionId.Should().NotBeNull();
        stored.DraftVersion!.ReusableContentId.Should().Be(item.Id);
        stored.DraftVersion.Status.Should().Be(PageVersionStatus.Draft);
        stored.PublishedVersionId.Should().BeNull("an item is not published by being created");
    }

    [Test]
    public async Task TwoVersionsOfOneItemCannotShareAVersionNumber()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var item = await CreateItemAsync(context, "site-footer", cancellationToken);

        // A version number is what a pinned placement names (spec section 9.2), so two rows sharing
        // one would make a pin ambiguous — and ambiguous on the delivery path, for content somebody
        // pinned precisely because it must not change.
        context.ReusableContentVersions.Add(Version(item, versionNumber: 1, PageVersionStatus.Published));

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task TwoItemsCannotShareAKeyEvenWhenOneIsInTheRecycleBin()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var first = await CreateItemAsync(context, "site-footer", cancellationToken);

        first.IsDeleted = true;
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        // The unique index on Key is deliberately unfiltered, unlike the library index below. A
        // deleted item still owns its key, and letting a second item take it would make the first
        // unrestorable — a constraint failure the editor who took the key never saw coming.
        var create = async () => await CreateItemAsync(context, "site-footer", cancellationToken);
        await create.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task TheLibraryIndexIsFilteredToUndeletedItems()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var filter = await context.Database
            .SqlQuery<string>(
                $"""
                 SELECT i.filter_definition AS Value
                 FROM sys.indexes i
                 JOIN sys.tables t ON t.object_id = i.object_id
                 WHERE t.name = 'ReusableContents'
                   AND i.name = 'IX_ReusableContents_FolderId_Name_Live'
                 """)
            .SingleAsync(cancellationToken);

        // The library screen never wants deleted rows, and the filter is what keeps them out of the
        // index rather than merely out of the results.
        filter.Should().Contain("IsDeleted").And.Contain("0");
    }

    [Test]
    public async Task ADeletedItemIsHiddenFromOrdinaryQueriesButItsHistoryStaysReachable()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var item = await CreateItemAsync(context, "site-footer", cancellationToken);

        item.IsDeleted = true;
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        // The filter is what makes "deleting an item stops it rendering" true without the delivery
        // resolver having to remember to ask.
        (await context.ReusableContents.AnyAsync(candidate => candidate.Id == item.Id, cancellationToken))
            .Should().BeFalse();

        (await context.ReusableContents
                .IgnoreQueryFilters()
                .AnyAsync(candidate => candidate.Id == item.Id, cancellationToken))
            .Should().BeTrue("the recycle bin asks for them explicitly");

        // The versions carry no filter of their own: a deleted item's history is the thing the
        // recycle bin exists to preserve.
        (await context.ReusableContentVersions
                .CountAsync(version => version.ReusableContentId == item.Id, cancellationToken))
            .Should().Be(1);
    }

    [Test]
    public async Task ADraftSavedTwiceFromStaleStateFailsRatherThanOverwriting()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var databaseName = $"cms_ru_conc_{Guid.NewGuid():N}";
        await using var database = await fixture.CreateDatabaseAsync(databaseName, cancellationToken);
        var context = database.Context;

        var item = await CreateItemAsync(context, "site-footer", cancellationToken);

        // Two editors, each having loaded the item's draft before the other saved. Shared content
        // makes this more likely rather than less: they need not have been on the same screen.
        await using var elena = fixture.CreateContext(databaseName);
        await using var marcus = fixture.CreateContext(databaseName);

        var elenasCopy = await elena.ReusableContentVersions
            .SingleAsync(version => version.Id == item.DraftVersionId, cancellationToken);
        var marcusCopy = await marcus.ReusableContentVersions
            .SingleAsync(version => version.Id == item.DraftVersionId, cancellationToken);

        elenasCopy.ContentJson = """{"schemaVersion":1,"zones":{"content":{"type":"html","value":"E"}}}""";
        await elena.SaveChangesAsync(cancellationToken);

        marcusCopy.ContentJson = """{"schemaVersion":1,"zones":{"content":{"type":"html","value":"M"}}}""";

        var save = async () => await marcus.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Test]
    public async Task ABlockTypeCannotBeDeletedWhileAnItemIsShapedByIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        await CreateItemAsync(context, "site-footer", cancellationToken);

        // Cleared on purpose. With the item still tracked, EF resolves the severed required
        // relationship in memory and throws before a statement is sent — which proves something
        // about the change tracker and nothing about the database. What is under test here is the
        // constraint itself, so the delete has to actually reach SQL Server.
        context.ChangeTracker.Clear();

        var blockType = await context.BlockTypes
            .SingleAsync(candidate => candidate.Key == CmsSeedData.RawHtmlBlockTypeKey, cancellationToken);

        // The service layer refuses this; the foreign key is the backstop for the case where that
        // guard is bypassed, since an item whose block type row is gone has no schema to validate or
        // render against.
        context.BlockTypes.Remove(blockType);

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>Creates an item and its first draft the way the creating transaction will.</summary>
    private static async Task<ReusableContent> CreateItemAsync(
        ApplicationDbContext context,
        string key,
        CancellationToken cancellationToken)
    {
        var blockType = await context.BlockTypes
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Key == CmsSeedData.RawHtmlBlockTypeKey, cancellationToken);

        var item = new ReusableContent
        {
            Key = key,
            Name = key,
            BlockTypeId = blockType.Id,
        };

        context.ReusableContents.Add(item);
        await context.SaveChangesAsync(cancellationToken);

        var draft = Version(item, versionNumber: 1, PageVersionStatus.Draft);

        context.ReusableContentVersions.Add(draft);
        await context.SaveChangesAsync(cancellationToken);

        item.DraftVersionId = draft.Id;
        await context.SaveChangesAsync(cancellationToken);

        return item;
    }

    private static ReusableContentVersion Version(
        ReusableContent item,
        int versionNumber,
        PageVersionStatus status) =>
        new()
        {
            ReusableContentId = item.Id,
            VersionNumber = versionNumber,
            Status = status,
            ContentJson = """{"schemaVersion":1,"templateKey":"rawHtml","templateRevision":1,"zones":{}}""",
            BlockTypeRevision = 1,
        };
}
