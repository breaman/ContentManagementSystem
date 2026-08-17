// Document-level keyboard shortcuts for the backoffice (task P6-23).
//
// Blazor can handle a key press on an element it rendered. What it cannot do is hear one that landed
// anywhere else on the page — and "anywhere else" is where an editor's focus usually is: inside
// CodeMirror, inside Quill, on a link in the properties panel. A shortcut that worked only while
// focus was inside one particular div would be worse than none, because it would work often enough
// to be relied on.
//
// Two rules keep it from being a nuisance. It never fires while somebody is typing into a plain
// field unless the chord holds a modifier, so `?` in a title box types a question mark. And it only
// calls preventDefault for a chord .NET actually claimed, so every browser shortcut the editor did
// not define still belongs to the browser.

/**
 * Starts listening for the given chords.
 *
 * @param {object} handler A DotNetObjectReference whose `MatchAsync` returns the matched id or null.
 * @returns {object} A handle whose `dispose()` removes the listener.
 */
export function listen(handler) {
    const onKeyDown = async event => {
        // A modifier-less chord inside a text field belongs to the field. Checked here rather than
        // in .NET because only the document knows what has focus.
        if (!event.ctrlKey && !event.metaKey && isTyping(event.target)) {
            return;
        }

        // Held-down keys repeat; a shortcut should not.
        if (event.repeat) {
            return;
        }

        const matched = await handler.invokeMethodAsync(
            "MatchAsync",
            event.key,
            event.ctrlKey || event.metaKey,
            event.shiftKey,
            event.altKey);

        if (matched) {
            // Only now. Preventing the default for every key press would take Ctrl+F, Ctrl+T, and
            // the browser's own find-as-you-type away from an editor who never asked for that.
            event.preventDefault();
        }
    };

    document.addEventListener("keydown", onKeyDown);

    return {
        dispose() {
            document.removeEventListener("keydown", onKeyDown);
        },
    };
}

/** Whether the element with focus is somewhere a bare key press means a character. */
function isTyping(target) {
    if (!target) {
        return false;
    }

    if (target.isContentEditable) {
        return true;
    }

    const tag = target.tagName;

    return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT";
}
