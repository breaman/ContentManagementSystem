using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ContentManagementSystem.Data.Tests.Migrations;

/// <summary>
/// Proves the migration path works from an empty database (task P0-16).
/// </summary>
/// <remarks>
/// This is the guard against the classic failure where migrations only apply on developer machines
/// that already carry earlier state. Every migration added from here on must keep this test green,
/// including its <c>Down</c> method until the roll-forward-only policy takes effect at launch
/// (task P9-23).
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class MigrationsApplyFromEmptyTests(SqlServerFixture fixture)
{
    [Test]
    public async Task AllMigrationsApplyToAnEmptyDatabase()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = fixture.CreateContext($"cms_up_{Guid.NewGuid():N}");

        var expected = context.Database.GetMigrations().ToList();
        expected.Should().NotBeEmpty("the solution ships at least the InitialDatabase migration");

        await context.Database.MigrateAsync(cancellationToken);

        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        applied.Should().BeEquivalentTo(expected);
    }

    [Test]
    public async Task EveryMigrationCanBeRolledBackToAnEmptyDatabase()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = fixture.CreateContext($"cms_down_{Guid.NewGuid():N}");
        await context.Database.MigrateAsync(cancellationToken);

        // Migrating to the empty target exercises every Down method in reverse order.
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Migration.InitialDatabase, cancellationToken: cancellationToken);

        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        applied.Should().BeEmpty();
    }

    [Test]
    public async Task IdentityAndAuditTablesExistAfterMigrating()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var tables = await context.Database
            .SqlQuery<string>($"SELECT name AS Value FROM sys.tables")
            .ToListAsync(cancellationToken);

        tables.Should().Contain(["AspNetUsers", "AspNetRoles", "AuditLogs"]);
    }
}
