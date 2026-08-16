using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Reusable;

/// <summary>
/// <c>/api/cms/v1/reusable</c> — the page endpoints minus URLs and the tree (task P4-09,
/// spec section 22).
/// </summary>
/// <remarks>
/// Deliberately shaped like <c>PageEndpoints</c> and its lifecycle sibling, because a reusable item
/// is a page's twin with the address removed: it has a draft, a version history, a publish, an
/// unpublish, and a recycle bin, and an editor who has learned one should not have to learn the
/// other. What is absent is what a reusable item does not have — no tree, no slug, no move, no
/// redirects, no SEO panel.
/// <para>
/// One file rather than the pages' four. The split there follows the lifecycle because each stage
/// carries real weight; here the whole surface is a dozen handlers that bind, call, and map.
/// </para>
/// </remarks>
public static class ReusableContentEndpoints
{
    /// <summary>Route prefix every reusable-content endpoint hangs off.</summary>
    internal const string Prefix = "/reusable";

    /// <summary>
    /// Maps the reusable-content endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapReusableContentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var reusable = group.MapGroup(Prefix).WithTags("Reusable content");

        reusable.MapGet("/", ListAsync)
            .WithName("ListReusableContent")
            .WithSummary("Lists the reusable content library, optionally filtered by folder or search.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        reusable.MapPost("/", CreateAsync)
            .WithName("CreateReusableContent")
            .WithSummary("Creates an item from a block type, with an empty schema-valid draft.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        reusable.MapGet("/{id:int}", GetAsync)
            .WithName("GetReusableContent")
            .WithSummary("Reads an item's metadata and its draft payload.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        reusable.MapPatch("/{id:int}", PatchAsync)
            .WithName("PatchReusableContent")
            .WithSummary("Updates name, description, and folder. Omitted members are left alone.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        reusable.MapPut("/{id:int}/draft", SaveDraftAsync)
            .WithName("SaveReusableDraft")
            .WithSummary("Saves the draft payload. Requires If-Match.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        reusable.MapPost("/{id:int}/draft/discard", DiscardDraftAsync)
            .WithName("DiscardReusableDraft")
            .WithSummary("Resets the draft to a copy of what is published.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        reusable.MapGet("/{id:int}/versions", ListVersionsAsync)
            .WithName("ListReusableVersions")
            .WithSummary("Lists the item's version history, newest first. Version ids are what a pin names.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        reusable.MapPost("/{id:int}/validate", ValidateAsync)
            .WithName("ValidateReusableContent")
            .WithSummary("Runs the publish checks and reports the impact, without publishing.")
            .RequireAuthorization(CmsPermissions.ContentRead)
            .RequireCmsAntiforgery();

        reusable.MapPost("/{id:int}/publish", PublishAsync)
            .WithName("PublishReusableContent")
            .WithSummary("Publishes the draft. Every late-bound page showing the item changes with it.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        reusable.MapPost("/{id:int}/unpublish", UnpublishAsync)
            .WithName("UnpublishReusableContent")
            .WithSummary("Retires the item, so every page placing it renders nothing in its place.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        reusable.MapDelete("/{id:int}", DeleteAsync)
            .WithName("DeleteReusableContent")
            .WithSummary("Moves the item to the recycle bin. Refused while any content still places it.")
            .RequireAuthorization(CmsPermissions.ContentDelete)
            .RequireCmsAntiforgery();

        reusable.MapPost("/{id:int}/restore", RestoreAsync)
            .WithName("RestoreReusableContent")
            .WithSummary("Restores an item from the recycle bin, unpublished.")
            .RequireAuthorization(CmsPermissions.ContentDelete)
            .RequireCmsAntiforgery();

        return group;
    }

    private static async Task<IResult> ListAsync(
        IReusableContentService reusable,
        CancellationToken cancellationToken,
        int? folderId = null,
        string? q = null) =>
        (await reusable.ListAsync(folderId, q, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> CreateAsync(
        CreateReusableContentRequest request,
        IReusableContentService reusable,
        CancellationToken cancellationToken) =>
        (await reusable.CreateAsync(request, cancellationToken))
        .ToHttpResult(created => Results.Created(
            $"{CmsApiEndpoints.BasePath}{Prefix}/{created.Summary.Id}",
            created));

    /// <remarks>
    /// Stamps the draft's <c>rowversion</c> as an <c>ETag</c>, which is what a subsequent draft save
    /// echoes back as <c>If-Match</c>.
    /// </remarks>
    private static async Task<IResult> GetAsync(
        int id,
        HttpContext httpContext,
        IReusableContentService reusable,
        CancellationToken cancellationToken) =>
        (await reusable.GetAsync(id, cancellationToken)).ToHttpResult(item =>
        {
            ETags.Stamp(httpContext, item.RowVersion);

            return Results.Ok(item);
        });

    private static async Task<IResult> PatchAsync(
        int id,
        PatchReusableContentRequest request,
        HttpContext httpContext,
        IReusableContentService reusable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Honoured but not required, exactly as on the page metadata patch: a patch names the members
        // it changes, so two concurrent patches to different fields merge rather than collide.
        var result = await reusable.PatchAsync(
            id,
            request with { ExpectedRowVersion = ETags.IfMatch(httpContext) ?? request.ExpectedRowVersion },
            cancellationToken);

        return result.ToHttpResult(item => Results.Ok(item));
    }

    /// <remarks>
    /// The precondition is mandatory, for the reason it is on a page's draft: an unconditional save
    /// is a lost update waiting for two editors to open the same item — and here the item is shared,
    /// so the two editors need not even have been looking at the same page.
    /// </remarks>
    private static async Task<IResult> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        HttpContext httpContext,
        IReusableContentService reusable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ETags.RequireIfMatch(httpContext, request.ExpectedRowVersion, out var expected) is { } refusal)
        {
            return refusal;
        }

        var result = await reusable.SaveDraftAsync(
            id,
            request with { ExpectedRowVersion = expected },
            cancellationToken);

        return result.ToHttpResult(saved =>
        {
            ETags.Stamp(httpContext, saved.Draft.RowVersion);

            return Results.Ok(saved);
        });
    }

    private static async Task<IResult> DiscardDraftAsync(
        int id,
        HttpContext httpContext,
        IReusableContentService reusable,
        CancellationToken cancellationToken) =>
        (await reusable.DiscardDraftAsync(id, cancellationToken)).ToHttpResult(draft =>
        {
            ETags.Stamp(httpContext, draft.RowVersion);

            return Results.Ok(draft);
        });

    private static async Task<IResult> ListVersionsAsync(
        int id,
        IReusableContentService reusable,
        CancellationToken cancellationToken) =>
        (await reusable.ListVersionsAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// Carries the impact list of spec section 9.4, which is what the confirmation dialog is built
    /// from. It is on the check and not only on the publish because a count reported after the
    /// irreversible part is a receipt rather than a confirmation.
    /// </remarks>
    private static async Task<IResult> ValidateAsync(
        int id,
        IReusableContentService reusable,
        CancellationToken cancellationToken) =>
        (await reusable.ValidateAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// An unacknowledged blast radius comes back as <c>422</c> carrying it, and the client resubmits
    /// with <c>acknowledgeWarnings</c> set. That is the server half of the rule spec section 9.4
    /// states for the UI — a screen that forgot to show the dialog cannot publish to forty pages
    /// regardless.
    /// </remarks>
    private static async Task<IResult> PublishAsync(
        int id,
        PublishPageRequest? request,
        IReusableContentService reusable,
        CancellationToken cancellationToken) =>
        (await reusable.PublishAsync(id, request?.AcknowledgeWarnings ?? false, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> UnpublishAsync(
        int id,
        PublishPageRequest? request,
        IReusableContentService reusable,
        CancellationToken cancellationToken) =>
        (await reusable.UnpublishAsync(id, request?.AcknowledgeWarnings ?? false, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// Refused with <c>409</c> and the where-used list while anything still places the item, which is
    /// the guard of spec section 9.4. Not cascaded: a deleted item is invisible to the resolver, so
    /// deleting one that is still placed blanks a zone on every page holding it.
    /// </remarks>
    private static async Task<IResult> DeleteAsync(
        int id,
        IReusableContentService reusable,
        CancellationToken cancellationToken) =>
        (await reusable.DeleteAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> RestoreAsync(
        int id,
        IReusableContentService reusable,
        CancellationToken cancellationToken) =>
        (await reusable.RestoreAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));
}
