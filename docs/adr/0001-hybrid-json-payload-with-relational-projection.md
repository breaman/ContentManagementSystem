# 0001 — Hybrid JSON payload with a relational reference projection

- **Identifier:** D1
- **Status:** Accepted
- **Source:** [`spec.md` §6.2](../../spec.md)

## Context

A page version's content has to hold a schema that developers and `Developer`-role users define at
runtime — zones and block-type properties are rows in the database, not CLR types. Storage has to
support two very different access patterns: rendering, which always wants the whole document for one
version, and integrity questions ("which pages use this image?", "what breaks if I delete this?"),
which want indexed joins.

Three options were considered:

| Option | Verdict |
|---|---|
| Fully relational (EAV) — `PageVersionFieldValue(versionId, zoneKey, index, type, …)` | Rejected. Rendering one page becomes a wide join over dozens of sparse rows, nested blocks need recursive CTEs, and every new field type is a schema change. |
| Fully JSON — one `nvarchar(max)`, no projection | Rejected. No referential integrity, and where-used degrades to a full scan with `JSON_VALUE`. Link integrity becomes unimplementable. |
| Hybrid — JSON payload plus derived reference rows | **Selected.** |

## Decision

Store the payload as a single JSON document in `PageVersion.ContentJson` (`nvarchar(max)`), and
derive `ContentReference` rows from it on every save and publish via `ReferenceIndexer`.

Because the schema is runtime data, EF Core's `ToJson()` and owned-entity JSON mapping do not
apply — both need a compile-time POCO. The payload is stored as a `string` and validated against the
runtime `Zone` / `BlockTypeProperty` definitions by `ContentSchemaValidator`.

## Consequences

- Rendering is one row read and one deserialization. No joins.
- Published versions are immutable, so the usual objection to document-in-a-column — concurrent
  partial writes — does not arise.
- Where-used, link integrity, cache-tag computation, and orphan detection all run against indexed
  relational rows.
- The projection is only as good as the field types feeding it. Every `IFieldType` must return its
  references from `ExtractReferences`; a field type that forgets produces silently stale content.
  This is why a contract test asserts the behaviour for every registered type (task P1-13).
- If payload-internal querying is ever needed, the migration path is computed persisted columns over
  SQL Server's JSON functions — no table restructuring.
