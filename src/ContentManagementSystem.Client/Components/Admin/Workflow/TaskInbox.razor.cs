using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Workflow;

/// <summary>
/// An approver's queue of what is waiting on them (task P7-12, spec section 11.9).
/// </summary>
/// <remarks>
/// Oldest first, because an inbox is a queue and the thing that has waited longest is the thing that
/// should be looked at. Every row links into the page's editor rather than offering approve and
/// reject buttons here: a decision made from a list is a decision made without reading the content,
/// which is the failure mode an approval step exists to prevent.
/// </remarks>
public partial class TaskInbox : ComponentBase
{
    /// <summary>Reads the queue.</summary>
    [Inject]
    private IWorkflowClient Client { get; set; } = default!;

    /// <summary>The requests, or null while they load.</summary>
    [PersistentState]
    public IReadOnlyList<WorkflowTaskSummary>? Tasks { get; set; }

    private bool _assignedToMe;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task ToggleAsync(ChangeEventArgs args)
    {
        _assignedToMe = args.Value is true;
        Tasks = null;

        await LoadAsync();
    }

    private async Task LoadAsync() => Tasks = await Client.GetTasksAsync(_assignedToMe);
}
