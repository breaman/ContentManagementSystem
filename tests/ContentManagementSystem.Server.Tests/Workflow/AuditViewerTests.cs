using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Auditing;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.TestSupport;

using FluentAssertions;

namespace ContentManagementSystem.Server.Tests.Workflow;

/// <summary>
/// The audit log answers "who unpublished the homepage and when" (task P7-20, criterion P7 #10).
/// </summary>
/// <remarks>
/// The criterion is about interactions, which is a property of the screen; what a test can assert is
/// the thing the screen depends on — that one filter, over the entity and its id, reaches the answer
/// without anybody paging through the site's whole history.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class AuditViewerTests(SqlServerFixture fixture)
{
    [Test]
    public async Task OneFilterFindsWhoUnpublishedAPageAndWhen()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var homepage = await workbench.AddPageAsync(template, "Home", cancellationToken);
        var other = await workbench.AddPageAsync(template, "About", cancellationToken);

        var publishing = workbench.Resolve<IPublishingService>();

        await publishing.PublishAsync(homepage.Summary.Id, true, cancellationToken);
        await publishing.PublishAsync(other.Summary.Id, true, cancellationToken);

        workbench.Clock.Advance(TimeSpan.FromMinutes(30));
        workbench.Users.UserId = 7;

        await publishing.UnpublishAsync(homepage.Summary.Id, cancellationToken);

        var found = await workbench.Resolve<IAuditQueryService>().ListAsync(
            new AuditQuery("Page", homepage.Summary.Id.ToString()),
            cancellationToken);

        found.IsSuccess.Should().BeTrue();

        var entries = found.Value!.Items;

        entries.Should().NotBeEmpty();
        entries.Should().OnlyContain(entry => entry.Entity == "Page");
        entries[0].UserId.Should().Be(7, "newest first is what an audit question is asking for");
        entries[0].ChangedColumns.Should().Contain(nameof(Data.Models.Cms.Page.PublishedVersionId));
        entries[0].When.Should().Be(workbench.Clock.GetUtcNow());
    }

    [Test]
    public async Task ACallerWithoutTheAuditPermissionIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.ContentRead, CmsPermissions.ContentEdit),
            cancellationToken);

        var found = await workbench.Resolve<IAuditQueryService>()
            .ListAsync(new AuditQuery(), cancellationToken);

        found.Outcome.Should().Be(CmsOutcome.Forbidden);
    }
}
