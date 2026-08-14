namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// One entry of a page's version history (spec section 11.1).
/// </summary>
/// <param name="Id">Identity of the version, used to address it in the API.</param>
/// <param name="VersionNumber">Its number within the page.</param>
/// <param name="Status">Where it sits in the editorial lifecycle.</param>
/// <param name="Label">Editor-supplied name, present only on a named checkpoint.</param>
/// <param name="Title">Page title as at this version.</param>
/// <param name="TemplateRevision">Template revision its payload was authored against.</param>
/// <param name="IsDraft">Whether this is the page's one mutable working version.</param>
/// <param name="IsPublished">Whether this is the version currently being served.</param>
/// <param name="CreatedOn">When the row was written.</param>
/// <param name="CreatedBy">Who wrote it.</param>
/// <param name="PublishedOn">When it went live, if it ever did.</param>
/// <param name="PublishedBy">Who published it.</param>
public sealed record PageVersionSummary(
    int Id,
    int VersionNumber,
    string Status,
    string? Label,
    string Title,
    int TemplateRevision,
    bool IsDraft,
    bool IsPublished,
    DateTimeOffset? CreatedOn,
    int CreatedBy,
    DateTimeOffset? PublishedOn,
    int? PublishedBy);

/// <summary>
/// One version with its payload, as <c>GET /pages/{id}/versions/{vid}</c> returns it.
/// </summary>
/// <param name="Summary">What the history list shows.</param>
/// <param name="ContentJson">The payload as stored.</param>
/// <param name="Seo">The search and social metadata this version carried.</param>
public sealed record PageVersionDetail(PageVersionSummary Summary, string ContentJson, PageSeo Seo);

/// <summary>
/// What a retention sweep removed.
/// </summary>
/// <param name="PagesExamined">How many pages were considered.</param>
/// <param name="VersionsRemoved">How many version rows were deleted.</param>
/// <param name="RemovedVersionIds">
/// Identities of the rows removed, so the nightly job can log precisely what it did
/// (spec section 11.7).
/// </param>
public sealed record RetentionSweepResult(
    int PagesExamined,
    int VersionsRemoved,
    IReadOnlyList<int> RemovedVersionIds);
