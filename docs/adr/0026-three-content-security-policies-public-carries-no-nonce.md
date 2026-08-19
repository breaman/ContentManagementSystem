# 0026 — Three content security policies, and the public one carries no nonce

- **Identifier:** D26
- **Status:** Accepted
- **Source:** task `P9-01`, [`spec.md` §20.5](../../spec.md#205-content-security-policy),
  supersedes one consequence of [ADR 0013](0013-backoffice-editor-bundle-and-style-nonce.md)

## Context

[§20.5](../../spec.md#205-content-security-policy) specifies one public policy and one backoffice
policy, and writes a `'nonce-{random}'` into both. `P6-08` and [ADR 0013](0013-backoffice-editor-bundle-and-style-nonce.md)
built the nonce machinery for the backoffice; `P9-01` is where the header actually goes out.

Three things had changed by then, and each one contradicts a detail of the policy as written.

1. **Public responses are cached.** `P8-06` made delivery output-cached and shared across instances.
   A per-request value written into a response that is then stored and replayed to everyone is not a
   nonce — it is a constant with a long life, quotable by anyone who has loaded the page once. The
   directive as specified would be strictly *weaker* than omitting it.
2. **The public document has no inline script.** Its only `<script>` elements are the JSON-LD blocks
   of `P8-02`, and `application/ld+json` is a *data block*: the HTML parser's "prepare the script
   element" steps return before the CSP check, because the type is not a classic script, a module, or
   an import map. Nothing on a public page ever needs to quote a nonce.
3. **Inline `style` attributes are authored content.** `style` is an allowed attribute under the
   `Extended` and `Developer` sanitization profiles, and six backoffice components position with a
   computed one — the tree's depth indent, the context menu's coordinates, the shell's pane geometry,
   a colour swatch, a page list's indent, an upload's progress bar. CSP has no nonce for a style
   attribute. ADR 0013 recorded that `style-src-attr` was **not** relaxed, and said in the same breath
   that a future need for inline style attributes "is a policy change requiring its own decision".
   This is that decision.

There is also a surface §20.5 does not name. Preview frames its own rendered content in an `iframe`
to apply a device width to it (§12.3), and the editing canvas frames that again. Both documents come
from this origin, and `frame-ancestors 'none'` refuses same-origin framing exactly as it refuses any
other.

## Decision

**Three profiles, selected from endpoint metadata, with the strictest as the default.**

| Profile | Given to | Differs from public by |
|---|---|---|
| `Public` | everything that asks for nothing | — |
| `Preview` | the `/preview` group | `frame-ancestors 'self'` |
| `Backoffice` | `MapRazorComponents<App>` | `'wasm-unsafe-eval'`, the nonce, `frame-ancestors 'self'`, `frame-src 'self'` |

```
default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline';
img-src 'self' data: https:; font-src 'self'; connect-src 'self';
frame-src <sanitizer host allowlist>; frame-ancestors 'none';
base-uri 'self'; form-action 'self'; object-src 'none'
```

Four things follow from the context above:

1. **The public policy has no nonce.** `script-src 'self'` alone, which is stronger than
   `'self' 'nonce-…'` for a cached document and is all the document needs.
2. **The backoffice policy has one nonce, quoted twice** — `script-src` for the import map Blazor
   renders inline, `style-src` for the `<style>` element CodeMirror injects. It is base64**url**
   encoded, so the value in the header and the value in the attribute are the same bytes in the HTML
   source, not merely after the parser has decoded a `&#x2B;`.
3. **`style-src-attr 'unsafe-inline'` in every profile.** Attributes only — not `<style>` elements,
   not stylesheets. What makes it acceptable is that the sanitizer has already reduced what such an
   attribute may say to `SanitizationPolicy.AllowedCssProperties`, which contains nothing that can
   position an element, cover the page, or fetch a URL. Widening that list is therefore a CSP change
   as well as a sanitizer change.
4. **`frame-src` is generated from `SanitizationOptions.AllowedIframeHosts`.** The sanitizer decides
   whether an authored `iframe` survives being stored; the policy decides whether the browser then
   loads it. One list, two enforcement points.

The profile is endpoint metadata rather than a path prefix, and there is deliberately no way to opt
*into* the public policy — it is what a route gets for saying nothing.

## Consequences

- **Bootstrap Icons is served from this origin.** It was a `<link>` to jsDelivr, which `default-src
  'self'` refuses; the alternative was a CDN host in the policy, for a font. It is copied out of
  `node_modules` by an MSBuild target, like the Bootstrap bundle beside it.
- **ADR 0013's `style-src-attr` consequence no longer holds.** Its reasoning about *Quill* still
  does — Quill 2 positions with classes, and that is still worth re-checking on an upgrade — but the
  directive is relaxed for this application's own components regardless.
- **The public policy will need revisiting if a public page ever needs an inline script.** The
  options at that point are a hash (stable content, computed at build) or making the response
  uncacheable, and the second is not really an option. A nonce is not available while the response is
  shared, and that is a property of the caching decision rather than of this one.
- **A CSP is on in Development too.** A policy that only runs in production is a policy nobody has
  tested, and its failures are the silent kind. `Cms:SecurityHeaders:ReportOnly` exists for measuring
  a change against real traffic, and `ContentSecurityPolicyEnabled` for a deployment that has found a
  genuine break and needs to ship the fix rather than a rollback. Neither is a launch setting.
- **The nonce is generated per request whether or not the document uses one.** Only the backoffice
  profile reads it, so the cost is a 16-byte RNG call on backoffice requests and nothing at all on
  public ones.
