using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;
using ContentManagementSystem.TestSupport;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Data.Tests.Cms;

/// <summary>
/// Asserts the rows the CMS cannot start without arrive with the migration (task P1-07).
/// </summary>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class CmsSeedDataTests(SqlServerFixture fixture)
{
    [Test]
    public async Task MigratingCreatesTheSingleSiteSettingsRow()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var settings = await context.SiteSettings.SingleAsync(cancellationToken);

        settings.Id.Should().Be(SiteSettings.SingletonId);
        settings.Culture.Should().Be("en-US", "localization is out of scope for v1 (Q1)");
        settings.WorkflowMode.Should().Be(WorkflowMode.None);
        settings.VersionRetentionDays.Should().Be(0, "no history is pruned until someone sets a policy");
    }

    [Test]
    public async Task ASecondSiteSettingsRowIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        // "There is only ever one row" is enforced by a check constraint rather than convention,
        // because otherwise "the site's culture" quietly becomes a question with two answers.
        var insert = async () => await context.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO SiteSettings (Id, SiteName, Culture, TimeZoneId, WorkflowMode,
                                       VersionRetentionDays, CreatedBy, ModifiedBy)
             VALUES (2, 'Second site', 'en-US', 'UTC', 0, 0, 0, 0)
             """,
            cancellationToken);

        await insert.Should().ThrowAsync<SqlException>()
            .Where(e => e.Message.Contains("CK_SiteSettings_SingleRow"));
    }

    [Test]
    public async Task MigratingCreatesTheBuiltInRawHtmlBlockType()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var blockType = await context.BlockTypes
            .Include(b => b.Properties)
            .Include(b => b.Revisions)
            .SingleAsync(b => b.Key == CmsSeedData.RawHtmlBlockTypeKey, cancellationToken);

        blockType.IsBuiltIn.Should().BeTrue();
        blockType.CurrentRevision.Should().Be(1);

        blockType.Properties.Should().ContainSingle()
            .Which.FieldTypeKey.Should().Be("html");

        // The captured revision has to exist from the start, or content authored against the
        // built-in type has no schema snapshot to render through.
        blockType.Revisions.Should().ContainSingle()
            .Which.RevisionNumber.Should().Be(1);
    }

    [Test]
    public async Task SeedRowsAreNotDuplicatedWhenMigrationsRunAgain()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var databaseName = $"cms_seed_{Guid.NewGuid():N}";

        await using (var first = await fixture.CreateDatabaseAsync(databaseName, cancellationToken))
        {
            await first.Database.MigrateAsync(cancellationToken);
        }

        // The Aspire ef-migrations resource runs on every start, so a second pass over an
        // already-migrated database is the normal case, not an edge one.
        await using var second = fixture.CreateContext(databaseName);
        await second.Database.MigrateAsync(cancellationToken);

        (await second.SiteSettings.CountAsync(cancellationToken)).Should().Be(1);
        (await second.BlockTypes.CountAsync(cancellationToken)).Should().Be(1);
        (await second.BlockTypeProperties.CountAsync(cancellationToken)).Should().Be(1);
        (await second.BlockTypeRevisions.CountAsync(cancellationToken)).Should().Be(1);
    }
}
