# Content Management System — Implementation Plan

**Status:** Draft for review
**Version:** 1.0
**Last updated:** 2026-08-12
**Specification:** [`spec.md`](./spec.md)
**Source requirements:** [`requirements.md`](./requirements.md)

---

## Table of contents

1. [How to read this plan](#1-how-to-read-this-plan)
2. [Delivery approach](#2-delivery-approach)
3. [Assumptions and estimating basis](#3-assumptions-and-estimating-basis)
4. [Roadmap at a glance](#4-roadmap-at-a-glance)
5. [Phase 0 — Foundations and de-risking spikes](#phase-0--foundations-and-de-risking-spikes)
6. [Phase 1 — Content structure](#phase-1--content-structure)
7. [Phase 2 — Pages, versioning, and publishing](#phase-2--pages-versioning-and-publishing)
8. [Phase 3 — Delivery, routing, and preview](#phase-3--delivery-routing-and-preview)
9. [Phase 4 — Reusable content](#phase-4--reusable-content)
10. [Phase 5 — Media library and image pipeline](#phase-5--media-library-and-image-pipeline)
11. [Phase 6 — Authoring experience](#phase-6--authoring-experience)
12. [Phase 7 — Workflow, permissions, and scheduling](#phase-7--workflow-permissions-and-scheduling)
13. [Phase 8 — SEO, caching, navigation, and search](#phase-8--seo-caching-navigation-and-search)
14. [Phase 9 — Hardening, accessibility, and launch](#phase-9--hardening-accessibility-and-launch)
    - [Phase 10 — Editor-managed site styling](#phase-10--editor-managed-site-styling) — added after the plan was first written; nested here so the numbered sections below keep the numbers other documents cite
15. [Post-v1 — the v2 backlog](#post-v1--the-v2-backlog)
16. [Cross-cutting workstreams](#16-cross-cutting-workstreams)
17. [Database migration sequence](#17-database-migration-sequence)
18. [Changes required to existing code](#18-changes-required-to-existing-code)
19. [CI/CD and quality gates](#19-cicd-and-quality-gates)
20. [Risk register](#20-risk-register)
21. [Definition of done](#21-definition-of-done)
22. [Launch and rollout](#22-launch-and-rollout)
23. [Requirements traceability](#23-requirements-traceability)

---

## 1. How to read this plan

Each phase states:

- **Objective** — the one sentence that justifies the phase.
- **Entry criteria** — what must be true before starting.
- **Deliverables** — concrete artifacts, with file paths in the existing solution layout.
- **Tasks** — the work, sized in engineer-days (`ed`).
- **Acceptance criteria** — observable, testable statements. These become the phase's test cases.
- **Exit criteria** — the gate to the next phase.
- **Risks** — phase-specific, cross-referenced to the [risk register](#20-risk-register).

Spec cross-references appear as `[§n]`, pointing into [`spec.md`](./spec.md).

---

## 2. Delivery approach

### 2.1 Vertical slices over horizontal layers

The plan is sequenced so that a **working end-to-end path exists as early as possible** and every phase
after it makes that path richer rather than building toward a big-bang integration.

The key milestone is the **end of Phase 3**, at which point the system can do the thing
`requirements.md` actually asks for: a developer defines a template with zones, an editor creates a
page from it, fills the zones, saves a draft, publishes, and an anonymous visitor sees it at a URL —
while further edits stay invisible until the next publish. Everything before Phase 3 is in service of
that moment; everything after it broadens and hardens it.

### 2.2 Admin UI is built incrementally, not deferred

Phases 1–5 each ship a **functional but unstyled** admin screen sufficient to exercise the feature —
plain Bootstrap forms and tables, no bespoke interaction design. Phase 6 replaces those screens with
the designed three-pane authoring experience from [§14].

This is deliberate. Deferring *all* UI until Phase 6 would make Phases 1–5 undemonstrable and untested
by real use; building the polished UI in Phase 1 would mean rebuilding it as the model changes.

### 2.3 Security and accessibility are not a phase

Sanitization ships in Phase 1 with the first field type — before any HTML can be stored. Authorization
policies ship in Phase 2 with the first write endpoint. Accessibility is checked continuously by
axe-core in CI from Phase 1. Phase 9 is a *verification and hardening* phase, not the phase in which
these concerns first appear. Retrofitting either is far more expensive than building with them.

### 2.4 Schema-first within each phase

Order inside a phase is consistently: entity + EF configuration → migration → service layer with unit
tests → API endpoint with integration tests → UI. Migrations are additive and reviewed as carefully as
code, because a bad content migration is not trivially reversible in production.

---

## 3. Assumptions and estimating basis

| Assumption | Value | If wrong |
|---|---|---|
| Team | 2 full-time engineers, both comfortable with .NET and Blazor; part-time design and QA | Halve or double the elapsed schedule accordingly |
| Engineer-day | ~6 productive hours | — |
| Estimates | Include unit and integration tests, code review, and documentation. They exclude formal QA cycles and stakeholder review latency | Add ~20% for a formal QA gate |
| Existing solution | Builds and runs via `aspire run` as delivered | Add Phase 0 remediation |
| Design system | Bootstrap 5.3 as already configured; no custom design system commissioned | Add 10–15 ed for bespoke design |
| Image library | **SkiaSharp (MIT)**, no AVIF output ([§13.9], Q3 resolved) | Adding AVIF via Magick.NET behind `IImageProcessor` adds ~3 ed |
| Localization | **Out of scope** — `en-US` only, no locale dimension in the model ([§19], Q1 resolved) | Re-adding later costs ~25–35 ed, not the ~15 an earlier locale-in-schema hedge would have made it |
| Environment | Single web instance at launch ([§29.2] Q4) | Redis output cache adds ~3 ed |

**Total v1: 203.5 engineer-days ≈ 20 calendar weeks with 2 engineers**, plus a 15% contingency →
**~23 weeks**.

Note that the 203.5 figure is *work*, not *duration*. Parallelism (Phases 7 and 8 alongside 4–6) is what
keeps two engineers continuously occupied rather than serialized behind a single critical path; it does
not reduce the total work.

Estimates are for planning, not commitment. The largest single uncertainty is Phase 6 (authoring UX),
where scope is most elastic.

---

## 4. Roadmap at a glance

```
Phase                                              ed    Cumulative   Depends on
──────────────────────────────────────────────────────────────────────────────────
0  Foundations & spikes                          12.0       12.0     —
1  Content structure                             28.0       40.0     0
2  Pages, versioning, publishing                 27.0       67.0     1
3  Delivery, routing, preview        ◄─ SLICE    22.5       89.5     2
4  Reusable content                              12.0      101.5     3
5  Media library & image pipeline                23.5      125.0     3
6  Authoring experience                          34.5      159.5     4, 5
7  Workflow, permissions, scheduling             16.0      175.5     2
8  SEO, caching, navigation, search              14.0      189.5     3
9  Hardening, accessibility, launch              14.0      203.5     all
10 Editor-managed site styling                    6.0      209.5     3, 6, 8
──────────────────────────────────────────────────────────────────────────────────
                                     v1 total  209.5 ed  ≈ 21 weeks @ 2 engineers
                                                         ≈ 24 weeks with contingency
```

Phase 10 was added on **2026-08-20**, after a re-read of `requirements.md` found styling to be the one
editor-facing capability the plan had left entirely to developers. It is v1 scope rather than backlog:
a CMS whose appearance takes a deployment to change is one an organisation works around, and the
workaround — inline `style` attributes typed into content — is worse than the gap.

Dependency graph:

```
        ┌────┐
        │ P0 │
        └─┬──┘
          ▼
        ┌────┐
        │ P1 │  structure: templates, zones, block types, field types
        └─┬──┘
          ▼
        ┌────┐
        │ P2 │  pages, versions, publish
        └─┬──┘
          ├──────────────────────────┐
          ▼                          ▼
        ┌────┐                    ┌────┐
        │ P3 │ ◄── VERTICAL SLICE │ P7 │  workflow, permissions   (parallelizable)
        └─┬──┘                    └────┘
          ├──────────┬──────────────┐
          ▼          ▼              ▼
       ┌────┐     ┌────┐         ┌────┐
       │ P4 │     │ P5 │         │ P8 │  SEO, caching, nav, search (parallelizable)
       └─┬──┘     └─┬──┘         └────┘
         └────┬─────┘
              ▼
           ┌────┐
           │ P6 │  authoring experience
           └─┬──┘
             ▼
           ┌────┐
           │ P9 │  hardening & launch
           └────┘
```

With two engineers, **P7 and P8 run in parallel with P4/P5** once P3 lands. Without that overlap the
critical path (P0→1→2→3→4/5→6→9) leaves one engineer idle for stretches; with it, both stay loaded and
the elapsed schedule tracks the work total rather than exceeding it.

---

## Phase 0 — Foundations and de-risking spikes

**Objective:** prove the three technical unknowns that would invalidate the architecture if they do not
work, and put the scaffolding in place so no later phase is blocked on setup.

**Duration:** 12 ed · **Entry criteria:** `aspire run` starts the existing solution successfully.

### 0.1 Spikes — do these first

These are timeboxed. If a spike fails, the architecture changes before 170 engineer-days are spent
on it.

| # | Spike | Question it answers | Box | Failure fallback |
|---|---|---|---|---|
| S1 | **Runtime-schema payload round trip** | Can a JSON payload be validated and deserialized against a *runtime-defined* schema (zones/properties as data) with acceptable performance and clear errors? | 2 ed | Fall back to a code-defined content type model, sacrificing runtime zone editing |
| S2 | **Dynamic component rendering under static SSR** | Does `DynamicComponent` compose template → zone → field renderer correctly with no interactive render mode, and does an error boundary isolate a failing block? | 2 ed | Source-generate a static render switch per template |
| S3 | **Editor JS interop in Blazor WASM** | Do CodeMirror 6 and Quill integrate cleanly (init, two-way bind, dispose without leaks) as local assets under a strict CSP? | 2 ed | A plainer textarea-plus-preview editor for v1 |

Each spike produces a short written finding committed to `docs/spikes/`, and its code is thrown away.
Nothing from a spike is promoted directly into the solution.

### 0.2 Scaffolding

| Task | ed |
|---|---|
| Create `ContentManagementSystem.Core` and `ContentManagementSystem.Rendering` (RCL) projects; wire into `.slnx`, `Directory.Packages.props`, project references | 1 |
| Create the four test projects; add xUnit, FluentAssertions, NSubstitute, bUnit, Testcontainers, Playwright, axe-core to central package management | 1.5 |
| Add Azurite (blob) to `AppHost.cs` for the dev media store; add Redis behind a feature flag | 1 |
| CI pipeline in `.github/workflows`: restore → build (warnings-as-errors already on) → unit → integration (Testcontainers) → E2E → axe → publish artifacts | 2 |
| `CONTRIBUTING.md` conventions; ADR folder `docs/adr/` seeded with D1–D12 from [§29.1] | 0.5 |

### Acceptance criteria

- All three spikes have a written finding with a go/no-go recommendation.
- `dotnet build` succeeds with zero warnings across the expanded solution.
- CI runs green on an empty test suite, including a Testcontainers SQL Server integration test that
  applies the existing migrations.
- `aspire run` starts SQL Server, Azurite, and the server; `/health` reports healthy.

### Exit criteria

No spike returned a no-go without an agreed fallback recorded as an ADR.

### Risks

R1 (spike failure), R9 (Testcontainers in CI).

---

## Phase 1 — Content structure

**Objective:** a developer can define templates with typed zones and block types, and the system can
validate a content payload against them.

**Duration:** 28 ed · **Entry criteria:** Phase 0 exit.

### 1.1 Domain and data

| Task | Files | ed |
|---|---|---|
| Entities: `Template`, `TemplateRevision`, `Zone`, `BlockType`, `BlockTypeRevision`, `BlockTypeProperty`, `Composition`, `CompositionProperty`, `BlockTypeComposition`, `SiteSettings` | `Data/Models/Cms/` | 2.5 |
| `IEntityTypeConfiguration<>` per entity: keys, unique indexes, `FieldLengths` constants, `ColumnTypes` | `Data/Configurations/Cms/` | 2 |
| Extend `FieldLengths` with CMS constants (`ContentKey = 100`, `Url = 2000`, `MetaDescription = 500`, …) | `Shared/Common/FieldLengths.cs` | 0.5 |
| Migration `AddCmsStructure` | `Data/Migrations/` | 1 |
| Seed: `SiteSettings` row (`Culture = en-US`), built-in `RawHtml` block type | `Data/Seeding/` | 0.5 |

### 1.2 Field type framework — the extensibility spine

| Task | Files | ed |
|---|---|---|
| `IFieldType`, `FieldTypeCapabilities`, `FieldConfiguration`, `ValidationResult`, `ContentReference` contracts | `Shared/Contracts/Fields/` | 1 |
| `IFieldTypeRegistry` + DI registration + startup discovery | `Core/Fields/` | 1 |
| Implement v1 field types per [§7.1]: `plainText`, `multilineText`, `richText`, `html`, `number`, `boolean`, `date`, `dateTime`, `choice`, `color`, `json` | `Core/Fields/Types/` | 3 |
| Reference-bearing types stubbed to a contract now, completed in their own phases: `media` (P5), `link`/`pageReference` (P3), `reusable` (P4), `blocks` (P1), `tags` (P8) | `Core/Fields/Types/` | 1 |
| Per-field-type configuration JSON Schema + validation on zone save | `Core/Fields/Configuration/` | 1 |

### 1.3 Payload engine

| Task | Files | ed |
|---|---|---|
| `ContentPayload` model, envelope, `System.Text.Json` converters; absent-vs-null semantics | `Shared/Content/` | 1.5 |
| `ContentSchemaValidator` — walks zone/property definitions, dispatches to field types, returns structured errors keyed by zone/block/property | `Core/Content/` | 2 |
| `ReferenceIndexer` — extracts `ContentReference` rows via `IFieldType.ExtractReferences` | `Core/Content/` | 1 |
| Snapshot tests pinning the payload envelope format | `Core.Tests/Content/` | 0.5 |

### 1.4 Sanitization — ships now, before any HTML can be stored

| Task | Files | ed |
|---|---|---|
| `SanitizationService` over HtmlSanitizer with the `Basic` / `Extended` / `Developer` profiles [§20.2] | `Core/Security/` | 1.5 |
| Markdig pipeline for `richText` markdown → HTML → sanitize; consistent between editor preview and delivery | `Core/Content/Markdown/` | 1 |
| XSS corpus test suite (OWASP payloads + polyglots) asserting neutralization per profile | `Core.Tests/Security/` | 1 |

### 1.5 Structure admin (functional UI)

| Task | Files | ed |
|---|---|---|
| Management API: `/api/cms/v1/templates`, `/zones`, `/block-types`, `/block-type-properties`, `/compositions`, `/field-types` | `Server/Api/Cms/Structure/` | 2 |
| `TemplateReconciler` + `SchemaSyncService`; `cms-templates` health check; export/apply CLI verbs [§27.1] | `Core/Structure/`, `Server/Cli/` | 2 |
| Plain admin screens under `/admin/structure` (list, create, edit zone, edit block type) | `Client/Components/Admin/Structure/` | 2 |

### Acceptance criteria

1. A `Developer` creates a template with four zones of differing field types through the admin UI, and
   the definitions persist.
2. `ContentSchemaValidator` accepts a valid payload and rejects an invalid one with errors identifying
   the exact zone, block id, and property.
3. Renaming a zone key is refused; renaming a display name succeeds.
4. Removing a zone leaves existing payload data intact and reachable as orphaned content.
5. A template defined in code but absent from the database is created at startup; a database template
   with no code component is marked orphaned and degrades the `cms-templates` health check.
6. Every payload in the XSS corpus is neutralized under each sanitization profile, with the stripped
   content reported.
7. Markdown rendered by the editor-preview path is byte-identical to the delivery path.

### Exit criteria

Structure can be defined and a payload validated against it. The XSS corpus is green in CI.

### Risks

R2 (runtime-schema complexity), R3 (sanitizer over-stripping legitimate content).

---

## Phase 2 — Pages, versioning, and publishing

**Objective:** the core promise — a page has a draft and a published version, and editing the draft
does not disturb what is published.

**Duration:** 27 ed · **Entry criteria:** Phase 1 exit.

### 2.1 Data

| Task | Files | ed |
|---|---|---|
| `Page`, `PageVersion` entities + configurations, including the mutual `Page.DraftVersionId` / `PageVersion.PageId` FK handling from [§23.5] | `Data/Models/Cms/`, `Data/Configurations/Cms/` | 2 |
| `ContentReference`, `EditLock` entities | | 1 |
| `rowversion` concurrency tokens; global query filters for `IsDeleted` | | 1 |
| `Page.Path` materialization and maintenance on insert/move | `Core/Content/PageTreeService.cs` | 1.5 |
| Migration `AddCmsPages` | | 1 |

### 2.2 Services

| Task | Files | ed |
|---|---|---|
| `PageService` — create from template, read, metadata patch | `Core/Content/` | 2 |
| Soft delete + recycle bin: subtree-aware delete/restore, route retirement, parent-redirect option, permanent-delete guard [§14.10] | `Core/Content/RecycleBinService.cs` | 1.5 |
| Duplication: shallow and deep, with intra-subtree link rewriting [§14.12] | `Core/Content/DuplicationService.cs` | 1.5 |
| `DraftService` — load, save (payload + concurrency), discard, checkpoint | `Core/Content/` | 2 |
| `PublishingService` — validate → snapshot → archive previous → repoint `PublishedVersionId` → reindex references → enqueue invalidation, all in one transaction | `Core/Publishing/` | 3 |
| `VersionService` — history, fetch, restore-into-draft, retention pruning | `Core/Publishing/` | 2 |
| `ContentDiffService` — structural diff with GUID-based block matching and word-level text diff [§11.4] | `Core/Publishing/` | 3 |
| `EditLockService` — acquire, heartbeat, override, reaper | `Core/Content/` | 1 |

### 2.3 API and UI

| Task | Files | ed |
|---|---|---|
| Page endpoints per [§22.1]; `ETag`/`If-Match`; RFC 9457 problem details; antiforgery on writes | `Server/Api/Cms/Pages/` | 2.5 |
| Authorization policies and permission constants (global roles; section ACLs land in P7) | `Server/Authorization/` | 1 |
| Plain admin screens: page list, create-from-template, generic zone form, version history, diff viewer | `Client/Components/Admin/Pages/` | 1 |

### Acceptance criteria

1. Creating a page from a template produces a draft version with an empty, schema-valid payload.
2. Saving the draft mutates the draft version in place and creates no new version row.
3. Publishing creates a new immutable version, archives the previous published version, and repoints
   `Page.PublishedVersionId` — all or nothing under a forced mid-transaction failure.
4. **After publishing, editing the draft leaves the published version byte-for-byte unchanged.**
5. Version history lists every version with status, author, and timestamp.
6. The diff between two versions reports a reordered block as *moved*, not as removed-plus-added.
7. Restoring an old version copies it into the draft and leaves the published version untouched.
8. Two concurrent draft saves: the second receives `409 Conflict` with both payloads.
9. An advisory lock is visible to a second editor and can be overridden; it expires after 2 minutes of
   silence.
10. Soft-deleting a page hides it from default queries while keeping full history retrievable.
11. Publishing with an unfilled required zone returns `422` naming that zone.

### Exit criteria

Acceptance test #4 passes — the requirement's central promise is mechanically verified.

### Risks

R4 (publish transaction correctness), R5 (diff algorithm complexity).

---

## Phase 3 — Delivery, routing, and preview

**Objective:** the vertical slice closes — published pages are reachable by anonymous visitors at real
URLs, and drafts are previewable but invisible.

**Duration:** 22.5 ed · **Entry criteria:** Phase 2 exit.

### 3.1 Routing

| Task | Files | ed |
|---|---|---|
| `PageRoute`, `Redirect`, `NotFoundLog` entities + migration `AddCmsRouting`; `binary(32)` URL hash unique indexes | | 1 |
| `SlugService` — generation, normalization, Unicode handling, reserved-prefix checks [§10.2–10.3] | `Core/Routing/` | 1.5 |
| `UrlService` — route materialization, cascade to descendants on move/rename, transactional | `Core/Routing/` | 2 |
| `RedirectService` — auto-creation, loop detection, chain flattening, hit counting, CSV import/export | `Core/Routing/` | 2 |
| `link` and `pageReference` field types completed (page-id storage, URL resolution at render) | `Core/Fields/Types/` | 1 |

### 3.2 Rendering

| Task | Files | ed |
|---|---|---|
| `ContentManagementSystem.Rendering`: `CmsTemplateBase`, `CmsZone`, `RenderContext`, `[CmsTemplate]`, `[CmsBlockType]` attributes | `Rendering/Infrastructure/` | 2 |
| Field renderer components for every Phase 1 field type | `Rendering/Fields/` | 2 |
| Two reference templates and three reference block types, exercising every field type | `Rendering/Templates/`, `Rendering/Blocks/` | 2 |
| Per-zone error boundaries and the fallback matrix from [§15.3] | `Rendering/Infrastructure/` | 1 |
| `PublishedContentService` — resolve → load → deserialize → render, read-only and cache-ready | `Core/Delivery/` | 2 |
| Delivery endpoint `MapGet("/{**slug}")`, registered last; 404 page; `NotFoundLog` writing | `Server/Delivery/` | 1.5 |

### 3.3 Preview

| Task | Files | ed |
|---|---|---|
| `/preview/{pageId}?version=` authenticated preview through the shared rendering path; `noindex`; toolbar | `Server/Delivery/Preview/` | 1.5 |
| `PreviewToken` entity, hashed-token issuance/validation, `/preview/s/{token}`, revocation UI | `Core/Preview/`, `Server/Delivery/Preview/` | 2 |
| Draft-link resolution inside preview; device-width frame | `Rendering/`, `Client/` | 1 |

### Acceptance criteria

1. **A published page is reachable at its URL by an anonymous request and renders its content.**
2. **An unpublished page returns 404 to anonymous requests and renders in preview for an editor.**
3. **After publishing, further draft edits do not change the anonymous response.**
4. Changing a published page's slug 301s the old URL to the new one, for the page and all descendants.
5. A redirect chain `A→B`, then `B→C`, is flattened to `A→C`; a cycle is refused at write time.
6. A live page at a URL takes precedence over a redirect with the same `FromUrl`.
7. An internal link renders the target's *current* URL even after that target has been moved.
8. A template throwing inside one block renders the rest of the page and logs the failure with page id,
   zone key, and version id.
9. An unknown field type key renders nothing, logs a warning, and does not throw.
10. A shareable preview link renders for an anonymous browser, expires on schedule, and is revocable;
   the token is not recoverable from the database.
11. Unresolved URLs are recorded in `NotFoundLog` with an accurate hit count.

### Exit criteria

**Demo milestone.** The full loop is demonstrable to a stakeholder: define a template → create a
page → fill zones → save draft → preview → publish → view anonymously → edit draft → confirm the
public page is unchanged → publish again.

### Risks

R6 (catch-all route ordering conflicts with Blazor framework paths), R7 (static SSR + `DynamicComponent`).

---

## Phase 4 — Reusable content

**Objective:** content authored once — footers, banners, carousels — appears on many pages and updates
everywhere in one publish.

**Duration:** 12 ed · **Entry criteria:** Phase 3 exit.

| Task | Files | ed |
|---|---|---|
| `ReusableContent`, `ReusableContentVersion` entities + migration `AddCmsReusableContent` | | 1.5 |
| `ReusableContentService` — CRUD, draft/publish/version lifecycle reusing the Phase 2 publishing primitives | `Core/Content/` | 2.5 |
| `reusable` field type: editor picker, renderer, late binding, pinning, reference extraction | `Core/Fields/Types/`, `Rendering/Fields/` | 2 |
| `ReusableContentResolver` in the delivery path, with recursion-depth guard and cycle detection | `Core/Delivery/` | 1.5 |
| Impact analysis service + `/references` endpoints for pages, media, and reusable content [§9.4] | `Core/Content/ReferenceQueryService.cs` | 2 |
| `/api/cms/v1/reusable` endpoints | `Server/Api/Cms/Reusable/` | 1.5 |
| Plain admin screens: reusable library, editor, where-used panel, publish-impact dialog | `Client/Components/Admin/Reusable/` | 1 |

### Acceptance criteria

1. A reusable item is created, published, and referenced from three pages.
2. **Publishing a new version of the reusable item changes all three published pages without
   republishing them.**
3. A page pinned to version 3 does not change when version 4 is published, and its UI shows a badge
   plus an "update to latest" action.
4. The publish-impact dialog reports the correct affected-page count, split by pinned and late-bound.
5. Deleting reusable content that is still referenced is refused, with an accurate where-used list.
6. Unpublishing reusable content renders nothing on dependent pages, logs a warning, and appears in
   the broken-references report.
7. A reusable item referencing itself (directly or transitively) is refused; a depth guard prevents
   runaway recursion at render time.

### Risks

R8 (cache invalidation fan-out on high-reference items — measured here, addressed in P8).

---

## Phase 5 — Media library and image pipeline

**Objective:** editors upload, organize, edit, and reference images safely and with good delivery
performance.

**Duration:** 23.5 ed · **Entry criteria:** Phase 3 exit. Runs in parallel with Phase 4.

> **Q3 resolved:** SkiaSharp (MIT) is the image library — no licensing gate on this phase.
> Consequence: **AVIF is not produced in v1**; renditions are WebP plus the original format
> ([§13.9.1]). Build the format capability assertion in task 5.2 so an unsupported encode fails loudly
> at startup rather than returning null at runtime.
>
> **Still blocking:** [§29.2] Q7 (SVG policy) must be resolved before task 5.1 completes.

### 5.1 Storage and upload

| Task | Files | ed |
|---|---|---|
| `MediaItem`, `MediaFolder`, `MediaRendition` entities + migration `AddCmsMedia` | | 2 |
| `IMediaStore` + `FileSystemMediaStore` (traversal-guarded, outside `wwwroot`) + `AzureBlobMediaStore` | `Core/Media/Stores/` | 2 |
| Upload pipeline, all ten steps of [§13.3]: size limits, extension allowlist, magic-number sniffing, decode-bomb guard, SVG policy, optional `IMalwareScanner`, SHA-256 dedupe, EXIF orientation via MetadataExtractor with `SKCodec.EncodedOrigin` fallback, then strip | `Core/Media/Upload/` | 3.5 |
| Chunked/resumable upload for large files; progress reporting | `Server/Api/Cms/Media/`, `Client/` | 1.5 |

### 5.2 Image processing

| Task | Files | ed |
|---|---|---|
| `IImageProcessor` abstraction + `SkiaSharpImageProcessor` (sole v1 implementation) + `SupportedOutputFormats` capability assertion at startup [§13.9] | `Core/Media/Processing/` | 2 |
| Non-destructive edit model: `EditsJson`, `EditsVersion`, library vs. usage scope, revert-to-original | `Core/Media/` | 2 |
| Focal-point cropping math and rendition spec normalization | `Core/Media/Processing/` | 1.5 |
| Rendition generation with per-key semaphore, persistence, and lazy population | `Core/Media/Renditions/` | 2 |

### 5.3 Delivery

| Task | Files | ed |
|---|---|---|
| Signed rendition endpoint `/media/{id}/{spec}`: HMAC validation, size allowlist, `Accept`-based WebP negotiation, `Vary: Accept`, immutable cache headers, sniffed content-type pinning, key rotation with grace period | `Server/Media/` | 3 |
| `media` and `mediaList` field types: editor picker, inline crop/rotate/focal UI, responsive `<picture>` renderer with `srcset`, `width`/`height`, LCP `fetchpriority` | `Core/Fields/Types/`, `Rendering/Fields/`, `Client/` | 3 |
| Media admin: browser (grid/list, folders, filters), detail/metadata panel, image editor, replace, where-used, soft delete + bin | `Client/Components/Admin/Media/` | 1 |

### Acceptance criteria

1. A JPEG upload produces a `MediaItem` with correct dimensions, size, hash, and stripped EXIF; GPS
   data is absent from the stored original.
2. Re-uploading identical bytes returns the existing item rather than creating a duplicate.
3. A file whose extension and magic bytes disagree is rejected; an HTML file renamed `.jpg` is rejected.
4. An oversized-dimension decode bomb is rejected before decoding.
5. SVG uploads follow the configured policy — sanitized to the strict profile, or refused.
6. Rotating an image in the library updates every usage; the original bytes are unchanged and
   revert-to-original restores it.
7. A usage-level crop affects only that page; other usages are unchanged.
8. An unsigned or tampered rendition URL returns 400/403; a valid one returns the image.
9. A rendition is generated once — twenty concurrent cold requests produce one encode.
10. `<picture>` output includes a WebP source, an accurate `srcset`, explicit `width`/`height`, and
    `loading="lazy"` on non-LCP images. Requesting AVIF is rejected at the spec-parsing layer, never
    silently producing an empty response.
11. Publishing a page whose image has neither alt text nor a decorative flag fails validation.
12. Permanent deletion of referenced media is refused with a correct where-used list.
13. A library-level edit bumps `EditsVersion`, changing rendition URLs and thereby busting client and
    CDN caches.

### Risks

R10 (licensing decision blocks work), R11 (rendition generation CPU cost under load), R12 (SVG XSS).

---

## Phase 6 — Authoring experience

**Objective:** replace the functional admin screens with the editing experience real editors will use
daily — including the edit/preview experience the requirements call out explicitly.

**Duration:** 34.5 ed · **Entry criteria:** Phases 4 and 5 exit.

| Task | Files | ed |
|---|---|---|
| Three-pane shell: resizable, collapsible, responsive down to tablet, persisted layout [§14.1] | `Client/Components/Admin/Shell/` | 3 |
| Content tree: lazy loading, virtualization, status indicators, drag reorder/reparent **plus keyboard-accessible move controls**, context menu, inline filter [§14.2] | `Client/Components/Admin/Tree/` | 4 |
| Editing canvas: zone cards, grouping, per-zone validation state, sticky action bar | `Client/Components/Admin/Canvas/` | 3 |
| Block list editor: add-constrained, reorder, collapse with summary line, duplicate, delete-with-undo, per-block validation | `Client/Components/Admin/Fields/BlockList/` | 4 |
| **Edit/Preview/Split rich-text editor** — CodeMirror 6 source modes, Quill WYSIWYG, shared Markdig preview pipeline, sync scroll, CMS-aware link/image insertion, counts [§14.4] | `Client/Components/Admin/Fields/RichText/` | 5 |
| HTML editor with a live "these tags will be stripped on save" warning | `Client/Components/Admin/Fields/Html/` | 1.5 |
| Pickers: page (tree), media (browser + upload inline), reusable content, link (unified) | `Client/Components/Admin/Pickers/` | 2.5 |
| Properties panel: metadata, SEO with a search-result preview widget, publishing, editorial fields | `Client/Components/Admin/Properties/` | 2 |
| Autosave with debounce, offline-safe queueing, and clear save-state indication; conflict resolution UI | `Client/Services/` | 2 |
| Toasts (reuse the existing `IToastService`), confirmation dialogs, undo affordances, empty and loading states | `Client/` | 1 |
| Keyboard shortcuts and a shortcut reference dialog | `Client/` | 1 |
| Dashboard: my work, scheduled, needs-attention, recent activity tiles, all permission-scoped and deep-linking into filtered lists [§14.9] | `Client/Components/Admin/Dashboard/` | 2 |
| Recycle bin UI: list, filter, restore (subtree-aware), permanent delete with typed confirmation [§14.10] | `Client/Components/Admin/RecycleBin/` | 1 |
| Bulk operations: selection model, impact preview, background execution with progress above 25 items, per-item result reporting [§14.11] | `Core/Content/BulkOperationService.cs`, `Client/` | 2.5 |

### Acceptance criteria

1. An editor completes create → fill → preview → publish without touching a raw JSON payload or a URL bar.
2. Markdown Edit/Preview/Split all work, and Preview matches the published page's rendering exactly.
3. The HTML editor warns *before* save about content the active profile will strip.
4. Blocks can be added, reordered, duplicated, and deleted entirely by keyboard; drag is an
   enhancement, never the only path.
5. Autosave fires on a 20-second idle, shows its state, and survives a transient network failure by
   retrying without losing input.
6. A save conflict presents keep-mine / take-theirs / open-diff, and no path silently discards work.
7. The tree remains responsive at 5,000 pages with 500 siblings under one parent.
8. The dashboard surfaces the signed-in user's drafts, review tasks, and overdue content, and every
   tile deep-links into a correctly filtered list.
9. A deleted page leaves the public site immediately, remains in the recycle bin with full history, and
   restores as a *draft* — never silently back onto the live site.
10. Deleting and restoring a page with children moves the whole subtree, with the count shown before
    confirming.
11. A deep duplicate rewrites links between pages inside the copied subtree to the new copies, while
    links out of the subtree still point at the originals.
12. A bulk publish of 100 pages runs as a background job with progress, and a partial failure leaves
    successful items published while reporting the rest individually.
13. axe-core reports zero critical or serious violations on every backoffice screen.
14. The whole authoring flow is operable at 200% browser zoom.

### Risks

R13 (scope elasticity — the most likely phase to overrun), R14 (JS interop memory leaks in long
editing sessions).

---

## Phase 7 — Workflow, permissions, and scheduling

**Objective:** more than one person can use the system safely.

**Duration:** 16 ed · **Entry criteria:** Phase 2 exit. Runs in parallel with Phases 4–6.

| Task | Files | ed |
|---|---|---|
| Seed the eight roles from [§3.2]; permission constants; policy provider; `CustomUserClaimsPrincipalFactory` extension | `Server/Authorization/`, `Data/Seeding/` | 2 |
| `PageAcl` entity + `AclService`: inheritance via `Page.Path` prefix match, deny-over-allow, depth precedence, admin bypass with audit [§21.2] | `Core/Security/` | 3 |
| Apply ACL checks in the service layer for every content and media operation; IDOR integration tests across boundaries | `Core/`, `Server.Tests/` | 2 |
| `WorkflowTask`, `Comment` entities + migration; `WorkflowService` with the three modes [§11.9] | `Core/Workflow/` | 3 |
| Review UI: submit/approve/reject, zone-anchored threaded comments, task inbox | `Client/Components/Admin/Workflow/` | 2 |
| `ScheduledJob` entity; `PublishSchedulerService` with atomic `UPDATE…OUTPUT` claiming; DST-aware scheduling UI | `Core/Scheduling/`, `Server/HostedServices/` | 2 |
| Real email sender replacing `IdentityNoOpEmailSender`; notification templates; in-app inbox | `Server/Components/Email/` | 1.5 |
| Audit log viewer with entity/user/date filters | `Client/Components/Admin/Audit/` | 0.5 |

### Acceptance criteria

1. An `Author` cannot publish: the API returns `403` and the content stays unpublished.
2. Submit → approve → publish works end to end, with email and in-app notifications at each step.
3. In `TwoStep` mode, the author cannot approve their own submission.
4. A rejection returns the content to a fresh draft with comments preserved and visible.
5. A user with an ACL on `/products` can edit that subtree and receives `403` on `/about`, including on
   direct API calls with a guessed id.
6. Denying `Content.Read` on a subtree hides it from the content tree entirely.
7. A page scheduled for a future time publishes within 60 seconds of it, and only once even with two
   server instances running.
8. A scheduled publish that fails validation marks the job failed, notifies the owner, and does not
   silently retry.
9. `UnpublishOn` retires the page and applies the configured redirect behavior.
10. The audit viewer answers "who unpublished the homepage and when" in under three interactions.

### Risks

R15 (ACL query performance at depth), R16 (duplicate scheduled publishes under scale-out).

---

## Phase 8 — SEO, caching, navigation, and search

**Objective:** the public site is fast, discoverable, and navigable.

**Duration:** 14 ed · **Entry criteria:** Phase 3 exit. Runs in parallel with Phases 5–6.

| Task | Files | ed |
|---|---|---|
| SEO fields on `PageVersion` (already in the P2 migration) surfaced end to end: meta tags, canonical, robots, OG/Twitter, JSON-LD with breadcrumbs [§18.1–18.2] | `Rendering/Seo/`, `Client/` | 2 |
| `sitemap.xml` with index splitting above 40k URLs; editable `robots.txt`; non-production `Disallow: /` | `Server/Delivery/Seo/` | 1.5 |
| Output caching: policies, tag accumulation during render, `UseOutputCache` placed after auth, ETag revalidation, authenticated-request bypass [§16] | `Server/`, `Core/Delivery/` | 2.5 |
| `HybridCache` for published content and route lookups | `Core/Delivery/` | 1 |
| `OutboxMessage` + `OutboxProcessorService` + `CacheInvalidator`; transactional invalidation fan-out driven by `ContentReference` | `Core/Caching/`, `Server/HostedServices/` | 2.5 |
| Optional Redis output cache behind configuration; multi-instance invalidation test | `Server/`, `AppHost.cs` | 1 |
| `NavigationMenu`/`NavigationItem` + migration; structural and managed navigation; menu admin UI; `nav:` tags | `Core/Navigation/`, `Rendering/`, `Client/` | 2 |
| `SearchDocument` + full-text index + `SearchIndexService`; backoffice search UI with filters [§17.1] | `Core/Search/`, `Client/` | 1.5 |

### Acceptance criteria

1. Every public page emits a correct `<title>`, meta description, canonical link, robots directive,
   and OG/Twitter tags; JSON-LD validates against Google's Rich Results test.
2. `sitemap.xml` contains exactly the published, indexable pages, and refreshes on publish.
3. Staging serves `Disallow: /` regardless of the configured `robots.txt`.
4. A cached page is served from the output cache, and publishing it evicts the entry immediately.
5. Publishing reusable content evicts every dependent page and nothing else.
6. An authenticated editor's request is never served from the anonymous cache, and vice versa.
7. With Redis configured and two instances running, a publish on instance A invalidates instance B.
8. An invalidation enqueued in a transaction that then fails is not dispatched; one in a committed
   transaction is dispatched even if the process is killed immediately after commit.
9. Navigation reflects publish state within one cache generation; unpublishing removes the item.
10. Backoffice search returns a page by title, body text, and slug across 50,000 seeded pages in
    under 500 ms.

### Risks

R17 (cache invalidation correctness — the highest-severity functional risk in the system),
R18 (full-text index maintenance cost).

---

## Phase 9 — Hardening, accessibility, and launch

**Objective:** verify the non-functional requirements and make the system operable.

**Duration:** 14 ed · **Entry criteria:** all prior phases exit.

| Task | ed |
|---|---|
| Security review: CSP with per-request nonces on public and backoffice policies, HSTS, `nosniff`, `Referrer-Policy`, `Permissions-Policy` | 1.5 |
| Rate limiting across all endpoint groups per [§20.6] | 1 |
| Identity hardening: password policy, breached-password screening, mandatory 2FA for privileged roles, self-registration decision from Q10 | 1 |
| Penetration-test pass: XSS corpus against live rendering, IDOR sweep, upload fuzzing, unsigned rendition URLs, preview-token enumeration, CSRF | 2 |
| Accessibility audit: axe across all screens, manual keyboard and screen-reader passes (NVDA + VoiceOver), 200% zoom, `prefers-reduced-motion`, remediation | 2.5 |
| Performance: k6 load tests against NFR-1/2/7/9 with 50k seeded pages; profile and fix the top three findings | 2 |
| Lighthouse CI on representative templates; Core Web Vitals remediation | 1 |
| Backup/restore drill including a media-store restore; documented runbook | 1 |
| Operational documentation: deployment, configuration reference, health checks, dashboards, alert thresholds, incident runbooks | 1 |
| User documentation: editor guide, developer template-authoring guide, admin guide | 1 |

### Acceptance criteria

1. Zero critical or high findings from the security pass; all mediums triaged with owners and dates.
2. WCAG 2.2 AA verified on backoffice and public output; zero critical/serious axe violations.
3. NFR-1, NFR-2, NFR-7, and NFR-9 met under load with a 50,000-page dataset.
4. Lighthouse mobile performance ≥ 90 on all reference templates.
5. A full restore from backup — database and media — produces a working site, timed against the RTO.
6. Every health check has a monitor and an alert threshold.
7. An editor unfamiliar with the system completes create → publish using only the written guide.

---

## Phase 10 — Editor-managed site styling

**Objective:** an administrator changes how the public site looks, from inside the CMS, without a
developer, a build, or a deployment — and cannot reach the backoffice while doing it.

**Duration:** 6 ed · **Entry criteria:** Phase 3 (delivery and preview), Phase 6 (the CodeMirror
editor bundle), and Phase 8 (output caching and the outbox) have exited. It is the last phase by
dependency, not by importance: it needs the public document, the preview frame, the editor, and
tag-based invalidation, and every one of those arrives earlier.

Specified in [§30]; the shape of the decision is [ADR 0027](./docs/adr/0027-site-stylesheet-is-content-appended-never-replacing.md).

| Task | ed |
|---|---|
| `SiteStylesheet` (singleton) + `SiteStylesheetRevision` entities, configuration, and migration #11 | 0.5 |
| `CssValidator` — a parse-based deny list with line/column diagnostics [§30.5], plus its corpus | 1.25 |
| `SiteStylesheetService` — draft save under `If-Match`, publish (snapshot + revision + `sitecss` eviction enqueued **inside** the transaction), revert, revision history | 1 |
| Delivery: `GET /css/site-custom.css` and `GET /preview/site-custom.css`, the `<link>` in the public and preview documents, output-cache tag and outbox handler | 0.75 |
| Management API under `/appearance/stylesheet`, and the `Appearance.Edit` permission | 0.5 |
| `/admin/appearance/stylesheet`: CSS source pane, live diagnostics, preview against a real page, publish dialog with the delta, revision list and revert | 1.5 |
| Tests: the draft/published promise over HTTP, the refusal corpus, eviction, authorization, and the public axe pass re-run with a published stylesheet | 0.5 |

### Acceptance criteria

1. **An administrator changes the public site's appearance end to end from the CMS** — writes CSS,
   previews it against a real page, publishes, and an anonymous request receives it — with no build
   and no deployment in between.
2. **Saving the draft does not change what an anonymous visitor receives.** Publishing does, on the
   next request, on every instance. This is `P2 #4`'s promise restated about styling, and it is tested
   the same way: over HTTP, against an anonymous client.
3. **A refused construct is refused on save and on publish**, naming the construct, the line, and the
   column — `@import`, `expression()`, `behavior`, `-moz-binding`, a `javascript:` value, a `url()`
   naming another host, and anything over 256 KB — and the previously published stylesheet keeps
   being served.
4. **The backoffice document does not link the stylesheet**, asserted against the rendered admin HTML
   rather than against the source, so it cannot regress silently.
5. A caller without `Appearance.Edit` is refused at the API, not merely hidden from in the UI.
6. Revert publishes an earlier revision, and publishing nothing restores the shipped design.
7. The public accessibility gate passes with a published stylesheet applied, and fails when a
   deliberately low-contrast one is published — the negative control, without which the gate proves
   nothing.

### Exit criteria

An administrator with no access to the repository restyles the public site and reverts it, and the
axe gate and the caching tests are green in CI.

### Risks

- **R21 — a published stylesheet makes the site unusable.** CSS cannot throw, so nothing alerts.
  Mitigated entirely by recovery: preview before publish, revert from a screen the stylesheet cannot
  affect, and the contrast gate.
- **Scope creep into a theme builder.** Colour pickers, font pickers, and a token model are a
  different feature with a different acceptance test. The line is: this phase ships a CSS file with an
  editor around it. If tokens are wanted, they are v2 and they are built *on* this.

---

## Post-v1 — the v2 backlog

Ordered by expected value, not by effort. Each carries a forward reference to its specification.

| # | Item | Spec | Rough size |
|---|---|---|---|
| 1 | In-context (on-page) editing | [§14.5] | 12 ed |
| 2 | Public site search UI and analytics | [§17.2] | 5 ed |
| 3 | Headless read API + webhooks | [§29.3] | 10 ed |
| 4 | Forms and lead capture | [§29.3] | 12 ed |
| 5 | Content import/export bundles | [§27.2] | 8 ed |
| 6 | Broken-link and orphaned-media reporting UI | gap #30 | 4 ed |
| 7 | Nested blocks beyond one level | [§29.3] | 5 ed |
| 8 | Per-template workflow configuration | [§11.9] | 4 ed |
| 9 | Multi-site support | [§29.3] | 25 ed — **assess before v2 locks the schema** |

Localization is **not** on this list. It was removed from scope entirely rather than deferred (Q1,
[§19]); if it ever returns it is a re-planning event, not a backlog item.

Item 9 carries a scheduling constraint rather than just a size: adding a `SiteId` discriminator is
dramatically cheaper before v2 adds tables than after. If multi-site is plausible within 18 months, the
decision should be taken during Phase 8, not deferred to v2 planning.

---

## 16. Cross-cutting workstreams

These run continuously and are budgeted inside each phase's estimates, not added on top.

| Workstream | Cadence | Owner |
|---|---|---|
| **Testing** | Every task ships with tests. Coverage gates in [CI](#19-cicd-and-quality-gates) | Engineering |
| **Security** | Sanitization from P1, authorization from P2, threat-model review at each phase exit | Engineering + security reviewer |
| **Accessibility** | axe-core in CI from P1; manual keyboard pass at each UI phase exit | Engineering + design |
| **Performance** | Benchmarks added alongside features; regression thresholds in CI from P3 | Engineering |
| **Documentation** | ADRs written when decisions are made, not reconstructed at the end | Whoever decides |
| **Observability** | Metrics and traces added with each service, not retrofitted in P9 | Engineering |

---

## 17. Database migration sequence

Migrations are additive and applied in this order. The existing Aspire `ef-migrations` resource
(`RunDatabaseUpdateOnStart`) applies them before the server starts.

| # | Migration | Phase | Contents |
|---|---|---|---|
| 1 | `InitialDatabase` | — | Existing Identity + `AuditLog` schema (per `README.md`, run once after template creation) |
| 2 | `AddCmsStructure` | 1 | `Template`, `TemplateRevision`, `Zone`, `BlockType`, `BlockTypeRevision`, `BlockTypeProperty`, `Composition`, `CompositionProperty`, `BlockTypeComposition`, `SiteSettings` |
| 3 | `AddCmsPages` | 2 | `Page`, `PageVersion`, `ContentReference`, `EditLock` |
| 4 | `AddCmsRouting` | 3 | `PageRoute`, `Redirect`, `NotFoundLog`, `PreviewToken` |
| 5 | `AddCmsReusableContent` | 4 | `ReusableContent`, `ReusableContentVersion` |
| 6 | `AddCmsMedia` | 5 | `MediaFolder`, `MediaItem`, `MediaRendition` |
| 7 | `AddCmsWorkflow` | 7 | `WorkflowTask`, `Comment`, `PageAcl`, `ScheduledJob` |
| 8 | `AddCmsDelivery` | 8 | `NavigationMenu`, `NavigationItem`, `SearchDocument` (+ full-text catalog), `OutboxMessage`, `Tag`, `PageTag` |
| 11 | `AddSiteStylesheet` | 10 | `SiteStylesheet` (singleton), `SiteStylesheetRevision` |

Migrations **9** (`AddAuditRetention`) and **10** (`AddNavigationIndex`) were added during Phase 9 and
are recorded in [`task.md`](./task.md#database-migration-sequence); the numbering here is kept in step
with that table rather than closed up, because a migration number is how the two documents refer to
the same file.

Rules:

- Every migration is verified to apply cleanly against a database restored from the previous
  migration, in CI, using Testcontainers.
- Every migration has a tested `Down` **through Phase 8**; after production launch, roll-forward-only
  becomes the policy and `Down` methods are retained only as documentation.
- Data migrations (backfills) are separate, idempotent, resumable, and batched — never inline in a
  schema migration, so a long backfill cannot hold a deployment hostage.
- Full-text catalog creation in migration 8 requires raw SQL, and Azure SQL versus SQL Server on-prem
  syntax differences must be handled explicitly.

---

## 18. Changes required to existing code

The CMS is additive, but these existing files need modification. Listing them prevents surprises during
review.

| File | Change | Phase | Why |
|---|---|---|---|
| `Data/Models/AuthDbContext.cs` | Exclude high-churn tables (`SearchDocument`, `OutboxMessage`, `MediaRendition`, `EditLock`, `NotFoundLog`) from `AddLogging()` audit capture | 1 | Otherwise `AuditLog` grows without bound and every `SaveChanges` slows measurably [§23.5] |
| `Data/Models/AuthDbContext.cs` | Add an `ApplySoftDeletes()` implementation — the virtual hook exists and is currently empty | 2 | A stray `Remove()` on a `Page` must not destroy version history |
| `Data/Models/ApplicationDbContext.cs` | Register CMS `DbSet`s and apply configurations from the assembly | 1 | — |
| `Server/Program.cs` | Register CMS services, field type registry, output cache, rate limiting, security headers, background services; add the delivery endpoint **after** all existing endpoint registrations | 1–8 | Route ordering: the `/{**slug}` catch-all must not shadow `/_blazor`, `/_framework`, `/account`, or `/api` [§10.3] |
| `Server/Program.cs` | Tighten the Identity password policy; decide on self-registration | 9 | Current settings (6 chars, no complexity) are template defaults, unsuitable for publish-capable accounts [§20.3] |
| `Server/Components/Email/IdentityNoOpEmailSender.cs` | Replace with a real sender | 7 | Workflow notifications and password resets are non-functional without it |
| `Server/Components/App.razor` | Add CSP nonce propagation; split public and admin head content | 8–9 | [§20.5] |
| `Server/Components/Routes.razor` | Scope interactive routing to `/admin`; keep public pages static SSR | 3 | [§5.3] — this is the decision that makes output caching possible |
| `AppHost.cs` | Add Azurite and optional Redis resources | 0 | Local dev parity with production storage |
| `Directory.Packages.props` | Add HtmlSanitizer, Markdig, **SkiaSharp**, **MetadataExtractor**, HybridCache, rate limiting, Testcontainers, bUnit, Playwright, k6 tooling | 0–5 | Central package management is already enabled. SkiaSharp per Q3; MetadataExtractor for EXIF orientation [§13.9.1] |
| `Shared/Common/FieldLengths.cs` | Add CMS field length constants | 1 | Keeps validation attributes and column definitions from drifting, per the file's own stated intent |
| `styles/site.scss` | Add backoffice and content typography layers | 6 | — |
| `Server/Delivery/CmsDeliveryDocument.razor`, `Delivery/CmsPageRenderer.cs` | Link the administrator's stylesheet after `site.css` — the published one on a live render, the draft on a preview one, and nothing at all when nothing is published. The renderer decides which, so the document has one link and no branch | 10 | [§30.1]. `App.razor` is deliberately **not** in this list |
| `Server/package.json` | Add `@codemirror/lang-css` to the source-editor bundle | 10 | The stylesheet editor is the same CodeMirror bundle with one more language mode (`D13`) |
| `Shared/Contracts/Security/CmsPermissions.cs`, `Server/Authorization/CmsPermissionMap.cs` | Add `Appearance.Edit`, held by `Administrator` and `Developer` | 10 | [§21.1]. Separate from `Settings.Edit` because publishing CSS reaches every visitor immediately, with no draft state on the public side |
| `README.md` | Document CMS setup, template authoring, and the schema sync CLI | 9 | — |

---

## 19. CI/CD and quality gates

### 19.1 Pipeline

```
PR ──► restore ──► build (warnings as errors, already configured)
                      │
                      ├─► unit tests                    (fast, every PR)
                      ├─► EF integration (Testcontainers) (every PR)
                      ├─► API integration                (every PR)
                      ├─► bUnit rendering tests          (every PR)
                      ├─► security corpus (XSS, upload)  (every PR)
                      ├─► axe-core accessibility         (every PR)
                      ├─► migration up/down verification (every PR)
                      │
                      ├─► E2E Playwright                 (main + nightly)
                      ├─► Lighthouse CI                  (nightly)
                      ├─► k6 load test                   (nightly)
                      └─► visual regression              (nightly)
```

### 19.2 Merge gates

- Build clean — `TreatWarningsAsErrors` is already enabled solution-wide.
- All fast-lane suites green.
- Line coverage ≥ 80% in `Core`; ≥ 90% on `PublishingService`, `SanitizationService`, `UrlService`,
  `RedirectService`, and `AclService`, which are the components where a defect is most expensive.
- Zero new critical/serious axe violations.
- No new high or critical findings from dependency and secret scanning.
- Any migration in the diff carries a reviewer sign-off.

### 19.3 Environments

| Environment | Purpose | Data | Notes |
|---|---|---|---|
| Local | Development | Seeded | `aspire run` |
| CI | Automated verification | Ephemeral (Testcontainers) | — |
| Staging | Stakeholder review, load testing | Scrubbed production copy | `robots.txt` forced to `Disallow: /`; preview tokens revoked on restore |
| Production | Live | — | Blue/green or slot-based deployment |

---

## 20. Risk register

| ID | Risk | Likelihood | Impact | Mitigation | Trigger for the contingency |
|---|---|---|---|---|---|
| R1 | A Phase 0 spike fails | Low | High | Timeboxed, with a recorded fallback for each | Spike exceeds its box by 50% |
| R2 | Runtime-defined schema proves too complex to validate cleanly | Medium | High | S1 proves it first; fallback is code-defined content types | Validator error messages cannot identify the offending field |
| R3 | Sanitizer strips content editors legitimately need | Medium | Medium | Three tiered profiles; pre-save "this will be stripped" warning; profile config per zone | More than 3 editor complaints in the first month |
| R4 | Publish transaction leaves inconsistent state | Low | **Critical** | Single transaction; fault-injection tests; outbox for side effects | Any occurrence — treat as a stop-the-line defect |
| R5 | Diff algorithm is slower or noisier than expected | Medium | Low | GUID-based block matching; diff computed on demand, never in the publish path | Diff takes over 2 s on a typical page |
| R6 | Catch-all route shadows framework or admin paths | Medium | High | Register last; reserved-prefix validation; explicit integration tests for `/_blazor`, `/_framework`, `/api`, `/admin`, `/account` | Any framework path 404s in testing |
| R7 | `DynamicComponent` under static SSR misbehaves | Low | High | Proven by S2 before commitment | S2 no-go |
| R8 | Invalidation fan-out is slow for a reusable item on 10,000 pages | Medium | Medium | Tag-based eviction is O(tags), not O(pages); measured in P4, tuned in P8 | Publish exceeds NFR-7 (2 s) |
| R9 | Testcontainers is unreliable in CI (ARM64, Docker-in-Docker) | Medium | Medium | Pin images; `azure-sql-edge` fallback already used in `AppHost`; a shared CI SQL instance as plan B | Flake rate above 5% |
| R10 | ~~Six Labors licensing stalls Phase 5~~ **Closed** — SkiaSharp selected | — | — | Residual: SkiaSharp's silent-null AVIF encode. Mitigated by asserting `SupportedOutputFormats` at startup and rejecting AVIF at spec-parse time | If AVIF is later required → `MagickNetImageProcessor` behind the existing abstraction |
| R11 | Rendition generation saturates CPU under traffic | Medium | High | Lazy generation + per-key semaphore + persistent renditions + signed URLs + size allowlist; pre-generate the standard set on upload | CPU above 70% sustained during load test |
| R12 | SVG sanitization is bypassed | Low | **Critical** | Prefer refusing SVG entirely (Q7); if permitted, strict profile plus serving from a separate origin | Any bypass found in testing → disable SVG |
| R13 | Phase 6 scope expands | **High** | Medium | Explicit acceptance criteria; polish backlogged rather than absorbed; the plain UI from P1–P5 remains a working fallback | 20% over budget at the midpoint → cut to the acceptance criteria only |
| R14 | JS interop leaks memory in long editing sessions | Medium | Medium | `IAsyncDisposable` on every interop component; an 8-hour soak test in P9 | Browser memory grows more than 50% over a 2-hour session |
| R15 | ACL resolution is slow on a deep tree | Low | Medium | Indexed `Page.Path` prefix matching; per-request ACL cache | Tree load exceeds 500 ms at depth 10 |
| R16 | Duplicate scheduled publishes under scale-out | Medium | Medium | Atomic `UPDATE…OUTPUT` claiming; idempotent publish; multi-instance test in P7 | Any duplicate observed |
| R17 | Cache invalidation misses a dependent page | Medium | **High** | Tags accumulated *during render* rather than hand-maintained; outbox delivery; a short TTL as a backstop so any miss self-heals within an hour | Any stale page reported after publish |
| R18 | Full-text index maintenance degrades write throughput | Low | Medium | Asynchronous indexing via the outbox; nightly reconcile | Save latency exceeds NFR-6 |
| R19 | Requirements shift mid-build (multi-site, or multilingual after all) | Low–Medium | **High** | Q1 answered: no locale in the model, so a reversal is a ~25–35 ed migration, not a config change. Multi-site assessed at Phase 8, before v2 adds tables | Either raised → stop and re-plan; do not absorb into a phase |
| R20 | Key-person dependency on Blazor/EF expertise | Medium | Medium | Pair on Phases 1–3; ADRs capture reasoning; no single-owner components | Either engineer unavailable for more than a week |
| R21 | A published site stylesheet makes the public site unusable — unreadable contrast, a covered viewport, a hidden navigation | Medium | High | CSS cannot fail loudly, so the mitigations are all recovery: preview against real pages before publishing, revert to any revision or to nothing from a screen the stylesheet cannot affect, the public axe/contrast gate run **with** the published sheet applied, and a `sitecss` eviction that takes effect on the next request rather than after a TTL | Any occurrence in production, or a contrast regression reaching the public gate |

---

## 21. Definition of done

A task is done when:

- Code is merged to `main` with a passing pipeline.
- Unit tests cover the happy path plus the failure and boundary cases.
- Integration tests cover the endpoint's authorization, validation, and concurrency behavior.
- Any new UI passes axe with zero critical/serious violations and is fully keyboard operable.
- Any editor-facing HTML path is covered by the XSS corpus.
- Telemetry (metric or trace) exists for anything that can be slow or can fail.
- Public API surface has XML documentation; non-obvious decisions have an ADR.
- The feature is demonstrable to a non-engineer without a debugger.

A **phase** is done when every acceptance criterion is a passing automated test — not a manual check —
except where explicitly marked as a manual audit.

---

## 22. Launch and rollout

### 22.1 Pre-launch

1. Content freeze on any legacy system being migrated.
2. Structure promotion: templates and zones applied to production via `cms schema apply`, verified
   with `cms schema diff`.
3. Content migration dry run in staging, with an unresolved-links report reviewed.
4. Redirect import from the legacy URL map; verify a sample of 100 old URLs resolve.
5. Full backup/restore drill.
6. Load test against production-equivalent infrastructure.
7. Editor training and guide handover.

### 22.2 Launch

Blue/green or slot-based cutover. DNS or slot swap with the previous version retained warm.

**Rollback plan:** swap back. Database migrations through launch are additive-only and backward
compatible with the previous application version, so an application rollback does not require a
database rollback. This constraint is why migrations 2–8 add tables rather than altering existing ones.

### 22.3 Post-launch

| When | Action |
|---|---|
| First 48 h | Monitor `NotFoundLog` hourly and create redirects for anything with real traffic; watch cache hit ratio, publish success rate, and error rate |
| First 2 weeks | Daily editor check-in; triage friction into a backlog; verify search-console coverage and indexing |
| First month | Review R3 (over-stripping), R11 (rendition CPU), R13 (deferred UI polish); re-baseline NFR measurements against real traffic |
| Ongoing | Quarterly restore drill; quarterly dependency and security review; monthly review of content past its review date |

---

## 23. Requirements traceability

Every statement in [`requirements.md`](./requirements.md) mapped to where it is specified and when it
is built. This is the checklist for verifying the delivered system against the original ask.

| # | Requirement (source line) | Spec | Phase | Acceptance test |
|---|---|---|---|---|
| R-1 | "Create templates that let them specify data zones" | [§8] | 1 | P1 #1 |
| R-2 | "Specify what type of data can be used in a zone (plain text, reusable content, html/markdown, etc)" | [§7], [§8.3] | 1 | P1 #1, #2 |
| R-3 | "In zones that are plain text or html/markdown … inline editing … 'edit/preview' editor experience" | [§14.4] | 6 | P6 #2, #3 |
| R-4 | "Reusable content … specified once but then reused in multiple (common footers, image carousels)" | [§9] | 4 | P4 #1, #2 |
| R-5 | "content editors should be able to create pages from those templates" | [§10.1], [§22.1] | 2 | P2 #1 |
| R-6 | "populate the 'placeholder' areas with actual content" | [§6.2], [§14.3] | 2, 6 | P2 #2, P6 #1 |
| R-7 | "Pages … need to have a url specified so that end users would be able to navigate to the pages" | [§10.2]–[§10.4] | 3 | P3 #1 |
| R-8 | "pages in draft mode before they get published out" | [§11.1], [§11.2] | 2 | P2 #3, P3 #2 |
| R-9 | "pages should be versioned" | [§11.1]–[§11.5] | 2 | P2 #5, #6, #7 |
| R-10 | "a published page could still be visible to unauthenticated users while content editors are making changes that only they can see internally" | [§11.1], [§12] | 2, 3 | **P2 #4, P3 #3** — the central promise |
| R-11 | "image management functionality … upload images" | [§13.3] | 5 | P5 #1–#5 |
| R-12 | "resize and rotate those images" | [§13.4], [§13.5] | 5 | P5 #6, #7 |
| R-13 | "'reference' those images inside the pages they are creating" | [§13.6], [§7.1] `media` | 5 | P5 #10 |
| R-14 | "do plenty of research and add elements that are clearly missing that would prevent this from being a usable system" | [§4.2] — 31 identified gaps | 1–10 | Per-gap, below |
| R-15 | "Administrators should be able to change the look and feel of the public site without a developer … a site-wide CSS file they can create and edit from an editor inside the system … public facing pages only" | [§30] | 10 | P10 #1–#4 |

### 23.1 Gap coverage

The 31 gaps from [§4.2] mapped to their delivery phase. Gaps marked v2 are in the
[post-v1 backlog](#post-v1--the-v2-backlog) with their spec already written.

| Phase | Gaps closed |
|---|---|
| 1 | #11 sanitization, #21 template/schema evolution |
| 2 | #9 version diff & rollback, #10 soft delete & recycle bin, #18 concurrency, #20 audit trail (viewer in P7), #29 editorial metadata |
| 3 | #1 URL management, #2 redirects, #8 shareable preview links |
| 4 | #16 where-used & link integrity |
| 5 | #12 upload validation, #13 alt text, #14 focal point, #15 renditions & responsive images |
| 6 | #19 backoffice search UI & content tree |
| 7 | #6 approval workflow, #5 scheduled publish/unpublish, #7 granular permissions, #20 audit viewer, #28 rate limiting (hardened in P9) |
| 8 | #3 SEO metadata, #4 sitemap & robots, #17 output caching & invalidation, #19 search index, #24 navigation |
| 9 | #28 rate limiting hardening; verification of all security and accessibility gaps |
| 10 | #31 editor-managed site styling |
| Schema in v1, UI in v2 | #23 localization |
| v2 backlog | #22 public search, #25 forms, #26 headless API & webhooks, #27 content import/export (structure promotion ships in v1), #30 broken-link & orphan reporting |

---

## Appendix — Phase entry/exit summary

| Phase | Entry | Exit gate |
|---|---|---|
| 0 | Solution builds and runs | Three spikes resolved; CI green |
| 1 | P0 exit | A payload validates against a runtime-defined schema; XSS corpus green |
| 2 | P1 exit | **Editing a draft provably does not alter the published version** |
| 3 | P2 exit | **Anonymous visitors see published pages at real URLs; drafts are invisible** — demo milestone |
| 4 | P3 exit | One reusable publish updates all late-bound pages; pinned pages unchanged |
| 5 | P3 exit | Safe upload, non-destructive edits, signed responsive renditions |
| 6 | P4 + P5 exit | Editors complete the full flow unaided; a11y clean |
| 7 | P2 exit | Authors cannot publish; ACLs enforced server-side; scheduling fires once |
| 8 | P3 exit | Publish invalidates exactly the right cache entries; SEO output correct |
| 9 | All | NFRs met; security and accessibility signed off; runbooks in place |
| 10 | P3, P6, P8 exit | **An administrator changes the public site's appearance with no developer, no build, and no deployment** — and cannot reach the backoffice with it |
