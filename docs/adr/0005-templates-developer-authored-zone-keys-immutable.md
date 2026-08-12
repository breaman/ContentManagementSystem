# 0005 — Templates are developer-authored and revisioned; zone keys are immutable

- **Identifier:** D5
- **Status:** Accepted
- **Source:** [`spec.md` §8.5](../../spec.md)

## Context

Payloads are keyed by zone key and block-type property key. If a key can be renamed, every payload
written under the old key becomes unreachable — content loss with no error message.

## Decision

Templates and block types are authored by developers, revisioned, and evolve under explicit rules
enforced in the service layer:

| Change | Rule |
|---|---|
| Add a zone | Free |
| Remove a zone | Allowed; existing payload data is retained as orphaned content |
| Rename a zone **key** | **Forbidden** |
| Rename a zone display name | Free |
| Change a field type | Requires an explicit converter choice |
| Delete a template | Blocked while pages reference it |

Each payload records the `templateRevision` it was written against, so a version can always be read
against the schema it was authored under.

## Consequences

- Content cannot be silently lost by a structure edit.
- Removing a zone is recoverable: the data is still in the payload and reachable as orphaned content.
- Code-defined templates absent from the database are created at startup by `TemplateReconciler`;
  database templates with no code component are marked `IsOrphaned` and **never deleted**, and they
  degrade the `cms-templates` health check while non-deleted pages still use them.
- Renaming a key is a two-step migration (add the new zone, convert, remove the old) rather than a
  one-click operation. This is the intended friction.
