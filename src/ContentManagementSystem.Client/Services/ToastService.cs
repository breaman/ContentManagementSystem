namespace ContentManagementSystem.Client.Services;

using ContentManagementSystem.Shared.Services;

/// <summary>
/// Default implementation of <see cref="IToastService"/>.
/// Raises <see cref="OnToastAdded"/> for each new notification, allowing
/// <c>ToastContainer</c> to render Bootstrap toasts reactively.
/// </summary>
public class ToastService : IToastService
{
    /// <inheritdoc />
    public event Action<ToastMessage>? OnToastAdded;

    /// <inheritdoc />
    public void ShowSuccess(string message, string? heading = null)
        => OnToastAdded?.Invoke(new ToastMessage(Guid.NewGuid(), message, ToastType.Success, heading));

    /// <inheritdoc />
    public void ShowError(string message, string? heading = null)
        => OnToastAdded?.Invoke(new ToastMessage(Guid.NewGuid(), message, ToastType.Error, heading));

    /// <inheritdoc />
    public void ShowWarning(string message, string? heading = null)
        => OnToastAdded?.Invoke(new ToastMessage(Guid.NewGuid(), message, ToastType.Warning, heading));

    /// <inheritdoc />
    public void ShowInfo(string message, string? heading = null)
        => OnToastAdded?.Invoke(new ToastMessage(Guid.NewGuid(), message, ToastType.Info, heading));
}