using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Server.Api.Cms.Structure;

/// <summary>
/// <c>/api/cms/v1/templates/{templateId}/zones</c> — the zone half of the structure API (task P1-22).
/// </summary>
/// <remarks>
/// Nested under the template because a zone has no identity apart from one: its key is unique within
/// a template and meaningless outside it, and addressing zones at the top level would make it
/// possible to ask for a zone without saying which template's content model is being changed.
/// <para>
/// Handlers bind, call, and map, exactly as the template endpoints do. Every rule about what a zone
/// may become — and in particular the evolution rules of spec section 8.5 — lives in
/// <see cref="IZoneService"/>.
/// </para>
/// </remarks>
public static class ZoneEndpoints
{
    /// <summary>
    /// Maps the zone endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapZoneEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var zones = group.MapGroup("/templates/{templateId:int}/zones")
            .WithTags("Structure");

        zones.MapGet("/", ListAsync)
            .WithName("ListZones")
            .WithSummary("Lists a template's zones in editor order.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        zones.MapGet("/{zoneId:int}", GetAsync)
            .WithName("GetZone")
            .WithSummary("Reads one zone definition.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        zones.MapPost("/", CreateAsync)
            .WithName("CreateZone")
            .WithSummary("Adds a zone to a template and cuts a new revision.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .RequireCmsAntiforgery();

        zones.MapPut("/{zoneId:int}", UpdateAsync)
            .WithName("UpdateZone")
            .WithSummary("Updates a zone. Its key and field type cannot change.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .RequireCmsAntiforgery();

        zones.MapDelete("/{zoneId:int}", DeleteAsync)
            .WithName("DeleteZone")
            .WithSummary("Removes a zone definition. Payload values under its key are retained.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .RequireCmsAntiforgery();

        return group;
    }

    private static async Task<IResult> ListAsync(
        int templateId,
        IZoneService zones,
        CancellationToken cancellationToken) =>
        (await zones.ListAsync(templateId, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> GetAsync(
        int templateId,
        int zoneId,
        IZoneService zones,
        CancellationToken cancellationToken) =>
        (await zones.GetAsync(templateId, zoneId, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> CreateAsync(
        int templateId,
        CreateZoneRequest request,
        IZoneService zones,
        CancellationToken cancellationToken) =>
        (await zones.CreateAsync(templateId, request, cancellationToken))
        .ToHttpResult(created => Results.Created(
            $"{CmsApiEndpoints.BasePath}/templates/{templateId}/zones/{created.Zone.Id}",
            created));

    private static async Task<IResult> UpdateAsync(
        int templateId,
        int zoneId,
        UpdateZoneRequest request,
        IZoneService zones,
        CancellationToken cancellationToken) =>
        (await zones.UpdateAsync(templateId, zoneId, request, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// Answers with a body rather than a 204. Removing a zone cuts a revision and leaves stored
    /// content orphaned under the key; both facts are things the admin screen has to say, and a
    /// no-content response would make it refetch the template to learn them.
    /// </remarks>
    private static async Task<IResult> DeleteAsync(
        int templateId,
        int zoneId,
        IZoneService zones,
        CancellationToken cancellationToken) =>
        (await zones.DeleteAsync(templateId, zoneId, cancellationToken)).ToHttpResult(value => Results.Ok(value));
}
