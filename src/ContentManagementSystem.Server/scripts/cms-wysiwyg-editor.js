// Quill — the constrained WYSIWYG surface of spec §14.4 (task P6-08).
//
// Its own bundle, so a page with no HTML-format rich text never downloads it (ADR-0013).

import Quill from "quill";

import { register, dispose, find, getValue, setValue } from "./editor-registry.js";

export { dispose, getValue, setValue };

/**
 * The toolbar, deliberately short.
 *
 * "Constrained" is the word spec §14.4 uses, and it is a content decision rather than a limitation:
 * every button here maps to something the Basic sanitization profile keeps, so nothing an author can
 * press produces markup that is stripped on save. A font-size dropdown would.
 *
 * Link and image are NOT in this list. They open the CMS pickers instead (task P6-11) — a
 * hand-typed URL to an internal page is a copy that nothing updates when the page moves (ADR-0006).
 */
const toolbar = [
    ["bold", "italic"],
    [{ header: [2, 3, 4, false] }],
    [{ list: "ordered" }, { list: "bullet" }],
    ["blockquote", "code-block"],
    ["clean"],
];

/**
 * Mounts a WYSIWYG editor into an element.
 *
 * @param {string} id stable key for the registry, and what dispose() is called with
 * @param {HTMLElement} element the container; Quill takes it over
 * @param {string} initialHtml the markup to open with
 * @param {object} dotNetRef DotNetObjectReference used to report changes back
 * @param {boolean} readOnly whether the surface refuses edits
 * @param {string} label the editor's accessible name
 */
export function create(id, element, initialHtml, dotNetRef, readOnly, label) {
    ensureStylesheet();

    const quill = new Quill(element, {
        theme: "snow",
        readOnly: readOnly === true,
        modules: { toolbar },
    });

    // On Quill's own editable element rather than on the host div; see cms-source-editor.js.
    if (label) {
        quill.root.setAttribute("aria-label", label);
    }

    if (initialHtml) {
        // dangerouslyPasteHTML is Quill's supported way to seed content and is not a bypass of
        // anything: what reaches it has already been sanitized on write, and what leaves it is
        // sanitized again on write and once more on render (ADR-0008).
        quill.clipboard.dangerouslyPasteHTML(initialHtml);
    }

    const onTextChange = () => dotNetRef.invokeMethodAsync("OnValueChangedFromJs", readHtml(quill));

    quill.on("text-change", onTextChange);

    register(id, {
        getValue: () => readHtml(quill),
        setValue: (value) => quill.clipboard.dangerouslyPasteHTML(value),
        destroy: () => {
            quill.off("text-change", onTextChange);

            // Quill has no destroy(). Its toolbar is a sibling node it created, so removing only
            // the container leaves toolbars accumulating on every mount — a visible leak within a
            // handful of open/close cycles, and the one spike S3 was written to catch.
            const toolbarNode = quill.container?.previousElementSibling;

            if (toolbarNode?.classList?.contains("ql-toolbar")) {
                toolbarNode.remove();
            }

            quill.container.innerHTML = "";
        },
        quill,
    });
}

/**
 * Inserts a link over the current selection, or as new text when nothing is selected.
 *
 * @param {string} id the editor's registry key
 * @param {string} href where the link goes
 * @param {string} text the words to show, used only when there is no selection
 */
export function insertLink(id, href, text) {
    const instance = find(id);

    if (!instance) {
        return;
    }

    const quill = instance.quill;
    const range = quill.getSelection(true) ?? { index: quill.getLength() - 1, length: 0 };

    if (range.length === 0) {
        const words = text || href;

        quill.insertText(range.index, words, "link", href);
        quill.setSelection(range.index + words.length, 0);
    } else {
        quill.formatText(range.index, range.length, "link", href);
    }

    quill.focus();
}

/**
 * Inserts an image at the caret.
 *
 * @param {string} id the editor's registry key
 * @param {string} src the image's address
 * @param {string} alt what the image says, for a reader who cannot see it
 */
export function insertImage(id, src, alt) {
    const instance = find(id);

    if (!instance) {
        return;
    }

    const quill = instance.quill;
    const range = quill.getSelection(true) ?? { index: quill.getLength() - 1, length: 0 };

    quill.insertEmbed(range.index, "image", src);

    // Quill's image blot carries no alt, so it is set on the node afterwards. An image with no
    // alternative text is a publish-blocking omission for the media field type and no less of one
    // inside prose.
    const inserted = quill.root.querySelector(`img[src="${CSS.escape(src)}"]:not([alt])`);

    if (inserted) {
        inserted.setAttribute("alt", alt ?? "");
    }

    quill.setSelection(range.index + 1, 0);
    quill.focus();
}

/** The plain text currently selected, which a link picker offers as the link's words. */
export function getSelection(id) {
    const instance = find(id);

    if (!instance) {
        return "";
    }

    const range = instance.quill.getSelection();

    return range && range.length > 0 ? instance.quill.getText(range.index, range.length) : "";
}

/**
 * Adds Quill's stylesheet the first time an editor is mounted.
 *
 * A <link> to a same-origin file rather than a tag on the host page, so an anonymous visitor to a
 * public page never downloads 24 KB of editor CSS — the same reasoning that splits the two bundles
 * (ADR-0013). It is an external stylesheet and not an inline one, so `style-src 'self'` covers it
 * and no nonce is involved.
 */
function ensureStylesheet() {
    if (document.querySelector('link[data-cms-quill]')) {
        return;
    }

    const link = document.createElement("link");

    link.rel = "stylesheet";
    link.href = "/css/quill.snow.css";
    link.dataset.cmsQuill = "";

    document.head.appendChild(link);
}

/**
 * Quill's document as HTML, with its placeholder for "empty" normalised away.
 *
 * An empty Quill holds `<p><br></p>`, which is markup rather than nothing — stored, it makes an
 * untouched zone look authored, and a required property pass a check it should fail.
 */
function readHtml(quill) {
    const html = quill.root.innerHTML;

    return html === "<p><br></p>" ? "" : html;
}
