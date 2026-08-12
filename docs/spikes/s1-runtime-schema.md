# S1 — Runtime-schema payload round trip

**Task:** `P0-03` · **Timebox:** 2 ed · **Date:** 2026-08-12
**Code:** [`spikes/S1.RuntimeSchema`](../../spikes/S1.RuntimeSchema) — throwaway, not in the solution
**Spec:** [§6.2](../../spec.md#62-the-central-storage-decision-json-payload--relational-projection),
[§7](../../spec.md#7-field-type-catalog), [§8.5](../../spec.md#85-template-evolution-and-schema-safety)

## Recommendation: **GO**

A JSON content payload can be validated, walked, and round-tripped against a schema that exists only
as *data*, with error messages that name the exact zone, block, and property, and at a cost of
roughly **1.2 µs per block**. No fallback to code-defined content types is needed.

`P1-15` (`ContentSchemaValidator`) and `P1-16` (`ReferenceIndexer`) can be built as planned.

---

## The question

> Can a JSON payload be validated and deserialized against a *runtime-defined* schema (zones and
> block-type properties as data) with acceptable performance and clear errors?
>
> Fallback if no: code-defined content types, losing runtime zone editing.

The risk being tested is **R2** — that a runtime-defined schema proves too complex to validate
cleanly, with the stated trigger being *"validator error messages cannot identify the offending
field."*

## What was built

A console harness that self-checks and prints pass/fail:

- **`data/schema.json`** — three template revisions and three block-type revisions. Nothing in the
  spike has a CLR counterpart; no POCO, no `JsonSerializer.Deserialize<T>`, no source generator. This
  file stands in for the `Template` / `Zone` / `BlockType` / `BlockTypeProperty` rows.
- **`PayloadValidator`** — walks a `JsonDocument` against the schema and dispatches every leaf to an
  `IFieldType`. Recurses through `blocks` back into itself with a depth guard.
- **Nine field types** — `plainText`, `richText`, `number`, `boolean`, `choice`, `media`, `link`,
  `reusable`, `blocks` — implementing a pared-down `IFieldType` (`Validate`, `ExtractReferences`).
- **`data/payload-valid.json` / `payload-invalid.json`** — a realistic page and a deliberately
  broken one carrying sixteen distinct classes of defect.

Run it with `dotnet run --project spikes/S1.RuntimeSchema -c Release`. **39 checks, 39 passing.**

## Findings

### 1. Round-tripping is lossless, including the parts that are easy to lose — GO

| Property | Result |
|---|---|
| Parse → serialize is identical after canonicalization | ✅ |
| An **orphaned zone** (removed from the template) survives | ✅ |
| An **explicitly cleared** value stays `null` | ✅ |
| A **never-authored** zone stays absent | ✅ |

Absent-vs-null ([§6.2](../../spec.md#62-the-central-storage-decision-json-payload--relational-projection))
is preserved for free by treating the payload as a document rather than binding it to a type — a
POCO with nullable members cannot represent the distinction without a wrapper per property. This is
an argument *for* the runtime approach, not merely a cost of it.

Block GUIDs survive a reorder, so `ContentDiffService` (`P2-14`) can report **moved** rather than
removed-plus-added. Verified by mutating through `JsonNode`, re-serializing, and re-validating.

### 2. Error messages identify the exact location — GO, and this was the risk

R2's trigger was error messages that cannot name the offending field. They can. Every diagnostic
carries a payload path an editor UI can jump to. Verbatim, from the invalid payload:

```
ERROR zones.hero[0].properties.headline
      [field.maxLength] Value is 158 characters; the maximum is 120.
ERROR zones.hero[0].properties.cta
      [field.link.pageId] An internal link must carry 'pageId'; URLs are resolved at render (ADR-0006).
warn  zones.hero[0].properties.subtitle
      [property.orphaned] No property with this key exists in the block type revision; the value is retained as orphaned content.
ERROR zones.hero[1]
      [block.id.duplicate] Block id '0f6c1f2e-…' appears more than once; ids must be unique within a list.
ERROR zones.hero[3]
      [blockType.revision.unknown] No revision 99 of block type 'quote' is known.
ERROR zones.sidebar[0].properties.columnCount
      [field.max] Value 9 is above the maximum 4.
ERROR zones.footer
      [zone.type.mismatch] Zone is defined as 'reusable' but the payload declares 'media'.
```

Three implementation notes worth carrying into `P1-15`:

- **The path is a stack, not a string.** Segments are pushed and popped during the walk and only
  joined into a string when a diagnostic is actually produced. Building `"zones.hero[0].properties.headline"`
  per property visited would dominate the cost and allocate on the happy path.
- **Diagnostics carry a stable `code` as well as a message.** The API error contract in
  [§22.2](../../spec.md#222-error-contract) needs a machine-readable discriminator; retrofitting one
  onto prose messages later is painful.
- **Severity is real.** Orphaned zones and properties are warnings, not errors, which is what makes
  the [§8.5](../../spec.md#85-template-evolution-and-schema-safety) "removing a zone keeps the data"
  rule implementable rather than aspirational.

### 3. Draft-vs-publish validation modes fall out naturally — GO

The same walk runs in `Draft` or `Publish` mode. Required-but-empty is an error only when publishing,
which is exactly the [§8.3](../../spec.md#83-zone-properties) rule ("blocks publish if empty, does not
block draft save") and half of `P2 #11`.

Template evolution was tested directly: the revision-7 payload validated against revision 8, which
**removes** `sidebar` and **adds a required** `announcement`.

- The draft still saves. `sidebar` becomes one `zone.orphaned` warning at `zones.sidebar`.
- Publishing fails with `zone.required` at `zones.announcement`, naming the zone.

That is the [§8.5](../../spec.md#85-template-evolution-and-schema-safety) contract, mechanically
demonstrated.

### 4. Reference extraction reaches nested blocks — GO, with a caveat

All five references in the realistic payload were found, including one **two block levels down**
(`sidebar` → `text-columns` → `children` → `quote` → `portrait`), with an accurate path:

```
Media            812   zones.hero[0].properties.image
Media            913   zones.hero[1].properties.portrait
Media            977   zones.sidebar[0].properties.children[0].properties.portrait
Page             44    zones.hero[0].properties.cta
Page             91    zones.readMore
ReusableContent  3     zones.footer
```

**The caveat, and it matters for `P1-13`:** nested references are only found because `blocks`
delegates back into the schema walk. A field type that contains other field types and does *not*
delegate silently drops every reference beneath it. The contract test as written in the task list
("every registered field type returns references for a representative populated value") would **not**
catch that — a `blocks` field type returning references only for its top level passes it.

> **Recommendation for `P1-13`:** the contract test needs a second case — a *container* field type
> must return the references of a nested populated value, not just its own. Added as a note against
> the task.

### 5. Performance is a non-issue — GO

Parse + validate + extract references, Release, Apple Silicon, .NET 10.0.9, p50/p95 over 2 000
iterations after warming every size:

| Blocks | Payload | parse+validate p50 | p95 | + references p50 | alloc/op |
|---:|---:|---:|---:|---:|---:|
| 1 | 581 B | 1.9 µs | 2.0 µs | 2.3 µs | 1.4 KB |
| 10 | 4.0 KB | 12.4 µs | 12.6 µs | 15.3 µs | 10.7 KB |
| 50 | 19.9 KB | 59.7 µs | 62.8 µs | 72.6 µs | 52.0 KB |
| 200 | 79.6 KB | 234.7 µs | 257.9 µs | 288.6 µs | 211.1 KB |

Linear at ~1.2 µs and ~1 KB per block. A 50-block page — far larger than a typical marketing page —
validates in **under 63 µs at p95**, against a save/publish budget measured in hundreds of
milliseconds. Validation runs on save and publish, not on render, so this never touches the delivery
path at all.

One measurement note: naive warmup produced numbers where 200 blocks appeared *faster per block* than
50, purely tiered-JIT artifact. The harness now warms every payload size before measuring any of
them. **Worth remembering when `P3-27` builds the render benchmark harness** — the same trap applies.

Parsed `ConfigurationJson` is cached per schema row. Without that cache, re-parsing configuration per
property visit dominated everything else.

## Consequences for Phase 1

1. **`P1-15` is a walk, not a deserializer.** Keep `JsonDocument`/`JsonElement` through validation.
   Do not introduce an intermediate CLR model — it is where absent-vs-null goes to die.
2. **Path-as-stack, and a stable diagnostic `code`** on every result. Both are cheap now, invasive
   later.
3. **`ValidationMode` (draft vs publish)** is a first-class parameter of the validator, not a caller-
   side filter of the results.
4. **Cache parsed field configuration** keyed by the schema row, invalidated when the revision changes.
5. **`P1-13`'s contract test needs the nested case** described above.
6. **Block-list depth is enforced by the validator**, not by the editor. The spike caps at one level
   of nesting per [§7.1](../../spec.md#71-v1-field-types); a payload nesting deeper is a hard error
   with a clear message.

## What this spike did not cover

- `SanitizeAsync` — sanitization is `P1-18`, and HtmlSanitizer's behavior is not in doubt.
- `ExtractSearchText` — same walk, different visitor; nothing new is proven by writing it.
- Concurrency and persistence — SQL Server `nvarchar(max)` round-tripping is not a risk.
- JSON Schema validation of `ConfigurationJson` itself (`P1-12`). The spike hand-reads configuration
  keys; the real implementation validates configuration against a per-field-type schema on save.
