# 0008 — Sanitize on write **and** on render

- **Identifier:** D8
- **Status:** Accepted
- **Source:** [`spec.md` §20.2](../../spec.md)

## Context

A CMS stores HTML that editors author. Sanitizing only on write assumes every write goes through the
application — but imports, direct database writes, restored backups, and content authored before a
profile changed all bypass that assumption.

## Decision

Run `SanitizationService` (over HtmlSanitizer) on write **and** again on render. Three profiles —
`Basic`, `Extended`, `Developer` — share cross-profile rules that no profile can relax: no
`<script>`, no `<style>`, no `on*` handlers, a URL scheme allowlist, forced
`rel="noopener noreferrer"` on external links, and a CSS property allowlist.

## Consequences

- Defense in depth: a payload that reached the database by some other route is still neutralised
  before it reaches a browser.
- Render-time sanitization costs CPU on the delivery path. It sits behind the output cache, so the
  cost is paid per cache miss rather than per request.
- Sanitization is lossy and users notice. The HTML editor warns *before* save about what the active
  profile will strip, because silent stripping is the single most common "the CMS ate my content"
  support ticket (task P6-13).
- The XSS corpus suite (OWASP payloads plus polyglots) is a CI merge gate from Phase 1 onward, and it
  reports what was stripped rather than only asserting safety — over-stripping is the other failure
  mode (risk R3).
- Markdown follows the same path: Markdig renders to HTML, then that HTML is sanitized, identically
  in editor preview and in delivery.
