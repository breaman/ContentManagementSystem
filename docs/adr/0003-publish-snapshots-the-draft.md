# 0003 — Publish snapshots the draft; the draft survives

- **Identifier:** D3
- **Status:** Accepted
- **Source:** [`spec.md` §11](../../spec.md), requirement R-10

## Context

The requirement the whole system exists to satisfy: a published page stays exactly as published
while an editor works on the next version of it.

## Decision

Publishing copies the draft into a **new immutable `PageVersion`**, archives the previously published
version, and repoints `Page.PublishedVersionId` — all inside one transaction. The draft version row
is not consumed, moved, or renumbered; it continues to exist and continues to be what the editor
edits.

Saving a draft mutates the draft version in place and creates no new version row.

## Consequences

- Editing after publishing cannot disturb the published response, because the two are different rows
  and delivery filters on `PublishedVersionId` at the data layer.
- Version history is a real audit trail rather than a reconstruction.
- Restoring an old version **copies** it into the draft; it never resurrects the old row, so history
  stays append-only.
- The publish transaction does a lot in one unit of work — snapshot, archive, repoint, reindex
  references, enqueue invalidation. Partial application would corrupt exactly the guarantee this
  decision exists to provide, so fault-injection tests force a failure at each step and assert
  all-or-nothing (task P2-12).
