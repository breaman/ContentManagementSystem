using ContentManagementSystem.Core.Preview;
using ContentManagementSystem.Shared.Contracts.Preview;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Preview;

/// <summary>
/// <c>/api/cms/v1/preview-tokens</c> — issuing and revoking shareable preview links (task P3-19,
/// spec section 12.2).
/// </summary>
/// <remarks>
/// Writes require <c>Content.Edit</c> rather than <c>Content.Publish</c>. Sharing work for review is
/// the ordinary act of whoever is doing the work, and a link that needs the publish permission to
/// create would mean an author cannot get their own draft looked at — which is the entire feature
/// (spec section 21.1).
/// <para>
/// <strong><c>DELETE</c> revokes; it does not delete.</strong> The row is stamped and kept, because
/// "this link was revoked on the 3rd, by this person" is the answer somebody needs when a
/// stakeholder reports that a link stopped working, and it is also the only record of who could once
/// read an unpublished page. A verb that removed the row would destroy that at the exact moment it
/// starts mattering.
/// </para>
/// </remarks>
public static class PreviewTokenEndpoints
{
    /// <summary>Path segment this resource hangs off.</summary>
    public const string Prefix = "/preview-tokens";

    /// <summary>
    /// Maps the preview-token endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapPreviewTokenEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var tokens = group.MapGroup(Prefix).WithTags("Preview");

        tokens.MapGet("/", ListAsync)
            .WithName("ListPreviewTokens")
            .WithSummary("Lists the preview links issued for a page, newest first.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        tokens.MapPost("/", IssueAsync)
            .WithName("CreatePreviewToken")
            .WithSummary("Issues a shareable preview link, returning its secret once.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        tokens.MapDelete("/{id:int}", RevokeAsync)
            .WithName("RevokePreviewToken")
            .WithSummary("Revokes one preview link, keeping the row as a record.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        tokens.MapDelete("/", RevokeAllAsync)
            .WithName("RevokeAllPreviewTokens")
            .WithSummary("Revokes every live preview link for a page.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        return group;
    }

    /// <remarks>
    /// Scoped to a page rather than listing every token on the site. A link is meaningful only
    /// beside the page it shares, and an unscoped list of every outstanding disclosure would be a
    /// screen nobody could act on.
    /// </remarks>
    private static async Task<IResult> ListAsync(
        int pageId,
        IPreviewTokenService tokens,
        CancellationToken cancellationToken) =>
        (await tokens.ListAsync(pageId, cancellationToken)).ToHttpResult(Results.Ok);

    /// <remarks>
    /// <c>201</c> with the secret in the body, and no <c>Location</c> pointing at a resource that
    /// would return it again — because nothing ever returns it again. The response to this request
    /// is the only place the token exists outside the recipient's mailbox (spec section 12.2).
    /// </remarks>
    private static async Task<IResult> IssueAsync(
        CreatePreviewTokenRequest request,
        IPreviewTokenService tokens,
        CancellationToken cancellationToken) =>
        (await tokens.IssueAsync(request, cancellationToken))
        .ToHttpResult(issued => Results.Created(
            $"{CmsApiEndpoints.BasePath}{Prefix}?pageId={issued.Summary.PageId}",
            issued));

    private static async Task<IResult> RevokeAsync(
        int id,
        IPreviewTokenService tokens,
        CancellationToken cancellationToken) =>
        (await tokens.RevokeAsync(id, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> RevokeAllAsync(
        int pageId,
        IPreviewTokenService tokens,
        CancellationToken cancellationToken) =>
        (await tokens.RevokeAllAsync(pageId, cancellationToken))
        .ToHttpResult(revoked => Results.Ok(new { Revoked = revoked }));
}
