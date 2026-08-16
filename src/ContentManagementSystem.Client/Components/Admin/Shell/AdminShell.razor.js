// Interop for the backoffice shell (task P6-01, spec section 14.1).
//
// Two jobs, both of which are here because .NET cannot do them well from WebAssembly:
//
//  1. Pane resizing. A pointer drag fires pointermove sixty to a hundred times a second. Marshalling
//     every one of those into .NET — and re-rendering the component after each, which is what Blazor
//     does when an event handler returns — makes a drag that stutters. So the drag runs entirely
//     here, writing the width straight onto a CSS custom property, and reports the final width to
//     .NET once, on release. .NET stays the authority on what the width *is*; this file is only the
//     hand that moves it.
//
//  2. Layout persistence. localStorage has no .NET equivalent.
//
// Every export is defensive about storage: Safari in private browsing throws from localStorage, and
// an editor whose pane widths are not remembered has a smaller problem than one who cannot open the
// backoffice at all.

const storagePrefix = "cms.shell.";

/**
 * Reads a saved layout.
 * @param {string} key Identifies the editor whose layout this is.
 * @returns {object|null} The stored layout, or null when there is none to read.
 */
export function loadLayout(key) {
    try {
        const raw = localStorage.getItem(storagePrefix + key);

        return raw ? JSON.parse(raw) : null;
    } catch {
        // Unreadable or unparseable: the caller falls back to the default layout.
        return null;
    }
}

/**
 * Stores a layout.
 * @param {string} key Identifies the editor whose layout this is.
 * @param {object} layout The layout to remember.
 */
export function saveLayout(key, layout) {
    try {
        localStorage.setItem(storagePrefix + key, JSON.stringify(layout));
    } catch {
        // Quota exceeded, or storage denied. Nothing here is worth failing an edit over.
    }
}

/**
 * Makes a separator element drag-resize a pane.
 *
 * @param {HTMLElement} handle The separator the pointer grabs.
 * @param {HTMLElement} host The element carrying the CSS custom property.
 * @param {object} options `{ variable, sign, min, max, method }`. `sign` is +1 when dragging right
 *   widens the pane (a pane on the left) and -1 when it narrows it (a pane on the right).
 * @param {object} dotnet A DotNetObjectReference to invoke `options.method` on when the drag ends.
 * @returns {object} A handle whose `dispose()` removes every listener this added.
 */
export function attachResizer(handle, host, options, dotnet) {
    let startX = 0;
    let startWidth = 0;
    let width = 0;
    let pointerId = null;

    const clamp = value => Math.min(options.max, Math.max(options.min, value));

    const onPointerDown = event => {
        // Primary button only. A right-click on a separator is meant for the context menu, and a
        // touch-contextmenu gesture must not leave the pane stuck to the finger.
        if (event.button !== 0 || pointerId !== null) {
            return;
        }

        pointerId = event.pointerId;
        startX = event.clientX;
        startWidth = parseFloat(getComputedStyle(host).getPropertyValue(options.variable)) || options.min;
        width = startWidth;

        // Pointer capture is what keeps the drag alive when the pointer outruns the separator, which
        // it always does — the separator is four pixels wide.
        handle.setPointerCapture(pointerId);
        event.preventDefault();
    };

    const onPointerMove = event => {
        if (event.pointerId !== pointerId) {
            return;
        }

        width = clamp(startWidth + ((event.clientX - startX) * options.sign));
        host.style.setProperty(options.variable, `${width}px`);
    };

    const onPointerUp = event => {
        if (event.pointerId !== pointerId) {
            return;
        }

        handle.releasePointerCapture(pointerId);
        pointerId = null;

        // One call per gesture rather than per frame. The .NET side re-renders with this width, which
        // overwrites the inline style above with the same value.
        dotnet.invokeMethodAsync(options.method, width);
    };

    handle.addEventListener("pointerdown", onPointerDown);
    handle.addEventListener("pointermove", onPointerMove);
    handle.addEventListener("pointerup", onPointerUp);
    handle.addEventListener("pointercancel", onPointerUp);

    return {
        dispose() {
            handle.removeEventListener("pointerdown", onPointerDown);
            handle.removeEventListener("pointermove", onPointerMove);
            handle.removeEventListener("pointerup", onPointerUp);
            handle.removeEventListener("pointercancel", onPointerUp);
        },
    };
}
