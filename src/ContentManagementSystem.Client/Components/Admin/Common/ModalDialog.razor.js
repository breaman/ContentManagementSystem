// Focus management for the backoffice's modal dialogs (tasks P6-03, P6-21).
//
// Two things a dialog has to do that Blazor cannot do for it, and that are wrong in most hand-rolled
// modals: keep Tab inside the dialog while it is open, and put focus back where it came from when it
// closes. Both need the live document — which element has focus right now, which of the dialog's
// descendants are actually focusable — and neither is knowable from .NET.
//
// Everything else about the dialog (the backdrop, Escape, the ARIA) stays in the component, where it
// can be asserted by a rendering test.

/** Selector for the things a dialog can hand focus to. */
const focusableSelector = [
    "a[href]",
    "button:not([disabled])",
    "input:not([disabled]):not([type=hidden])",
    "select:not([disabled])",
    "textarea:not([disabled])",
    "[tabindex]:not([tabindex='-1'])",
].join(",");

/**
 * Traps Tab inside a dialog and remembers where focus should return to.
 *
 * @param {HTMLElement} dialog The dialog element.
 * @returns {object} A handle whose `dispose()` releases the trap and restores focus.
 */
export function trapFocus(dialog) {
    // Captured before anything inside the dialog is focused, which the component does on open.
    const origin = document.activeElement;

    const onKeyDown = event => {
        if (event.key !== "Tab") {
            return;
        }

        const focusable = [...dialog.querySelectorAll(focusableSelector)]
            .filter(element => element.offsetParent !== null || element === document.activeElement);

        if (focusable.length === 0) {
            // Nothing to move to; keep focus on the dialog itself rather than letting it escape to
            // the page behind, which is still there and still scrollable.
            event.preventDefault();

            return;
        }

        const first = focusable[0];
        const last = focusable[focusable.length - 1];

        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    };

    dialog.addEventListener("keydown", onKeyDown);

    return {
        dispose() {
            dialog.removeEventListener("keydown", onKeyDown);

            // Restoring focus is the half that gets forgotten. Without it, closing a dialog drops
            // focus onto <body> and a keyboard user starts again from the top of the document.
            if (origin && typeof origin.focus === "function" && origin.isConnected) {
                origin.focus();
            }
        },
    };
}
