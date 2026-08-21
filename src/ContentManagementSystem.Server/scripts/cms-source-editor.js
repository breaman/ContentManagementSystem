// CodeMirror 6 — the Markdown, HTML, and CSS source surfaces of spec §14.4 and §30.3
// (tasks P6-08, P6-13, and P10-10).
//
// Its own bundle, separate from the WYSIWYG one, so a page with only plain-text zones downloads
// neither and a page with only a markdown zone downloads one (ADR-0013).

import { EditorView, keymap, lineNumbers, highlightActiveLine, drawSelection } from "@codemirror/view";
import { EditorState } from "@codemirror/state";
import { defaultKeymap, history, historyKeymap, indentWithTab } from "@codemirror/commands";
import { markdown } from "@codemirror/lang-markdown";
import { html as htmlLanguage } from "@codemirror/lang-html";
import { css as cssLanguage } from "@codemirror/lang-css";

import { cspNonce, register, dispose, find, getValue, setValue } from "./editor-registry.js";

export { dispose, getValue, setValue };

/**
 * Mounts a source editor into an element.
 *
 * @param {string} id stable key for the registry, and what dispose() is called with
 * @param {HTMLElement} element the container; Blazor renders nothing inside it
 * @param {string} initialValue the document to open with
 * @param {object} dotNetRef DotNetObjectReference used to report changes back
 * @param {string} language "markdown", "html", or "css"
 * @param {string} label the editor's accessible name
 */
export function create(id, element, initialValue, dotNetRef, language, label) {
    const view = new EditorView({
        parent: element,
        state: EditorState.create({
            doc: initialValue ?? "",
            extensions: [
                lineNumbers(),
                highlightActiveLine(),
                drawSelection(),
                history(),
                // indentWithTab is deliberately last so it does not shadow the default binding:
                // Tab must still move focus out of the editor unless the author is mid-indent,
                // or the editor becomes a keyboard trap (spec §28).
                keymap.of([...defaultKeymap, ...historyKeymap, indentWithTab]),
                languageFor(language),
                EditorView.lineWrapping,
                // The load-bearing line. See editor-registry.js.
                EditorView.cspNonce.of(cspNonce()),
                EditorView.editable.of(!element.hasAttribute("data-readonly")),
                EditorView.updateListener.of((update) => {
                    if (!update.docChanged) {
                        return;
                    }

                    dotNetRef.invokeMethodAsync("OnValueChangedFromJs", update.state.doc.toString());
                }),
            ],
        }),
    });

    // The name goes on CodeMirror's own contenteditable element, not on the host div. The host is a
    // bare <div>, which may not carry aria-label at all, and the zone card's aria-labelledby cannot
    // reach through to an element the library created — so without this a screen reader lands on the
    // editor and announces "edit text, blank".
    if (label) {
        view.contentDOM.setAttribute("aria-label", label);
    }

    register(id, {
        getValue: () => view.state.doc.toString(),
        setValue: (value) =>
            view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } }),
        // destroy() removes the DOM and every listener CodeMirror registered.
        destroy: () => view.destroy(),
        view,
    });
}

/**
 * The language support for a mode name.
 *
 * A third mode on this bundle rather than a fourth bundle: the site stylesheet editor is the same
 * CodeMirror with a different grammar, and splitting it out would download the editor twice for an
 * administrator who edits a Markdown zone and then the stylesheet (ADR-0013).
 *
 * Markdown is the fallback because it is the mode the zone editors open with, and an unrecognised
 * name is a bug in the caller rather than a reason to render an unusable editor.
 *
 * @param {string} language the requested mode
 */
function languageFor(language) {
    switch (language) {
        case "html":
            return htmlLanguage();
        case "css":
            return cssLanguage();
        default:
            return markdown();
    }
}

/**
 * Replaces the current selection, or inserts at the caret when nothing is selected.
 *
 * This is what CMS-aware link and image insertion writes through (task P6-11): the picker decides
 * what the reference is, and the editor only has to put the resulting text where the author was.
 *
 * @param {string} id the editor's registry key
 * @param {string} text what to insert
 * @param {boolean} selectInserted whether to leave the inserted text selected
 */
export function replaceSelection(id, text, selectInserted) {
    const instance = find(id);

    if (!instance) {
        return;
    }

    const view = instance.view;
    const { from, to } = view.state.selection.main;

    view.dispatch({
        changes: { from, to, insert: text },
        selection: selectInserted
            ? { anchor: from, head: from + text.length }
            : { anchor: from + text.length },
    });

    // Focus goes back to the editor rather than staying on whatever dialog closed, so the author
    // carries on typing where the insertion landed.
    view.focus();
}

/** The text currently selected, which a link picker offers as the link's words. */
export function getSelection(id) {
    const instance = find(id);

    if (!instance) {
        return "";
    }

    const { from, to } = instance.view.state.selection.main;

    return instance.view.state.doc.sliceString(from, to);
}

/**
 * Reports the editor's scroll position as a fraction of its scrollable height.
 *
 * A fraction rather than a pixel offset, because the pane it is synchronised with (task P6-10)
 * renders different content at a different height — matching pixels would drift further apart the
 * further down a long document an author scrolled.
 */
export function scrollFraction(id) {
    const instance = find(id);

    if (!instance) {
        return 0;
    }

    const element = instance.view.scrollDOM;
    const scrollable = element.scrollHeight - element.clientHeight;

    return scrollable > 0 ? element.scrollTop / scrollable : 0;
}

/** Subscribes to the editor's scrolling, for split mode. Returns a handle with dispose(). */
export function onScroll(id, dotNetRef) {
    const instance = find(id);

    if (!instance) {
        return null;
    }

    const element = instance.view.scrollDOM;

    // rAF-coalesced: a scroll gesture fires dozens of events per second and each one would
    // otherwise be an interop call into .NET and a re-render of the preview beside it.
    let queued = false;

    const handler = () => {
        if (queued) {
            return;
        }

        queued = true;

        requestAnimationFrame(() => {
            queued = false;

            const scrollable = element.scrollHeight - element.clientHeight;

            dotNetRef.invokeMethodAsync(
                "OnScrolledFromJs",
                scrollable > 0 ? element.scrollTop / scrollable : 0);
        });
    };

    element.addEventListener("scroll", handler, { passive: true });

    return {
        dispose: () => element.removeEventListener("scroll", handler),
    };
}
