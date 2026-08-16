// Shared between the two editor bundles: one registry of live editors, and the disposal contract
// the .NET side calls through. Kept here rather than duplicated so that a page which loads only the
// source editor and a page which loads both behave identically.
//
// Bundled by esbuild into wwwroot/js/ — local static assets, no CDN, so the CSP in spec §20.5 can
// stay strict (ADR-0013).

const instances = new Map();

// Counters the E2E teardown test reads (task P6-31a). Created must equal disposed after a run of
// mount/unmount cycles, and the map must be empty — a registry that only ever grows is precisely
// the leak R14 names.
const stats = {
    created: 0,
    disposed: 0,
};

// One shared object across both bundles. Whichever loads first creates it; the second finds it and
// adds to it, so the counts are of every editor on the page rather than of one kind of editor.
const shared = (globalThis.__cmsEditors ??= {
    stats,
    live: () => instances.size,
    liveIds: () => [...instances.keys()],
    // DOM-level leak check. Quill in particular appends its toolbar as a SIBLING of the container,
    // so a naive dispose leaves toolbars accumulating on every mount.
    domCounts: () => ({
        codeMirror: document.querySelectorAll(".cm-editor").length,
        quillEditor: document.querySelectorAll(".ql-editor").length,
        quillToolbar: document.querySelectorAll(".ql-toolbar").length,
    }),
});

/**
 * The nonce the host page issued for this request.
 *
 * CodeMirror injects its theme as a <style> element at runtime; without this a strict style-src
 * blocks it and the editor renders unstyled — with no exception and no console error, which is why
 * spike S3 called it the load-bearing finding.
 */
export function cspNonce() {
    return document.querySelector('meta[name="csp-nonce"]')?.content ?? "";
}

/** Records a live editor under its id. */
export function register(id, instance) {
    // A second create under an id already in use would strand the first editor in the DOM with
    // nothing left holding its handle. Disposing it first keeps the registry a true census.
    if (instances.has(id)) {
        dispose(id);
    }

    instances.set(id, instance);
    shared.stats.created++;
}

/** Finds a live editor, or undefined once it has been disposed. */
export function find(id) {
    return instances.get(id);
}

/**
 * Tears an editor down and forgets it.
 *
 * Every instance supplies its own destroy(), because the two libraries need different things:
 * CodeMirror has a real destroy() that takes its DOM and listeners with it, while Quill has none at
 * all and its toolbar has to be removed by hand.
 */
export function dispose(id) {
    const instance = instances.get(id);

    if (!instance) {
        return false;
    }

    instance.destroy();
    instances.delete(id);
    shared.stats.disposed++;

    return true;
}

/** Reads an editor's current content, or null when it is already gone. */
export function getValue(id) {
    return instances.get(id)?.getValue() ?? null;
}

/**
 * Pushes a programmatic (.NET-side) write into an editor.
 *
 * Compares first. Replacing the document unconditionally fires the editor's own change event, which
 * echoes straight back into .NET and re-triggers this — the classic wrapper bug, which surfaces as a
 * cursor jumping to position 0 while somebody is typing.
 */
export function setValue(id, value) {
    const instance = instances.get(id);

    if (!instance || instance.getValue() === value) {
        return;
    }

    instance.setValue(value ?? "");
}
