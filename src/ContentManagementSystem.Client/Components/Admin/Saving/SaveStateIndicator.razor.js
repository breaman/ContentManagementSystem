// The browser's own unsaved-changes prompt, for the one navigation .NET cannot see (task P6-18).
//
// Blazor's location-changing handler covers moving around inside the backoffice. Closing the tab,
// reloading, or typing a different address does not go through the router at all — and a tab closed
// fifteen seconds after the last keystroke is precisely the case autosave exists for, because the
// idle save has not fired yet.
//
// The message is deliberately not ours to write: every current browser ignores whatever a page
// passes and shows its own wording, and pretending otherwise would leave a string in the source
// that nobody ever sees.

/** The handler while armed, or null. One at a time, so arming twice does not warn twice. */
let handler = null;

/** Warns the editor before the document goes away. */
export function arm() {
    if (handler) {
        return;
    }

    handler = event => {
        // Both are required: preventDefault is the modern signal, returnValue is what older engines
        // still read, and a page that sets neither is closed without a word.
        event.preventDefault();
        event.returnValue = "";
    };

    window.addEventListener("beforeunload", handler);
}

/** Stops warning, because everything is saved. */
export function disarm() {
    if (!handler) {
        return;
    }

    window.removeEventListener("beforeunload", handler);
    handler = null;
}
