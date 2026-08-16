using ContentManagementSystem.Client.Components.Admin.Shell;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// Remembers each editor's backoffice pane geometry between visits (task P6-01, spec section 14.1).
/// </summary>
/// <remarks>
/// Deliberately not a server-side preference. Pane widths are a property of the screen the editor is
/// sitting at, not of the account: the same person on a laptop and on a 34-inch monitor wants two
/// different layouts, and a preference synchronized through the database would give them one. The
/// key is still per-user so that a shared machine does not hand one editor another's shell.
/// </remarks>
public interface IShellLayoutStore
{
    /// <summary>Reads a stored layout.</summary>
    /// <param name="userKey">Identifies the editor whose layout to read.</param>
    /// <param name="cancellationToken">Token observed while reading.</param>
    /// <returns>The stored layout, or <see cref="ShellLayout.Default"/> when there is none.</returns>
    ValueTask<ShellLayout> LoadAsync(string userKey, CancellationToken cancellationToken = default);

    /// <summary>Stores a layout.</summary>
    /// <param name="userKey">Identifies the editor whose layout this is.</param>
    /// <param name="layout">The geometry to remember.</param>
    /// <param name="cancellationToken">Token observed while writing.</param>
    ValueTask SaveAsync(string userKey, ShellLayout layout, CancellationToken cancellationToken = default);
}
