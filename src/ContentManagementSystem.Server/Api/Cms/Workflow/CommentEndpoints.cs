using ContentManagementSystem.Core.Workflow;
using ContentManagementSystem.Server.Api.Cms.Pages;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Workflow;

namespace ContentManagementSystem.Server.Api.Cms.Workflow;

/// <summary>
/// <c>/api/cms/v1/pages/{id}/comments</c> — threaded review remarks (task P7-11).
/// </summary>
/// <remarks>
/// Guarded by <c>Content.Read</c> rather than <c>Content.Edit</c>, and that is deliberate: half the
/// point of review comments is that somebody who may not change the content can still say what is
/// wrong with it. An approver's editing is confined to what is assigned to them, and a viewer may
/// not edit at all — neither should be silent.
/// </remarks>
public static class CommentEndpoints
{
    /// <summary>
    /// Maps the comment endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapCommentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var comments = group.MapGroup($"{PageEndpoints.Prefix}/{{pageId:int}}/comments")
            .WithTags("Workflow");

        comments.MapGet("/", ListAsync)
            .WithName("GetPageComments")
            .WithSummary("Lists a page's comment threads.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        comments.MapPost("/", AddAsync)
            .WithName("AddPageComment")
            .WithSummary("Adds a remark or a reply.")
            .RequireAuthorization(CmsPermissions.ContentRead)
            .RequireCmsAntiforgery();

        group.MapPost("/comments/{commentId:int}/resolve", ResolveAsync)
            .WithName("ResolveComment")
            .WithSummary("Marks a thread dealt with, or reopens it.")
            .WithTags("Workflow")
            .RequireAuthorization(CmsPermissions.ContentRead)
            .RequireCmsAntiforgery();

        return group;
    }

    private static async Task<IResult> ListAsync(
        int pageId,
        ICommentService comments,
        CancellationToken cancellationToken,
        string? zoneKey = null,
        bool includeResolved = true) =>
        (await comments.ListAsync(pageId, zoneKey, includeResolved, cancellationToken))
        .ToHttpResult(Results.Ok);

    private static async Task<IResult> AddAsync(
        int pageId,
        CreateCommentRequest request,
        ICommentService comments,
        CancellationToken cancellationToken) =>
        (await comments.AddAsync(pageId, request, cancellationToken))
        .ToHttpResult(comment => Results.Created(
            $"{CmsApiEndpoints.BasePath}{PageEndpoints.Prefix}/{pageId}/comments",
            comment));

    private static async Task<IResult> ResolveAsync(
        int commentId,
        ICommentService comments,
        CancellationToken cancellationToken,
        bool resolved = true) =>
        (await comments.ResolveAsync(commentId, resolved, cancellationToken)).ToHttpResult(Results.Ok);
}
