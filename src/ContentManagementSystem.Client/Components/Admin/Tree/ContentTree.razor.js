// Interop for the content tree (task P6-04).
//
// One export, for one thing .NET cannot know: where an element is on the screen. A context menu
// opened by a right-click has the pointer's coordinates in the event; one opened by Shift+F10 on the
// focused row has nothing, and has to be placed under that row. Without this, the keyboard path
// would have to open the menu somewhere arbitrary — which is how a menu ends up in the top-left
// corner of the viewport while the row it belongs to is halfway down the tree.

/**
 * Reports where a menu anchored to an element should be drawn.
 *
 * @param {HTMLElement} element The row the menu belongs to.
 * @returns {{x: number, y: number}} Viewport coordinates of the row's bottom-left corner.
 */
export function anchorOf(element) {
    const rect = element.getBoundingClientRect();

    return { x: rect.left, y: rect.bottom };
}
