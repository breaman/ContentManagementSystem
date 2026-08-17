using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Pages;

/// <summary>
/// <c>/api/cms/v1/pages</c> — reading pages and writing their drafts (task P2-16).
/// </summary>
/// <remarks>
/// Handlers bind, call, and map. Every rule about what a page may be — the slug shape, which
/// template revision a payload may name, whether a save lost a race — lives in the services, for the
/// reason <c>CONTRIBUTING.md</c> gives: the endpoint policy is the door and the service check is the
/// lock, and the same operation is reachable from a CLI verb with no HTTP request at all.
/// <para>
/// The split across five files follows the lifecycle rather than the route table.
/// <see cref="PageLifecycleEndpoints"/> owns the verbs that change what the public site serves,
/// <see cref="PageVersionEndpoints"/> owns history, <see cref="PageLockEndpoints"/> owns the
/// advisory lock, and <see cref="PageBulkEndpoints"/> owns doing any of it to many pages at once.
/// Reading a page and writing its draft — the two things an editor does all day — are here.
/// </para>
/// </remarks>
public static class PageEndpoints
{
    /// <summary>Route prefix every page endpoint hangs off, relative to the versioned group.</summary>
    internal const string Prefix = "/pages";

    /// <summary>
    /// Maps the page read and draft-write endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapPageEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var pages = group.MapGroup(Prefix).WithTags("Pages");

        pages.MapGet("/", ListAsync)
            .WithName("ListPages")
            .WithSummary("Lists pages matching a set of filters, one cursor page at a time.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        pages.MapGet("/tree", TreeAsync)
            .WithName("GetPageTree")
            .WithSummary("Fetches a slice of the content tree, for a lazily expanded tree control.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        pages.MapPost("/", CreateAsync)
            .WithName("CreatePage")
            .WithSummary("Creates a page from a template, with an empty schema-valid draft.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        pages.MapGet("/{id:int}", GetAsync)
            .WithName("GetPage")
            .WithSummary("Reads a page's metadata and its draft payload.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        // For prose. A rich-text zone has to put an address in the document it is writing, so the
        // one it puts there is the one the CMS resolved rather than one an author remembered
        // (task P6-11). Property-valued links keep storing the id instead (ADR-0006).
        pages.MapGet("/{id:int}/link", GetLinkAsync)
            .WithName("GetPageLink")
            .WithSummary("Resolves a page's current URL, for inserting a link into rich text.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        pages.MapGet("/{id:int}/draft", GetDraftAsync)
            .WithName("GetPageDraft")
            .WithSummary("Reads a page's draft payload on its own.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        pages.MapPut("/{id:int}/draft", SaveDraftAsync)
            .WithName("SavePageDraft")
            .WithSummary("Saves the draft payload. Requires If-Match.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        pages.MapPost("/{id:int}/draft/discard", DiscardDraftAsync)
            .WithName("DiscardPageDraft")
            .WithSummary("Resets the draft to a copy of what is published.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        // POST for a read, as the markup preview is, and for the same reason: the thing being asked
        // about is a whole page's payload, which has no business in a query string. Content.Read
        // rather than Content.Edit — comparing a document the caller is holding against one they may
        // already read grants them nothing new (task P6-19).
        pages.MapPost("/{id:int}/draft/diff", DiffDraftAsync)
            .WithName("DiffPageDraft")
            .WithSummary("Compares an unsaved payload against the stored draft, for a save conflict.")
            .RequireAuthorization(CmsPermissions.ContentRead)
            .RequireCmsAntiforgery();

        pages.MapPost("/{id:int}/draft/checkpoint", CheckpointDraftAsync)
            .WithName("CheckpointPageDraft")
            .WithSummary("Bookmarks the draft as a named, frozen version.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        pages.MapPatch("/{id:int}/metadata", PatchMetadataAsync)
            .WithName("PatchPageMetadata")
            .WithSummary("Updates title, slug, SEO, and editorial metadata. Omitted members are left alone.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        pages.MapPost("/{id:int}/move", MoveAsync)
            .WithName("MovePage")
            .WithSummary(
                "Reparents or reorders a page, rebuilding the URLs beneath it. " +
                "Send preview=true to be told what would happen without it happening.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        return group;
    }

    /// <remarks>
    /// The filters arrive as separate query parameters rather than as a bound object, so that an
    /// unrecognised one is visible in the route's own signature. <c>rootOnly</c> is a flag because a
    /// null <c>parentId</c> already means "do not filter by parent", and one nullable value cannot
    /// carry both meanings.
    /// </remarks>
    private static async Task<IResult> ListAsync(
        IPageService pages,
        CancellationToken cancellationToken,
        int? parentId = null,
        bool rootOnly = false,
        int? templateId = null,
        string? status = null,
        string? q = null,
        DateTimeOffset? modifiedAfter = null,
        string? cursor = null,
        int? limit = null) =>
        (await pages.ListAsync(
            new PageQuery(parentId, rootOnly, templateId, status, q, modifiedAfter, cursor, limit),
            cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> TreeAsync(
        IPageService pages,
        CancellationToken cancellationToken,
        int? parentId = null,
        int depth = 1) =>
        (await pages.TreeAsync(parentId, depth, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// Resolved with unpublished targets included, because the caller holds <c>Content.Read</c> and
    /// is looking at the backoffice: an editor linking to a section that goes live next week must be
    /// able to find its URL. The public delivery path resolves the same ids without that flag, so
    /// nothing here can leak a draft URL to an anonymous visitor.
    /// <para>
    /// A page that does not exist answers <c>404</c> rather than a link with a null URL, which are
    /// two different facts: "there is no such page" and "that page has no address you may see".
    /// </para>
    /// </remarks>
    private static async Task<IResult> GetLinkAsync(
        int id,
        ILinkResolver links,
        CancellationToken cancellationToken)
    {
        var resolved = await links.ResolveAsync([id], includeUnpublished: true, cancellationToken);

        return resolved.TryGetValue(id, out var link)
            ? Results.Ok(new PageLink(link.PageId, link.Url, link.IsPublished, link.Title))
            : Results.NotFound();
    }

    private static async Task<IResult> CreateAsync(
        CreatePageRequest request,
        IPageService pages,
        CancellationToken cancellationToken) =>
        (await pages.CreateAsync(request, cancellationToken))
        .ToHttpResult(created => Results.Created(
            $"{CmsApiEndpoints.BasePath}{Prefix}/{created.Summary.Id}",
            created));

    /// <remarks>
    /// Stamps the draft's <c>rowversion</c> as an <c>ETag</c>, which is the value a subsequent draft
    /// save has to echo back as <c>If-Match</c> (task P2-20).
    /// </remarks>
    private static async Task<IResult> GetAsync(
        int id,
        HttpContext httpContext,
        IPageService pages,
        CancellationToken cancellationToken) =>
        (await pages.GetAsync(id, cancellationToken)).ToHttpResult(page =>
        {
            ETags.Stamp(httpContext, page.RowVersion);

            return Results.Ok(page);
        });

    private static async Task<IResult> GetDraftAsync(
        int id,
        HttpContext httpContext,
        IDraftService drafts,
        CancellationToken cancellationToken) =>
        (await drafts.GetAsync(id, cancellationToken)).ToHttpResult(draft =>
        {
            ETags.Stamp(httpContext, draft.RowVersion);

            return Results.Ok(draft);
        });

    /// <remarks>
    /// The precondition is mandatory. An unconditional draft save is a lost update waiting for two
    /// editors to open the same page, and this is the one write in the API whose whole purpose is to
    /// be arbitrated; refusing with <c>428</c> tells a client exactly what it left out.
    /// <para>
    /// A conflict comes back as <c>409</c> carrying the stored draft, not as a bodiless <c>412</c>,
    /// so the losing editor can be offered keep-mine, take-theirs, or a diff (acceptance criterion
    /// P2 #8). <see cref="ETags"/> records the reasoning.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        HttpContext httpContext,
        IDraftService drafts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ETags.RequireIfMatch(httpContext, request.ExpectedRowVersion, out var expected) is { } refusal)
        {
            return refusal;
        }

        var result = await drafts.SaveAsync(
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
        IDraftService drafts,
        CancellationToken cancellationToken) =>
        (await drafts.DiscardAsync(id, cancellationToken)).ToHttpResult(draft =>
        {
            ETags.Stamp(httpContext, draft.RowVersion);

            return Results.Ok(draft);
        });

    /// <remarks>
    /// Answers the question a save conflict raises and the version diff cannot: both copies of a
    /// contested draft are the same version row, and the one that lost was never written.
    /// </remarks>
    private static async Task<IResult> DiffDraftAsync(
        int id,
        DiffDraftRequest request,
        IContentDiffService diffs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await diffs.CompareDraftAsync(id, request.ContentJson, cancellationToken))
            .ToHttpResult(value => Results.Ok(value));
    }

    private static async Task<IResult> CheckpointDraftAsync(
        int id,
        CheckpointDraftRequest? request,
        IDraftService drafts,
        CancellationToken cancellationToken) =>
        (await drafts.CheckpointAsync(id, request?.Label, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// <c>If-Match</c> is honoured but not required here, unlike on the draft save. A patch names the
    /// members it changes, so two concurrent patches to different fields merge rather than collide,
    /// and insisting on a precondition would make "clear the review date" fail for a client that had
    /// never read the page. A caller that did read first may still state one, and gets the same
    /// database-arbitrated check the draft save gets.
    /// </remarks>
    private static async Task<IResult> PatchMetadataAsync(
        int id,
        PatchPageMetadataRequest request,
        HttpContext httpContext,
        IPageService pages,
        CancellationToken cancellationToken) =>
        (await pages.PatchMetadataAsync(id, request, ETags.IfMatch(httpContext), cancellationToken))
        .ToHttpResult(page =>
        {
            ETags.Stamp(httpContext, page.RowVersion);

            return Results.Ok(page);
        });

    /// <remarks>
    /// A <c>POST</c> rather than a <c>PATCH</c> of the parent, and antiforgery-guarded like every
    /// other write, because a preview is a write in every respect except that it is rolled back: it
    /// opens a transaction, moves rows, and rebuilds routes. Treating it as a read would leave the
    /// most expensive operation in the page API reachable without a token.
    /// </remarks>
    private static async Task<IResult> MoveAsync(
        int id,
        MovePageRequest request,
        IPageService pages,
        CancellationToken cancellationToken) =>
        (await pages.MoveAsync(id, request, cancellationToken))
        .ToHttpResult(Results.Ok);
}
