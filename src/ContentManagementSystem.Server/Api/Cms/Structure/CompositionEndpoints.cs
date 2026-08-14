using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Server.Api.Cms.Structure;

/// <summary>
/// <c>/api/cms/v1/compositions</c> and <c>/field-types</c> — the rest of the structure API
/// (task P1-24).
/// </summary>
/// <remarks>
/// The two live together because they are the two halves of what a developer needs before defining
/// anything: which shared property groups exist, and which field types a property may be bound to.
/// One is data and writable, the other is code and strictly read-only.
/// </remarks>
public static class CompositionEndpoints
{
    /// <summary>Path segment the composition endpoints hang off.</summary>
    public const string GroupPath = "/compositions";

    /// <summary>Path segment the field type introspection endpoints hang off.</summary>
    public const string FieldTypesPath = "/field-types";

    /// <summary>
    /// Maps the composition and field-type endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapCompositionEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var compositions = group.MapGroup(GroupPath).WithTags("Structure");

        compositions.MapGet("/", ListAsync)
            .WithName("ListCompositions")
            .WithSummary("Lists every shared property group, with how far each one reaches.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        compositions.MapGet("/{id:int}", GetAsync)
            .WithName("GetComposition")
            .WithSummary("Reads one composition with its properties and where it is used.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        compositions.MapPost("/", CreateAsync)
            .WithName("CreateComposition")
            .WithSummary("Creates a shared property group.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .AddEndpointFilter<CmsAntiforgeryFilter>();

        compositions.MapPut("/{id:int}", UpdateAsync)
            .WithName("UpdateComposition")
            .WithSummary("Updates a composition's editor-facing metadata.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .AddEndpointFilter<CmsAntiforgeryFilter>();

        compositions.MapDelete("/{id:int}", DeleteAsync)
            .WithName("DeleteComposition")
            .WithSummary("Deletes a composition. Refused while any block type composes it.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .AddEndpointFilter<CmsAntiforgeryFilter>();

        compositions.MapPost("/{id:int}/properties", CreatePropertyAsync)
            .WithName("CreateCompositionProperty")
            .WithSummary("Adds a property and recuts every block type composing the group.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .AddEndpointFilter<CmsAntiforgeryFilter>();

        compositions.MapPut("/{id:int}/properties/{propertyId:int}", UpdatePropertyAsync)
            .WithName("UpdateCompositionProperty")
            .WithSummary("Updates a property. Its key and field type cannot change.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .AddEndpointFilter<CmsAntiforgeryFilter>();

        compositions.MapDelete("/{id:int}/properties/{propertyId:int}", DeletePropertyAsync)
            .WithName("DeleteCompositionProperty")
            .WithSummary("Removes a property and recuts every block type composing the group.")
            .RequireAuthorization(CmsPermissions.StructureEdit)
            .AddEndpointFilter<CmsAntiforgeryFilter>();

        var fieldTypes = group.MapGroup(FieldTypesPath).WithTags("Structure");

        // Read-only, with no write verbs at all rather than write verbs that refuse. A field type
        // arrives with a deployment; there is no state here for a client to change.
        fieldTypes.MapGet("/", ListFieldTypes)
            .WithName("ListFieldTypes")
            .WithSummary("Lists every registered field type with its configuration JSON Schema.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        fieldTypes.MapGet("/{key}", GetFieldType)
            .WithName("GetFieldType")
            .WithSummary("Reads one field type's configuration JSON Schema.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        return group;
    }

    private static async Task<IResult> ListAsync(
        ICompositionService compositions,
        CancellationToken cancellationToken) =>
        (await compositions.ListAsync(cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> GetAsync(
        int id,
        ICompositionService compositions,
        CancellationToken cancellationToken) =>
        (await compositions.GetAsync(id, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> CreateAsync(
        CreateCompositionRequest request,
        ICompositionService compositions,
        CancellationToken cancellationToken) =>
        (await compositions.CreateAsync(request, cancellationToken))
        .ToHttpResult(created => Results.Created(
            $"{CmsApiEndpoints.BasePath}{GroupPath}/{created.Composition.Id}",
            created));

    private static async Task<IResult> UpdateAsync(
        int id,
        UpdateCompositionRequest request,
        ICompositionService compositions,
        CancellationToken cancellationToken) =>
        (await compositions.UpdateAsync(id, request, cancellationToken)).ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> DeleteAsync(
        int id,
        ICompositionService compositions,
        CancellationToken cancellationToken) =>
        (await compositions.DeleteAsync(id, cancellationToken)).ToHttpResult(_ => Results.NoContent());

    private static async Task<IResult> CreatePropertyAsync(
        int id,
        CreatePropertyRequest request,
        ICompositionService compositions,
        CancellationToken cancellationToken) =>
        (await compositions.CreatePropertyAsync(id, request, cancellationToken))
        .ToHttpResult(created => Results.Created(
            $"{CmsApiEndpoints.BasePath}{GroupPath}/{id}/properties/{created.Property.Id}",
            created));

    private static async Task<IResult> UpdatePropertyAsync(
        int id,
        int propertyId,
        UpdatePropertyRequest request,
        ICompositionService compositions,
        CancellationToken cancellationToken) =>
        (await compositions.UpdatePropertyAsync(id, propertyId, request, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    private static async Task<IResult> DeletePropertyAsync(
        int id,
        int propertyId,
        ICompositionService compositions,
        CancellationToken cancellationToken) =>
        (await compositions.DeletePropertyAsync(id, propertyId, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    private static IResult ListFieldTypes(IFieldTypeCatalog fieldTypes) => Results.Ok(fieldTypes.All);

    private static IResult GetFieldType(string key, IFieldTypeCatalog fieldTypes) =>
        fieldTypes.Find(key) is { } descriptor
            ? Results.Ok(descriptor)
            : CmsProblems.Problem(
                System.Net.HttpStatusCode.NotFound,
                "not-found",
                "Not found",
                Shared.Contracts.Fields.ValidationResult.Error(
                    StructureCodes.NotFound,
                    $"No field type is registered under the key '{key}'."));
}
