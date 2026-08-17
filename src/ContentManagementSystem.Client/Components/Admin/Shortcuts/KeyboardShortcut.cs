namespace ContentManagementSystem.Client.Components.Admin.Shortcuts;

/// <summary>
/// One keyboard shortcut: what it is called, what it does, and what to press (task P6-23).
/// </summary>
/// <param name="Id">Stable identifier the handler switches on.</param>
/// <param name="Key">
/// The <c>KeyboardEvent.key</c> value, matched case-insensitively. A key rather than a code, because
/// the shortcut an editor is told to press is the letter printed on their keyboard.
/// </param>
/// <param name="Description">What it does, phrased as an action.</param>
/// <param name="Group">Which section of the reference dialog it appears under.</param>
/// <param name="Control">
/// Whether Control — or Command, which is matched with it — must be held. The two are one modifier
/// here: an editor on a Mac presses ⌘S and one on Windows presses Ctrl+S, and neither should have to
/// learn the other's.
/// </param>
/// <param name="Shift">Whether Shift must be held.</param>
/// <param name="RequiresEditing">
/// Whether the shortcut needs the editor to be able to write. A read-only viewer is shown the
/// reference dialog without the shortcuts they cannot use, which is the same rule the toolbar
/// follows by hiding the buttons.
/// </param>
/// <remarks>
/// <strong>The table is the documentation.</strong> The listener matches against this list and the
/// reference dialog renders the same list, so a shortcut that works and is not documented — or is
/// documented and does not work — cannot be written without deleting one of the two uses.
/// </remarks>
public sealed record KeyboardShortcut(
    string Id,
    string Key,
    string Description,
    string Group,
    bool Control = false,
    bool Shift = false,
    bool RequiresEditing = false)
{
    /// <summary>
    /// How the chord is written on screen.
    /// </summary>
    /// <remarks>
    /// "Ctrl / ⌘" rather than one or the other, because the browser cannot be asked which keyboard is
    /// attached and guessing from the user agent is how a Mac user is told to press a key they do not
    /// have.
    /// </remarks>
    public string Chord
    {
        get
        {
            var parts = new List<string>(3);

            if (Control) parts.Add("Ctrl / ⌘");

            if (Shift) parts.Add("Shift");

            parts.Add(Key switch
            {
                "?" => "?",
                "/" => "/",
                " " => "Space",
                _ => Key.ToUpperInvariant(),
            });

            return string.Join(" + ", parts);
        }
    }
}
