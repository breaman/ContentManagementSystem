using ContentManagementSystem.Core.Workflow;
using ContentManagementSystem.Server.Api.Cms.Pages;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Workflow;

namespace ContentManagementSystem.Server.Api.Cms.Workflow;

/// <summary>
/// <c>/api/cms/v1/pages/{id}/submit|approve|reject</c> and the approver's inbox (task P7-11,
/// spec section 11.9).
/// </summary>
/// <remarks>
/// Three verbs rather than a status field on the page. A client that could <c>PATCH</c> a version's
/// status to <c>Approved</c> would be a client that could approve its own submission by editing a
/// field, which is exactly the rule <c>TwoStep</c> exists to enforce — so status transitions happen
/// only through these dedicated endpoints, as <c>CONTRIBUTING.md</c> requires.
/// <para>
/// The endpoint policies are the floor, not the decision: <c>Content.Approve</c> admits an approver
/// to the route, and <c>WorkflowService</c> then asks whether they may approve <em>this</em>, which
/// is where the self-approval clause and the section ACLs live.
/// </para>
/// </remarks>
public static class WorkflowEndpoints
{
    /// <summary>
    /// Maps the review endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapWorkflowEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var pages = group.MapGroup($"{PageEndpoints.Prefix}/{{pageId:int}}").WithTags("Workflow");

        pages.MapGet("/workflow", GetAsync)
            .WithName("GetPageWorkflow")
            .WithSummary("Reports where a page stands in review, and what this caller may do next.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        pages.MapPost("/submit", SubmitAsync)
            .WithName("SubmitPageForReview")
            .WithSummary("Puts the current draft in front of an approver and locks it.")
            .RequireAuthorization(CmsPermissions.ContentSubmit)
            .RequireCmsAntiforgery();

        pages.MapPost("/approve", ApproveAsync)
            .WithName("ApprovePage")
            .WithSummary("Accepts a submission, so the version may be published.")
            .RequireAuthorization(CmsPermissions.ContentApprove)
            .RequireCmsAntiforgery();

        pages.MapPost("/reject", RejectAsync)
            .WithName("RejectPage")
            .WithSummary("Sends a submission back and restores an editable draft of it.")
            .RequireAuthorization(CmsPermissions.ContentApprove)
            .RequireCmsAntiforgery();

        group.MapGet("/workflow/tasks", InboxAsync)
            .WithName("GetWorkflowTasks")
            .WithSummary("Lists the review requests waiting on the caller.")
            .WithTags("Workflow")
            .RequireAuthorization(CmsPermissions.ContentApprove);

        return group;
    }

    private static async Task<IResult> GetAsync(
        int pageId,
        IWorkflowService workflow,
        CancellationToken cancellationToken) =>
        (await workflow.GetAsync(pageId, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> SubmitAsync(
        int pageId,
        SubmitForReviewRequest? request,
        IWorkflowService workflow,
        CancellationToken cancellationToken) =>
        (await workflow.SubmitAsync(pageId, request ?? new SubmitForReviewRequest(), cancellationToken))
        .ToHttpResult(Results.Ok);

    private static async Task<IResult> ApproveAsync(
        int pageId,
        WorkflowDecisionRequest? request,
        IWorkflowService workflow,
        CancellationToken cancellationToken) =>
        (await workflow.ApproveAsync(pageId, request ?? new WorkflowDecisionRequest(), cancellationToken))
        .ToHttpResult(Results.Ok);

    private static async Task<IResult> RejectAsync(
        int pageId,
        WorkflowDecisionRequest? request,
        IWorkflowService workflow,
        CancellationToken cancellationToken) =>
        (await workflow.RejectAsync(pageId, request ?? new WorkflowDecisionRequest(), cancellationToken))
        .ToHttpResult(Results.Ok);

    /// <param name="assignedToMe">
    /// Whether to leave out the requests addressed to nobody in particular. Defaults to false,
    /// because in <c>Simple</c> mode most requests are addressed to nobody and an inbox that hid them
    /// would be empty on the sites that use review most.
    /// </param>
    /// <param name="limit">Most rows to return.</param>
    /// <param name="workflow">The review service.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    private static async Task<IResult> InboxAsync(
        IWorkflowService workflow,
        CancellationToken cancellationToken,
        bool assignedToMe = false,
        int limit = 50) =>
        (await workflow.InboxAsync(assignedToMe, limit, cancellationToken)).ToHttpResult(Results.Ok);
}
