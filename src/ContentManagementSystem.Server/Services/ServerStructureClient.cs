using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="IStructureClient"/>, over the structure services directly
/// (task P1-29).
/// </summary>
/// <param name="templates">Template reads and writes.</param>
/// <param name="zones">Zone reads and writes.</param>
/// <param name="blockTypes">Block type reads and writes.</param>
/// <param name="fieldTypes">The registered field types and their configuration schemas.</param>
/// <param name="gate">Keeps concurrently initializing components off each other's database work.</param>
/// <remarks>
/// Used during pre-rendering, so a structure screen arrives with its content already in the HTML
/// rather than showing a spinner until the WebAssembly runtime finishes downloading. It calls the
/// services rather than looping back through its own HTTP API — a request to itself would need a
/// cookie it does not have and an antiforgery token that has not been issued yet.
/// <para>
/// Authorization is unaffected by the shortcut: every one of these services checks the caller's
/// permissions itself, against the same request principal the API would have seen. That is exactly
/// why <c>CONTRIBUTING.md</c> puts the check in the service layer rather than on the endpoint.
/// </para>
/// </remarks>
public sealed class ServerStructureClient(
    ITemplateService templates,
    IZoneService zones,
    IBlockTypeService blockTypes,
    IFieldTypeCatalog fieldTypes,
    PrerenderGate gate) : IStructureClient
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateSummary>> GetTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => templates.ListAsync(token), cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<TemplateDetail?> GetTemplateAsync(int id, CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => templates.GetAsync(id, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<StructureClientResult<TemplateDetail>> CreateTemplateAsync(
        CreateTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => templates.CreateAsync(request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<ZoneSaveResult>> CreateZoneAsync(
        int templateId,
        CreateZoneRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => zones.CreateAsync(templateId, request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<ZoneSaveResult>> UpdateZoneAsync(
        int templateId,
        int zoneId,
        UpdateZoneRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => zones.UpdateAsync(templateId, zoneId, request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<ZoneRemovalResult>> DeleteZoneAsync(
        int templateId,
        int zoneId,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => zones.DeleteAsync(templateId, zoneId, token), cancellationToken));

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => blockTypes.ListAsync(token), cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<BlockTypeDetail?> GetBlockTypeAsync(int id, CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => blockTypes.GetAsync(id, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<StructureClientResult<BlockTypeDetail>> CreateBlockTypeAsync(
        CreateBlockTypeRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => blockTypes.CreateAsync(request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<PropertySaveResult>> CreatePropertyAsync(
        int blockTypeId,
        CreatePropertyRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(
            token => blockTypes.CreatePropertyAsync(blockTypeId, request, token),
            cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<PropertySaveResult>> UpdatePropertyAsync(
        int blockTypeId,
        int propertyId,
        UpdatePropertyRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(
            token => blockTypes.UpdatePropertyAsync(blockTypeId, propertyId, request, token),
            cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<PropertyRemovalResult>> DeletePropertyAsync(
        int blockTypeId,
        int propertyId,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(
            token => blockTypes.DeletePropertyAsync(blockTypeId, propertyId, token),
            cancellationToken));

    /// <inheritdoc />
    public Task<IReadOnlyList<FieldTypeDescriptor>> GetFieldTypesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(fieldTypes.All);

    /// <summary>
    /// Turns a service outcome into what a screen shows.
    /// </summary>
    /// <remarks>
    /// The same projection <c>CmsProblems</c> applies on the way out to HTTP, so a screen sees the
    /// identical diagnostics whether it was pre-rendered on the server or is running in the browser.
    /// A pre-render that reported failures differently would be a second error contract nobody
    /// tests.
    /// </remarks>
    private static StructureClientResult<T> Project<T>(CmsResult<T> result) =>
        result.IsSuccess
            ? StructureClientResult<T>.Success(
                result.Value!,
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning))
            : StructureClientResult<T>.Failure(
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Error),
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning));
}
