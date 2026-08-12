using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Components;

public partial class ToastContainer : ComponentBase, IDisposable
{
    [Inject] private IToastService ToastService { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    
    private readonly List<ToastMessage> _toasts = [];
    private readonly Queue<Guid> _pendingShow = new();

    protected override void OnInitialized()
    {
        ToastService.OnToastAdded += HandleToastAdded;
    }

    private void HandleToastAdded(ToastMessage toast)
    {
        _toasts.Add(toast);
        _pendingShow.Enqueue(toast.Id);
        InvokeAsync(StateHasChanged);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        while (_pendingShow.TryDequeue(out var id))
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("showBootstrapToast", $"toast-{id}");
            }
            catch
            {
                // Swallow JS interop errors during prerender
            }
        }
    }

    private void RemoveToast(Guid id)
    {
        var toast = _toasts.FirstOrDefault(t => t.Id == id);
        if (toast is not null)
            _toasts.Remove(toast);
    }

    private static string GetBootstrapClass(ToastType type) => type switch
    {
        ToastType.Success => "success",
        ToastType.Error   => "danger",
        ToastType.Warning => "warning",
        ToastType.Info    => "info",
        _                 => "secondary"
    };

    public void Dispose()
    {
        ToastService.OnToastAdded -= HandleToastAdded;
    }
}