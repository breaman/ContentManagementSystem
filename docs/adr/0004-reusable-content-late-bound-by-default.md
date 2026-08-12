# 0004 — Reusable content is late-bound by default, pinnable by exception

- **Identifier:** D4
- **Status:** Accepted
- **Source:** [`spec.md` §9.2](../../spec.md), goal G4

## Context

Footers, banners, and carousels are authored once and appear on many pages. The goal is that
updating one of them updates every page that uses it, in a single publish, without republishing
those pages. But some pages need to keep the version they were reviewed against.

## Decision

A `reusable` field stores a `reusableContentId`. At render time the delivery pipeline resolves it to
that item's **currently published version** — late binding. A page may optionally set
`pinnedVersionId` to freeze a specific version.

## Consequences

- One publish of a reusable item changes every late-bound page. Pinned pages do not change.
- Pinning is visible in the UI as a badge plus an "update to latest" action, so a pinned page cannot
  quietly rot.
- The publish-impact dialog reports affected pages split by pinned and late-bound, and reusable
  publishes are audited with their impact list — otherwise "why did 40 pages change at 14:02?" has no
  answer.
- Late binding means a reusable publish fans out cache invalidation across every dependent page. The
  cost of that fan-out is measured in Phase 4 and tuned in Phase 8 (risk R8).
- Resolution needs a cycle check and a depth guard: reusable content can reference reusable content.
