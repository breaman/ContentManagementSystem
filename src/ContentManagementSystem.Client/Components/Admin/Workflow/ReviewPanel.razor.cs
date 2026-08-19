using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Workflow;

/// <summary>
/// Submit, approve, and reject, on the page being edited (task P7-12, spec section 11.9).
/// </summary>
/// <remarks>
/// Which buttons appear is decided by the server, not here. <see cref="PageWorkflowState"/> carries
/// three flags — may submit, may decide, may publish — computed against the same rules the write
/// endpoints enforce, including the self-approval clause, which needs to know who submitted. A
/// second copy of that reasoning in WebAssembly would be a second implementation to keep in step,
/// and the first time they disagreed an editor would be offered a button that produced a 403.
/// <para>
/// The panel reloads its state after every action rather than patching what it holds. A decision
/// changes the draft's status, the pending request, the history, and all three flags at once; a
/// screen that updated the parts it knew about would show a stale version of the rest.
/// </para>
/// </remarks>
public partial class ReviewPanel : ComponentBase
{
    /// <summary>Reads and writes the review state.</summary>
    [Inject]
    private IWorkflowClient Client { get; set; } = default!;

    /// <summary>The page under review.</summary>
    [Parameter]
    [EditorRequired]
    public int PageId { get; set; }

    /// <summary>Raised after a decision, so the editor around this can reload what it shows.</summary>
    /// <remarks>
    /// A rejection replaces the page's draft with a fresh copy, so the canvas an editor is looking
    /// at is pointing at a version that is no longer the draft. The parent has to know.
    /// </remarks>
    [Parameter]
    public EventCallback OnChanged { get; set; }

    /// <summary>The state, or null while loading or when the caller cannot see it.</summary>
    [PersistentState]
    public PageWorkflowState? State { get; set; }

    /// <summary>Whether a request is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <summary>What went wrong with the last action, if anything.</summary>
    private string? Error { get; set; }

    private string _note = string.Empty;

    /// <summary>Whether the site runs any approval ceremony at all.</summary>
    private bool IsEnabled => State is not null && State.Mode != WorkflowModes.None;

    /// <summary>
    /// Whether this caller could decide something, even if not this.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>CanDecide</c>, which is about this particular submission. The difference is
    /// what lets the panel explain a refusal to an approver instead of silently showing them
    /// nothing.
    /// </remarks>
    private bool IsApprover => State is { CanDecide: false, CanPublish: true };

    /// <summary>One line saying where the page stands, for the status region.</summary>
    private string StatusLine => State switch
    {
        null => "Loading…",
        { Pending: not null } => "Waiting for review.",
        { DraftStatus: "Approved" } => "Approved. It can be published now.",
        { DraftStatus: "Rejected" } => "Sent back. A fresh draft has been restored for you.",
        _ => "Not submitted.",
    };

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (State?.PageId != PageId)
        {
            State = await Client.GetWorkflowAsync(PageId);
        }
    }

    private Task SubmitAsync() => ActAsync(() =>
        Client.SubmitAsync(PageId, new SubmitForReviewRequest(Note: Trimmed())));

    private Task ApproveAsync() => ActAsync(() =>
        Client.ApproveAsync(PageId, new WorkflowDecisionRequest(Trimmed())));

    private Task RejectAsync() => ActAsync(() =>
        Client.RejectAsync(PageId, new WorkflowDecisionRequest(Trimmed())));

    /// <summary>Runs one action, then reloads rather than patching what is held.</summary>
    private async Task ActAsync(Func<Task<PageWorkflowState?>> action)
    {
        IsBusy = true;
        Error = null;

        try
        {
            var result = await action();

            if (result is null)
            {
                // The server refused. Almost always this means somebody else moved the page on
                // while this screen was open, so the honest response is to show what is true now.
                Error = "That could not be done. The page may have moved on since this screen loaded.";
                State = await Client.GetWorkflowAsync(PageId);

                return;
            }

            State = result;
            _note = string.Empty;

            await OnChanged.InvokeAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? Trimmed() => string.IsNullOrWhiteSpace(_note) ? null : _note.Trim();
}
