// Bundled with esbuild into ../Server/wwwroot/js/editors.js — a local ESM module, no CDN, so the
// CSP in spec §20.5 can stay strict. Loaded from .NET with
// IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/editors.js").

import { EditorView, keymap, lineNumbers, highlightActiveLine } from "@codemirror/view";
import { EditorState } from "@codemirror/state";
import { defaultKeymap, history, historyKeymap } from "@codemirror/commands";
import { markdown } from "@codemirror/lang-markdown";
import { html as htmlLanguage } from "@codemirror/lang-html";
import Quill from "quill";

// Every live editor is tracked so the harness can prove disposal actually happened. A registry that
// only ever grows is exactly the leak this spike is looking for.
const instances = new Map();

const stats = {
    created: 0,
    disposed: 0,
    changeEvents: 0,
};

window.__cmsEditors = {
    stats,
    live: () => instances.size,
    liveIds: () => [...instances.keys()],
    // DOM-level leak check: destroyed editors must take their nodes with them. Quill in particular
    // appends its toolbar as a SIBLING of the container, so a naive dispose leaves toolbars behind.
    domCounts: () => ({
        codeMirror: document.querySelectorAll(".cm-editor").length,
        quillEditor: document.querySelectorAll(".ql-editor").length,
        quillToolbar: document.querySelectorAll(".ql-toolbar").length,
        styleTags: document.querySelectorAll("style").length,
    }),
};

function cspNonce() {
    return document.querySelector('meta[name="csp-nonce"]')?.content ?? "";
}

export function createMarkdownEditor(id, element, initialValue, dotNetRef) {
    const language = element.dataset.language === "html" ? htmlLanguage() : markdown();

    const view = new EditorView({
        parent: element,
        state: EditorState.create({
            doc: initialValue ?? "",
            extensions: [
                lineNumbers(),
                highlightActiveLine(),
                history(),
                keymap.of([...defaultKeymap, ...historyKeymap]),
                language,
                // CodeMirror injects its theme as a <style> element at runtime. Without a nonce that
                // is an inline style and a strict style-src blocks it — the editor renders unstyled
                // and unusable. This facet is the supported way to keep the CSP strict.
                EditorView.cspNonce.of(cspNonce()),
                EditorView.updateListener.of((update) => {
                    if (!update.docChanged) {
                        return;
                    }

                    stats.changeEvents++;
                    dotNetRef.invokeMethodAsync("OnValueChangedFromJs", update.state.doc.toString());
                }),
            ],
        }),
    });

    instances.set(id, { kind: "codemirror", view, dotNetRef });
    stats.created++;
}

export function createRichTextEditor(id, element, initialHtml, dotNetRef) {
    const quill = new Quill(element, {
        theme: "snow",
        modules: {
            toolbar: [["bold", "italic"], [{ header: [2, 3, false] }], ["link", "blockquote"]],
        },
    });

    if (initialHtml) {
        quill.clipboard.dangerouslyPasteHTML(initialHtml);
    }

    const onTextChange = () => {
        stats.changeEvents++;
        dotNetRef.invokeMethodAsync("OnValueChangedFromJs", quill.root.innerHTML);
    };

    quill.on("text-change", onTextChange);

    instances.set(id, { kind: "quill", quill, onTextChange, container: element, dotNetRef });
    stats.created++;
}

export function getValue(id) {
    const instance = instances.get(id);

    if (!instance) {
        return null;
    }

    return instance.kind === "codemirror"
        ? instance.view.state.doc.toString()
        : instance.quill.root.innerHTML;
}

export function setValue(id, value) {
    const instance = instances.get(id);

    if (!instance) {
        return;
    }

    if (instance.kind === "codemirror") {
        const view = instance.view;

        // Replacing the whole document would fire the update listener and echo straight back into
        // .NET. The binding stays one-directional for programmatic writes by comparing first.
        if (view.state.doc.toString() === value) {
            return;
        }

        view.dispatch({
            changes: { from: 0, to: view.state.doc.length, insert: value ?? "" },
        });

        return;
    }

    if (instance.quill.root.innerHTML !== value) {
        instance.quill.clipboard.dangerouslyPasteHTML(value ?? "");
    }
}

export function dispose(id) {
    const instance = instances.get(id);

    if (!instance) {
        return false;
    }

    if (instance.kind === "codemirror") {
        // destroy() removes the DOM and every listener CodeMirror registered.
        instance.view.destroy();
    } else {
        instance.quill.off("text-change", instance.onTextChange);

        // Quill has no destroy(). Its toolbar is a sibling node it created, so removing only the
        // container leaves the toolbar in the document — the leak this spike was written to catch.
        instance.quill.container?.previousElementSibling?.classList?.contains("ql-toolbar") &&
            instance.quill.container.previousElementSibling.remove();
        instance.container.innerHTML = "";
    }

    instances.delete(id);
    stats.disposed++;

    return true;
}
