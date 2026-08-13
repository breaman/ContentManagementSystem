using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Server.Api.Cms.Structure;

/// <summary>
/// <c>/api/cms/v1/templates</c> — the template half of the structure API (task P1-21).
/// </summary>
/// <remarks>
/// Handlers do nothing but bind, call, and map. Every rule about what a template may be lives in
/// <see cref="ITemplateService"/>, which is also where the permission check runs; an endpoint that
/// decided anything for itself would be a rule the CLI verbs and the schema sync do not get.
/// <para>
/// There is deliberately no delete. See <see cref="ITemplateService"/> for why it waits for the page
/// table it must guard against.
/// </para>
/// </remarks>
public static class TemplateEndpoints
{
    /// <summary>
    /// Maps the template endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapTemplateEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var templates = group.MapGroup("/templates")
            .WithTags("Structure");

        templates.MapGet("/", ListAsync)
            .WithName("ListTemplates")
            .WithSummary("Lists every template.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        templates.MapGet("/{id:int}", GetAsync)
            .WithName("GetTemplate")
            .WithSummary("Reads one template and its current zone definitions.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        templates.MapPost("/", CreateAsync)
            .WithName("CreateTemplate")
            .WithSummary("Creates a template and cuts its first revision.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .AddEndpointFilter<CmsAntiforgeryFilter>();

        templates.MapPut("/{id:int}", UpdateAsync)
            .WithName("UpdateTemplate")
            .WithSummary("Updates a template's editor-facing metadata.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .AddEndpointFilter<CmsAntiforgeryFilter>();

        templates.MapGet("/{id:int}/revisions", ListRevisionsAsync)
            .WithName("ListTemplateRevisions")
            .WithSummary("Lists a template's structural revisions, newest first.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        templates.MapGet("/{id:int}/revisions/{revisionNumber:int}", GetRevisionAsync)
            .WithName("GetTemplateRevision")
            .WithSummary("Reads the zone definitions one revision captured.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        return group;
    }

    /// <remarks>
    /// Unpaged, unlike the page collections. A deployment has tens of templates — they are written
    /// by developers, one per page shape — and a cursor here would be ceremony over a list that
    /// fits on a screen.
    /// </remarks>
    private static async Task<IResult> ListAsync(
        ITemplateService templates,
        CancellationToken cancellationToken) =>
        (await templates.ListAsync(cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> GetAsync(
        int id,
        ITemplateService templates,
        CancellationToken cancellationToken) =>
        (await templates.GetAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> CreateAsync(
        CreateTemplateRequest request,
        ITemplateService templates,
        CancellationToken cancellationToken) =>
        (await templates.CreateAsync(request, cancellationToken))
        .ToHttpResult(created => Results.Created($"/api/cms/v1/templates/{created.Template.Id}", created));

    private static async Task<IResult> UpdateAsync(
        int id,
        UpdateTemplateRequest request,
        ITemplateService templates,
        CancellationToken cancellationToken) =>
        (await templates.UpdateAsync(id, request, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> ListRevisionsAsync(
        int id,
        ITemplateService templates,
        CancellationToken cancellationToken) =>
        (await templates.ListRevisionsAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> GetRevisionAsync(
        int id,
        int revisionNumber,
        ITemplateService templates,
        CancellationToken cancellationToken) =>
        (await templates.GetRevisionAsync(id, revisionNumber, cancellationToken)).ToHttpResult(value => Results.Ok(value));
}
