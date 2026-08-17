namespace ContentManagementSystem.Client.Services;

/// <summary>
/// Where an editor's work has got to, as the save-state indicator reports it (task P6-18,
/// spec section 11.3).
/// </summary>
/// <remarks>
/// Five states rather than a boolean, because "saved" and "not saved" cannot tell an editor the one
/// thing they need to know when a save fails: whether their typing is still going to be written.
/// </remarks>
public enum AutosavePhase
{
    /// <summary>Nothing has been typed since the last save. The only state that is safe to leave in.</summary>
    Saved = 0,

    /// <summary>There are changes, and the idle timer has not run out yet.</summary>
    Pending = 1,

    /// <summary>A save is in flight.</summary>
    Saving = 2,

    /// <summary>
    /// A save failed for a reason that may not recur, and another attempt is scheduled.
    /// </summary>
    /// <remarks>
    /// The offline case, and the whole point of queueing: the edit is still held, the attempt will
    /// be made again, and nothing an editor typed has been dropped (acceptance criterion P6 #5).
    /// </remarks>
    Retrying = 3,

    /// <summary>
    /// A save was refused for a reason retrying cannot fix — a validation error, or a conflict.
    /// </summary>
    /// <remarks>
    /// Autosave stops here on purpose. Repeating a request the server has already reasoned about
    /// produces the same refusal every twenty seconds and buries the message that explains it.
    /// </remarks>
    Refused = 4,
}

/// <summary>
/// What the save-state indicator shows, and what the ARIA live region announces (tasks P6-18, P6-22).
/// </summary>
/// <param name="Phase">Where the work has got to.</param>
/// <param name="SavedOn">
/// When the last successful save happened, or null before the first one. This is the "Saved 14:32"
/// of spec section 14.1 — a time rather than a word, because "saved" on its own is indistinguishable
/// from "saved twenty minutes ago and silently broken since".
/// </param>
/// <param name="Attempt">
/// How many attempts the current save has taken, counted from one. Shown while retrying so a
/// stuck connection reads as an effort being made rather than as a screen that has given up.
/// </param>
/// <param name="Message">Why the last attempt did not work, when something is worth saying.</param>
public sealed record AutosaveStatus(
    AutosavePhase Phase,
    DateTimeOffset? SavedOn = null,
    int Attempt = 0,
    string? Message = null)
{
    /// <summary>Nothing typed, nothing saved: how a freshly opened editor starts.</summary>
    public static AutosaveStatus Clean { get; } = new(AutosavePhase.Saved);

    /// <summary>Whether there is work the editor would lose by closing the tab now.</summary>
    public bool HasUnsavedWork => Phase is not AutosavePhase.Saved;
}
