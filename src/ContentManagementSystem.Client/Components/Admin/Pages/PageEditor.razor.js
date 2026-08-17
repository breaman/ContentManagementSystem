// The publish dialog's deep links (task P6-20).
//
// The card is already addressable — every zone renders as `id="zone-{key}"` with `tabindex="-1"`
// (task P6-05) — but going to it needs the live document: scrolling is the browser's, and moving
// focus is what makes the link work for somebody who is not looking at the screen. A plain
// `href="#zone-hero"` would scroll and, in a single-page application, also push a history entry
// nobody asked for.

/**
 * Scrolls a zone card into view and puts focus on it.
 *
 * @param {string} id The card's element id.
 */
export function focusZone(id) {
    const card = document.getElementById(id);

    if (!card) {
        return;
    }

    // Respects prefers-reduced-motion through `behavior: "auto"`, which follows the user agent's
    // own setting rather than forcing a smooth scroll on somebody who asked for none (task P6-39).
    card.scrollIntoView({ behavior: "auto", block: "center" });
    card.focus({ preventScroll: true });
}
