# 0018 — Compositions are flattened into block type revisions, and editing one recuts every host

- **Identifier:** D18
- **Status:** Accepted
- **Source:** tasks `P1-23`, `P1-24`, [`spec.md` §6.3, §8.5](../../spec.md)

## Context

A `Composition` is a named group of property definitions that block types share instead of
re-declaring — spacing options, analytics attributes, the things that repeat across every block type
in a design system. A block type composes zero or more of them, and their properties land in the same
block instance as its own.

That indirection collides with the guarantee in
[§8.5](../../spec.md#85-template-evolution-and-schema-safety): published content renders against the
revision it captured, so a structural change cannot retroactively alter what is live. A composition
sits one level away from anything content addresses. A block instance names a block type and a
revision number; it never names a composition. So when someone edits a shared group, what exactly is
supposed to have changed?

Two shapes were possible.

**Resolve compositions at read time.** A revision snapshot records "this block type composes
`spacing-options`", and whatever reads it looks the group up. Cheap to write, one row per edit — and
it breaks the guarantee outright. Editing the group changes what every already-published block
renders, in every version of every page, retroactively. The revision would be pinning a pointer
rather than a schema.

**Flatten at write time.** The snapshot records the composed properties themselves, and editing a
group cuts a new revision on every block type composing it. The guarantee holds exactly as it does
for a directly declared property.

## Decision

**A block type revision snapshot contains the fully flattened property set — its own properties
followed by each composed group's — and a write to a composition cuts a new revision on every block
type composing it, in one transaction.**

Consequences that follow directly:

- **A composition is not itself revisioned.** Nothing in a payload addresses it, so a revision number
  on it would be a number nothing could ever capture. The API instead returns
  `AffectedBlockTypeKeys` from every composition write — the honest answer to "what did that do".
- **Own properties come first, then each group in `SortOrder`.** The two sort orders live in
  different tables and are free to overlap; merging on the number would shuffle a composed group
  into the middle of a block type's own properties the first time a developer numbered them alike.
  `ContentSchemaSnapshot.WriteSlots` therefore records each slot's *effective* position — its index
  in the flattened sequence — not the raw column it came from.
- **Composed and own keys share one namespace, checked in both directions.** Adding a property whose
  key a composed group already contributes is refused; composing a group whose keys the host already
  declares is refused; and adding a property *to a group* is refused when it would collide on any
  block type composing it. That last one is the case that matters — the collision is not where the
  edit is made, and without the check the failure surfaces as a broken editor on a block type nobody
  was looking at.
- **A composed property is not editable on its host.** The API exposes no route for it and the admin
  screen renders no control. Editing it there would fork one shared definition into many, which is
  the thing compositions exist to prevent.
- **Detaching a group is additive-safe.** It cuts a revision; earlier revisions keep the properties,
  so already-published blocks are untouched. The values in those blocks survive as orphaned content,
  exactly as a removed zone's do.

## Consequences

- **Editing a group used by twelve block types writes twelve revisions in one transaction.** This is
  the cost of the guarantee, and it is why the write reports which block types it reached. A group
  composed into hundreds of block types would make this expensive; if that ever happens, the fix is
  a bounded batch with a background continuation, not read-time resolution.
- **`BlockTypeSchemaWriter` is shared by both services** — the block type service and the composition
  service — because a flattening rule implemented twice would let the two disagree about a block
  type's property order, and the disagreement would only show up in a snapshot nobody reads until a
  page renders wrong.
- **A composition delete is guarded and refuses while any block type composes it.** It is the one
  structural delete Phase 1 can honestly ship: its guard is a join table that exists, unlike a
  template delete, which must ask a page table that does not.
- **The schema sync obeys the same rule.** Composing a group from a file cuts a revision; a file that
  omits a group already composed leaves it composed
  ([ADR 0019](0019-schema-sync-is-additive-and-non-destructive.md)).
