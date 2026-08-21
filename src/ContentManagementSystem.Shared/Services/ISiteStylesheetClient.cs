using ContentManagementSystem.Shared.Contracts.Appearance;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Shared.Services;

/// <summary>
/// What the stylesheet editor needs, wherever it happens to be running (task P10-11).
/// </summary>
/// <remarks>
/// Two implementations, like every other client here: one over HTTP for the WebAssembly backoffice,
/// and one over the service directly for pre-rendering — a request to itself would need a cookie it
/// does not have and an antiforgery token that has not been issued yet.
/// </remarks>
public interface ISiteStylesheetClient
{
    /// <summary>Reads the draft, the published copy, and the draft's diagnostics.</summary>
    /// <param name="cancellationToken">Token observed while reading.</param>
    Task<SiteStylesheetDetail?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks a stylesheet without storing it.</summary>
    /// <param name="css">The stylesheet as it currently stands in the editor.</param>
    /// <param name="cancellationToken">Token observed while checking.</param>
    Task<CssValidationReport?> ValidateAsync(string css, CancellationToken cancellationToken = default);

    /// <summary>Saves the draft.</summary>
    /// <param name="css">The whole stylesheet.</param>
    /// <param name="rowVersion">The concurrency token last read, sent as <c>If-Match</c>.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<SiteStylesheetDetail>> SaveDraftAsync(
        string css,
        string? rowVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes the draft.</summary>
    /// <param name="note">What the change was for.</param>
    /// <param name="cancellationToken">Token observed while publishing.</param>
    Task<StructureClientResult<SiteStylesheetDetail>> PublishAsync(
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes an earlier revision, or publishes nothing.</summary>
    /// <param name="revisionId">The revision, or null for nothing.</param>
    /// <param name="copyToDraft">Whether to load the reverted CSS into the draft as well.</param>
    /// <param name="cancellationToken">Token observed while reverting.</param>
    Task<StructureClientResult<SiteStylesheetDetail>> RevertAsync(
        int? revisionId,
        bool copyToDraft,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the published history, newest first.</summary>
    /// <param name="cancellationToken">Token observed while reading.</param>
    Task<IReadOnlyList<SiteStylesheetRevisionSummary>> GetRevisionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Reads one revision's CSS.</summary>
    /// <param name="revisionId">The revision.</param>
    /// <param name="cancellationToken">Token observed while reading.</param>
    Task<string?> GetRevisionCssAsync(int revisionId, CancellationToken cancellationToken = default);
}
