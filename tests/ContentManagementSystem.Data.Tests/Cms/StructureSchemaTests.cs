using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.TestSupport;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Data.Tests.Cms;

/// <summary>
/// Asserts the uniqueness guarantees the content model depends on (task P1-02).
/// </summary>
/// <remarks>
/// Every index here protects an invariant that content payloads assume: a payload names its
/// template and its zones by key alone, so a duplicate key makes a stored payload ambiguous about
/// which schema it was authored against. These run against real SQL Server because an index is not
/// something the in-memory provider enforces.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class StructureSchemaTests(SqlServerFixture fixture)
{
    [Test]
    public async Task TemplateKeysAreUniqueAcrossTheSite()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        context.Templates.Add(CreateTemplate("marketing-landing"));
        await context.SaveChangesAsync(cancellationToken);

        context.Templates.Add(CreateTemplate("marketing-landing"));

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task ZoneKeysAreUniqueWithinATemplate()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var template = CreateTemplate("marketing-landing");
        template.Zones.Add(CreateZone("body"));
        context.Templates.Add(template);
        await context.SaveChangesAsync(cancellationToken);

        context.Zones.Add(CreateZone("body", template.Id));

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task TheSameZoneKeyMayAppearOnTwoDifferentTemplates()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var landing = CreateTemplate("marketing-landing");
        landing.Zones.Add(CreateZone("body"));

        var article = CreateTemplate("article");
        article.Zones.Add(CreateZone("body"));

        context.Templates.AddRange(landing, article);
        await context.SaveChangesAsync(cancellationToken);

        var bodyZones = await context.Zones
            .Where(z => z.Key == "body")
            .CountAsync(cancellationToken);

        bodyZones.Should().Be(2, "zone keys are scoped to their template, not global");
    }

    [Test]
    public async Task BlockTypePropertyKeysAreUniqueWithinABlockType()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var blockType = CreateBlockType("hero-banner");
        blockType.Properties.Add(CreateBlockTypeProperty("headline"));
        context.BlockTypes.Add(blockType);
        await context.SaveChangesAsync(cancellationToken);

        context.BlockTypeProperties.Add(CreateBlockTypeProperty("headline", blockType.Id));

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task BlockTypeKeysAreUniqueAcrossTheSite()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        context.BlockTypes.Add(CreateBlockType("hero-banner"));
        await context.SaveChangesAsync(cancellationToken);

        context.BlockTypes.Add(CreateBlockType("hero-banner"));

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task TemplateRevisionNumbersAreUniqueWithinATemplate()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var template = CreateTemplate("marketing-landing");
        context.Templates.Add(template);
        await context.SaveChangesAsync(cancellationToken);

        context.TemplateRevisions.Add(CreateTemplateRevision(template.Id, 1));
        await context.SaveChangesAsync(cancellationToken);

        // A page version pins itself to a revision number. Two rows sharing one would make that
        // pin resolve to two different schemas.
        context.TemplateRevisions.Add(CreateTemplateRevision(template.Id, 1));

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task DeletingATemplateWithZonesIsRefusedByTheDatabase()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var template = CreateTemplate("marketing-landing");
        template.Zones.Add(CreateZone("body"));
        context.Templates.Add(template);
        await context.SaveChangesAsync(cancellationToken);

        // The service layer blocks this long before EF sees it. The restricted foreign key is the
        // backstop: cascading would take the zone definitions with the template and leave existing
        // payloads with no schema to validate against.
        var delete = async () => await context.Database.ExecuteSqlAsync(
            $"DELETE FROM Templates WHERE Id = {template.Id}",
            cancellationToken);

        await delete.Should().ThrowAsync<SqlException>()
            .Where(e => e.Message.Contains("FK_Zones_Templates_TemplateId"));
    }

    private static Template CreateTemplate(string key) => new()
    {
        Key = key,
        Name = key,
    };

    private static Zone CreateZone(string key, int templateId = 0) => new()
    {
        TemplateId = templateId,
        Key = key,
        Name = key,
        FieldTypeKey = "richText",
    };

    private static BlockType CreateBlockType(string key) => new()
    {
        Key = key,
        Name = key,
    };

    private static BlockTypeProperty CreateBlockTypeProperty(string key, int blockTypeId = 0) => new()
    {
        BlockTypeId = blockTypeId,
        Key = key,
        Name = key,
        FieldTypeKey = "plainText",
    };

    private static TemplateRevision CreateTemplateRevision(int templateId, int revisionNumber) => new()
    {
        TemplateId = templateId,
        RevisionNumber = revisionNumber,
        ZoneSnapshotJson = """{"zones":[]}""",
    };
}
