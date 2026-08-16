using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Reusable;

/// <summary>
/// <c>/api/cms/v1/references</c> — where-used, for anything content can point at (task P4-08,
/// spec section 9.4).
/// </summary>
/// <remarks>
/// One endpoint per target kind rather than one taking a type parameter, so that a client cannot ask
/// a question the system has no answer for by mistyping a string, and so each route can carry the
/// permission its subject actually needs.
/// <para>
/// The media route ships now although the media library is P5. Its answer is honest today — nothing
/// references a media item that does not exist, and the reference rows media placements project have
/// existed since P1 — and shipping the route with the others means the backoffice's where-used panel
/// is written once rather than grown a third branch later.
/// </para>
/// </remarks>
public static class ReferenceEndpoints
{
    /// <summary>Route prefix every where-used endpoint hangs off.</summary>
    internal const string Prefix = "/references";

    /// <summary>
    /// Maps the where-used endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapReferenceEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var references = group.MapGroup(Prefix).WithTags("References");

        references.MapGet("/pages/{id:int}", PagesAsync)
            .WithName("GetPageReferences")
            .WithSummary("Lists the pages and reusable items whose content links to a page.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        references.MapGet("/media/{id:int}", MediaAsync)
            .WithName("GetMediaReferences")
            .WithSummary("Lists the pages and reusable items whose content shows a media item.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        references.MapGet("/reusable/{id:int}", ReusableAsync)
            .WithName("GetReusableContentReferences")
            .WithSummary("Lists the pages and reusable items that place a reusable item, with impact counts.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        return group;
    }

    private static async Task<IResult> PagesAsync(
        int id,
        IReferenceQueryService references,
        ICmsAuthorization authorization,
        CancellationToken cancellationToken) =>
        await AnswerAsync(
            ContentReferenceTargetType.Page,
            id,
            references,
            authorization,
            cancellationToken);

    private static async Task<IResult> MediaAsync(
        int id,
        IReferenceQueryService references,
        ICmsAuthorization authorization,
        CancellationToken cancellationToken) =>
        await AnswerAsync(
            ContentReferenceTargetType.Media,
            id,
            references,
            authorization,
            cancellationToken);

    private static async Task<IResult> ReusableAsync(
        int id,
        IReferenceQueryService references,
        ICmsAuthorization authorization,
        CancellationToken cancellationToken) =>
        await AnswerAsync(
            ContentReferenceTargetType.ReusableContent,
            id,
            references,
            authorization,
            cancellationToken);

    /// <summary>
    /// Answers a where-used question for one target.
    /// </summary>
    /// <remarks>
    /// The permission check is here rather than in <c>IReferenceQueryService</c>, and deliberately
    /// so: that service is called from inside operations that have already authorized their caller —
    /// a publish, a delete guard — and a second check there would let a publish succeed while the
    /// impact list it is required to record came back empty. The endpoint policy is the door; this is
    /// the lock behind it, matching the pattern <c>CONTRIBUTING.md</c> describes.
    /// <para>
    /// An entity nothing points at answers <c>200</c> with an empty impact rather than <c>404</c>.
    /// "Nothing uses this" is the answer, and it is the one the delete button needs; distinguishing
    /// it from "no such entity" would put an existence probe for every id in the system behind a read
    /// permission that does not otherwise grant one.
    /// </para>
    /// </remarks>
    private static async Task<IResult> AnswerAsync(
        ContentReferenceTargetType targetType,
        int id,
        IReferenceQueryService references,
        ICmsAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(authorization);

        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<ReferenceImpact>
                .Forbidden("Reading content is not permitted.", ReusableCodes.Forbidden)
                .ToHttpResult(value => Results.Ok(value));
        }

        return Results.Ok(await references.WhereUsedAsync(targetType, id, cancellationToken));
    }
}
