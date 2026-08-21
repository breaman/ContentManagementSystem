using ContentManagementSystem.Core.Appearance;
using ContentManagementSystem.Shared.Contracts.Appearance;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Appearance;

/// <summary>
/// <c>/api/cms/v1/appearance/stylesheet</c> — the administrator-authored site stylesheet
/// (task P10-09, spec section 22.1).
/// </summary>
/// <remarks>
/// Every route here requires <c>Appearance.Edit</c>, including the reads: the draft is unpublished
/// work, and "what is the site about to look like" is not a question the backoffice answers to
/// everyone who can sign in.
/// <para>
/// The service checks the same permission again. The policy is the fast rejection at the door; the
/// service check is the one that still runs when something else calls it.
/// </para>
/// </remarks>
public static class AppearanceEndpoints
{
    /// <summary>Path segment this resource hangs off.</summary>
    public const string Prefix = "/appearance/stylesheet";

    /// <summary>
    /// Maps the appearance endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapAppearanceEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var stylesheet = group.MapGroup(Prefix).WithTags("Appearance");

        stylesheet.RequireAuthorization(CmsPermissions.AppearanceEdit);

        stylesheet.MapGet("/", GetAsync)
            .WithName("GetSiteStylesheet")
            .WithSummary("Reads the draft, the published copy, and the draft's diagnostics.");

        stylesheet.MapPut("/draft", SaveDraftAsync)
            .WithName("SaveSiteStylesheetDraft")
            .WithSummary("Saves the draft. Changes nothing an anonymous visitor receives.")
            .RequireCmsAntiforgery();

        stylesheet.MapPost("/validate", ValidateAsync)
            .WithName("ValidateSiteStylesheet")
            .WithSummary("Checks a stylesheet without storing it, for the editor's live diagnostics.")
            .RequireCmsAntiforgery();

        stylesheet.MapPost("/publish", PublishAsync)
            .WithName("PublishSiteStylesheet")
            .WithSummary("Publishes the draft. Every visitor sees it on their next request.")
            .RequireCmsAntiforgery();

        stylesheet.MapPost("/revert", RevertAsync)
            .WithName("RevertSiteStylesheet")
            .WithSummary("Publishes an earlier revision, or publishes nothing at all.")
            .RequireCmsAntiforgery();

        stylesheet.MapGet("/revisions", ListRevisionsAsync)
            .WithName("ListSiteStylesheetRevisions")
            .WithSummary("Lists the published history, newest first.");

        stylesheet.MapGet("/revisions/{id:int}", GetRevisionAsync)
            .WithName("GetSiteStylesheetRevision")
            .WithSummary("Reads one revision's CSS.");

        return group;
    }

    private static async Task<IResult> GetAsync(
        HttpContext httpContext,
        ISiteStylesheetService stylesheet,
        CancellationToken cancellationToken) =>
        (await stylesheet.GetAsync(cancellationToken)).ToHttpResult(detail => Stamped(httpContext, detail));

    private static async Task<IResult> SaveDraftAsync(
        SaveSiteStylesheetDraftRequest request,
        HttpContext httpContext,
        ISiteStylesheetService stylesheet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await stylesheet.SaveDraftAsync(
            request.Css,
            ETags.IfMatch(httpContext),
            cancellationToken);

        return result.ToHttpResult(detail => Stamped(httpContext, detail));
    }

    private static async Task<IResult> ValidateAsync(
        ValidateSiteStylesheetRequest request,
        ISiteStylesheetService stylesheet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await stylesheet.ValidateAsync(request.Css, cancellationToken)).ToHttpResult(Results.Ok);
    }

    private static async Task<IResult> PublishAsync(
        PublishSiteStylesheetRequest? request,
        HttpContext httpContext,
        ISiteStylesheetService stylesheet,
        CancellationToken cancellationToken) =>
        (await stylesheet.PublishAsync(request?.Note, cancellationToken))
        .ToHttpResult(detail => Stamped(httpContext, detail));

    private static async Task<IResult> RevertAsync(
        RevertSiteStylesheetRequest request,
        HttpContext httpContext,
        ISiteStylesheetService stylesheet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await stylesheet.RevertAsync(request.RevisionId, request.CopyToDraft, cancellationToken))
            .ToHttpResult(detail => Stamped(httpContext, detail));
    }

    private static async Task<IResult> ListRevisionsAsync(
        ISiteStylesheetService stylesheet,
        CancellationToken cancellationToken) =>
        (await stylesheet.ListRevisionsAsync(cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> GetRevisionAsync(
        int id,
        ISiteStylesheetService stylesheet,
        CancellationToken cancellationToken) =>
        (await stylesheet.GetRevisionCssAsync(id, cancellationToken)).ToHttpResult(Results.Ok);

    /// <summary>
    /// Answers with the stylesheet and its entity tag, so the next save can be conditional.
    /// </summary>
    private static IResult Stamped(HttpContext httpContext, SiteStylesheetDetail detail)
    {
        ETags.Stamp(httpContext, detail.RowVersion);

        return Results.Ok(detail);
    }
}
