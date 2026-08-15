using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Server.Api.Cms.Structure;

/// <summary>
/// <c>/api/cms/v1/block-types</c> — block types, their properties, and their compositions
/// (task P1-23).
/// </summary>
/// <remarks>
/// Properties are nested under the block type for the reason zones are nested under the template: a
/// property key is unique within its owner and meaningless outside it. Composed groups are addressed
/// here too, because composing one is a structural change to the <em>block type</em> — it cuts a
/// block type revision — even though the group it points at is edited elsewhere.
/// <para>
/// There is deliberately no block type delete. See <see cref="IBlockTypeService"/>.
/// </para>
/// </remarks>
public static class BlockTypeEndpoints
{
    /// <summary>Path segment the block type endpoints hang off.</summary>
    public const string GroupPath = "/block-types";

    /// <summary>
    /// Maps the block type endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapBlockTypeEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var blockTypes = group.MapGroup(GroupPath).WithTags("Structure");

        blockTypes.MapGet("/", ListAsync)
            .WithName("ListBlockTypes")
            .WithSummary("Lists every block type.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        blockTypes.MapGet("/{id:int}", GetAsync)
            .WithName("GetBlockType")
            .WithSummary("Reads one block type with its own, composed, and effective properties.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        blockTypes.MapPost("/", CreateAsync)
            .WithName("CreateBlockType")
            .WithSummary("Creates a block type and cuts its first revision.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .RequireCmsAntiforgery();

        blockTypes.MapPut("/{id:int}", UpdateAsync)
            .WithName("UpdateBlockType")
            .WithSummary("Updates a block type's editor-facing metadata.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .RequireCmsAntiforgery();

        blockTypes.MapGet("/{id:int}/revisions", ListRevisionsAsync)
            .WithName("ListBlockTypeRevisions")
            .WithSummary("Lists a block type's structural revisions, newest first.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        blockTypes.MapGet("/{id:int}/revisions/{revisionNumber:int}", GetRevisionAsync)
            .WithName("GetBlockTypeRevision")
            .WithSummary("Reads the flattened property set one revision captured.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        blockTypes.MapPost("/{id:int}/properties", CreatePropertyAsync)
            .WithName("CreateBlockTypeProperty")
            .WithSummary("Adds a property to a block type and cuts a new revision.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .RequireCmsAntiforgery();

        blockTypes.MapPut("/{id:int}/properties/{propertyId:int}", UpdatePropertyAsync)
            .WithName("UpdateBlockTypeProperty")
            .WithSummary("Updates a property. Its key and field type cannot change.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .RequireCmsAntiforgery();

        blockTypes.MapDelete("/{id:int}/properties/{propertyId:int}", DeletePropertyAsync)
            .WithName("DeleteBlockTypeProperty")
            .WithSummary("Removes a property. Values stored under its key are retained.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .RequireCmsAntiforgery();

        blockTypes.MapPost("/{id:int}/compositions", AttachCompositionAsync)
            .WithName("AttachComposition")
            .WithSummary("Composes a shared property group into a block type.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .RequireCmsAntiforgery();

        blockTypes.MapDelete("/{id:int}/compositions/{compositionId:int}", DetachCompositionAsync)
            .WithName("DetachComposition")
            .WithSummary("Removes a composed group from a block type.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .RequireCmsAntiforgery();

        return group;
    }

    private static async Task<IResult> ListAsync(
        IBlockTypeService blockTypes,
        CancellationToken cancellationToken) =>
        (await blockTypes.ListAsync(cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> GetAsync(
        int id,
        IBlockTypeService blockTypes,
        CancellationToken cancellationToken) =>
        (await blockTypes.GetAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> CreateAsync(
        CreateBlockTypeRequest request,
        IBlockTypeService blockTypes,
        CancellationToken cancellationToken) =>
        (await blockTypes.CreateAsync(request, cancellationToken))
        .ToHttpResult(created => Results.Created(
            $"{CmsApiEndpoints.BasePath}{GroupPath}/{created.BlockType.Id}",
            created));

    private static async Task<IResult> UpdateAsync(
        int id,
        UpdateBlockTypeRequest request,
        IBlockTypeService blockTypes,
        CancellationToken cancellationToken) =>
        (await blockTypes.UpdateAsync(id, request, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> ListRevisionsAsync(
        int id,
        IBlockTypeService blockTypes,
        CancellationToken cancellationToken) =>
        (await blockTypes.ListRevisionsAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> GetRevisionAsync(
        int id,
        int revisionNumber,
        IBlockTypeService blockTypes,
        CancellationToken cancellationToken) =>
        (await blockTypes.GetRevisionAsync(id, revisionNumber, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> CreatePropertyAsync(
        int id,
        CreatePropertyRequest request,
        IBlockTypeService blockTypes,
        CancellationToken cancellationToken) =>
        (await blockTypes.CreatePropertyAsync(id, request, cancellationToken))
        .ToHttpResult(created => Results.Created(
            $"{CmsApiEndpoints.BasePath}{GroupPath}/{id}/properties/{created.Property.Id}",
            created));

    private static async Task<IResult> UpdatePropertyAsync(
        int id,
        int propertyId,
        UpdatePropertyRequest request,
        IBlockTypeService blockTypes,
        CancellationToken cancellationToken) =>
        (await blockTypes.UpdatePropertyAsync(id, propertyId, request, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> DeletePropertyAsync(
        int id,
        int propertyId,
        IBlockTypeService blockTypes,
        CancellationToken cancellationToken) =>
        (await blockTypes.DeletePropertyAsync(id, propertyId, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> AttachCompositionAsync(
        int id,
        AttachCompositionRequest request,
        IBlockTypeService blockTypes,
        CancellationToken cancellationToken) =>
        (await blockTypes.AttachCompositionAsync(id, request, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> DetachCompositionAsync(
        int id,
        int compositionId,
        IBlockTypeService blockTypes,
        CancellationToken cancellationToken) =>
        (await blockTypes.DetachCompositionAsync(id, compositionId, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));
}
