using ContentManagementSystem.Shared.Contracts.Appearance;

namespace ContentManagementSystem.Core.Appearance;

/// <summary>
/// The administrator-authored site stylesheet: drafted, validated, published, and reverted
/// (spec section 30, D27).
/// </summary>
/// <remarks>
/// Everything here enforces <c>Appearance.Edit</c> itself rather than trusting the endpoint that
/// called it. The endpoint policy is a fast rejection at the door; this is the check that still runs
/// when the service is reached from a second endpoint somebody forgot to decorate.
/// <para>
/// <strong>Reading the published stylesheet is not on this interface.</strong> Delivery reads it
/// through <see cref="IPublishedStylesheetReader"/>, which is anonymous by construction and cannot
/// see the draft — the same split the page services make between the backoffice and the public
/// site, and for the same reason.
/// </para>
/// </remarks>
public interface ISiteStylesheetService
{
    /// <summary>Reads the draft, the published copy, and what the validator makes of the draft.</summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The stylesheet, or a forbidden result.</returns>
    Task<CmsResult<SiteStylesheetDetail>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a stylesheet without storing it, for the editor's live diagnostics.
    /// </summary>
    /// <param name="css">The stylesheet to check.</param>
    /// <param name="cancellationToken">Token observed while checking permissions.</param>
    /// <returns>
    /// A successful result carrying the report. An invalid stylesheet is a successful answer to
    /// "what is wrong with this" — the refusal belongs to the save, not to the question.
    /// </returns>
    Task<CmsResult<CssValidationReport>> ValidateAsync(
        string? css,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the draft. Changes nothing about what an anonymous visitor receives.
    /// </summary>
    /// <param name="css">The whole stylesheet.</param>
    /// <param name="expectedRowVersion">
    /// The Base64 <c>rowversion</c> the caller last read, or null to save unconditionally. A
    /// mismatch returns <c>Conflict</c> carrying the stylesheet that won, so the editor can offer a
    /// real choice rather than a banner (spec section 11.8).
    /// </param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The stored stylesheet, or why it was refused.</returns>
    Task<CmsResult<SiteStylesheetDetail>> SaveDraftAsync(
        string css,
        string? expectedRowVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the draft: snapshots it into a revision, points the public site at it, and evicts
    /// the stylesheet's cache entry on every instance.
    /// </summary>
    /// <param name="note">What the change was for. Recorded on the revision.</param>
    /// <param name="cancellationToken">Token observed while publishing.</param>
    /// <returns>The stylesheet as it now stands, or why the publish was refused.</returns>
    /// <remarks>
    /// The draft is validated again here rather than trusted. A draft can reach the database by a
    /// path this service did not run — a restore, an import, a migration — and publish is the last
    /// point before it reaches every visitor (D8's reasoning, applied to CSS).
    /// </remarks>
    Task<CmsResult<SiteStylesheetDetail>> PublishAsync(
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an earlier revision, or publishes nothing at all.
    /// </summary>
    /// <param name="revisionId">The revision to publish, or null to publish nothing.</param>
    /// <param name="copyToDraft">Whether to load the reverted CSS into the draft as well.</param>
    /// <param name="cancellationToken">Token observed while reverting.</param>
    /// <returns>The stylesheet as it now stands.</returns>
    Task<CmsResult<SiteStylesheetDetail>> RevertAsync(
        int? revisionId,
        bool copyToDraft,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the published history, newest first.</summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The revisions.</returns>
    Task<CmsResult<IReadOnlyList<SiteStylesheetRevisionSummary>>> ListRevisionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Reads one revision's CSS, for comparison or for loading into the draft.</summary>
    /// <param name="revisionId">The revision.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The CSS, or a not-found result.</returns>
    Task<CmsResult<string>> GetRevisionCssAsync(
        int revisionId,
        CancellationToken cancellationToken = default);
}
