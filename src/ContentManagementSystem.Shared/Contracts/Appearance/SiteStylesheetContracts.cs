namespace ContentManagementSystem.Shared.Contracts.Appearance;

/// <summary>
/// The site stylesheet as the editor screen needs it: the draft being worked on, what the public is
/// currently being served, and enough about each to describe the difference (spec section 30.3).
/// </summary>
/// <param name="DraftCss">What the administrator is working on.</param>
/// <param name="PublishedCss">
/// What anonymous visitors receive, or null when nothing has been published — in which case the
/// public document links no second stylesheet at all.
/// </param>
/// <param name="HasUnpublishedChanges">
/// Whether the draft differs from the published copy. Computed here rather than in the browser so
/// "publish" and "nothing to publish" cannot disagree with the server.
/// </param>
/// <param name="DraftByteLength">Size of the draft in UTF-8 bytes, for the counter against the cap.</param>
/// <param name="PublishedByteLength">Size of the published copy, so the publish dialog can state a delta.</param>
/// <param name="MaxBytes">The configured cap, so the editor can show it rather than discover it on save.</param>
/// <param name="PublishedOn">When the current published copy went live.</param>
/// <param name="PublishedBy">Who published it, by display name.</param>
/// <param name="Diagnostics">What the validator makes of the draft as stored.</param>
/// <param name="RowVersion">Concurrency token, echoed back as <c>If-Match</c> on the next save.</param>
public sealed record SiteStylesheetDetail(
    string DraftCss,
    string? PublishedCss,
    bool HasUnpublishedChanges,
    int DraftByteLength,
    int PublishedByteLength,
    int MaxBytes,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    IReadOnlyList<CssDiagnostic> Diagnostics,
    string RowVersion);

/// <summary>One published state of the stylesheet, as the revision list shows it.</summary>
/// <param name="Id">Identity, for reverting to it or reading it back.</param>
/// <param name="ByteLength">Size in UTF-8 bytes.</param>
/// <param name="Note">What the administrator said the change was for.</param>
/// <param name="CreatedOn">When it was published.</param>
/// <param name="CreatedBy">Who published it, by display name.</param>
/// <param name="IsCurrent">Whether this revision is what the public site is serving now.</param>
public sealed record SiteStylesheetRevisionSummary(
    int Id,
    int ByteLength,
    string? Note,
    DateTimeOffset CreatedOn,
    string? CreatedBy,
    bool IsCurrent);

/// <summary>Saving the draft (spec section 22.1).</summary>
/// <param name="Css">The whole stylesheet. There is no partial save; it is one file.</param>
public sealed record SaveSiteStylesheetDraftRequest(string Css);

/// <summary>Validating without saving, for the editor's live diagnostics.</summary>
/// <param name="Css">The stylesheet to check.</param>
public sealed record ValidateSiteStylesheetRequest(string Css);

/// <summary>Publishing the draft.</summary>
/// <param name="Note">
/// Optional. Recorded on the revision, and it is the question a revert is trying to answer six
/// months later.
/// </param>
public sealed record PublishSiteStylesheetRequest(string? Note);

/// <summary>
/// Reverting: publishing an earlier revision, or publishing nothing at all.
/// </summary>
/// <param name="RevisionId">
/// The revision to publish, or null to publish nothing — which returns the site to the design the
/// deployment ships and is the recovery path for a stylesheet that broke the layout.
/// </param>
/// <param name="CopyToDraft">
/// Whether to load the reverted CSS into the draft as well. False leaves the draft alone, so an
/// administrator can take the site back to safety without losing the work that broke it.
/// </param>
public sealed record RevertSiteStylesheetRequest(int? RevisionId, bool CopyToDraft = false);

/// <summary>What the validator made of a stylesheet, without storing it.</summary>
/// <param name="IsValid">Whether it may be saved and published.</param>
/// <param name="ByteLength">Its size in UTF-8 bytes.</param>
/// <param name="MaxBytes">The configured cap.</param>
/// <param name="Diagnostics">Everything found, in the order it appears in the file.</param>
public sealed record CssValidationReport(
    bool IsValid,
    int ByteLength,
    int MaxBytes,
    IReadOnlyList<CssDiagnostic> Diagnostics);

/// <summary>Stable diagnostic codes the stylesheet service returns in <c>CmsResult</c>.</summary>
public static class SiteStylesheetCodes
{
    /// <summary>The caller does not hold <c>Appearance.Edit</c>.</summary>
    public const string Forbidden = "stylesheet.forbidden";

    /// <summary>The stylesheet contains something spec section 30.5 refuses.</summary>
    public const string Refused = "stylesheet.refused";

    /// <summary>The save lost a race; the result carries the stylesheet that won.</summary>
    public const string Conflict = "stylesheet.conflict";

    /// <summary>There is nothing to publish — the draft already matches what is live.</summary>
    public const string NothingToPublish = "stylesheet.nothingToPublish";

    /// <summary>The revision asked for does not exist.</summary>
    public const string RevisionNotFound = "stylesheet.revisionNotFound";
}
