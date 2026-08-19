using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Workflow;

/// <summary>
/// The signed-in editor's in-app inbox (task P7-19, spec section 14.8).
/// </summary>
/// <remarks>
/// Everybody has one and nobody needs a permission for it: the server scopes every query to the
/// caller, so the only inbox reachable here is the reader's own.
/// <para>
/// Read state is stored rather than inferred from having opened the screen. "Which of these have I
/// dealt with" is the question an inbox exists to answer, and a list that marked everything read on
/// sight would answer it wrongly for anybody who glances at it between meetings.
/// </para>
/// </remarks>
public partial class NotificationsScreen : ComponentBase
{
    /// <summary>Reads and updates the inbox.</summary>
    [Inject]
    private IWorkflowClient Client { get; set; } = default!;

    /// <summary>The inbox, or null while it loads.</summary>
    [PersistentState]
    public NotificationInbox? Inbox { get; set; }

    /// <summary>Whether a request is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync() => Inbox = await Client.GetNotificationsAsync();

    private Task MarkAsync(int id) => MarkAsync((int?)id);

    private Task MarkAllAsync() => MarkAsync(null);

    private async Task MarkAsync(int? id)
    {
        IsBusy = true;

        try
        {
            await Client.MarkNotificationsReadAsync(id);

            // Reloaded rather than patched, because the unread count the shell's badge reads comes
            // back with it and two numbers computed separately are two numbers that disagree.
            Inbox = await Client.GetNotificationsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
