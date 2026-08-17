using ContentManagementSystem.Client.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Components.Admin.Saving;

/// <summary>
/// "Saved 14:32", and everything else the save state can be (task P6-18, spec section 14.1).
/// </summary>
/// <remarks>
/// A time rather than a word, because "Saved" on its own is indistinguishable from "saved twenty
/// minutes ago and quietly broken ever since" — which is the failure an autosave indicator exists to
/// make impossible.
/// <para>
/// It also arms the browser's own unsaved-changes prompt while there is work in flight. Blazor's
/// location-changing handler covers navigation <em>within</em> the backoffice; closing the tab is
/// outside .NET's reach entirely, and a tab closed twenty seconds after the last keystroke is
/// exactly the case autosave was built for.
/// </para>
/// </remarks>
public partial class SaveStateIndicator : ComponentBase, IAsyncDisposable
{
    /// <summary>Path of the collocated interop module, relative to the host page.</summary>
    private const string ModulePath =
        "./Components/Admin/Saving/SaveStateIndicator.razor.js";

    /// <summary>The browser's JavaScript runtime, used for the unload guard.</summary>
    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    /// <summary>Where the editor's work has got to.</summary>
    [Parameter]
    public AutosaveStatus Status { get; set; } = AutosaveStatus.Clean;

    /// <summary>
    /// Whether to warn on closing the tab while there is unsaved work.
    /// </summary>
    /// <remarks>
    /// On by default and switchable off, because a screen that is read-only or already navigating
    /// away deliberately should not be arguing with the person leaving it.
    /// </remarks>
    [Parameter]
    public bool GuardUnload { get; set; } = true;

    /// <summary>The imported interop module.</summary>
    private IJSObjectReference? _module;

    /// <summary>Whether the browser prompt is currently armed.</summary>
    private bool _armed;

    /// <summary>The phase the last announcement was made for, so a redraw does not repeat it.</summary>
    private AutosavePhase _announced = AutosavePhase.Saved;

    /// <summary>What the announcement currently holds, or null when there is nothing new to say.</summary>
    private string? Announcement { get; set; }

    /// <summary>What the indicator reads.</summary>
    private string Text => Status.Phase switch
    {
        AutosavePhase.Saving => "Saving…",
        AutosavePhase.Pending => "Unsaved changes",
        AutosavePhase.Retrying => "Not saved — trying again",
        AutosavePhase.Refused => "Not saved",
        _ when Status.SavedOn is { } saved => $"Saved {saved.ToLocalTime():HH:mm}",
        _ => "No changes",
    };

    private string Icon => Status.Phase switch
    {
        AutosavePhase.Saving => "bi-arrow-repeat",
        AutosavePhase.Pending => "bi-pencil",
        AutosavePhase.Retrying => "bi-arrow-clockwise",
        AutosavePhase.Refused => "bi-exclamation-octagon-fill",
        _ => "bi-check-circle",
    };

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Status.Phase == _announced) return;

        _announced = Status.Phase;

        Announcement = Status.Phase switch
        {
            AutosavePhase.Saved when Status.SavedOn is { } saved => $"Saved at {saved.ToLocalTime():HH:mm}.",
            AutosavePhase.Retrying =>
                "Your changes have not been saved yet. They are still here and will be saved again shortly.",
            AutosavePhase.Refused =>
                $"Your changes were not saved. {Status.Message ?? "Nothing has been lost; see the messages on the page."}",
            // Pending and Saving both pass silently. Announcing "unsaved changes" on the first
            // keystroke of every sentence would make the region unusable.
            _ => null,
        };
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var wanted = GuardUnload && Status.HasUnsavedWork;

        if (wanted == _armed) return;

        _armed = wanted;

        await Quietly(async () =>
        {
            _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);

            await _module.InvokeVoidAsync(wanted ? "arm" : "disarm");
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            // Disarmed before the reference goes, or the handler outlives the editor and warns the
            // person leaving a screen that no longer exists.
            await Quietly(async () =>
            {
                await _module.InvokeVoidAsync("disarm");
                await _module.DisposeAsync();
            });
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Runs interop that is allowed to be impossible — a pre-render, or a page going away.</summary>
    private static async Task Quietly(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is JSException
                                              or JSDisconnectedException
                                              or InvalidOperationException
                                              or TaskCanceledException)
        {
            // Static rendering, or the document is gone. The indicator still reads correctly; it
            // simply cannot arm a prompt in a document that is not being displayed.
        }
    }
}
