using System.Text.Json;

using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.HealthChecks;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContentManagementSystem.Server.Tests.Structure;

/// <summary>A template declared in code, for the reconciler to find (tasks P1-25).</summary>
/// <remarks>
/// An ordinary class rather than a Razor component. The reconciler reads an attribute; nothing about
/// it requires the declaring type to be renderable, and a component here would drag the whole Blazor
/// test host into a test about database rows.
/// </remarks>
[CmsTemplate(ReconciledKeys.Template, "Reconciled landing page", Description = "From code.", SortOrder = 7)]
public sealed class ReconciledTemplateComponent;

/// <summary>A block type declared in code.</summary>
[CmsBlockType(ReconciledKeys.BlockType, "Reconciled quote", IconKey = "quote")]
public sealed class ReconciledBlockTypeComponent;

/// <summary>Keys the fixtures above declare, so the assertions cannot drift from them.</summary>
public static class ReconciledKeys
{
    /// <summary>Key of the code-declared template.</summary>
    public const string Template = "reconciled-landing";

    /// <summary>Key of the code-declared block type.</summary>
    public const string BlockType = "reconciled-quote";
}

/// <summary>
/// Startup reconciliation, schema sync, and the health check that reports on both
/// (tasks P1-25, P1-26, P1-27).
/// </summary>
/// <remarks>
/// Driven against a real database because every rule under test is about the difference between what
/// code declares and what rows exist, and there is no such thing as that difference in a fake.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class StructureStartupTests(SqlServerFixture fixture)
{
    private CmsApplicationFactory _factory = null!;
    private string _schemaDirectory = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync()
    {
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current!.Execution.CancellationToken);
        _schemaDirectory = Path.Combine(Path.GetTempPath(), $"cms-schema-{Guid.NewGuid():N}");
    }

    [After(HookType.Test)]
    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();

        if (Directory.Exists(_schemaDirectory)) Directory.Delete(_schemaDirectory, recursive: true);
    }

    [Test]
    public async Task ReconciliationCreatesWhatCodeDeclaresAndOrphansWhatItDoesNot()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // A template the database holds and no component declares. This is the shape of a bad
        // deployment: the component was removed, or never shipped.
        context.Templates.Add(new Template
        {
            Key = "database-only",
            Name = "Database only",
            CurrentRevision = 1,
            IsOrphaned = false,
        });

        await context.SaveChangesAsync(cancellationToken);

        var report = await Reconciler(context).ReconcileAsync(cancellationToken);

        report.TemplatesCreated.Should().Contain(ReconciledKeys.Template);
        report.BlockTypesCreated.Should().Contain(ReconciledKeys.BlockType);
        report.TemplatesOrphaned.Should().Contain("database-only");

        var created = await context.Templates
            .Include(template => template.Revisions)
            .SingleAsync(template => template.Key == ReconciledKeys.Template, cancellationToken);

        created.Name.Should().Be("Reconciled landing page");
        created.SortOrder.Should().Be(7);
        created.IsOrphaned.Should().BeFalse();
        // Stored without the version, so a rebuild does not rewrite the column on every row.
        created.ComponentTypeName.Should().Be(
            $"{typeof(ReconciledTemplateComponent).FullName}, ContentManagementSystem.Server.Tests");
        // A page created the moment after this must have a revision to capture.
        created.Revisions.Should().ContainSingle();

        var orphaned = await context.Templates.SingleAsync(t => t.Key == "database-only", cancellationToken);

        // Marked, never deleted: dropping it would take its zone definitions, and with them the
        // ability to read payloads already stored against it (spec section 8.4).
        orphaned.IsOrphaned.Should().BeTrue();
    }

    [Test]
    public async Task ReconciliationIsIdempotentAndLeavesEditedNamesAlone()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Reconciler(context).ReconcileAsync(cancellationToken);

        var template = await context.Templates
            .SingleAsync(candidate => candidate.Key == ReconciledKeys.Template, cancellationToken);

        template.Name = "Renamed by an editor";
        await context.SaveChangesAsync(cancellationToken);

        var second = await Reconciler(context).ReconcileAsync(cancellationToken);

        second.HasChanges.Should().BeFalse();

        await context.Entry(template).ReloadAsync(cancellationToken);

        // The attribute's name is an initial value, not a source of truth. Rewriting it every
        // startup would silently undo a rename after each deploy.
        template.Name.Should().Be("Renamed by an editor");
    }

    [Test]
    public async Task ReconciliationAdoptsATemplateWhoseComponentComesBack()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Templates.Add(new Template
        {
            Key = ReconciledKeys.Template,
            Name = "Created in the backoffice ahead of its markup",
            CurrentRevision = 1,
            IsOrphaned = true,
        });

        await context.SaveChangesAsync(cancellationToken);

        var report = await Reconciler(context).ReconcileAsync(cancellationToken);

        report.TemplatesAdopted.Should().Contain(ReconciledKeys.Template);
        report.TemplatesCreated.Should().NotContain(ReconciledKeys.Template);

        var template = await context.Templates
            .SingleAsync(candidate => candidate.Key == ReconciledKeys.Template, cancellationToken);

        template.IsOrphaned.Should().BeFalse();
    }

    [Test]
    public async Task TheBuiltInBlockTypeIsNeverOrphaned()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Reconciler(context).ReconcileAsync(cancellationToken);

        var builtIn = await context.BlockTypes.SingleAsync(candidate => candidate.IsBuiltIn, cancellationToken);

        // It is declared by the system, not by a scanned attribute. Orphaning it would degrade the
        // health check on every fresh install.
        builtIn.IsOrphaned.Should().BeFalse();
    }

    [Test]
    public async Task TheHealthCheckDegradesOnlyOnceAnOrphanedTemplateHasAPage()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var health = _factory.Services.GetRequiredService<HealthCheckService>();

        var clean = await health.CheckHealthAsync(
            registration => registration.Name == CmsTemplatesHealthCheck.Name,
            cancellationToken);

        clean.Entries[CmsTemplatesHealthCheck.Name].Status.Should().Be(HealthStatus.Healthy);

        var orphan = new Template
        {
            Key = "health-orphan",
            Name = "Health orphan",
            CurrentRevision = 1,
            IsOrphaned = true,
        };

        context.Templates.Add(orphan);
        await context.SaveChangesAsync(cancellationToken);

        var unused = await health.CheckHealthAsync(
            registration => registration.Name == CmsTemplatesHealthCheck.Name,
            cancellationToken);

        // An orphan nobody has built on is housekeeping, not an operational matter — and a template
        // created in the backoffice ahead of its markup is orphaned by design (task P1-21), so
        // degrading on that would train an operator to ignore this check (task P2-01).
        unused.Entries[CmsTemplatesHealthCheck.Name].Status.Should().Be(HealthStatus.Healthy);

        var page = new Page
        {
            PublicId = Guid.NewGuid(),
            Slug = "stranded",
            Path = "/",
            TemplateId = orphan.Id,
        };

        context.Pages.Add(page);
        await context.SaveChangesAsync(cancellationToken);

        var degraded = await health.CheckHealthAsync(
            registration => registration.Name == CmsTemplatesHealthCheck.Name,
            cancellationToken);

        var entry = degraded.Entries[CmsTemplatesHealthCheck.Name];

        // Degraded, never unhealthy: a bad deployment has to be visible without taking down a site
        // whose other pages render perfectly well (spec section 8.4).
        entry.Status.Should().Be(HealthStatus.Degraded);
        entry.Description.Should().Contain("health-orphan");
        entry.Data["orphanedTemplates"].Should().BeEquivalentTo(new[] { "health-orphan" });

        page.IsDeleted = true;
        await context.SaveChangesAsync(cancellationToken);

        var recycled = await health.CheckHealthAsync(
            registration => registration.Name == CmsTemplatesHealthCheck.Name,
            cancellationToken);

        // A page in the recycle bin is not being served, so it does not make the orphan urgent.
        // Restoring it starts the check reporting again.
        recycled.Entries[CmsTemplatesHealthCheck.Name].Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task TheSchemaSyncCreatesRecordsAndIsIdempotent()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await WriteAsync("composition.sync-spacing.json", new SchemaDocument(
            SchemaKind.Composition,
            "sync-spacing",
            "Spacing",
            Slots: [new SchemaSlot("marginTop", "Margin top", FieldTypeKeys.Number)]));

        await WriteAsync("block-type.sync-quote.json", new SchemaDocument(
            SchemaKind.BlockType,
            "sync-quote",
            "Quote",
            Slots: [new SchemaSlot("attribution", "Attribution", FieldTypeKeys.PlainText)],
            Compositions: ["sync-spacing"]));

        await WriteAsync("template.sync-landing.json", new SchemaDocument(
            SchemaKind.Template,
            "sync-landing",
            "Landing",
            Slots:
            [
                new SchemaSlot("hero", "Hero", FieldTypeKeys.PlainText, IsRequired: true),
                new SchemaSlot("body", "Body", FieldTypeKeys.RichText, SortOrder: 1),
            ]));

        await using var scope = _factory.Services.CreateAsyncScope();
        var sync = scope.ServiceProvider.GetRequiredService<ISchemaSyncService>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var first = await sync.ApplyAsync(_schemaDirectory, cancellationToken);

        first.Errors.Should().BeEmpty();
        first.HasPendingWork.Should().BeTrue();
        first.Changes.Where(c => c.Change is SchemaChangeKind.Refused).Should().BeEmpty();

        var template = await context.Templates
            .Include(candidate => candidate.Zones)
            .SingleAsync(candidate => candidate.Key == "sync-landing", cancellationToken);

        template.Zones.Should().HaveCount(2);
        template.Zones.Single(zone => zone.Key == "hero").IsRequired.Should().BeTrue();
        // One revision for the whole file, not one per zone.
        template.CurrentRevision.Should().Be(1);

        var blockType = await context.BlockTypes
            .Include(candidate => candidate.Properties)
            .Include(candidate => candidate.Compositions)
                .ThenInclude(binding => binding.Composition)
                    .ThenInclude(composition => composition.Properties)
            .SingleAsync(candidate => candidate.Key == "sync-quote", cancellationToken);

        blockType.Properties.Should().ContainSingle();
        // The composition was created by an earlier file in the same pass, which is what applying in
        // dependency order buys: one commit can add a group and the block type that composes it.
        blockType.Compositions.Should().ContainSingle();

        var second = await sync.ApplyAsync(_schemaDirectory, cancellationToken);

        second.HasPendingWork.Should().BeFalse();
        second.HasProblems.Should().BeFalse();
    }

    [Test]
    public async Task TheSchemaSyncRefusesToRetypeAnExistingSlotAndKeepsUnlistedOnes()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await WriteAsync("template.retype.json", new SchemaDocument(
            SchemaKind.Template,
            "retype",
            "Retype",
            Slots:
            [
                new SchemaSlot("body", "Body", FieldTypeKeys.PlainText),
                new SchemaSlot("extra", "Extra", FieldTypeKeys.PlainText),
            ]));

        await using var scope = _factory.Services.CreateAsyncScope();
        var sync = scope.ServiceProvider.GetRequiredService<ISchemaSyncService>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await sync.ApplyAsync(_schemaDirectory, cancellationToken);

        // The file now says 'body' holds numbers, and drops 'extra' entirely.
        await WriteAsync("template.retype.json", new SchemaDocument(
            SchemaKind.Template,
            "retype",
            "Retype",
            Slots: [new SchemaSlot("body", "Body", FieldTypeKeys.Number)]));

        var report = await sync.ApplyAsync(_schemaDirectory, cancellationToken);

        report.Changes.Should().Contain(change =>
            change.Change == SchemaChangeKind.Refused && change.Detail.Contains("plainText"));
        report.Changes.Should().Contain(change =>
            change.Change == SchemaChangeKind.KeptUnlisted && change.Detail.Contains("extra"));

        var template = await context.Templates
            .AsNoTracking()
            .Include(candidate => candidate.Zones)
            .SingleAsync(candidate => candidate.Key == "retype", cancellationToken);

        // Neither the retype nor the removal happened. Both would make stored values unreadable, in
        // an environment nobody is watching (spec sections 8.5 and 27.1).
        template.Zones.Single(zone => zone.Key == "body").FieldTypeKey.Should().Be(FieldTypeKeys.PlainText);
        template.Zones.Should().HaveCount(2);
    }

    [Test]
    public async Task ADiffWritesNothing()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await WriteAsync("template.diff-only.json", new SchemaDocument(
            SchemaKind.Template,
            "diff-only",
            "Diff only",
            Slots: [new SchemaSlot("body", "Body", FieldTypeKeys.PlainText)]));

        await using var scope = _factory.Services.CreateAsyncScope();
        var sync = scope.ServiceProvider.GetRequiredService<ISchemaSyncService>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var report = await sync.DiffAsync(_schemaDirectory, cancellationToken);

        // The CLI's drift check is this value; a diff that always reported "in sync" would be no
        // check at all.
        report.HasPendingWork.Should().BeTrue();

        var exists = await context.Templates
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Key == "diff-only", cancellationToken);

        exists.Should().BeFalse();
    }

    [Test]
    public async Task AConfigurationTheFieldTypeRefusesIsReportedRatherThanStored()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await WriteAsync("template.bad-config.json", new SchemaDocument(
            SchemaKind.Template,
            "bad-config",
            "Bad config",
            Slots:
            [
                new SchemaSlot(
                    "body",
                    "Body",
                    FieldTypeKeys.PlainText,
                    Configuration: JsonDocument.Parse("""{"maxlength":10}""").RootElement.Clone()),
            ]));

        await using var scope = _factory.Services.CreateAsyncScope();
        var sync = scope.ServiceProvider.GetRequiredService<ISchemaSyncService>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var report = await sync.ApplyAsync(_schemaDirectory, cancellationToken);

        report.HasProblems.Should().BeTrue();

        var template = await context.Templates
            .AsNoTracking()
            .Include(candidate => candidate.Zones)
            .SingleOrDefaultAsync(candidate => candidate.Key == "bad-config", cancellationToken);

        // The record is created; the slot the field type cannot honour is not.
        template!.Zones.Should().BeEmpty();
    }

    [Test]
    public async Task ExportedFilesApplyBackWithNothingToDo()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await using var scope = _factory.Services.CreateAsyncScope();
        var sync = scope.ServiceProvider.GetRequiredService<ISchemaSyncService>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var template = new Template
        {
            Key = "round-trip",
            Name = "Round trip",
            CurrentRevision = 1,
            IsOrphaned = true,
        };

        template.Zones.Add(new Zone
        {
            Key = "body",
            Name = "Body",
            FieldTypeKey = FieldTypeKeys.PlainText,
            ConfigurationJson = """{"maxLength":120}""",
            IsRequired = true,
            SortOrder = 3,
        });

        context.Templates.Add(template);
        await context.SaveChangesAsync(cancellationToken);

        var written = await sync.ExportAsync(_schemaDirectory, cancellationToken);

        written.Should().Contain(path => path.EndsWith("template.round-trip.json"));

        var report = await sync.DiffAsync(_schemaDirectory, cancellationToken);

        // Export then diff is the loop a developer actually runs, and the drift check in CI depends
        // on it settling: anything the exporter drops would show up here as permanent drift.
        report.Changes.Where(change => change.Change is not SchemaChangeKind.KeptUnlisted)
            .Should().BeEmpty();
        report.HasPendingWork.Should().BeFalse();
    }

    private ITemplateReconciler Reconciler(ApplicationDbContext context) =>
        new TemplateReconciler(
            context,
            new CmsComponentScanner(
                new CmsStructureAssemblies(typeof(ReconciledTemplateComponent).Assembly),
                NullLogger<CmsComponentScanner>.Instance),
            NullLogger<TemplateReconciler>.Instance);

    private async Task WriteAsync(string fileName, SchemaDocument document)
    {
        Directory.CreateDirectory(_schemaDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(_schemaDirectory, fileName),
            JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            TestContext.Current!.Execution.CancellationToken);
    }
}
