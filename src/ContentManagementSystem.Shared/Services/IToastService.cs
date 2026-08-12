namespace ContentManagementSystem.Shared.Services;

/// <summary>Represents a toast notification message.</summary>
public record ToastMessage(Guid Id, string Message, ToastType Type, string? Heading = null);

/// <summary>The type of toast notification, mapped to Bootstrap contextual colors.</summary>
public enum ToastType { Success, Error, Warning, Info }

/// <summary>
/// Service for showing Bootstrap toast notifications from anywhere in the application.
/// Components subscribe to <see cref="OnToastAdded"/> to render new toasts.
/// </summary>
public interface IToastService
{
    /// <summary>Raised when a new toast is requested.</summary>
    event Action<ToastMessage>? OnToastAdded;

    /// <summary>Shows a success toast with the given message.</summary>
    void ShowSuccess(string message, string? heading = null);

    /// <summary>Shows an error toast with the given message.</summary>
    void ShowError(string message, string? heading = null);

    /// <summary>Shows a warning toast with the given message.</summary>
    void ShowWarning(string message, string? heading = null);

    /// <summary>Shows an info toast with the given message.</summary>
    void ShowInfo(string message, string? heading = null);
}