# 0002 — Static SSR for the public site, interactive WebAssembly for the backoffice

- **Identifier:** D2
- **Status:** Accepted
- **Source:** [`spec.md` §5.3](../../spec.md)

## Context

The public site and the backoffice want opposite things. Public pages need full HTML in the first
response for crawlers and Core Web Vitals, and they need to be cacheable. The backoffice needs a
rich, stateful editing surface.

## Decision

Two render models in one application:

| | Public delivery | Backoffice |
|---|---|---|
| Routes | `/{**slug}` catch-all, `/media/*`, `/sitemap.xml`, `/robots.txt` | `/admin/**` |
| Render mode | Static SSR (`@rendermode` unset) | Interactive WebAssembly |
| Auth | Anonymous | Cookie auth with role policies |
| Data access | Direct, in-process, read-only, cached | HTTP via the Management API |

Interactive routing is scoped to `/admin` in `Routes.razor` (task P3-14).

## Consequences

- Output caching becomes viable: an interactive circuit cannot be cached, a static SSR response can.
  Everything in the Phase 8 caching workstream depends on this.
- Public pages carry no SignalR circuit, so a backoffice outage does not take cached public content
  down with it (NFR-11).
- Public templates and block components must render without an interactive render mode. This is what
  spike S2 exists to prove, since the delivery pipeline composes them through `DynamicComponent`.
- Individual interactive components (a search box, say) can still be opted in per component inside an
  otherwise static page.
- Per the repository's Blazor rules, interactive components use `InteractiveWebAssembly` only —
  never `InteractiveServer`.
