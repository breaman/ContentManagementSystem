// Split mode's other half (task P6-10). The source editor reports where it is as a fraction of its
// scrollable height, and this puts the preview at the same fraction of its own.
//
// A fraction rather than a pixel offset because the two panes are different heights: one line of
// markdown can become a picture. Matching pixels drifts further apart the further down a long
// document an author scrolls, which is exactly where synchronised scrolling is worth having.

/**
 * Scrolls an element to a fraction of its scrollable height.
 *
 * @param {HTMLElement} element the pane to scroll
 * @param {number} fraction from 0 (top) to 1 (bottom)
 */
export function scrollToFraction(element, fraction) {
    if (!element) {
        return;
    }

    const scrollable = element.scrollHeight - element.clientHeight;

    if (scrollable <= 0) {
        return;
    }

    const target = Math.round(scrollable * Math.min(Math.max(fraction, 0), 1));

    // Nothing is done when the pane is already there. Assigning scrollTop unconditionally fires a
    // scroll event, and a preview that reported its own position back would put the two panes in a
    // feedback loop neither could settle out of.
    if (Math.abs(element.scrollTop - target) < 1) {
        return;
    }

    // "auto" rather than "smooth": the author is driving with the other pane's scrollbar, and an
    // animated follower always lags behind the gesture that caused it. It also respects
    // prefers-reduced-motion by construction rather than by a media query (task P6-39).
    element.scrollTo({ top: target, behavior: "auto" });
}
