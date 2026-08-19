using ContentManagementSystem.Core.Search;
using ContentManagementSystem.Core.Tags;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Search;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Search;

/// <summary>
/// <c>/api/cms/v1/search</c> and <c>/api/cms/v1/tags</c> — the backoffice search box, its filters,
/// and the tag vocabulary they narrow by (tasks P8-19, P8-20).
/// </summary>
/// <remarks>
/// Search is a <c>GET</c> with the filters in the query string, so a search an editor wants to keep
/// is a URL they can bookmark and send to somebody — which is most of what "saved search" would
/// otherwise have to be built for.
/// <para>
/// Reading needs <c>Content.Read</c> and the results are cut by the caller's access rules in the
/// service, not here: a search that returned the titles of pages the caller may not open would be a
/// disclosure the endpoint has no way to notice.
/// </para>
/// </remarks>
public static class SearchEndpoints
{
    /// <summary>Path segment the search resource hangs off.</summary>
    public const string SearchPrefix = "/search";

    /// <summary>Path segment the tag resource hangs off.</summary>
    public const string TagsPrefix = "/tags";

    /// <summary>
    /// Maps the search and tag endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet(SearchPrefix, SearchAsync)
            .WithName("SearchContent")
            .WithTags("Search")
            .WithSummary("Searches pages, media, and reusable content, with the backoffice filters.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        var tags = group.MapGroup(TagsPrefix).WithTags("Tags");

        tags.MapGet("/", ListTagsAsync)
            .WithName("ListTags")
            .WithSummary("Lists every tag with the number of pages carrying it.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        tags.MapGet("/suggest", SuggestTagsAsync)
            .WithName("SuggestTags")
            .WithSummary("Suggests tags for what an editor has typed so far.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        tags.MapPut("/{id:int}", RenameTagAsync)
            .WithName("RenameTag")
            .WithSummary("Renames a tag everywhere, merging it if the new name is already taken.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        tags.MapDelete("/{id:int}", DeleteTagAsync)
            .WithName("DeleteTag")
            .WithSummary("Deletes a tag and takes it off every page carrying it.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        return group;
    }

    private static async Task<IResult> SearchAsync(
        ISearchService search,
        CancellationToken cancellationToken,
        string? q = null,
        SearchResultKind? kind = null,
        int? templateId = null,
        string? status = null,
        int? ownerUserId = null,
        string? tag = null,
        DateTimeOffset? modifiedFrom = null,
        DateTimeOffset? modifiedTo = null,
        bool? hasUnpublishedChanges = null,
        bool pastReviewDate = false,
        int skip = 0,
        int? limit = null) =>
        (await search.SearchAsync(
            new SearchQuery(
                q,
                kind,
                templateId,
                status,
                ownerUserId,
                tag,
                modifiedFrom,
                modifiedTo,
                hasUnpublishedChanges,
                pastReviewDate,
                skip,
                limit),
            cancellationToken))
        .ToHttpResult(Results.Ok);

    private static async Task<IResult> ListTagsAsync(
        ITagService tags,
        CancellationToken cancellationToken) =>
        (await tags.ListAsync(cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> SuggestTagsAsync(
        ITagService tags,
        CancellationToken cancellationToken,
        string? prefix = null,
        int limit = 10) =>
        (await tags.SuggestAsync(prefix, limit, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> RenameTagAsync(
        int id,
        RenameTagRequest request,
        ITagService tags,
        CancellationToken cancellationToken) =>
        (await tags.RenameAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> DeleteTagAsync(
        int id,
        ITagService tags,
        CancellationToken cancellationToken) =>
        (await tags.DeleteAsync(id, cancellationToken)).ToHttpResult(Results.Ok);
}
