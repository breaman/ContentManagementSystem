using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Notifications;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Workflow;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.TestSupport;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Server.Tests.Workflow;

/// <summary>
/// Submit, approve, reject, and the rules that stand between a draft and a live page
/// (tasks P7-22 and P7-23, criteria P7 #1 to P7 #4).
/// </summary>
/// <remarks>
/// Every refusal here is asserted on the service rather than on an endpoint, for the reason task
/// P7-06 gives: an endpoint policy is a fast rejection at the door, and the check that still runs
/// when the service is called from anywhere else is the one that decides whether content is safe.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class WorkflowServiceTests(SqlServerFixture fixture)
{
    [Test]
    public async Task AnAuthorCannotPublishAndTheContentStaysUnpublished()
    {
        // Criterion P7 #1 and task P7-22. An Author holds Content.Edit and not Content.Publish, so
        // the refusal comes from the permission rather than from the workflow mode — it holds on a
        // site running no approval ceremony at all.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.ContentRead, CmsPermissions.ContentEdit),
            cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var page = await workbench.AddPageAsync(template, "Our new range", cancellationToken);

        var published = await workbench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, acknowledgeWarnings: true, cancellationToken);

        published.Outcome.Should().Be(CmsOutcome.Forbidden);

        var stored = await workbench.Context.Pages
            .AsNoTracking()
            .FirstAsync(candidate => candidate.Id == page.Summary.Id, cancellationToken);

        stored.PublishedVersionId.Should().BeNull("a refused publish leaves the page unpublished");
    }

    [Test]
    public async Task SubmitApproveAndPublishWorksEndToEnd()
    {
        // Criterion P7 #2, minus the mail: what is asserted is that each step moves the version and
        // that the notification rows the emails are rendered from are written.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var mail = new RecordingEmailSender();

        await using var workbench = await PageWorkbench.CreateAsync(
            fixture,
            cancellationToken: cancellationToken,
            configure: services =>
            {
                // The mail half of the criterion, observed at the transport boundary. Anything
                // further out is a mail server's behaviour rather than this system's.
                services.RemoveAll<ICmsEmailSender>();
                services.AddSingleton<ICmsEmailSender>(mail);
            });

        await SetModeAsync(workbench, WorkflowMode.TwoStep, cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var page = await workbench.AddPageAsync(template, "Autumn campaign", cancellationToken);

        var author = await AddUserAsync(workbench, "author", cancellationToken);

        // Given the seeded Approver role, so the submission has somebody to be addressed to. The
        // service finds them through the role assignments rather than being told, which is what
        // makes "submitted to nobody in particular" work at all in Simple mode.
        var approver = await AddUserAsync(
            workbench,
            "approver",
            cancellationToken,
            CmsRoleSeedData.ApproverId);

        var submitted = await AsUserAsync(workbench, author, cancellationToken, services =>
            services.GetRequiredService<IWorkflowService>().SubmitAsync(
                page.Summary.Id,
                new SubmitForReviewRequest(Note: "Ready for the sale."),
                cancellationToken));

        submitted.IsSuccess.Should().BeTrue(Because(submitted));
        submitted.Value!.DraftStatus.Should().Be(nameof(PageVersionStatus.InReview));
        submitted.Value.Pending.Should().NotBeNull();

        // A draft under review is frozen. That is what makes the approval a statement about the
        // content that then publishes rather than about whatever it had become by then.
        var edited = await AsUserAsync(workbench, author, cancellationToken, services =>
            services.GetRequiredService<IDraftService>().DiscardAsync(page.Summary.Id, cancellationToken));

        edited.Outcome.Should().Be(CmsOutcome.Conflict);

        var approved = await AsUserAsync(workbench, approver, cancellationToken, services =>
            services.GetRequiredService<IWorkflowService>().ApproveAsync(
                page.Summary.Id,
                new WorkflowDecisionRequest("Looks right."),
                cancellationToken));

        approved.IsSuccess.Should().BeTrue(Because(approved));
        approved.Value!.DraftStatus.Should().Be(nameof(PageVersionStatus.Approved));
        approved.Value.CanPublish.Should().BeTrue();

        var published = await AsUserAsync(workbench, approver, cancellationToken, services =>
            services.GetRequiredService<IPublishingService>()
                .PublishAsync(page.Summary.Id, acknowledgeWarnings: true, cancellationToken));

        published.IsSuccess.Should().BeTrue(Because(published));

        var notified = await workbench.Context.Notifications
            .AsNoTracking()
            .Where(row => row.PageId == page.Summary.Id)
            .Select(row => row.Kind)
            .ToListAsync(cancellationToken);

        notified.Should().Contain(NotificationKind.Submitted).And.Contain(NotificationKind.Approved);

        mail.Sent.Should().Contain(message => message.To == "approver@example.test")
            .And.Contain(message => message.To == "author@example.test");
    }

    /// <summary>A mail transport that keeps what it was asked to send.</summary>
    /// <remarks>
    /// Registered in place of the deployment's sender, which with no SMTP host configured writes to
    /// the log. Asserting on log output would be asserting on a message format; asserting on this is
    /// asserting that the notification reached a transport at all, which is what criterion P7 #2 is
    /// about.
    /// </remarks>
    private sealed class RecordingEmailSender : ICmsEmailSender
    {
        private readonly List<(string To, string Subject)> _sent = [];

        public IReadOnlyList<(string To, string Subject)> Sent
        {
            get
            {
                lock (_sent) return [.. _sent];
            }
        }

        public bool IsConfigured => true;

        public Task<bool> SendAsync(
            string toAddress,
            string subject,
            string body,
            CancellationToken cancellationToken = default)
        {
            lock (_sent) _sent.Add((toAddress, subject));

            return Task.FromResult(true);
        }
    }

    [Test]
    public async Task InTwoStepModeTheAuthorCannotApproveTheirOwnSubmission()
    {
        // Criterion P7 #3 and task P7-23. The caller holds Content.Approve throughout; what refuses
        // them is that they are the person who submitted it.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);
        await SetModeAsync(workbench, WorkflowMode.TwoStep, cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var page = await workbench.AddPageAsync(template, "Price change", cancellationToken);
        var author = await AddUserAsync(workbench, "author-approver", cancellationToken);

        await AsUserAsync(workbench, author, cancellationToken, services =>
            services.GetRequiredService<IWorkflowService>().SubmitAsync(
                page.Summary.Id,
                new SubmitForReviewRequest(),
                cancellationToken));

        var approved = await AsUserAsync(workbench, author, cancellationToken, services =>
            services.GetRequiredService<IWorkflowService>().ApproveAsync(
                page.Summary.Id,
                new WorkflowDecisionRequest(),
                cancellationToken));

        approved.Outcome.Should().Be(CmsOutcome.Forbidden);
        approved.Diagnostics.Diagnostics.Should().Contain(
            diagnostic => diagnostic.Code == WorkflowCodes.SelfApproval);

        // And publishing is refused too, or the rule would be one button press away from nothing.
        var published = await AsUserAsync(workbench, author, cancellationToken, services =>
            services.GetRequiredService<IPublishingService>()
                .PublishAsync(page.Summary.Id, acknowledgeWarnings: true, cancellationToken));

        published.Outcome.Should().Be(CmsOutcome.Invalid);
        published.Diagnostics.Diagnostics.Should().Contain(
            diagnostic => diagnostic.Code == WorkflowCodes.ApprovalRequired);
    }

    [Test]
    public async Task InSimpleModeAnApproverMayApproveWhatTheySubmitted()
    {
        // The other half of the same rule. Simple mode exists for sites where submitting is what
        // somebody without publish rights does; an approver who submitted was never going to be
        // stopped by ceremony they could have skipped.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);
        await SetModeAsync(workbench, WorkflowMode.Simple, cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var page = await workbench.AddPageAsync(template, "Quick fix", cancellationToken);
        var user = await AddUserAsync(workbench, "simple-approver", cancellationToken);

        await AsUserAsync(workbench, user, cancellationToken, services =>
            services.GetRequiredService<IWorkflowService>().SubmitAsync(
                page.Summary.Id,
                new SubmitForReviewRequest(),
                cancellationToken));

        var approved = await AsUserAsync(workbench, user, cancellationToken, services =>
            services.GetRequiredService<IWorkflowService>().ApproveAsync(
                page.Summary.Id,
                new WorkflowDecisionRequest(),
                cancellationToken));

        approved.IsSuccess.Should().BeTrue(Because(approved));
    }

    [Test]
    public async Task ARejectionReturnsTheContentToAFreshDraftWithCommentsPreserved()
    {
        // Criterion P7 #4. The refused row keeps exactly what was refused, the author carries on
        // from a copy of it, and the conversation that led there survives — which it does by
        // construction, because comments hang off the page rather than off a version.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);
        await SetModeAsync(workbench, WorkflowMode.TwoStep, cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var page = await workbench.AddPageAsync(template, "Draft release", cancellationToken);

        var author = await AddUserAsync(workbench, "rejected-author", cancellationToken);
        var approver = await AddUserAsync(workbench, "rejecting-approver", cancellationToken);

        var beforeDraftId = (await workbench.DraftOfAsync(page.Summary.Id, cancellationToken)).Id;

        await AsUserAsync(workbench, author, cancellationToken, services =>
            services.GetRequiredService<IWorkflowService>().SubmitAsync(
                page.Summary.Id,
                new SubmitForReviewRequest(),
                cancellationToken));

        await AsUserAsync(workbench, approver, cancellationToken, services =>
            services.GetRequiredService<ICommentService>().AddAsync(
                page.Summary.Id,
                new CreateCommentRequest("The headline is wrong.", "body"),
                cancellationToken));

        var rejected = await AsUserAsync(workbench, approver, cancellationToken, services =>
            services.GetRequiredService<IWorkflowService>().RejectAsync(
                page.Summary.Id,
                new WorkflowDecisionRequest("Please rework the headline."),
                cancellationToken));

        rejected.IsSuccess.Should().BeTrue(Because(rejected));

        var refused = await workbench.Context.PageVersions
            .AsNoTracking()
            .FirstAsync(version => version.Id == beforeDraftId, cancellationToken);

        refused.Status.Should().Be(PageVersionStatus.Rejected, "what was refused stays as it was refused");

        var draft = await workbench.DraftOfAsync(page.Summary.Id, cancellationToken);

        draft.Id.Should().NotBe(beforeDraftId, "the author gets a fresh, editable copy");
        draft.ContentJson.Should().Be(refused.ContentJson);

        var comments = await AsUserAsync(workbench, author, cancellationToken, services =>
            services.GetRequiredService<ICommentService>().ListAsync(
                page.Summary.Id,
                cancellationToken: cancellationToken));

        comments.Value!.Should().ContainSingle()
            .Which.Body.Should().Be("The headline is wrong.");

        // And the draft is editable again, which is the whole point of the round trip.
        var saved = await AsUserAsync(workbench, author, cancellationToken, services =>
            services.GetRequiredService<IDraftService>().DiscardAsync(page.Summary.Id, cancellationToken));

        saved.Outcome.Should().NotBe(CmsOutcome.Conflict);
    }

    [Test]
    public async Task SubmittingIsRefusedOnASiteWithNoWorkflow()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var page = await workbench.AddPageAsync(template, "Nothing to approve", cancellationToken);

        var submitted = await workbench.Resolve<IWorkflowService>()
            .SubmitAsync(page.Summary.Id, new SubmitForReviewRequest(), cancellationToken);

        submitted.Outcome.Should().Be(CmsOutcome.Invalid);
        submitted.Diagnostics.Diagnostics.Should().Contain(
            diagnostic => diagnostic.Code == WorkflowCodes.WorkflowDisabled);
    }

    /// <summary>Sets the site's workflow mode, which every rule in this file turns on.</summary>
    private static async Task SetModeAsync(
        PageWorkbench workbench,
        WorkflowMode mode,
        CancellationToken cancellationToken)
    {
        var settings = await workbench.Context.SiteSettings.FirstAsync(cancellationToken);
        settings.WorkflowMode = mode;

        await workbench.Context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Inserts a user, so a decision has somebody to be attributed to.</summary>
    private static async Task<int> AddUserAsync(
        PageWorkbench workbench,
        string name,
        CancellationToken cancellationToken,
        int? roleId = null)
    {
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

        if (roleId is { } role)
        {
            workbench.Context.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<int>
            {
                UserId = user.Id,
                RoleId = role,
            });

            await workbench.Context.SaveChangesAsync(cancellationToken);
        }

        return user.Id;
    }

    /// <summary>
    /// Runs one call in a fresh scope as a named user.
    /// </summary>
    /// <remarks>
    /// A scope per call for two reasons that both matter here. The access-rule resolver caches for
    /// the length of a request, so arranging and acting in one scope asks a resolver that has
    /// already made up its mind; and the self-approval rule turns on <em>who</em> the caller is, so
    /// a test of it has to be able to be two people.
    /// </remarks>
    private static async Task<T> AsUserAsync<T>(
        PageWorkbench workbench,
        int userId,
        CancellationToken cancellationToken,
        Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = workbench.NewScope();

        ((StubUserService)scope.ServiceProvider.GetRequiredService<Data.Interfaces.IUserService>())
            .UserId = userId;

        return await work(scope.ServiceProvider);
    }

    /// <summary>Renders a failed result's diagnostics into the assertion message.</summary>
    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
