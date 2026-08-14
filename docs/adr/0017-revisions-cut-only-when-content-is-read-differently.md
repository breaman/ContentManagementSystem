# 0017 — A revision is cut only when content would be read differently

- **Identifier:** D17
- **Status:** Accepted
- **Source:** tasks `P1-21`, `P1-22`, `P1-23`, [`spec.md` §8.5](../../spec.md)

## Context

[§8.5](../../spec.md#85-template-evolution-and-schema-safety) says every structural change to a
template's zone set creates a new `TemplateRevision`, and that published versions render against the
revision they captured. It does not say what counts as structural, and the structure API cannot avoid
answering: every write either cuts a revision or does not.

The temptation is to cut one on every write. It is the conservative-looking choice — no change can
possibly go unrecorded — and it is wrong for a reason that only shows up months later. A revision
history exists to answer one question: *what did this template look like when that page was
published?* A template whose history is forty revisions, thirty-eight of which are a developer fixing
the wording of a help text, cannot answer it. The signal is there and unreadable, which is the same
as absent.

The opposite error is worse and easier to make by accident. If a change that alters how a stored
value is **judged** — a zone becoming required, a `maxLength` tightening — does not cut a revision,
then the snapshot a published page renders against silently changes underneath it. That is exactly
the guarantee §8.5 exists to give, broken quietly.

## Decision

**A write cuts a revision when it changes how a stored value is read or judged. It does not when it
only changes what a human is shown.**

Applied uniformly to zones, block-type properties, and the records that own them:

| Change | Cuts a revision | Why |
|---|---|---|
| Add a slot | yes | The schema gains a key. Existing payloads read it as absent. |
| Remove a slot | yes | The schema loses a key; stored values under it become orphaned. |
| `IsRequired` changes | yes | The same payload now passes or fails a publish. |
| Configuration changes | yes | The same value now passes or fails a field type's rules. |
| Compose or detach a group | yes | The flattened property set changes ([ADR 0018](0018-compositions-flattened-into-block-type-revisions.md)). |
| Name, help text, group, sort order | **no** | Nothing about reading a stored value changes. |
| Record-level metadata (template name, icon, summary template) | **no** | Same. |
| Key rename | refused | [ADR 0005](0005-templates-developer-authored-zone-keys-immutable.md). |
| Field type change | refused | See below. |

`ZoneSaveResult.CurrentRevision` and `PropertySaveResult.CurrentRevision` are returned on every
write, so a client can tell which happened without diffing what it sent against what came back.

### The field type change is refused, not deferred

[§8.5](../../spec.md#85-template-evolution-and-schema-safety) says changing a zone's field type
"requires an explicit migration: the `Developer` picks a converter (or 'clear values'), and a
background job rewrites drafts." Neither the converter nor the job — nor the drafts they rewrite —
exists before Phase 2. Three options were available:

1. **Accept the change and deal with the values later.** Every payload under that key becomes
   unreadable by the field type that now owns it, with no record of what it used to be. A hole with
   a date on it.
2. **Accept it only while no content exists.** True today and false the moment Phase 2 lands, at
   which point the rule changes underneath anyone who learned it.
3. **Refuse it, with a distinct code and a working alternative.** Chosen.

`StructureCodes.FieldTypeImmutable` is separate from `KeyImmutable` because the remedies differ and
a client should be able to offer them: a key is immutable forever, a field type needs a converter
that does not exist yet. The message names the path that does work — remove the slot and add it
again, which starts the key empty and is explicit about the loss.

### Built-in block types are structurally frozen

`BlockType.IsBuiltIn` refuses property adds, edits, and removals outright, because the code that
renders a built-in expects exactly the properties it ships with and no editor can repair a renderer.
Metadata stays editable: renaming "Raw HTML" changes nothing that any code depends on.

## Consequences

- **A revision snapshot's `name` and `sortOrder` can lag the live definition.** Accepted knowingly.
  A snapshot exists to pin *validation*; the editor builds its labels from the live definition. The
  alternative buries the history.
- **A developer correcting six help texts produces no revisions at all.** Intended. The audit log
  records who changed what; the revision history records what content has to be read against.
- **`P1-32`'s remaining rule — template delete blocked while pages reference it — is unaffected**,
  and lands with `P2-01` when there is a page table to ask.
- **Anything that changes the flattening rule must revisit this**, because "the schema gains a key"
  becomes true in a second way once a composition can contribute one.
