using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Pages;

/// <summary>
/// The verbs that change what the public site serves, and what is in the recycle bin (task P2-17).
/// </summary>
/// <remarks>
/// Separated from <see cref="PageEndpoints"/> because these are the operations with consequences
/// outside the backoffice. Every one of them is a dedicated route with its own permission rather
/// than a status field somebody could set through the metadata patch — which is the mass-assignment
/// rule spec section 20.1 states and task P2-22 exists to keep.
/// </remarks>
public static class PageLifecycleEndpoints
{
    /// <summary>
    /// Maps the lifecycle endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapPageLifecycleEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var pages = group.MapGroup(PageEndpoints.Prefix).WithTags("Pages");

        pages.MapGet("/recycle-bin", ListRecycleBinAsync)
            .WithName("ListRecycleBin")
            .WithSummary("Lists the pages in the recycle bin, subtree roots first.")
            .RequireAuthorization(CmsPermissions.ContentDelete);

        pages.MapPost("/{id:int}/duplicate", DuplicateAsync)
            .WithName("DuplicatePage")
            .WithSummary("Copies a page, and with ?deep=true everything beneath it.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        pages.MapGet("/{id:int}/delete-impact", DescribeDeleteAsync)
            .WithName("DescribePageDelete")
            .WithSummary("Reports how many pages a delete would take with it, without deleting them.")
            .RequireAuthorization(CmsPermissions.ContentDelete);

        pages.MapDelete("/{id:int}", DeleteAsync)
            .WithName("DeletePage")
            .WithSummary("Soft-deletes a page and its subtree into the recycle bin.")
            .RequireAuthorization(CmsPermissions.ContentDelete)
            .RequireCmsAntiforgery();

        pages.MapPost("/{id:int}/restore", RestoreAsync)
            .WithName("RestorePage")
            .WithSummary("Restores a page and its subtree from the recycle bin, as drafts.")
            .RequireAuthorization(CmsPermissions.ContentDelete)
            .RequireCmsAntiforgery();

        pages.MapDelete("/{id:int}/permanent", PurgeAsync)
            .WithName("PurgePage")
            .WithSummary("Permanently removes a page already in the recycle bin. Irreversible.")
            .RequireAuthorization(CmsPermissions.UsersManage)
            .RequireCmsAntiforgery();

        pages.MapPost("/{id:int}/validate", ValidateAsync)
            .WithName("ValidatePage")
            .WithSummary("Runs the publish checks without publishing.")
            .RequireAuthorization(CmsPermissions.ContentRead)
            .RequireCmsAntiforgery();

        pages.MapPost("/{id:int}/publish", PublishAsync)
            .WithName("PublishPage")
            .WithSummary("Publishes the current draft as a new immutable version.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        pages.MapPost("/{id:int}/unpublish", UnpublishAsync)
            .WithName("UnpublishPage")
            .WithSummary("Retires a page from the public site, leaving its draft alone.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        return group;
    }

    private static async Task<IResult> ListRecycleBinAsync(
        IRecycleBinService recycleBin,
        CancellationToken cancellationToken) =>
        (await recycleBin.ListAsync(cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> DuplicateAsync(
        int id,
        IDuplicationService duplication,
        CancellationToken cancellationToken,
        bool deep = false,
        int? parentId = null) =>
        (await duplication.DuplicateAsync(id, deep, parentId, cancellationToken))
        .ToHttpResult(created => Results.Created(
            $"{CmsApiEndpoints.BasePath}{PageEndpoints.Prefix}/{created.Summary.Id}",
            created));

    private static async Task<IResult> DescribeDeleteAsync(
        int id,
        IRecycleBinService recycleBin,
        CancellationToken cancellationToken) =>
        (await recycleBin.DescribeAsync(id, cancellationToken)).ToHttpResult(Results.Ok);

    /// <remarks>
    /// Answers with the affected subtree rather than a 204. One delete takes every descendant with
    /// it, and how many pages that was — and how many of them were live — is the thing the screen
    /// has to say afterwards; a no-content response would make it refetch the tree to find out.
    /// </remarks>
    private static async Task<IResult> DeleteAsync(
        int id,
        IRecycleBinService recycleBin,
        CancellationToken cancellationToken) =>
        (await recycleBin.DeleteAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> RestoreAsync(
        int id,
        IRecycleBinService recycleBin,
        CancellationToken cancellationToken) =>
        (await recycleBin.RestoreAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// The one irreversible operation in the system, behind <c>Users.Manage</c> rather than
    /// <c>Content.Delete</c>: an editor who can empty the recycle bin can destroy version history.
    /// It refuses while any stored content still points at the page, and the refusal names the pages
    /// in the way (spec section 14.10).
    /// </remarks>
    private static async Task<IResult> PurgeAsync(
        int id,
        IRecycleBinService recycleBin,
        CancellationToken cancellationToken) =>
        (await recycleBin.PurgeAsync(id, cancellationToken))
        .ToHttpResult(removed => Results.Ok(new PurgeResult(id, removed)));

    private static async Task<IResult> ValidateAsync(
        int id,
        IPublishingService publishing,
        CancellationToken cancellationToken) =>
        (await publishing.ValidateAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// An unacknowledged warning comes back as <c>422</c> carrying it, and the client resubmits with
    /// <c>acknowledgeWarnings</c> set (spec section 22.2). The body is optional so that a publish
    /// with nothing to acknowledge needs no body at all.
    /// </remarks>
    private static async Task<IResult> PublishAsync(
        int id,
        PublishPageRequest? request,
        IPublishingService publishing,
        CancellationToken cancellationToken) =>
        (await publishing.PublishAsync(id, request?.AcknowledgeWarnings ?? false, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// Reports the version number that was retired rather than a bare 204, so a confirmation can say
    /// what stopped being served instead of only that something did.
    /// </remarks>
    private static async Task<IResult> UnpublishAsync(
        int id,
        IPublishingService publishing,
        CancellationToken cancellationToken) =>
        (await publishing.UnpublishAsync(id, cancellationToken))
        .ToHttpResult(version => Results.Ok(new UnpublishResult(id, version)));
}
