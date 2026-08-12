# S3 — Editor JS interop in Blazor WebAssembly

**Task:** `P0-05` · **Timebox:** 2 ed · **Date:** 2026-08-12
**Code:** [`spikes/S3.EditorInterop`](../../spikes/S3.EditorInterop) — throwaway, not in the solution
**Spec:** [§14.4](../../spec.md#144-the-editpreview-experience), [§20.5](../../spec.md#205-content-security-policy)

## Recommendation: **GO**

CodeMirror 6 and Quill both initialize, bind in both directions, and dispose without leaking, as
locally bundled assets, under a CSP with **no `unsafe-inline` and no `unsafe-eval`**. The
textarea-plus-preview fallback is not needed.

**One load-bearing requirement:** the backoffice must issue a **per-request style nonce and expose it
to JavaScript**. Without it CodeMirror renders unstyled and unusable. This is not optional polish —
it is proven below with a control experiment.

`P6-08` through `P6-11` can be built as planned.

---

## The question

> Do CodeMirror 6 and Quill integrate cleanly (init, two-way bind, dispose without leaks) as local
> assets under a strict CSP?
>
> Fallback if no: a textarea-plus-preview editor for v1.

## What was built

A hosted Blazor WebAssembly app — Client plus ASP.NET Core Server, the same shape as this
solution — driven by Playwright in the same process:

- `src/editors.js`, bundled by **esbuild** into one local ESM module. No CDN, no `<script>` from
  another origin.
- `JsEditorComponentBase` — module import, `DotNetObjectReference` for change notifications,
  programmatic writes pushed down, full teardown in `DisposeAsync`.
- `MarkdownEditor` (CodeMirror 6) and `RichTextEditor` (Quill), both `@bind-Value`-able.
- A harness page whose editors can be mounted and unmounted repeatedly.
- **Two CSP variants** served from the same app: `/` with a per-request nonce, `/no-nonce` without —
  the control.

Run it with `dotnet run --project spikes/S3.EditorInterop/Server -c Release` (it installs the
Playwright browser on first run; `npm install && npm run build` first if the bundle is missing).
**23 checks, 23 passing.**

## Findings

### 1. Both editors work under the strict policy — GO

The policy under test, with no `unsafe-inline` and no `unsafe-eval` anywhere:

```
default-src 'self';
script-src 'self' 'wasm-unsafe-eval';
style-src  'self' 'nonce-{random}';
img-src    'self' data:;
font-src   'self';
connect-src 'self';
object-src 'none';
base-uri 'self';
form-action 'self';
frame-ancestors 'self'
```

Under it: Blazor WebAssembly boots, CodeMirror mounts, Quill mounts and builds its toolbar, both
receive their initial values from .NET, **zero CSP violations, zero console errors** across the whole
session. `'wasm-unsafe-eval'` is the only relaxation the runtime needs, exactly as
[§20.5](../../spec.md#205-content-security-policy) anticipated.

### 2. The style nonce is load-bearing — the single most important finding

CodeMirror 6 does not ship a stylesheet. It **injects a `<style>` element at runtime** (via
`style-mod`), which a strict `style-src` treats as inline and blocks. CodeMirror exposes the
`EditorView.cspNonce` facet for exactly this:

```js
EditorView.cspNonce.of(document.querySelector('meta[name="csp-nonce"]')?.content ?? "")
```

The control page — same code, same strict policy, no nonce — proves what happens without it:

| | with nonce | without nonce |
|---|---|---|
| CSP violations | none | `style-src-elem ← inline` |
| Computed `white-space` on `.cm-content` | `pre` (CodeMirror's own styling) | `normal` (browser default) |
| Usable? | yes | **no — renders as an unstyled div** |

So the backoffice host page must emit a per-request nonce **and expose it to JavaScript**, since the
nonce is generated server-side and consumed by a client-side library. A `<meta name="csp-nonce">`
tag rendered by the host page is the mechanism used here and it works.

> **A trap worth naming:** the failure is silent. There is no exception, no failed request, and the
> editor still *functions* — it is only unstyled. Anyone debugging this from a screenshot will look
> at the JavaScript for hours. Assert on it in a `P6` test.

### 3. Quill 2 needs no `style-src-attr` relaxation — better than expected

The concern was inline `style` attributes, which nonces cannot cover: relaxing `style-src-attr`
would have weakened the backoffice policy. Measured rather than assumed — the link tooltip was
opened over a selection, the interaction most likely to position an element inline:

- The tooltip opened (verified, not assumed).
- Its `style` attribute: **none** — Quill 2 positions with classes.
- `style-src-attr` violations: **0**.

The strict policy stands as written. This should be **re-tested on any Quill upgrade**; it is a
property of Quill 2's implementation, not a guarantee.

### 4. Two-way binding works in both directions, without echoing — GO

- **Editor → .NET:** typing in either editor updates the bound .NET value through
  `DotNetObjectReference` + `[JSInvokable]`.
- **.NET → editor:** a programmatic write reaches both editors.
- **No echo loop:** writing the same value a second time produced **zero** further change events
  (39 → 39). The guard is comparing against the last synchronized value on *both* sides before
  dispatching. Without it, each side's update re-triggers the other's — the classic wrapper bug,
  which surfaces as a cursor that jumps to position 0 while the editor is typing.

### 5. Disposal is clean, and Quill's toolbar is the trap — GO

After **11 mount/unmount cycles**:

| Check | Result |
|---|---|
| Editors created vs. disposed | 22 / 22 |
| JS-side registry entries remaining | 0 |
| `.cm-editor` nodes remaining | 0 |
| `.ql-editor` / `.ql-toolbar` nodes remaining | 0 / 0 |
| Injected `<style>` elements | 1 after the first mount, **1 after eleven** |
| Console errors | none |

Three things had to be right, and only the first is obvious:

1. **`view.destroy()`** for CodeMirror — removes its DOM and every listener it registered.
2. **Quill has no `destroy()`.** It appends its **toolbar as a sibling** of the container, so
   disposing only the container leaves toolbars accumulating on every mount — a visible leak within
   a handful of open/close cycles. The teardown must remove the toolbar explicitly.
3. **`DotNetObjectReference.Dispose()`** on the .NET side. Skip it and the JS registry keeps the
   component alive for the lifetime of the page. `IAsyncDisposable` on the component is what makes
   this run when Blazor removes it from the render tree.

CodeMirror's injected stylesheet is created once per document, not per editor, so nothing accumulates
there.

### 6. Asset weight is acceptable for a backoffice — GO

| Asset | Raw | Gzipped |
|---|---:|---:|
| `editors.js` — CodeMirror 6 + Quill + markdown/HTML languages, minified | 696 KB | **231 KB** |
| `quill.snow.css` | 24 KB | — |

Backoffice-only: none of this is served to an anonymous visitor of a public page, which is the whole
point of the [§5.3](../../spec.md#53-the-two-front-doors) split. It is loaded next to a Blazor
WebAssembly runtime that is already larger.

Still worth trimming in `P6-08`: the two editors could be split into separate dynamically-imported
modules so opening a page with only plain-text zones pays for neither.

## Consequences for Phase 6

1. **The backoffice host page emits a per-request CSP nonce and exposes it via
   `<meta name="csp-nonce">`**, and the CodeMirror initializer passes it through
   `EditorView.cspNonce`. Without this the editor is silently unusable.
2. **Build the editor bundle locally** (esbuild) as part of the front-end build alongside the
   existing Sass step. Add `npm run build` to CI so a missing bundle fails the build rather than the
   page.
3. **One base class for JS-editor components** — `JsEditorComponentBase` in the spike is close to
   what `P6-08` should ship: module import, `DotNetObjectReference`, echo suppression, and
   `IAsyncDisposable` teardown, so no individual editor wrapper can forget a step.
4. **Teardown is explicitly tested.** A Playwright test that mounts and unmounts an editor ten times
   and asserts zero surviving nodes is cheap and catches the entire class of leak. Add it to `P6`'s
   test list.
5. **Split the bundle per editor** so the Markdown editor and the WYSIWYG surface load independently.
6. **Re-run this spike's CSP checks on any Quill or CodeMirror upgrade.** Finding 3 is a property of
   the current versions.

## What this spike did not cover

- **The Edit/Preview/Split UI** ([§14.4](../../spec.md#144-the-editpreview-experience)) — `P6-08`
  to `P6-10`. The spike proves the interop substrate, not the surface built on it.
- **The shared Markdig → sanitize preview pipeline** (`P6-09`); that is a `P1-19` dependency.
- **CMS-aware link and image insertion** (`P6-11`).
- **Accessibility of the editor surfaces** (`P6` / `P9`). Neither library was audited here; axe-core
  against the real editor screens is the right instrument.
- **Server-interactive Blazor.** Irrelevant — this project is WebAssembly-only by policy.
