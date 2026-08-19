namespace ContentManagementSystem.Shared.Contracts.Workflow;

/// <summary>
/// The workflow modes, as <see cref="PageWorkflowState.Mode"/> spells them (spec section 11.9).
/// </summary>
/// <remarks>
/// Strings rather than a shared enum, matching how <c>PageVersionSummary</c> carries a status: the
/// wire contract is the record, and the enum these are the names of lives in <c>Data</c>, which the
/// WebAssembly client does not reference. Named constants rather than literals so a screen comparing
/// against "Twostep" fails to compile instead of failing to match.
/// </remarks>
public static class WorkflowModes
{
    /// <summary>Anyone holding <c>Content.Publish</c> publishes directly.</summary>
    public const string None = "None";

    /// <summary>Users without <c>Content.Publish</c> submit; any approver may approve and publish.</summary>
    public const string Simple = "Simple";

    /// <summary>Submit, approve, and publish are three acts, and the approver may not be the author.</summary>
    public const string TwoStep = "TwoStep";
}

/// <summary>
/// A request to put the current draft in front of an approver (spec section 11.9).
/// </summary>
/// <param name="AssignedToUserId">
/// The approver being asked, or null to ask all of them. Null is the ordinary case in
/// <c>Simple</c> mode, where whoever gets to it first takes it.
/// </param>
/// <param name="DueDate">When the author would like a decision. Advisory; nothing enforces it.</param>
/// <param name="Note">What the author wants the reviewer to know before they start.</param>
public sealed record SubmitForReviewRequest(
    int? AssignedToUserId = null,
    DateOnly? DueDate = null,
    string? Note = null);

/// <summary>
/// An approver's verdict.
/// </summary>
/// <param name="Note">
/// Why. Optional on an approval and, in practice, the whole message on a rejection — the author sees
/// it at the top of the draft they get back.
/// </param>
public sealed record WorkflowDecisionRequest(string? Note = null);

/// <summary>
/// One review request, as an inbox or a page's history lists it.
/// </summary>
/// <param name="Id">Identity of the request.</param>
/// <param name="PageId">Page under review.</param>
/// <param name="PageTitle">That page's title, so an inbox reads as content rather than as ids.</param>
/// <param name="PageVersionId">The exact version submitted.</param>
/// <param name="VersionNumber">Its number within the page.</param>
/// <param name="State">Where the request has got to: pending, approved, rejected, or cancelled.</param>
/// <param name="AssignedToUserId">The approver asked, or null when all of them were.</param>
/// <param name="AssignedToName">That approver's name.</param>
/// <param name="DueDate">When a decision was wanted.</param>
/// <param name="SubmissionNote">What the author said on submitting.</param>
/// <param name="SubmittedOn">When it was submitted.</param>
/// <param name="SubmittedByUserId">Who submitted it.</param>
/// <param name="SubmittedByName">Their name.</param>
/// <param name="DecidedOn">When it was settled, if it has been.</param>
/// <param name="DecidedByUserId">Who settled it.</param>
/// <param name="DecidedByName">Their name.</param>
/// <param name="DecisionNote">What they said.</param>
public sealed record WorkflowTaskSummary(
    int Id,
    int PageId,
    string PageTitle,
    int PageVersionId,
    int VersionNumber,
    string State,
    int? AssignedToUserId,
    string? AssignedToName,
    DateOnly? DueDate,
    string? SubmissionNote,
    DateTimeOffset? SubmittedOn,
    int SubmittedByUserId,
    string? SubmittedByName,
    DateTimeOffset? DecidedOn,
    int? DecidedByUserId,
    string? DecidedByName,
    string? DecisionNote);

/// <summary>
/// Everything the review panel needs about one page.
/// </summary>
/// <param name="PageId">The page.</param>
/// <param name="Mode">The site's workflow mode: <c>None</c>, <c>Simple</c>, or <c>TwoStep</c>.</param>
/// <param name="DraftStatus">The draft version's status, which is what locks the editor.</param>
/// <param name="Pending">The open request, or null when nothing is under review.</param>
/// <param name="History">Settled requests, newest first.</param>
/// <param name="CanSubmit">Whether this caller may submit the draft right now.</param>
/// <param name="CanDecide">Whether this caller may approve or reject what is open right now.</param>
/// <param name="CanPublish">Whether this caller may publish the draft right now.</param>
/// <remarks>
/// The three <c>Can…</c> flags are computed server-side rather than inferred by the client from the
/// mode and the caller's roles. That inference is the whole of the workflow rule — including the
/// self-approval clause, which needs to know who submitted — and a second copy of it in WebAssembly
/// would be a second implementation to keep in step. The server still checks on the way in; these
/// only decide which buttons are drawn.
/// </remarks>
public sealed record PageWorkflowState(
    int PageId,
    string Mode,
    string DraftStatus,
    WorkflowTaskSummary? Pending,
    IReadOnlyList<WorkflowTaskSummary> History,
    bool CanSubmit,
    bool CanDecide,
    bool CanPublish);
