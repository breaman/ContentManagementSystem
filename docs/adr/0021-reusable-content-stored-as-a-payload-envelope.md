# 0021 — A reusable item's content is an ordinary payload envelope over its block type's properties

- **Identifier:** D21
- **Status:** Accepted
- **Source:** tasks `P4-01`, `P4-03`, `P4-06`, [`spec.md` §6.2, §9.1](../../spec.md)

## Context

[§9.1](../../spec.md#91-model) says a reusable content item is "a named, independently versioned
content instance" whose shape is a **block type**. [§23.2](../../spec.md#232-content-tables) gives
`ReusableContentVersion` a `ContentJson` column and a `BlockTypeRevision` column and says nothing
about what goes in the first one.

That is the whole question. By the time Phase 4 starts, Phases 1 to 3 have built a considerable
amount of machinery that operates on *content*:

- `ContentSchemaValidator` walks a payload against a captured revision and dispatches every value to
  the field type that wrote it;
- `ReferenceIndexer` and `ContentReferenceProjector` turn a payload into the `ContentReference` rows
  that where-used, cache invalidation, and every delete guard are built on ([ADR-0001](./0001-hybrid-json-payload-with-relational-projection.md));
- `PayloadDiff` compares two versions;
- `ContentPayloadRemapper` rewrites references when content is duplicated;
- `CmsZone` and `FieldValueDispatch` render a stored value through the renderer its own discriminator
  names.

Every one of them takes a `ContentPayload` — an envelope of `schemaVersion`, `templateKey`,
`templateRevision`, and a `zones` object. None of them takes a block instance.

Meanwhile a block instance, as `BlocksFieldType` stores it, is
`{ id, blockTypeKey, blockTypeRevision, properties }`, where `properties` is an object keyed by
property alias whose values carry their own `type` discriminator.

The two look different. They are not.

## Decision

**A `ReusableContentVersion.ContentJson` is an ordinary content payload envelope.** Its
`templateKey` and `templateRevision` members carry the **block type** key and revision, and its
`zones` object holds the block's **properties**.

```jsonc
{
  "schemaVersion": 1,
  "templateKey": "rawHtml",        // the block type key
  "templateRevision": 1,           // the block type revision
  "zones": {                       // the block's properties
    "content": { "type": "html", "value": "<p>© Contoso</p>" }
  }
}
```

`ReusableContentSchema.For(catalog, blockTypeKey, revision)` presents the block type revision as the
`ContentSchema` the walk expects, which is a one-line construction because `ContentSchema` and
`BlockTypeSchema` are both lists of `ContentPropertySchema` differing only in what they call the
list.

The justification is one sentence: **a zone and a block-type property are the same thing to every
reader of a payload.** Both are a keyed slot with a field type and a captured configuration; both
store a value read by whatever wrote it; both are dispatched by that value's discriminator and never
by the schema. `FieldValueDispatch` already exists because that was true at render time, and
`SlotRules` already exists because it was true at validation time. This decision is the third
application of the same observation, at the storage layer.

## Consequences

**Phase 4 wrote no second validator, no second indexer, no second diff, and no second remapper.**
`ReusableContentService` calls `IContentSchemaValidator.ValidateAsync(payload, schema, mode, ct)` and
`IContentReferenceProjector.ProjectAsync(ReusableContentVersion, id, payload, ct)` — the same
implementations a page save calls, differing in one enum value. A reusable item therefore inherits,
for free and without anybody deciding to: absent-versus-null fidelity, orphaned-property retention,
unknown-field-type tolerance, the diagnostic path format, and reference extraction at any nesting
depth.

**Nesting works without a special case.** A reusable item whose block type has a `reusable` property
produces a `ContentReference` row exactly as a page does, so `ReferenceQueryService` walks item→item
edges with the same query it uses for page→item edges, and the cycle check reads one table.

**The envelope's member names now say something they did not.** `templateKey` holding a block type
key is the honest cost of this decision, and it is paid in documentation rather than in code:
`ReusableContentVersion.ContentJson` states it, `ReusableContentSchema` explains it, and
`ReusableContentService.CheckEnvelope` refuses a payload naming the wrong one. Renaming the members
was never available — they are the on-disk contract for every page version ever written
([`ContentPayloadMembers`](../../src/ContentManagementSystem.Shared/Content/ContentPayloadMembers.cs)).

**A reusable item renders through its block type's component.** `ReusableRenderer` builds a
`BlockRenderContext` from the resolved version and hands it to the component the block type key
declares, through the same `BlockParameters.For` the `blocks` renderer uses. A reusable footer and an
inline footer block of the same type produce identical markup, which is what makes "promote this
block to a reusable item" a future feature rather than a rewrite.

**Block ids are derived, not stored.** A block in a zone carries a GUID an editor's client generated;
a reusable item is one item, not one of a list, and has none. `BlockIds.ForReusableVersion` derives a
stable id from the **version** — not the item, because the render key must change when a publish
replaces the content beneath it, and not freshly per render, because then it would change on every
request.

## Alternatives considered

**Store a bare block instance (`{ id, blockTypeKey, blockTypeRevision, properties }`).** The obvious
reading of §9.1, and the one this started from. It needs a validator entry point for a standalone
block — the walk's block-property validation is private to `ContentSchemaValidator.Walk` — and a
reference-extraction path that synthesises `{ "type": "blocks", "items": [ block ] }` to reuse
`BlocksFieldType.ExtractReferences`. Since both roads end in "wrap it in something the existing code
already reads", the envelope wins on being the wrapper that is *already* the unit of storage,
caching, and diffing. Rejected.

**Store the block instance wrapped in a single-zone payload** — `zones: { content: { type: "blocks",
items: [block] } }`. Reuses everything, and adds a level of indirection that every reader would have
to unwrap, a block id nobody authored, and a `min: 1, max: 1` configuration to keep the list from
holding two. Rejected as the same decision with a redundant layer.

**Add a parallel `ReusableContentPayload` type.** Honest member names, and four more places for the
absent-versus-null distinction to be lost. Rejected — that distinction is the one thing
[`ContentPayload`](../../src/ContentManagementSystem.Shared/Content/ContentPayload.cs) exists to
protect, and it has never survived being reimplemented.

**Give the payload envelope new `schemaKey`/`schemaRevision` members and treat the old names as
aliases.** Clean going forward, and it splits every stored payload into two shapes that every reader
must handle for the life of the system, in exchange for two member names reading better. Rejected.
Worth revisiting only if the envelope is versioned for some other reason.
