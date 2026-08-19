using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Scheduling;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.TestSupport;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Workflow;

/// <summary>
/// Scheduled publishing: it fires, it fires once, and a failure stops rather than repeating
/// (task P7-24, criteria P7 #7 to P7 #9).
/// </summary>
/// <remarks>
/// Driven through <see cref="ScheduledJobRunner"/> rather than through the hosted service. The
/// interesting behaviour is what one pass does, and a suite that asserted on it through a thirty
/// second timer would be a suite that waits — the timer itself is four lines with nothing to be
/// wrong about.
/// <para>
/// The two-instance case is the point of the whole design (risk R16). It is arranged by running two
/// passes against the same database with no coordination between them, which is exactly what two
/// servers do, and asserting that the second finds nothing to claim.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class PublishSchedulerTests(SqlServerFixture fixture)
{
    [Test]
    public async Task APageScheduledForTheFutureIsPublishedWhenItsTimeComes()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var page = await ScheduleAsync(workbench, "Sale starts", TimeSpan.FromHours(1), cancellationToken);

        // Nothing is due yet, so nothing happens. Asserting this first is what stops the test from
        // passing on a runner that publishes everything it can find.
        (await RunAsync(workbench, cancellationToken)).Should().Be(0);

        await IsPublishedAsync(workbench, page, cancellationToken)
            .ContinueWith(t => t.Result.Should().BeFalse(), cancellationToken);

        workbench.Clock.Advance(TimeSpan.FromHours(2));

        (await RunAsync(workbench, cancellationToken)).Should().Be(1);
        (await IsPublishedAsync(workbench, page, cancellationToken)).Should().BeTrue();

        var job = await JobAsync(workbench, page, ScheduledJobKind.Publish, cancellationToken);
        job.State.Should().Be(ScheduledJobState.Completed);
    }

    [Test]
    public async Task TwoInstancesPollingOneJobPublishExactlyOnce()
    {
        // Criterion P7 #7 and task P7-24. Both passes run against the same rows with no coordination
        // between them; the atomic UPDATE … OUTPUT is the only thing stopping a double publish.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var page = await ScheduleAsync(workbench, "Product launch", TimeSpan.FromMinutes(5), cancellationToken);

        workbench.Clock.Advance(TimeSpan.FromMinutes(10));

        var first = await RunAsync(workbench, cancellationToken);
        var second = await RunAsync(workbench, cancellationToken);

        (first + second).Should().Be(1, "exactly one instance claims the job");

        var versions = await workbench.Context.PageVersions
            .AsNoTracking()
            .CountAsync(version => version.PageId == page && version.Status == PageVersionStatus.Published,
                cancellationToken);

        versions.Should().Be(1, "and therefore exactly one published version exists");
    }

    [Test]
    public async Task AScheduledPublishThatFailsValidationIsMarkedFailedAndIsNotRetried()
    {
        // Criterion P7 #8. The version fails the publish checks because a required zone is empty;
        // what matters is that the job stops rather than trying again every thirty seconds forever.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var template = await workbench.UseTemplateAsync(
            "article",
            cancellationToken,
            PageWorkbench.TextZone("body", required: true));

        var detail = await workbench.AddPageAsync(template, "Half written", cancellationToken);
        var owner = await AddOwnerAsync(workbench, cancellationToken);

        await ScheduleJobAsync(workbench, detail.Summary.Id, owner, TimeSpan.FromMinutes(1), cancellationToken);

        workbench.Clock.Advance(TimeSpan.FromMinutes(5));

        await RunAsync(workbench, cancellationToken);

        var job = await JobAsync(workbench, detail.Summary.Id, ScheduledJobKind.Publish, cancellationToken);

        job.State.Should().Be(ScheduledJobState.Failed);
        job.FailureReason.Should().NotBeNullOrWhiteSpace("the owner is told what to fix");

        (await IsPublishedAsync(workbench, detail.Summary.Id, cancellationToken)).Should().BeFalse();

        // The second pass is the assertion: a failed job is terminal, so nothing is claimed again.
        workbench.Clock.Advance(TimeSpan.FromMinutes(5));
        (await RunAsync(workbench, cancellationToken)).Should().Be(0);

        var told = await workbench.Context.Notifications
            .AsNoTracking()
            .Where(row => row.PageId == detail.Summary.Id)
            .Select(row => row.Kind)
            .ToListAsync(cancellationToken);

        told.Should().Contain(NotificationKind.ScheduledPublishFailed);
    }

    [Test]
    public async Task AScheduleThatIsRewrittenReplacesTheOutstandingJobRatherThanStackingOnIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var page = await ScheduleAsync(workbench, "Moving target", TimeSpan.FromHours(1), cancellationToken);

        await using (var second = workbench.NewScope())
        {
            await second.ServiceProvider.GetRequiredService<ISchedulingService>().SetAsync(
                page,
                new SetScheduleRequest(workbench.Clock.GetUtcNow().AddHours(3), null),
                cancellationToken);
        }

        var outstanding = await workbench.Context.ScheduledJobs
            .AsNoTracking()
            .CountAsync(
                job => job.PageId == page
                    && (job.State == ScheduledJobState.Pending || job.State == ScheduledJobState.Claimed),
                cancellationToken);

        outstanding.Should().Be(1, "two pending publishes for one page is the shape of a double publish");

        // The first hour passes and nothing happens, because the job that named it was cancelled.
        workbench.Clock.Advance(TimeSpan.FromHours(2));
        (await RunAsync(workbench, cancellationToken)).Should().Be(0);
    }

    [Test]
    public async Task ClearingASchedulesCancelsTheOutstandingJob()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var page = await ScheduleAsync(workbench, "Cancelled campaign", TimeSpan.FromHours(1), cancellationToken);

        await using (var second = workbench.NewScope())
        {
            await second.ServiceProvider.GetRequiredService<ISchedulingService>()
                .SetAsync(page, new SetScheduleRequest(null, null), cancellationToken);
        }

        workbench.Clock.Advance(TimeSpan.FromHours(2));

        (await RunAsync(workbench, cancellationToken)).Should().Be(0);
        (await IsPublishedAsync(workbench, page, cancellationToken)).Should().BeFalse();
    }

    [Test]
    public async Task ASchedulePointingAtThePastIsRefusedRatherThanRunImmediately()
    {
        // "Publish at a time that has gone" is either a mistake or a way of saying "publish now",
        // and guessing which is how a page goes live an hour before somebody meant it to.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var detail = await workbench.AddPageAsync(template, "Yesterday", cancellationToken);

        var result = await workbench.Resolve<ISchedulingService>().SetAsync(
            detail.Summary.Id,
            new SetScheduleRequest(workbench.Clock.GetUtcNow().AddMinutes(-1), null),
            cancellationToken);

        result.Outcome.Should().Be(CmsOutcome.Invalid);
    }

    /// <summary>Creates a page and schedules it, returning its id.</summary>
    private static async Task<int> ScheduleAsync(
        PageWorkbench workbench,
        string title,
        TimeSpan fromNow,
        CancellationToken cancellationToken)
    {
        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var detail = await workbench.AddPageAsync(template, title, cancellationToken);
        var owner = await AddOwnerAsync(workbench, cancellationToken);

        await ScheduleJobAsync(workbench, detail.Summary.Id, owner, fromNow, cancellationToken);

        return detail.Summary.Id;
    }

    /// <summary>Sets a schedule through the real service, as the API does.</summary>
    private static async Task ScheduleJobAsync(
        PageWorkbench workbench,
        int pageId,
        int ownerId,
        TimeSpan fromNow,
        CancellationToken cancellationToken)
    {
        await using var scope = workbench.NewScope();

        ((StubUserService)scope.ServiceProvider.GetRequiredService<Data.Interfaces.IUserService>())
            .UserId = ownerId;

        var result = await scope.ServiceProvider.GetRequiredService<ISchedulingService>().SetAsync(
            pageId,
            new SetScheduleRequest(workbench.Clock.GetUtcNow().Add(fromNow), null),
            cancellationToken);

        result.IsSuccess.Should().BeTrue(string.Join(
            "; ",
            result.Diagnostics.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
    }

    /// <summary>Runs one poller pass, as one instance would.</summary>
    private static Task<int> RunAsync(PageWorkbench workbench, CancellationToken cancellationToken) =>
        workbench.Resolve<ScheduledJobRunner>().RunOnceAsync(cancellationToken);

    private static async Task<bool> IsPublishedAsync(
        PageWorkbench workbench,
        int pageId,
        CancellationToken cancellationToken) =>
        await workbench.Context.Pages
            .AsNoTracking()
            .Where(page => page.Id == pageId)
            .Select(page => page.PublishedVersionId)
            .FirstAsync(cancellationToken) is not null;

    private static Task<ScheduledJob> JobAsync(
        PageWorkbench workbench,
        int pageId,
        ScheduledJobKind kind,
        CancellationToken cancellationToken) =>
        workbench.Context.ScheduledJobs
            .AsNoTracking()
            .Where(job => job.PageId == pageId && job.Kind == kind)
            .OrderByDescending(job => job.Id)
            .FirstAsync(cancellationToken);

    /// <summary>Inserts the editor a scheduled job runs as.</summary>
    private static async Task<int> AddOwnerAsync(PageWorkbench workbench, CancellationToken cancellationToken)
    {
        var name = $"scheduler-{Guid.NewGuid():N}"[..24];

        var user = new Data.Models.User
        {
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            MemberSince = DateTimeOffset.UnixEpoch,
        };

        workbench.Context.Users.Add(user);
        await workbench.Context.SaveChangesAsync(cancellationToken);

        return user.Id;
    }
}
