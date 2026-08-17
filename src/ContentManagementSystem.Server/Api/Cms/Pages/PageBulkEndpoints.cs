using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Pages;

/// <summary>
/// <c>/api/cms/v1/pages/bulk</c> — one operation over many pages (task P6-29, spec section 14.11).
/// </summary>
/// <remarks>
/// Three routes, and the shape is the point: describe, start, poll. A batch that runs in the
/// background cannot answer in its response, so the response says where to look — and a batch small
/// enough to have run inline answers with the same body, already finished, so a client has one shape
/// to read rather than two.
/// <para>
/// The endpoint policies here are a floor, not the rule. Which permission a batch needs depends on
/// what it does, so <c>BulkOperationService</c> checks the operation's own permission — publish, or
/// delete, or edit — and every item re-checks it inside the service that owns it. The route policy
/// is the lowest of them, which is what keeps an editor who may only edit from being refused at the
/// door before the service can explain why.
/// </para>
/// </remarks>
public static class PageBulkEndpoints
{
    /// <summary>Route prefix the bulk endpoints hang off, relative to the page group.</summary>
    internal const string Prefix = "/bulk";

    /// <summary>
    /// Maps the bulk operation endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapPageBulkEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var bulk = group.MapGroup($"{PageEndpoints.Prefix}{Prefix}").WithTags("Pages");

        // A POST for a read, for the reason the markup preview endpoint gives: the request body is a
        // selection of up to 500 identities, which has no business in a query string or in every
        // access log between here and the browser.
        bulk.MapPost("/preview", DescribeAsync)
            .WithName("PreviewBulkOperation")
            .WithSummary("Reports what a bulk operation would run over, without running any of it.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        bulk.MapPost("/", StartAsync)
            .WithName("StartBulkOperation")
            .WithSummary("Runs one operation over a selection of pages, in the background when large.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        bulk.MapGet("/{jobId:guid}", GetAsync)
            .WithName("GetBulkOperation")
            .WithSummary("Reports a bulk operation's progress and its per-item results.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        return group;
    }

    private static async Task<IResult> DescribeAsync(
        BulkOperationRequest request,
        IBulkOperationService bulk,
        CancellationToken cancellationToken) =>
        (await bulk.DescribeAsync(request, cancellationToken)).ToHttpResult(Results.Ok);

    /// <remarks>
    /// Answers <c>202 Accepted</c> with the job when it is still running and <c>200 OK</c> when it
    /// finished inside the request. The distinction is the one thing a client cannot work out for
    /// itself before it sees the body, and it decides whether the screen polls or reports.
    /// </remarks>
    private static async Task<IResult> StartAsync(
        BulkOperationRequest request,
        IBulkOperationService bulk,
        CancellationToken cancellationToken) =>
        (await bulk.StartAsync(request, cancellationToken)).ToHttpResult(job => job.IsFinished
            ? Results.Ok(job)
            : Results.Accepted($"{CmsApiEndpoints.BasePath}{PageEndpoints.Prefix}{Prefix}/{job.Id}", job));

    private static IResult GetAsync(Guid jobId, IBulkOperationService bulk) =>
        bulk.Get(jobId).ToHttpResult(Results.Ok);
}
