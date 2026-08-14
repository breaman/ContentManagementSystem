# 0019 — The schema sync is additive and non-destructive; it refuses rather than applies

- **Identifier:** D19
- **Status:** Accepted
- **Source:** tasks `P1-26`, `P1-28`, [`spec.md` §27.1, §8.5](../../spec.md)

## Context

[§27.1](../../spec.md#271-structure-templates-block-types-zones-compositions) promotes structure by
committing zone and property definitions to versioned JSON under `Server/CmsSchema/` and applying
them at startup "in an idempotent, additive-only pass (never destructive)". Two words in that
sentence have to be pinned down before any of it can be written.

**"Additive-only" is ambiguous.** Read strictly, the pass may only ever insert: a file that changes
an existing zone's `maxLength` would do nothing, and a structure promotion could never actually
promote a change. Read loosely, it may apply anything that is not a deletion — including retyping a
zone, which makes every stored value under that key unreadable.

**A promotion runs where nobody is watching.** It is a startup pass in an environment a developer is
not looking at, against content they cannot see. Whatever it does wrong, it does silently. That
asymmetry — cheap to get right, expensive to discover — is what decides the reading.

## Decision

**The pass creates what is missing, updates what is safe, refuses what is destructive, and never
removes anything.**

| The file says | The pass does |
|---|---|
| A record that does not exist | Creates it, marked `IsOrphaned` until code claims the key |
| A slot that does not exist | Adds it, cutting a revision |
| An existing slot with different labels, grouping, or order | Updates it, cutting no revision ([ADR 0017](0017-revisions-cut-only-when-content-is-read-differently.md)) |
| An existing slot with different `isRequired` or configuration | Updates it, cutting a revision |
| An existing slot with a **different field type** | **Refuses**, and leaves the slot alone |
| A configuration the field type rejects | **Refuses**, and leaves the slot alone |
| A group to compose that is not composed | Composes it, unless a key would collide |
| Nothing about a slot that exists in the database | Keeps it, and says so |

Supporting decisions, each of which was a fork in the road:

- **One file per record**, named `template.<key>.json` and so on, rather than one manifest. The
  reason the format exists at all is source control; a single file would make every structural
  change a conflict against every other one. The `key` **inside** the document is authoritative, not
  the filename, so a file renamed in a merge still describes the record it says it does.
- **The whole pass is one transaction.** A promotion that applied four files out of six would leave
  a content model matching no commit.
- **Compositions, then block types, then templates.** A block type file may name a group the same
  promotion introduces, and dependency order is what lets one commit add both. Because the
  composition is added but not yet saved when the block type file is read, the lookup checks the
  change tracker before the database — a query alone goes to the server and does not find it.
- **Built-in block types are neither applied nor exported.** Their property set ships with the code
  and the sync refuses to reshape it, so a file describing one could only ever be a refusal on every
  future run: permanent drift in the CI check, from a record nobody can change anyway.
- **A startup failure does not stop the host.** A site whose content model is one commit out of date
  still serves every page it has; a site that will not start serves none. The failure is logged and
  the `cms-templates` health check reports on what it can see.

### `diff` exits non-zero, and a refusal counts as drift

`dotnet run -- cms schema diff` is the CI drift check, and a check that always succeeds is not a
check. It exits **2** when the files and the database disagree — distinct from **1**, which means the
command itself failed. A refusal counts as drift even though nothing is pending, because a file
asking for something that will never be applied should be taken out of the repository rather than
reported on every future run. A slot the database has and the files do not is **not** drift: the
sync is deliberately not removing it, and failing CI for that would make the check impossible to keep
green while anyone edits structure in the backoffice.

## Consequences

- **A definition can only be removed by a human, through the API or the admin screens.** Promotion
  cannot do it. This is the intended asymmetry: adding is safe and automatic, removing is a decision
  someone makes while looking at what depends on it.
- **A field type change still has to be made by hand in every environment**, which is tedious and
  correct — it is the change that makes stored values unreadable, and
  [ADR 0017](0017-revisions-cut-only-when-content-is-read-differently.md) refuses it through the API
  too.
- **`export` then `diff` must settle to nothing.** Anything the exporter drops shows up as permanent
  drift, so the round trip is asserted by a test rather than left to be discovered in CI.
- **The report is one computation with three callers** — the startup pass, `cms schema apply`, and
  `cms schema diff` — so the drift check in CI and the thing that runs at startup cannot disagree
  about what a file means.
