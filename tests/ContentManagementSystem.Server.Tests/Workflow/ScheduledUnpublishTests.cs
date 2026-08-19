using ContentManagementSystem.Core.Publishing;
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
/// Scheduled retirement, and the redirect it may leave behind (task P7-15, criterion P7 #9).
/// </summary>
/// <remarks>
/// The redirect is configuration rather than a default, and both branches are asserted here. A
/// redirect the system invents is a URL the site then promises to serve forever, so a deployment
/// that has not asked for them gets none; one that has, keeps the traffic.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class ScheduledUnpublishTests(SqlServerFixture fixture)
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task RetirementWithdrawsThePublicUrlAndLeavesARedirectOnlyWhenConfigured(bool redirect)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var settings = await workbench.Context.SiteSettings.FirstAsync(cancellationToken);
        settings.RedirectToParentOnUnpublish = redirect;
        await workbench.Context.SaveChangesAsync(cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var parent = await workbench.AddPageAsync(template, "Press", cancellationToken);
        var child = await workbench.AddPageAsync(template, "Old release", cancellationToken, parent.Summary.Id);

        var publishing = workbench.Resolve<IPublishingService>();

        (await publishing.PublishAsync(parent.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();
        (await publishing.PublishAsync(child.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        var url = await workbench.Context.PageRoutes
            .AsNoTracking()
            .Where(route => route.PageId == child.Summary.Id && route.IsPublished)
            .Select(route => route.Url)
            .FirstAsync(cancellationToken);

        // A schedule names the editor it runs as, and that column is a real foreign key: the
        // caller has to be somebody.
        workbench.Users.UserId = await AddOwnerAsync(workbench, cancellationToken);

        await ScheduleRetirementAsync(workbench, child.Summary.Id, cancellationToken);

        workbench.Clock.Advance(TimeSpan.FromHours(2));

        (await workbench.Resolve<ScheduledJobRunner>().RunOnceAsync(cancellationToken)).Should().Be(1);

        var page = await workbench.Context.Pages
            .AsNoTracking()
            .FirstAsync(candidate => candidate.Id == child.Summary.Id, cancellationToken);

        page.PublishedVersionId.Should().BeNull("the page is retired from the public site");

        var served = await workbench.Context.PageRoutes
            .AsNoTracking()
            .AnyAsync(route => route.PageId == child.Summary.Id && route.IsPublished, cancellationToken);

        served.Should().BeFalse("and its published route goes with it");

        var redirected = await workbench.Context.Redirects
            .AsNoTracking()
            .AnyAsync(rule => rule.FromUrl == url && rule.ToPageId == parent.Summary.Id, cancellationToken);

        redirected.Should().Be(redirect);
    }

    /// <summary>Inserts the editor the schedule belongs to.</summary>
    private static async Task<int> AddOwnerAsync(PageWorkbench workbench, CancellationToken cancellationToken)
    {
        var name = $"retirer-{Guid.NewGuid():N}"[..24];

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

    private static async Task ScheduleRetirementAsync(
        PageWorkbench workbench,
        int pageId,
        CancellationToken cancellationToken)
    {
        await using var scope = workbench.NewScope();

        ((StubUserService)scope.ServiceProvider.GetRequiredService<Data.Interfaces.IUserService>())
            .UserId = workbench.Users.UserId;

        var result = await scope.ServiceProvider.GetRequiredService<ISchedulingService>().SetAsync(
            pageId,
            new SetScheduleRequest(null, workbench.Clock.GetUtcNow().AddHours(1)),
            cancellationToken);

        result.IsSuccess.Should().BeTrue(string.Join(
            "; ",
            result.Diagnostics.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
    }
}
