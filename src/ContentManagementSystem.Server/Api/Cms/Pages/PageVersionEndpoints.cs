using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Pages;

/// <summary>
/// <c>/api/cms/v1/pages/{id}/versions</c> — history, one version, the diff, and restore (task P2-18).
/// </summary>
/// <remarks>
/// Nested under the page for the reason zones are nested under their template: a version number is
/// unique within a page and meaningless outside one, and the pair is the address. Asking for a
/// version of another page therefore answers 404 rather than handing back a row the caller did not
/// ask about.
/// </remarks>
public static class PageVersionEndpoints
{
    /// <summary>
    /// Maps the version endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapPageVersionEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var versions = group.MapGroup($"{PageEndpoints.Prefix}/{{pageId:int}}/versions")
            .WithTags("Pages");

        versions.MapGet("/", ListAsync)
            .WithName("ListPageVersions")
            .WithSummary("Lists a page's version history, newest first.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        versions.MapGet("/{versionId:int}", GetAsync)
            .WithName("GetPageVersion")
            .WithSummary("Reads one version with its payload.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        versions.MapGet("/{fromVersionId:int}/diff/{toVersionId:int}", DiffAsync)
            .WithName("DiffPageVersions")
            .WithSummary("Compares two versions of the page, matching blocks by their stable id.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        versions.MapPost("/{versionId:int}/restore", RestoreAsync)
            .WithName("RestorePageVersion")
            .WithSummary("Copies a version into the draft. The published version is untouched.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        return group;
    }

    /// <remarks>
    /// Deliberately unpaged, unlike the page collection. Retention caps a page's history at the last
    /// twenty versions plus whatever the policy protects (spec section 11.7), so the list is bounded
    /// by construction and a cursor would be ceremony over a set that fits on one screen.
    /// </remarks>
    private static async Task<IResult> ListAsync(
        int pageId,
        IVersionService versions,
        CancellationToken cancellationToken) =>
        (await versions.ListAsync(pageId, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> GetAsync(
        int pageId,
        int versionId,
        IVersionService versions,
        CancellationToken cancellationToken) =>
        (await versions.GetAsync(pageId, versionId, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// A <c>GET</c> even though it computes rather than reads: it has no side effects, it is
    /// idempotent, and the two version ids are the whole of its input, so a diff is bookmarkable and
    /// cacheable. Cost is bounded by the word-level diff degrading past ten thousand words
    /// (task P2-14), not by the method.
    /// </remarks>
    private static async Task<IResult> DiffAsync(
        int pageId,
        int fromVersionId,
        int toVersionId,
        IContentDiffService diffs,
        CancellationToken cancellationToken) =>
        (await diffs.CompareAsync(pageId, fromVersionId, toVersionId, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// Answers with the draft as it now stands, and stamps its new <c>ETag</c>: a restore rewrites
    /// the draft, so the token the editor was holding is stale the moment this returns and its next
    /// save would otherwise be refused as a conflict with itself.
    /// </remarks>
    private static async Task<IResult> RestoreAsync(
        int pageId,
        int versionId,
        HttpContext httpContext,
        IVersionService versions,
        CancellationToken cancellationToken) =>
        (await versions.RestoreAsync(pageId, versionId, cancellationToken)).ToHttpResult(draft =>
        {
            ETags.Stamp(httpContext, draft.RowVersion);

            return Results.Ok(draft);
        });
}
