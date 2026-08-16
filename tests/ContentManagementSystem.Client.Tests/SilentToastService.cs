using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Tests;

/// <summary>
/// A toast service that records what it was asked to show and renders nothing.
/// </summary>
/// <remarks>
/// The real one raises an event a container elsewhere in the layout listens for, which is not in
/// scope when a single component is rendered. Recording rather than discarding, so a test that
/// cares what the editor was told can say so.
/// </remarks>
public sealed class SilentToastService : IToastService
{
    /// <inheritdoc />
    public event Action<ToastMessage>? OnToastAdded;

    /// <summary>Everything shown, in order.</summary>
    public List<ToastMessage> Shown { get; } = [];

    /// <inheritdoc />
    public void ShowSuccess(string message, string? heading = null) =>
        Raise(message, heading, ToastType.Success);

    /// <inheritdoc />
    public void ShowError(string message, string? heading = null) =>
        Raise(message, heading, ToastType.Error);

    /// <inheritdoc />
    public void ShowWarning(string message, string? heading = null) =>
        Raise(message, heading, ToastType.Warning);

    /// <inheritdoc />
    public void ShowInfo(string message, string? heading = null) =>
        Raise(message, heading, ToastType.Info);

    private void Raise(string message, string? heading, ToastType type)
    {
        var toast = new ToastMessage(Guid.NewGuid(), message, type, heading);

        Shown.Add(toast);
        OnToastAdded?.Invoke(toast);
    }
}
