using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Appearance;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Appearance;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="ISiteStylesheetClient"/>, over the stylesheet service directly
/// (task P10-11).
/// </summary>
/// <param name="stylesheet">The editing service, which checks <c>Appearance.Edit</c> itself.</param>
/// <param name="gate">Keeps concurrently initializing components off each other's database work.</param>
/// <remarks>
/// Used during pre-rendering, so the editor arrives with the administrator's CSS already in the
/// page rather than showing an empty box until the WebAssembly runtime has downloaded — which on a
/// text editor is the difference between "loading" and "your work is gone".
/// </remarks>
public sealed class ServerSiteStylesheetClient(ISiteStylesheetService stylesheet, PrerenderGate gate)
    : ISiteStylesheetClient
{
    /// <inheritdoc />
    public async Task<SiteStylesheetDetail?> GetAsync(CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => stylesheet.GetAsync(token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<CssValidationReport?> ValidateAsync(
        string css,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => stylesheet.ValidateAsync(css, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<StructureClientResult<SiteStylesheetDetail>> SaveDraftAsync(
        string css,
        string? rowVersion,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(
            token => stylesheet.SaveDraftAsync(css, rowVersion, token),
            cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<SiteStylesheetDetail>> PublishAsync(
        string? note,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => stylesheet.PublishAsync(note, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<SiteStylesheetDetail>> RevertAsync(
        int? revisionId,
        bool copyToDraft,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(
            token => stylesheet.RevertAsync(revisionId, copyToDraft, token),
            cancellationToken));

    /// <inheritdoc />
    public async Task<IReadOnlyList<SiteStylesheetRevisionSummary>> GetRevisionsAsync(
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => stylesheet.ListRevisionsAsync(token), cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<string?> GetRevisionCssAsync(
        int revisionId,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(
            token => stylesheet.GetRevisionCssAsync(revisionId, token),
            cancellationToken)).Value;

    /// <summary>Narrows a service result to what a screen needs from it.</summary>
    private static StructureClientResult<T> Project<T>(CmsResult<T> result) =>
        result.IsSuccess
            ? StructureClientResult<T>.Success(
                result.Value!,
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning))
            : StructureClientResult<T>.Failure(
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Error),
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning));
}
