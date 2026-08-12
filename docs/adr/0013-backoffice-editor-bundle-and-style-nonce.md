# 0013 — Locally bundled editors, and a per-request style nonce for the backoffice

- **Identifier:** D13
- **Status:** Accepted
- **Source:** [spike S3](../spikes/s3-editor-interop.md), [`spec.md` §14.4, §20.5](../../spec.md)

## Context

[§14.4](../../spec.md#144-the-editpreview-experience) selects CodeMirror 6 for Markdown and HTML
source modes and Quill for the constrained WYSIWYG surface, "both loaded as local static assets — no
CDN, so the CSP in §20.5 can stay strict."

Spike S3 built that and measured what the two libraries actually require of the policy. It found one
requirement the spec did not anticipate: **CodeMirror 6 ships no stylesheet.** It injects a `<style>`
element at runtime, which a strict `style-src` treats as inline and blocks. The editor then renders
as an unstyled `<div>` — with no exception, no failed request, and no console error. It still
*functions*, so the failure is invisible to everything except a human looking at the screen.

## Decision

Three things, together:

1. **Editor JavaScript is bundled locally** with esbuild into the backoffice's static assets, as part
   of the front-end build alongside the existing Sass step. No CDN, no external origin in any
   fetch-directive.
2. **The backoffice host page emits a per-request CSP nonce** and exposes it to JavaScript through a
   `<meta name="csp-nonce">` tag. `style-src` is `'self' 'nonce-{random}'`. The CodeMirror
   initializer passes that nonce through the `EditorView.cspNonce` facet.
3. **No `unsafe-inline` and no `unsafe-eval` anywhere.** `script-src` relaxes only to
   `'wasm-unsafe-eval'`, which the Blazor WebAssembly runtime requires.

The policy S3 verified end to end:

```
default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'nonce-{random}';
img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none';
base-uri 'self'; form-action 'self'; frame-ancestors 'self'
```

## Consequences

- The backoffice host page cannot be a purely static `index.html` — it is rendered per request so it
  can carry a fresh nonce. This is a small but real constraint on how `/admin` is served.
- A nonce generated on the server is consumed by a client-side library, so the `<meta>` tag (or an
  equivalent channel) is part of the contract between them, not an implementation detail either side
  may drop.
- **The failure mode is silent.** `P6-08` carries a test asserting that CodeMirror's own styling is
  in effect (a computed style differing from the browser default), because nothing else will catch a
  regression here.
- `style-src-attr` is **not** relaxed. Quill 2 positions with classes rather than inline style
  attributes, verified against the link tooltip. This is a property of the current version — re-run
  the S3 checks on any Quill upgrade, and if a future version needs inline style attributes, that is
  a policy change requiring its own decision.
- Bundle weight lands in the backoffice only: 696 KB raw, 231 KB gzipped for both editors. `P6-08`
  should split it per editor so a page with only plain-text zones loads neither.
- CI must run the front-end bundle build, so a missing bundle fails the build rather than the page.
