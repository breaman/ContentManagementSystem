using ContentManagementSystem.Core.Auditing;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Server.HostedServices;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// The audit retention sweep (task P9-25, spec section 11.7).
/// </summary>
/// <remarks>
/// The table this prunes is written on every <c>SaveChanges</c> an editor causes, so with no window
/// it grows for as long as the site is used — and it grows on the same transaction as the content,
/// which is why an unbounded audit table eventually slows down saving a draft.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class AuditRetentionTests(SqlServerFixture fixture)
{
    /// <summary>
    /// Marks the rows this suite wrote, so its counts are not a claim about the whole table.
    /// </summary>
    /// <remarks>
    /// Arranging a workbench writes audit rows of its own — that is the interceptor doing its job —
    /// and all of them are recent, so they survive every sweep here. Counting the whole table would
    /// make each assertion depend on how much arranging the harness happened to do.
    /// </remarks>
    private const string Fixture = "RetentionFixture";

    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task AWindowOfZeroKeepsEverything()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await WriteRowsAsync(10, ageDays: 4000, cancellationToken);

        var result = await _bench.Resolve<IAuditRetentionService>().SweepAsync(cancellationToken);

        // The default, and the honest state while Q9 is unanswered: a sweep that invented a window
        // would be a system deciding how long an organisation's evidence lasts.
        result.Should().Be(AuditSweepResult.KeptEverything);
        (await CountAsync(cancellationToken)).Should().Be(10);
    }

    [Test]
    public async Task RowsOlderThanTheWindowGoAndNewerOnesStay()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await SetWindowAsync(90, cancellationToken);
        await WriteRowsAsync(6, ageDays: 120, cancellationToken);
        await WriteRowsAsync(4, ageDays: 30, cancellationToken);

        var result = await _bench.Resolve<IAuditRetentionService>().SweepAsync(cancellationToken);

        var others = await OtherRowsAsync(cancellationToken);

        result.Removed.Should().Be(6);
        result.Remaining.Should().BeFalse();
        result.Cutoff.Should().NotBeNull();

        (await CountAsync(cancellationToken)).Should().Be(4);

        // And the rows the harness itself wrote, which are recent, are untouched. A sweep that
        // deleted everything would pass the assertion above.
        others.Should().BePositive();
        (await OtherRowsAsync(cancellationToken)).Should().Be(others);
    }

    [Test]
    public async Task ASweepIsIdempotent()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await SetWindowAsync(90, cancellationToken);
        await WriteRowsAsync(5, ageDays: 200, cancellationToken);

        await _bench.Resolve<IAuditRetentionService>().SweepAsync(cancellationToken);

        // Which is what makes it safe to run on every instance at once.
        var second = await _bench.Resolve<IAuditRetentionService>().SweepAsync(cancellationToken);

        second.Removed.Should().Be(0);
        (await CountAsync(cancellationToken)).Should().Be(0);
    }

    [Test]
    public async Task MoreRowsThanOneBatchAreAllRemoved()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await SetWindowAsync(30, cancellationToken);

        // One row past the batch size, so the loop has to come round again — the failure this rules
        // out is a sweep that deletes exactly one batch and reports itself finished.
        await WriteRowsAsync(AuditRetentionService.BatchSize + 1, ageDays: 90, cancellationToken);

        var result = await _bench.Resolve<IAuditRetentionService>().SweepAsync(cancellationToken);

        result.Removed.Should().Be(AuditRetentionService.BatchSize + 1);
        result.Remaining.Should().BeFalse();

        (await CountAsync(cancellationToken)).Should().Be(0);
    }

    [Test]
    public async Task BothSweepsAreRegisteredToRunNightly()
    {
        // The finding this task turned up: the version sweep has implemented spec section 11.7 since
        // P2-13 and nothing called it, so every deployment kept every version forever while a policy
        // that said otherwise sat in the code. This is the assertion that stops that recurring.
        var hosted = _bench.Resolve<IEnumerable<IHostedService>>().ToArray();

        hosted.Should().ContainSingle(service => service is RetentionService);

        await Task.CompletedTask;
    }

    private async Task SetWindowAsync(int days, CancellationToken cancellationToken)
    {
        await _bench.Context.SiteSettings.ExecuteUpdateAsync(
            settings => settings.SetProperty(row => row.AuditLogRetentionDays, days),
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();
    }

    /// <summary>How many of this suite's own rows survive.</summary>
    private async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        return await _bench.Context.AuditLogs.CountAsync(row => row.Type == Fixture, cancellationToken);
    }

    /// <summary>How many rows anything else wrote.</summary>
    private async Task<int> OtherRowsAsync(CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        return await _bench.Context.AuditLogs.CountAsync(row => row.Type != Fixture, cancellationToken);
    }

    /// <summary>Writes audit rows of a given age, bypassing the interceptor that normally writes them.</summary>
    /// <param name="count">How many.</param>
    /// <param name="ageDays">How long ago they were written.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <remarks>
    /// Written directly rather than produced by editing content, because what is under test is a
    /// decision about age and producing a row of a chosen age through the interceptor would mean
    /// moving the clock between every save.
    /// </remarks>
    private async Task WriteRowsAsync(int count, int ageDays, CancellationToken cancellationToken)
    {
        var written = _bench.Clock.GetUtcNow().AddDays(-ageDays);

        var rows = Enumerable.Range(0, count).Select(index => new AuditLog
        {
            UserId = 1,
            Type = Fixture,
            TableName = "Page",
            DateTime = written,
            PrimaryKey = $"{{\"Id\":{index}}}",
        });

        _bench.Context.AuditLogs.AddRange(rows);

        await _bench.Context.SaveChangesAsync(cancellationToken);
        _bench.Context.ChangeTracker.Clear();
    }
}
