using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// What a bulk operation does to each of the pages it is given (task P6-29, spec section 14.11).
/// </summary>
/// <remarks>
/// A closed set rather than an open command name, because each member is a permission and a
/// confirmation of its own. "Run this named thing over 400 pages" is not something the API should be
/// able to be talked into.
/// <para>
/// Tagging is in spec section 14.11's table and is deliberately absent here: <c>Tag</c> and
/// <c>PageTag</c> arrive with task P8-20, and an operation that silently matched nothing would read
/// as "these pages have no tags" rather than as "tagging has not shipped".
/// </para>
/// </remarks>
public enum BulkOperation
{
    /// <summary>Publish each page's draft, running the full publish checks per item.</summary>
    Publish = 0,

    /// <summary>Take each page off the public site, leaving its draft alone.</summary>
    Unpublish = 1,

    /// <summary>Send each page and its subtree to the recycle bin.</summary>
    Delete = 2,

    /// <summary>Set — or, with a null owner, clear — the editor accountable for each page.</summary>
    SetOwner = 3,

    /// <summary>Set or clear the date each page is next due a review.</summary>
    SetReviewByDate = 4,
}

/// <summary>
/// The numbers a bulk operation is bounded by (spec section 14.11).
/// </summary>
/// <remarks>
/// In <c>Shared</c> because both ends need them and neither may own them: the server decides whether
/// a batch runs in the background, and the confirmation dialog has to be able to say <em>why</em> it
/// will. Two copies of "25" would eventually be two different numbers, and the screen would be the
/// one lying.
/// </remarks>
public static class BulkLimits
{
    /// <summary>
    /// Above this many items, a batch runs as a background job rather than inside the request.
    /// </summary>
    public const int BackgroundThreshold = 25;

    /// <summary>The largest selection one job will accept.</summary>
    public const int MaxSelection = 500;
}

/// <summary>
/// Which pages a bulk operation runs over.
/// </summary>
/// <param name="PageIds">
/// The pages an editor picked, in the order they were picked. Duplicates are ignored rather than
/// refused: a selection built from a tree and a filtered list can legitimately name a page twice.
/// </param>
/// <param name="IncludeDescendants">
/// Whether everything beneath each named page is included as well. This is what makes "publish
/// branch" one selection rather than a client walking the tree and sending forty requests — and it
/// is why the impact preview resolves the selection server-side before anybody confirms anything.
/// </param>
/// <remarks>
/// A separate record from the request so the same selection can be described and then run: the
/// preview and the execution resolve it by the same code, which is the rule task P6-03 established
/// for the move confirmation and there is no reason for this one to be weaker.
/// </remarks>
public sealed record BulkSelection(IReadOnlyList<int> PageIds, bool IncludeDescendants = false);

/// <summary>
/// Body of <c>POST /api/cms/v1/pages/bulk</c> and its preview.
/// </summary>
/// <param name="Operation">What to do to each page.</param>
/// <param name="Selection">Which pages to do it to.</param>
/// <param name="OwnerUserId">
/// The new owner, for <see cref="BulkOperation.SetOwner"/>. Null clears the owner, which is a real
/// request — "nobody owns this any more" is how a leaver's pages are handed back.
/// </param>
/// <param name="ReviewByDate">
/// The new review date, for <see cref="BulkOperation.SetReviewByDate"/>. Null clears it.
/// </param>
/// <param name="AcknowledgeWarnings">
/// Whether the editor has seen the warnings a publish would raise and still wants to proceed. Passed
/// through to each item's publish, so a batch cannot push past a warning a single publish would have
/// stopped (spec section 22.2).
/// </param>
public sealed record BulkOperationRequest(
    BulkOperation Operation,
    BulkSelection Selection,
    int? OwnerUserId = null,
    DateOnly? ReviewByDate = null,
    bool AcknowledgeWarnings = false);

/// <summary>
/// One page a bulk operation would touch, as the impact preview lists it.
/// </summary>
/// <param name="PageId">Identity of the page.</param>
/// <param name="Title">Its title, so the preview reads as content rather than as identities.</param>
/// <param name="IsPublished">Whether it is currently on the public site.</param>
/// <param name="WasSelected">
/// Whether an editor picked this page outright, as against it being swept in as somebody's
/// descendant. The distinction is the whole point of the preview for a branch publish: "you selected
/// 3 pages, this will publish 41" is the sentence worth showing before anybody agrees to it.
/// </param>
public sealed record BulkImpactItem(int PageId, string Title, bool IsPublished, bool WasSelected);

/// <summary>
/// What a bulk operation would do, without doing any of it (spec section 14.11).
/// </summary>
/// <param name="Operation">The operation described.</param>
/// <param name="Items">Every page it would touch, selected roots before their descendants.</param>
/// <param name="SelectedCount">How many pages the editor picked.</param>
/// <param name="PublishedCount">How many of the resolved pages are currently live.</param>
/// <param name="RunsInBackground">
/// Whether the batch is large enough to be run as a background job rather than inside the request.
/// Reported rather than left for the client to work out, so the threshold lives in one place and a
/// screen never has to guess whether it should be polling.
/// </param>
/// <param name="Warnings">
/// Non-blocking diagnostics — most usefully that a page in the selection no longer exists, which is
/// what a stale selection looks like and is not a reason to refuse the other 39.
/// </param>
/// <remarks>
/// A read, not a rolled-back write. Unlike a move — whose consequences are only knowable by making
/// them (task P6-03) — what a bulk publish will attempt is a list of pages, and the per-item outcome
/// genuinely cannot be known before it is tried: a publish runs full validation, and validating 400
/// drafts to preview a batch would cost as much as running it.
/// </remarks>
public sealed record BulkImpact(
    BulkOperation Operation,
    IReadOnlyList<BulkImpactItem> Items,
    int SelectedCount,
    int PublishedCount,
    bool RunsInBackground,
    IReadOnlyList<ApiDiagnostic> Warnings)
{
    /// <summary>How many pages the operation would touch once the selection is resolved.</summary>
    public int ItemCount => Items.Count;
}

/// <summary>
/// Where a bulk job has got to.
/// </summary>
public enum BulkJobState
{
    /// <summary>Accepted and working through its items.</summary>
    Running = 0,

    /// <summary>Every item has been attempted. Some of them may have failed.</summary>
    Completed = 1,

    /// <summary>
    /// The job itself stopped — the process is shutting down, or an item threw something the runner
    /// could not attribute. Distinct from <see cref="Completed"/> with failures, because the items
    /// after the stopping point were never attempted and an editor needs to know which.
    /// </summary>
    Faulted = 2,
}

/// <summary>
/// What happened to one page in a bulk operation.
/// </summary>
/// <param name="PageId">Identity of the page.</param>
/// <param name="Title">Its title, so the report reads without a second lookup.</param>
/// <param name="Succeeded">Whether the operation applied to this page.</param>
/// <param name="Diagnostics">
/// Why it did not, when it did not. The same shape a single-item failure returns, so a batch report
/// says "Pricing: the hero zone is required" rather than "1 item failed".
/// </param>
public sealed record BulkItemResult(
    int PageId,
    string Title,
    bool Succeeded,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

/// <summary>
/// A bulk operation's progress and its per-item outcomes (spec section 14.11).
/// </summary>
/// <param name="Id">Identity of the job, which is what a client polls with.</param>
/// <param name="Operation">The operation being run.</param>
/// <param name="State">Where the job has got to.</param>
/// <param name="Total">How many items the job was given.</param>
/// <param name="Results">
/// What has happened so far, in the order the items were attempted. A running job reports the items
/// it has finished, which is what makes the progress bar honest rather than animated.
/// </param>
/// <param name="StartedOn">When the job was accepted.</param>
/// <param name="FinishedOn">When the last item was attempted, or null while it is still running.</param>
/// <remarks>
/// <strong>A partial failure is an ordinary outcome, not an error.</strong> A job whose items all
/// failed still reports <see cref="BulkJobState.Completed"/> — every one of them was attempted and
/// every one has a reason attached. Reporting the job as failed would put an editor in front of one
/// message where there are forty, which is the thing spec section 14.11 asks this not to do.
/// </remarks>
public sealed record BulkJobStatus(
    Guid Id,
    BulkOperation Operation,
    BulkJobState State,
    int Total,
    IReadOnlyList<BulkItemResult> Results,
    DateTimeOffset StartedOn,
    DateTimeOffset? FinishedOn)
{
    /// <summary>How many items have been attempted.</summary>
    public int Completed => Results.Count;

    /// <summary>How many items the operation applied to.</summary>
    public int Succeeded => Results.Count(result => result.Succeeded);

    /// <summary>How many items reported a reason it did not.</summary>
    public int Failed => Results.Count(result => !result.Succeeded);

    /// <summary>Whether the job has stopped attempting items.</summary>
    public bool IsFinished => State is not BulkJobState.Running;
}
