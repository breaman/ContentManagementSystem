using ContentManagementSystem.Shared.Contracts.Content;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Bulk;

/// <summary>
/// Confirms a bulk operation, follows it, and reports what happened to each page
/// (task P6-29, spec section 14.11).
/// </summary>
/// <remarks>
/// One dialog with three faces rather than three dialogs, because they are three moments of one
/// decision and an editor should not have to find the report after the confirmation closed.
/// <list type="number">
/// <item><description>The impact: what was selected, what that resolves to, and what is live.</description></item>
/// <item><description>The progress: how far through, with the number said as well as drawn.</description></item>
/// <item><description>The report: what applied, and for each page that did not, the reason.</description></item>
/// </list>
/// <para>
/// <strong>The failures are the report.</strong> A batch that publishes thirty-eight of forty-one
/// pages has produced three sentences worth reading and thirty-eight worth nothing, so the successes
/// are a count and the failures are a list with their diagnostics attached — which is what spec
/// section 14.11 means by reporting a per-item result rather than failing the batch.
/// </para>
/// </remarks>
public partial class BulkOperationDialog : ComponentBase
{
    /// <summary>What the operation would run over, or null when there is nothing to confirm.</summary>
    [Parameter]
    public BulkImpact? Impact { get; set; }

    /// <summary>The running or finished job, or null while the impact is still being confirmed.</summary>
    [Parameter]
    public BulkJobStatus? Job { get; set; }

    /// <summary>Heading, which is also the dialog's accessible name.</summary>
    [Parameter]
    public string Title { get; set; } = "Run this over the selected pages?";

    /// <summary>What the operation is called in the question — "Publish", "Delete".</summary>
    [Parameter]
    public string Verb { get; set; } = "Publish";

    /// <summary>The same verb in the past tense, for the failure heading.</summary>
    [Parameter]
    public string PastTense { get; set; } = "published";

    /// <summary>Bootstrap variant of the confirm button — danger for anything destructive.</summary>
    [Parameter]
    public string ConfirmClass { get; set; } = "btn-primary";

    /// <summary>Whether the confirmed operation is in flight, which disables the button and Escape.</summary>
    [Parameter]
    public bool IsBusy { get; set; }

    /// <summary>Raised when the editor goes ahead.</summary>
    [Parameter]
    public EventCallback OnConfirm { get; set; }

    /// <summary>Raised when the editor closes the dialog, before or after the run.</summary>
    [Parameter]
    public EventCallback OnClose { get; set; }

    /// <summary>Whether the dialog is on screen at all.</summary>
    private bool IsOpen => Impact is not null || Job is not null;

    /// <summary>
    /// What the primary button does, which is not the same thing before and after.
    /// </summary>
    /// <remarks>
    /// Once a job exists there is nothing left to confirm, so the button becomes the way out. It is
    /// the same button rather than a swapped pair because focus is already on it — the dialog put it
    /// there — and moving focus when the job finishes would take it away from somebody reading.
    /// </remarks>
    private string ConfirmLabel => Job is null
        ? Impact is { } impact ? $"{Verb} {impact.ItemCount} page(s)" : Verb
        : "Done";

    /// <summary>What the secondary button does. It never runs anything, so it is always a way out.</summary>
    private string CancelLabel => Job is null ? "Cancel" : "Close";

    /// <summary>Whether the primary button can be pressed.</summary>
    /// <remarks>
    /// Disabled while the job runs, because "Done" is not true yet and a button that closed the
    /// dialog under that label would tell an editor the batch had finished.
    /// </remarks>
    private bool CanConfirm => Job is null || Job.IsFinished;

    /// <summary>
    /// What a screen reader is told, and only when it changes.
    /// </summary>
    /// <remarks>
    /// Silent while the job runs. A region that announced every poll would read a number aloud once a
    /// second for the length of the batch, which is the failure task P6-22 spends its whole note
    /// avoiding — so the announcement is the outcome, made once, when there is an outcome.
    /// </remarks>
    private string? Announcement => Job is { IsFinished: true } job ? Outcome(job) : null;

    /// <summary>The question the confirmation is actually asking.</summary>
    /// <remarks>
    /// It states the resolved count against the selected one whenever they differ, because that
    /// difference is the entire reason this dialog exists: "publish this branch" is a click on one
    /// page and an act on forty-one.
    /// </remarks>
    private string Question(BulkImpact impact) =>
        impact.ItemCount == impact.SelectedCount
            ? $"{Verb} {impact.ItemCount} selected page(s)? {impact.PublishedCount} of them are live now."
            : $"You selected {impact.SelectedCount} page(s). This will {Verb.ToLowerInvariant()} " +
              $"{impact.ItemCount}, including everything beneath them. " +
              $"{impact.PublishedCount} of them are live now.";

    /// <summary>How the finished job is summarised in one sentence.</summary>
    private string Outcome(BulkJobStatus job) => job.State switch
    {
        BulkJobState.Faulted =>
            $"This stopped after {job.Completed} of {job.Total} page(s). " +
            $"{job.Succeeded} were {PastTense}; the rest were not attempted.",
        _ when job.Failed == 0 => $"All {job.Succeeded} page(s) were {PastTense}.",
        _ => $"{job.Succeeded} of {job.Total} page(s) were {PastTense}. {job.Failed} were not.",
    };

    /// <summary>The items worth listing: the ones with a reason attached.</summary>
    private static IReadOnlyList<BulkItemResult> Failures(BulkJobStatus job) =>
        [.. job.Results.Where(result => !result.Succeeded)];

    /// <summary>Runs the operation, or closes the dialog once it has run.</summary>
    private Task OnConfirmAsync() => Job is null ? OnConfirm.InvokeAsync() : OnClose.InvokeAsync();
}
