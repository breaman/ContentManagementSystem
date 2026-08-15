# Content Management System — Implementation Task List

**Status:** In progress — Phase 0 complete; **Phase 1's 33 tasks all done**, its exit gate open on
`P1 #1` alone, which needs a browser driving the admin form; **Phase 2 complete** — data, services,
API, admin UI, and every test task, with all eleven acceptance criteria met. Next up is Phase 3.
**Version:** 1.0
**Last updated:** 2026-08-14
**Sources:** [`requirements.md`](./requirements.md) · [`spec.md`](./spec.md) · [`plan.md`](./plan.md)

---

## How to use this document

This is the working checklist for implementing the CMS. It is derived from [`plan.md`](./plan.md)
(phases, sequencing, estimates) and [`spec.md`](./spec.md) (behavior, schema, contracts), and it is
intended to be **edited in place as work is performed**.

### Conventions

- Every task has a stable ID (`P3-14`). IDs are never reused or renumbered — if a task is dropped,
  mark it `~~struck~~` with a reason rather than deleting the line.
- `ed` = engineer-days, carried over from the plan's estimates.
- `[§n]` references a section of [`spec.md`](./spec.md).
- Acceptance criteria are numbered to match `plan.md` (`P2 #4` = Phase 2, criterion 4) so the
  traceability table at the bottom stays valid.

### Status marks

| Mark | Meaning |
|---|---|
| `- [ ]` | Not started |
| `- [~]` | In progress — put the owner and date in the trailing note |
| `- [x]` | Done — merged, tests green, meets the [definition of done](#definition-of-done) |
| `- [!]` | Blocked — put the blocker in the trailing note |
| `- [-]` | Deferred / descoped — record where it went |

### Updating

When a task changes state, update **three** places:
1. the checkbox on the task line,
2. the phase's progress row in [Progress summary](#progress-summary),
3. the **Last updated** date at the top of this file.

When a phase's every acceptance criterion is a passing automated test, mark the phase exit gate done
and record the date in the progress table.

---

## Progress summary

| Phase | Tasks | Done | ed | Status | Exit gate met |
|---|---|---|---|---|---|
| [0 — Foundations & spikes](#phase-0--foundations-and-de-risking-spikes) | 19 | 19 | 12.0 | Complete — all three spikes returned go | 2026-08-12 |
| [1 — Content structure](#phase-1--content-structure) | 33 | 33 | 28.0 | All 33 tasks done; gate open on `P1 #1` alone, which needs a browser journey | — |
| [2 — Pages, versioning, publishing](#phase-2--pages-versioning-and-publishing) | 29 | 29 | 27.0 | Complete — all 29 tasks and all 11 acceptance criteria | 2026-08-14 |
| [3 — Delivery, routing, preview](#phase-3--delivery-routing-and-preview) | 31 | 0 | 22.5 | Not started | — |
| [4 — Reusable content](#phase-4--reusable-content) | 19 | 0 | 12.0 | Not started | — |
| [5 — Media library & image pipeline](#phase-5--media-library-and-image-pipeline) | 33 | 0 | 23.5 | Not started | — |
| [6 — Authoring experience](#phase-6--authoring-experience) | 41 | 0 | 34.5 | Not started | — |
| [7 — Workflow, permissions, scheduling](#phase-7--workflow-permissions-and-scheduling) | 26 | 0 | 16.0 | Not started | — |
| [8 — SEO, caching, navigation, search](#phase-8--seo-caching-navigation-and-search) | 26 | 0 | 14.0 | Not started | — |
| [9 — Hardening, accessibility, launch](#phase-9--hardening-accessibility-and-launch) | 24 | 0 | 14.0 | Not started | — |
| **v1 total** | **281** | **81** | **203.5** | | |

Dependency order: `P0 → P1 → P2 → P3 → {P4, P5} → P6 → P9`, with **P7 parallel from P2 exit** and
**P8 parallel from P3 exit**.

---

## Blocking decisions

These come from [§29.2](./spec.md#292-open-questions). Each one gates the phase named in
"Needed by" — resolve it before that phase's dependent tasks start, and record the answer here.

- [ ] **Q2** — Expected content scale: hundreds or tens of thousands of pages? Affects tree UI, search
  backend, caching topology. *Owner: Product · Needed by: Phase 1* · **Answer:** _pending_
- [ ] **Q4** — Single instance or scaled out at launch? Determines whether Redis output cache is
  required. *Owner: Ops · Needed by: Phase 2* · **Answer:** _pending_
- [ ] **Q5** — Which email provider replaces `IdentityNoOpEmailSender`?
  *Owner: Ops · Needed by: Phase 1 (implemented P7)* · **Answer:** _pending_
- [ ] **Q6** — Is a CDN in front of the site? Changes cache headers, adds a purge integration.
  *Owner: Ops · Needed by: Phase 6* · **Answer:** _pending_
- [ ] **Q7** — Is SVG upload permitted at all? **Blocks `P5-06`.** Safest answer is no.
  *Owner: Security · Needed by: Phase 5* · **Answer:** _pending_
- [ ] **Q8** — Existing site to migrate, and must its URL structure be preserved?
  *Owner: Product · Needed by: Phase 3* · **Answer:** _pending_
- [ ] **Q9** — Retention/compliance obligations on content versions and audit logs?
  *Owner: Legal · Needed by: Phase 5* · **Answer:** _pending_
- [ ] **Q10** — Does self-service registration stay enabled, and with what default role?
  *Owner: Security · Needed by: Phase 1 (enforced P9)* · **Answer:** _pending_

Resolved already, recorded for reference: **Q1** — no localization, `en-US` only ([§19]).
**Q3** — SkiaSharp (MIT) is the image library; **AVIF is not produced in v1** ([§13.9]).

---

## Phase 0 — Foundations and de-risking spikes

**Objective:** prove the three technical unknowns and put scaffolding in place so no later phase is
blocked on setup. **12 ed** · Entry: `aspire run` starts the existing solution.

### Pre-work

- [x] **P0-01** Create the `InitialDatabase` migration (per `README.md`, never run on this repo yet):
  `dotnet ef migrations add InitialDatabase -p ../ContentManagementSystem.Data`. Verify it applies via
  the Aspire `ef-migrations` resource. — 0.25 ed
  *2026-08-12 — created; `Up` and `Down` both verified, and asserted continuously by
  `MigrationsApplyFromEmptyTests`. Applied by the Aspire `ef-migrations` resource on startup.*
- [x] **P0-02** Confirm `aspire run` starts SQL Server + server and `/health` reports healthy; record
  the baseline in `docs/`. — 0.25 ed
  *2026-08-12 — SQL Server, Azurite, and the server all start; `/health` and `/alive` return
  `Healthy`. Baseline recorded in [`docs/phase-0-baseline.md`](./docs/phase-0-baseline.md).*

### 0.1 Spikes — timeboxed, do these first

Each spike produces a written finding in `docs/spikes/` with a go/no-go recommendation. **Spike code is
thrown away** — nothing is promoted directly into the solution.

- [x] **P0-03** **S1 — Runtime-schema payload round trip.** Can a JSON payload be validated and
  deserialized against a *runtime-defined* schema (zones/properties as data) with acceptable
  performance and clear errors? *Box: 2 ed. Fallback: code-defined content types, losing runtime zone
  editing.* → [`docs/spikes/s1-runtime-schema.md`](./docs/spikes/s1-runtime-schema.md)
  *2026-08-12 — **GO**. 39/39 checks. Errors name the exact zone/block/property (the R2 trigger);
  ~1.2 µs per block; absent-vs-null and orphaned zones survive the round trip. Raised a gap in
  `P1-13` — see the note on that task.*
- [x] **P0-04** **S2 — Dynamic component rendering under static SSR.** Does `DynamicComponent` compose
  template → zone → field renderer with no interactive render mode, and does an error boundary isolate
  a failing block? *Box: 2 ed. Fallback: source-generated static render switch per template.*
  → [`docs/spikes/s2-dynamic-ssr.md`](./docs/spikes/s2-dynamic-ssr.md)
  *2026-08-12 — **GO**. 40/40 checks. Boundaries isolate a failing block in all three failure shapes
  (lifecycle, mid-`BuildRenderTree`, post-await), the whole [§15.3] fallback matrix behaves, and
  ~7 µs per block. Constraints recorded against `P3-08`, `P3-11`, `P3-13`, and `P3-27`.*
- [x] **P0-05** **S3 — Editor JS interop in Blazor WASM.** Do CodeMirror 6 and Quill integrate cleanly
  (init, two-way bind, dispose without leaks) as local assets under a strict CSP? *Box: 2 ed. Fallback:
  textarea-plus-preview editor for v1.* → [`docs/spikes/s3-editor-interop.md`](./docs/spikes/s3-editor-interop.md)
  *2026-08-12 — **GO**. 23/23 checks, driven through a real browser. No `unsafe-inline`, no
  `unsafe-eval`; 22 editors created and 22 disposed across 11 mount/unmount cycles. **The backoffice
  must expose a per-request style nonce** or CodeMirror renders silently unstyled — see `D13`.*
- [x] **P0-06** Record an ADR for any spike that returned no-go, capturing the agreed fallback. *(Exit
  criterion: no no-go without a recorded fallback.)*
  *2026-08-12 — all three spikes returned **go**, so no fallback ADR was required. Recorded
  [`ADR-0013` (D13)](./docs/adr/0013-backoffice-editor-bundle-and-style-nonce.md) anyway for the
  backoffice CSP nonce and local editor bundling, because S3 turned a spec assumption into a hard
  constraint on how `/admin` is served.*

### 0.2 Scaffolding

- [x] **P0-07** Create `ContentManagementSystem.Core` (class library) and
  `ContentManagementSystem.Rendering` (Razor Class Library); wire into `ContentManagementSystem.slnx`,
  `Directory.Packages.props`, and project references per [§5.2]. — 1 ed
  *2026-08-12 — both created with assembly-marker types for the reflection-based discovery that
  `TemplateReconciler` and the field-type registry will need. `Server` references both.*
- [x] **P0-08** Create `tests/ContentManagementSystem.Core.Tests` (unit, xUnit + FluentAssertions +
  NSubstitute). — 0.4 ed
- [x] **P0-09** Create `tests/ContentManagementSystem.Data.Tests` (EF integration, Testcontainers SQL
  Server). — 0.4 ed
- [x] **P0-10** Create `tests/ContentManagementSystem.Server.Tests` (API/delivery integration,
  `WebApplicationFactory`). — 0.4 ed
  *2026-08-12 — `CmsApplicationFactory` boots the real `Program` against a throwaway migrated
  database. Required making `Program` public.*
- [x] **P0-11** Create `tests/ContentManagementSystem.E2E.Tests` (Playwright + axe-core). — 0.3 ed
  *2026-08-12 — browsers self-install on first run via `PlaywrightBrowsers`, so neither a developer
  machine nor a CI agent needs PowerShell.*
- [x] **P0-12** Add bUnit to the rendering test path; register all test packages in central package
  management. — 0.3 ed
  *2026-08-12 — bUnit sits in `Core.Tests` alongside the field-type and payload tests. All test
  packages are declared in `Directory.Packages.props`; shared settings live in
  `tests/Directory.Build.props`. **FluentAssertions is pinned to 7.2.2** — 8.x is commercially
  licensed.*
- [x] **P0-13** Add Azurite (blob) to `aspire/ContentManagementSystem.AppHost/AppHost.cs` as the dev
  media store; wire the connection into the server. — 0.6 ed
  *2026-08-12 — the server registers the blob client only when the connection string is present, so
  the API test harness does not fail its health check on storage it was never given.*
- [x] **P0-14** Add Redis to `AppHost.cs` behind a feature flag / configuration switch (unused until
  P8). — 0.4 ed
  *2026-08-12 — provisioned only when `Cms:UseRedisOutputCache` is true.*
- [x] **P0-15** CI pipeline in `.github/workflows`: restore → build (warnings-as-errors, already on) →
  unit → integration (Testcontainers) → E2E → axe → publish artifacts. — 1.2 ed
  *2026-08-12 — `.github/workflows/ci.yml`, five jobs. **Not yet executed on a runner** — it needs a
  push to validate.*
- [x] **P0-16** Add a CI job that applies existing migrations against a Testcontainers SQL Server
  instance, proving the migration path works from empty. — 0.4 ed
  *2026-08-12 — its own `migrations` job, kept separate from the integration suite so the one
  failure that blocks every deployment gets its own signal.*
- [x] **P0-17** Pin Testcontainers images; add the `azure-sql-edge` fallback path already used by
  `AppHost` for ARM64 CI agents *(mitigates R9)*. — 0.4 ed
  *2026-08-12 — `SqlServerImage` pins SQL Server 2022 CU20, falls back to Azure SQL Edge on arm64,
  and honours a `CMS_TEST_SQL_IMAGE` override. Note Edge has no full-text search, which Phase 8
  needs — see the open question below.*
- [x] **P0-18** `CONTRIBUTING.md` with the conventions from [§23] (entity base classes, `ColumnTypes`,
  `FieldLengths`, migration review rules). — 0.25 ed
- [x] **P0-19** Seed `docs/adr/` with D1–D12 from [§29.1], one file per decision. — 0.25 ed

### Acceptance criteria — Phase 0

- [x] **P0 #1** All three spikes have a written finding with a go/no-go recommendation.
  *2026-08-12 — [S1](./docs/spikes/s1-runtime-schema.md), [S2](./docs/spikes/s2-dynamic-ssr.md),
  [S3](./docs/spikes/s3-editor-interop.md). All three **go**; no architectural fallback is needed.
  Spike code lives in [`spikes/`](./spikes), deliberately outside `ContentManagementSystem.slnx`.*
- [x] **P0 #2** `dotnet build` succeeds with zero warnings across the expanded solution.
  *Verified 2026-08-12. Required suppressing `ASPIRE004` in the AppHost, narrowly and with a reason —
  the non-executable `Data` reference is deliberate, since `AddEFMigrations` needs its generated
  `Projects.*` metadata.*
- [~] **P0 #3** CI runs green on an empty test suite, including a Testcontainers SQL Server integration
  test that applies the existing migrations.
  *All 8 tests pass locally across the four suites; the migration test applies and reverts from
  empty. **The workflow itself has not run on a GitHub runner yet.***
- [x] **P0 #4** `aspire run` starts SQL Server, Azurite, and the server; `/health` reports healthy.
  *Verified 2026-08-12; see [`docs/phase-0-baseline.md`](./docs/phase-0-baseline.md).*

**Exit gate:** no spike returned a no-go without an agreed fallback recorded as an ADR. — [x] met on
**2026-08-12** — all three returned go, so no fallback was needed. R1 and R2 close; R7 closes.
*`P0 #3` remains open: the CI workflow has still not executed on a GitHub runner.*

**Risks:** R1 (spike failure), R9 (Testcontainers in CI).

**Raised during Phase 0 — needs a decision by Phase 8:** the arm64 test fallback runs **Azure SQL
Edge**, which has no full-text search. `P8-18` builds `SearchIndexService` on a SQL Server full-text
index, so those tests cannot run on an arm64 developer machine under the current fallback. Either the
full-text tests get gated to amd64/CI, or arm64 developers run SQL Server under emulation (verified
working on Apple Silicon via Docker Desktop), or the search backend changes. Recording it now because
it is cheap to plan for and expensive to discover in Phase 8.

---

## Phase 1 — Content structure

**Objective:** a developer can define templates with typed zones and block types, and the system can
validate a content payload against them. **28 ed** · Entry: Phase 0 exit.

### 1.1 Domain and data — 6.5 ed

- [x] **P1-01** Entities in `Data/Models/Cms/`: `Template`, `TemplateRevision`, `Zone`, `BlockType`,
  `BlockTypeRevision`, `BlockTypeProperty`, `Composition`, `CompositionProperty`,
  `BlockTypeComposition`, `SiteSettings` — shapes per [§23.1]. — 2.5 ed
  *2026-08-13 — all ten plus the `WorkflowMode` enum. Two additions beyond [§23.1]:
  `BlockType.IsBuiltIn`, so the seeded `RawHtml` type cannot be deleted, and
  `SiteSettings.SingletonId`. `SiteSettings.HomePageId` / `NotFoundPageId` /
  `DefaultOgImageMediaId` are plain `int?` for now — their foreign keys land with `Page` in P2-01
  and `MediaItem` in P5-01.*
- [x] **P1-02** `IEntityTypeConfiguration<>` per entity in `Data/Configurations/Cms/`: keys, unique
  indexes (`Template.Key`, `(TemplateId, Zone.Key)`, `(BlockTypeId, Key)`, `(TemplateId,
  RevisionNumber)`), `FieldLengths` constants, `ColumnTypes`. — 2 ed
  *2026-08-13 — all four unique indexes asserted against real SQL Server by
  `Cms/StructureSchemaTests`, including that the same zone key on two templates is allowed. Every
  structural foreign key is `DeleteBehavior.Restrict`: cascading would take zone definitions with a
  deleted template and leave stored payloads with no schema to validate against. Added
  `ColumnTypes.Json` and `ColumnTypes.UnboundedText` rather than writing `nvarchar(max)` inline.*
- [x] **P1-03** Extend `Shared/Common/FieldLengths.cs` with CMS constants (`ContentKey = 100`,
  `Url = 2000`, `MetaDescription = 500`, `Name = 200`, `Description = 500`,
  `ComponentTypeName = 500`, …). — 0.5 ed
  *2026-08-13 — two names in that list collide with constants the template repo already ships.
  `Name = 200` is exactly the existing `EntityName`, so CMS name columns reuse it rather than
  introducing a second way to say 200. `Description = 500` collides with the inherited
  `Description = 4000`, so the CMS one is **`ShortDescription`**. Also added `IconKey`,
  `SummaryTemplate`, `GroupName`, `Culture`, `TimeZoneId`, `RevisionNotes`, `VerificationToken`.*
- [x] **P1-04** Register CMS `DbSet`s on `Data/Models/ApplicationDbContext.cs` and apply configurations
  from the assembly. *(Existing-code change.)* — 0.25 ed
  *2026-08-13 — `ApplyConfigurationsFromAssembly`, so a later phase's entity is one new file rather
  than an edit here that is easy to forget.*
- [x] **P1-05** Configure `AuthDbContext.AddLogging()` to **skip** `SearchDocument`, `OutboxMessage`,
  `MediaRendition`, `EditLock`, `NotFoundLog` — high-churn derived tables that would otherwise grow
  `AuditLog` without bound [§23.5]. *(Existing-code change; table names registered now, tables added
  later.)* — 0.25 ed
  *2026-08-13 — matched by entity-type name, since the tables arrive across P2, P5, and P8 and the
  exclusion should already be in place when they do. **Not yet covered by a test** — there is no
  excluded table to write one against until `EditLock` lands in `P2-02`; add it there.*
- [x] **P1-06** Migration `AddCmsStructure` (`Data/Migrations/`) — migration #2 in the
  [sequence](#database-migration-sequence). Verify `Up` and `Down` both apply cleanly in CI. — 1 ed
  *2026-08-13 — reviewed statement by statement: ten `CreateTable`s, no drop-plus-add, `Down` drops
  in dependency order. `Up` and `Down` are both asserted from empty by
  `MigrationsApplyFromEmptyTests`, which now covers two migrations.*
- [x] **P1-07** Seeding in `Data/Seeding/`: the single `SiteSettings` row (`Culture = en-US`) and the
  built-in `RawHtml` block type [§9.1]. — 0.5 ed
  *2026-08-13 — `CmsSeedData`, applied through `HasData` so the rows arrive in the same transaction
  as the schema and the Aspire `ef-migrations` resource needs no extra step. That makes seeding
  idempotent by construction; a test re-runs `MigrateAsync` over an already-migrated database and
  asserts no row is duplicated. Singleton-ness of `SiteSettings` is a check constraint, not a
  convention. `RawHtml` ships with its `content` property and revision 1, so content authored
  against it has a captured schema from the start.*
  ***Corrected 2026-08-13 while building `P1-21`.*** The seeded `PropertySnapshotJson` was written
  before `P1-15` defined the snapshot format and wrapped its array in a `{"properties": …}` object,
  which `ContentSchemaSnapshot.Read` refuses — the one block type every deployment ships with would
  have been the only one whose captured schema could not be loaded. Nothing read the column yet, so
  nothing caught it. Fixed in `CmsSeedData` and in the `HasData` values inside migration #2, which
  has never been applied anywhere but a developer machine; `dotnet ef migrations
  has-pending-model-changes` confirms the model snapshot still matches. Now asserted by
  `Content/ContentSchemaSnapshotTests` through the real reader.*

### 1.2 Field type framework — the extensibility spine — 7 ed

- [x] **P1-08** Contracts in `Shared/Contracts/Fields/`: `IFieldType` (with `ValidateAsync`,
  `SanitizeAsync`, `ExtractReferences`, `ExtractSearchText` per [§7]), `FieldTypeCapabilities`,
  `FieldConfiguration`, `ValidationResult`, `ContentReference`. — 1 ed
  *2026-08-13 — **one deliberate deviation from the [§7] signature**: `ValidateAsync` takes a
  fourth `ValidationMode` parameter. [S1](./docs/spikes/s1-runtime-schema.md) requires draft-vs-
  publish to be a validator parameter rather than a filter on results (consequence 3), and it
  cannot ride on `FieldConfiguration`, which is cached per schema row and must stay free of
  request-scoped state (consequence 4). `ValidationResult` carries `ValidationDiagnostic`s with a
  stable `code`, a `ValidationSeverity`, and a relative path the schema walk prefixes — the field
  type cannot know where in the document it sits. `FieldTypeCapabilities` gained a `Container`
  flag beyond the spec's list; it is how `P1-13`'s nested case knows which field types to exercise.
  Note `ContentReference` here is the extracted value type; the EF entity of the same name is a
  separate type arriving in `P2-02`, per [§7] and [§23.2] both using the name.*
- [x] **P1-09** `IFieldTypeRegistry` in `Core/Fields/` + `services.AddCmsFieldType<T>()` DI extension +
  startup discovery. — 1 ed
  *2026-08-13 — lookups return null rather than throwing, since a payload naming a field type no
  longer deployed is expected and delivery answers it with a logged warning [§15.3]. Duplicate keys
  fail at startup: two field types answering to `richText` has no defensible default. Assembly
  discovery scans **public** types only — a field type key ends up in stored payloads, so
  registering an assembly's private implementations behind the author's back is wrong, and the
  first version of the scan took a private test double down with it.*
- [x] **P1-10** Implement value field types in `Core/Fields/Types/` per [§7.1]: `plainText`,
  `multilineText`, `richText`, `html`, `number`, `boolean`, `date`, `dateTime`, `choice`, `color`,
  `json`. — 3 ed
  *2026-08-13 — all eleven, on a `FieldTypeBase` that holds the four rules they share: the payload's
  `type` discriminator must agree with the schema, the property must be an object, an empty value is
  only an error when **publishing** a `required` property, and everything else is the subclass's.
  Two things the spec left implicit and this had to settle. **`IFieldType.EditorComponent` and
  `RendererComponent` are now `Type?`** — `Core` sits below `Rendering` and `Client` in the
  reference graph and cannot name a component in either, so the hosting layer maps them to keys
  instead; recorded as [`ADR-0014` (D14)](./docs/adr/0014-field-type-components-resolved-by-the-hosting-layer.md),
  with consequences landing on `P3-09` and `P6`. And **`IContentSanitizer` / `SanitizationProfile`
  now exist in `Shared/Contracts/Security/`**, injected into `richText` and `html`; `P1-18`
  implements them. `AddCmsFieldTypes()` therefore produces a container that fails to resolve until a
  sanitizer is registered, which is the intended reading — the alternative is a deployment that
  quietly stores unsanitized markup. Also worth knowing: a configured `pattern` is untrusted input
  on a hot path, so matches run under a 100 ms timeout with a bounded compiled cache, and an
  unusable pattern is a **warning**, not an error — the author of the page cannot fix the template's
  regex, and failing the save would strand every page on that template. 146 tests green.*
- [x] **P1-11** Stub reference-bearing field types to their contract, completed in later phases:
  `media` (P5), `link`/`pageReference` (P3), `reusable` (P4), `tags` (P8); implement `blocks` fully
  here. — 1 ed
  *2026-08-13 — seven, not five: `mediaList` was in [§7.1] and in `FieldTypeKeys` but not in this
  task's list, and it ships with `media`. **What is deliberately not stubbed is `ExtractReferences`.**
  Every one of these implements it fully now. Deferring it would leave every page saved before the
  owning phase with no `ContentReference` row and nothing to go back and add them — where-used would
  under-report and invalidation would miss those pages, which is precisely the [§7.3] failure. What
  each type actually defers is only what needs another phase's tables: existence and
  `allowedTypes`/`minWidth`/`aspectRatio` (P5), URL resolution and `allowedTemplates` (P3),
  resolution and cycle guards (P4), the tag projection (P8). Each class says so in its own remarks.
  `tags` is **not** reference-bearing — a tag names a concept, not an entity, and has no
  `ContentReferenceTargetType`.*
  *Four decisions the spec left open. **Stored shapes**: `pageReference` follows `choice` and stores
  one id or an array under `value` rather than inventing a second member for the multiple case;
  `mediaList` and `blocks` both use `items`, as [§6.2] already does for `blocks`. **A configured
  `min` now also fails an empty list at publish** — previously `min: 1` was the one count rule an
  unfilled property slipped past, since the base class short-circuits empties to the required check.
  **`FieldTypeBase` gained a virtual `PayloadMember`**, so a structured type says its payload lives
  under `mediaId` / `kind` / `items` and inherits all four shared rules unchanged. **`blocks` takes
  `Lazy<IFieldTypeRegistry>`**: a container must dispatch to the field types of its nested values, so
  it depends on the registry that is built from it, and the deferred handle is what makes that legal
  rather than a container cycle. `AddFieldTypeRegistry` registers it.*
  *Two contract docs were refined by what this task forced. `IFieldType.ExtractReferences` said a
  container should "delegate back into the schema walk"; there is no callback for that, and what it
  must actually do is walk its contents and dispatch each nested value through the same method on
  the field type that wrote it — **by the stored `type` discriminator, not the schema**, because a
  value has to be read by whatever wrote it. And `ContentReference.Path` said field types always
  leave it null; a list or a container reports a path **relative to its own value** (`items[1]`,
  `items[0].properties.image`) and the walk prefixes the rest. 110 new tests, 256 green in
  `Core.Tests`.*
- [x] **P1-12** Per-field-type configuration JSON Schema + validation on zone save, in
  `Core/Fields/Configuration/` [§7.2]. — 1 ed
  *2026-08-13 — **the schema is declared in C# beside the field type and the JSON Schema is
  generated from it**, not authored as a document and interpreted. Recorded as
  [`ADR-0015` (D15)](./docs/adr/0015-field-configuration-declared-in-code-json-schema-generated.md):
  a hand-written subset interpreter silently ignores the first keyword an extension author reaches
  for, a full one is a third-party dependency on every structure write, and two of the rules that
  matter — that a `pattern` compiles under .NET and that a lower bound is not above its upper bound
  — cannot be said in JSON Schema at all. `FieldConfigurationSchemaWriter` still emits a draft
  2020-12 document per field type for `P1-24` and the `P1-29` configuration form, carrying those two
  as `x-cms` annotations. `IFieldType` gains `ConfigurationSchema`.*
  ***Configuration is closed** — a setting the schema does not declare is refused, so a mistyped
  `maxlength` is a save error rather than a line that persists and does nothing. That forced the
  settings the stubbed field types will read to be declared now and marked `NotEnforcedUntil`
  (`media`/`mediaList` P5, `link`/`pageReference` P3, `reusable` P4); configuring one is accepted
  with a warning naming the phase, because refusing it makes a developer build half a content model
  and come back.*
  *Two things this turned up. **`required` was a second source of truth** — the field types read it
  from `ConfigurationJson` while `Zone.IsRequired` and `BlockTypeProperty.IsRequired` are columns,
  and nothing said which won. It is now `FieldConfiguration.IsRequired`, supplied by
  `Parse(json, isRequired)`; declaring it as a setting throws and writing it into the blob is
  refused. **`patternMessage` was read but undocumented**, which is exactly what a closed schema
  would have made unstorable. `Fields/Configuration/FieldConfigurationContractTests` now checks both
  directions — every setting a field type reads is one it declares (recorded through a
  `FieldConfiguration` subclass, which is why the class is no longer sealed), and every setting it
  declares can actually be configured. 74 new tests, 330 green in `Core.Tests`.*
  *Also folded the date, instant, and hex-colour parsing the field types had privately into a shared
  `ValueFormats`, so a bound accepted at zone save is parsed by the same code that will read it at
  content-validation time. A configuration validator with its own parser accepts bounds the field
  type then silently ignores.*
- [x] **P1-13** Contract test asserting **every registered field type returns references for a
  representative populated value** — the omission that silently produces stale content [§7.3].
  — included above
  *Widened by [S1](./docs/spikes/s1-runtime-schema.md): the test as originally worded passes for a
  `blocks` field type that reports only its top level and silently drops every reference nested
  inside it. **Add a second case** — a container field type must return the references of a nested
  populated value.*
  *2026-08-13 — `Fields/ReferenceExtractionContractTests`, driven off the registry a real
  deployment builds, so a field type registered by the assembly scan is covered whether or not
  anyone remembers this file. The S1 second case is in: a `Container` must return the reference of a
  value nested a level below where the flat case puts it, which is the gap the flat case cannot see.
  The friction is intentional — a new reference-bearing field type with no sample here fails by
  name rather than passing vacuously. Two rules beyond the task's wording: an extracted row must
  carry a positive `TargetId` (a row pointing at 0 is a foreign key that fails on the publish path),
  and a field type that does **not** claim `ReferenceBearing` must report nothing, since the
  capability is what the engine dispatches on.*

### 1.3 Payload engine — 5 ed

- [x] **P1-14** `ContentPayload` model + envelope + `System.Text.Json` converters in `Shared/Content/`,
  with explicit **absent-vs-null** semantics [§6.2]. — 1.5 ed
  *2026-08-13 — a reader over the envelope, not a deserializer: every accessor hands back a
  `JsonElement`, and `ContentValueState` makes **Absent / Cleared / Present** a value callers cannot
  collapse by accident. **`ContentPayload` is deliberately not `IDisposable`** — it holds a detached
  clone rather than owning a `JsonDocument`, because [§16.1] caches deserialized content for fifteen
  minutes and a disposable in that cache is a use-after-free with a very long fuse. The cost is one
  copy of the bytes at parse time. **Nothing here rejects a malformed envelope**; a payload with no
  `zones` still parses, because `P1-15` needs a readable object to report that against. Writes go
  through `ContentPayloadBuilder`, which preserves zone order (a zone that moves to the end of the
  object reads to the `P2-14` diff as a removal plus an addition) and copies through envelope
  members this build does not recognise. `ContentPayloadJsonConverter` is attribute-applied, so a
  payload survives an API round trip as the document that went in.*
- [x] **P1-15** `ContentSchemaValidator` in `Core/Content/` — walks zone/block-property definitions,
  dispatches to field types, returns structured errors keyed by zone / block id / property. — 2 ed
  *Four constraints proven by [S1](./docs/spikes/s1-runtime-schema.md): stay on
  `JsonDocument`/`JsonElement` (no intermediate CLR model, or absent-vs-null is lost); build the
  error path from a push/pop stack so nothing allocates on the happy path; give every diagnostic a
  stable `code` alongside its message; make draft-vs-publish a validator parameter, not a filter
  applied to the results.*
  *2026-08-13 — all four honoured; `ContentPath` is the push/pop stack. **The schema needed a model
  and a storage format, and both landed here**: `Core/Content/Schema/` holds `ContentSchema` /
  `BlockTypeSchema` / `ContentPropertySchema` (a zone and a block property are the same thing at
  validation time) behind `IContentSchemaCatalog`, keyed by **key and revision** because that is how
  content addresses its schema [§8.5]. `ContentSchemaSnapshot` defines what
  `TemplateRevision.ZoneSnapshotJson` and `BlockTypeRevision.PropertySnapshotJson` actually contain —
  those columns existed with no format, and leaving it for `P1-21` risked two. Configuration is
  embedded as an object, not an escaped string, and parsed once per revision (S1 consequence 4).*
  *The division of labour with the field types is the thing to know. **The walk does not re-check
  what a field type already checks** — required-and-empty, the `type` discriminator disagreeing with
  the schema, block ids, `allowedBlockTypes`, list counts are all `P1-10`/`P1-11` code, reached by
  dispatching every value including absent ones. What the walk adds is what only it can see: the
  envelope, the captured revision, the keys the schema cannot account for, and the absolute path. It
  also skips anything the `blocks` field type has already reported on — a non-object block, a block
  with no type key — so one defect never produces two diagnostics.*
  *Severity follows one rule: **content that outlived its structure is a warning, everything else is
  an error.** Orphaned zone, orphaned block property, unknown block type revision, and a field type
  no longer registered are all warnings ([§8.5], [§15.3]) — erroring would strand the page and leave
  an editor no way to fix it. An unknown *template* revision is an error, because nothing below it
  can be checked at all. `ContentValidationDiagnostic` carries `ZoneKey`, `BlockId`, and
  `PropertyKey` beside the path: the backoffice addresses a block by GUID, not by index, which makes
  `P1 #2` a literal assertion.*
- [x] **P1-16** `ReferenceIndexer` in `Core/Content/` — extracts `ContentReference` rows via
  `IFieldType.ExtractReferences`. — 1 ed
  *2026-08-13 — **driven by the payload, not by the schema**, and dispatching on each zone's stored
  `type` discriminator. Two cases decided it, both of which make a schema-driven walk return nothing
  and erase a page's reference rows on its next save: a template revision that is no longer known
  (deleted template, environment promoted out of order), and a zone removed from the template whose
  retained content still points at real media. Both directions of error are not equal — an extra row
  makes a delete guard cautious, a missing one makes a page go stale [§7.3] — so this over-reports
  by design. It needs no catalog and no schema at all as a result. Occurrences are returned, not
  distinct targets; collapsing them is the projection's business in `P2-02`.*
- [x] **P1-17** Snapshot tests pinning the payload envelope format in `Core.Tests/Content/`. — 0.5 ed
  *2026-08-13 — `Content/Snapshots/*.json`, compared canonicalised, regenerated only under
  `CMS_UPDATE_SNAPSHOTS=1` so a snapshot can never silently rewrite itself. Three are pinned: the
  envelope a page starts life with, the [§6.2] example with a cleared zone beside it, and the schema
  snapshot format `P1-15` introduced. One decision is visible in them — **the default JSON encoder is
  kept, so `<` and non-ASCII are escaped**. A relaxed encoder would store authored HTML more
  compactly; the reason not to is that a payload reaches the editor as an API response and could
  reach a page as embedded JSON, and escaping that is safe everywhere beats escaping that is safe
  until someone inlines it into a `<script>` block. 67 tests across 1.3, 397 green in `Core.Tests`.*

### 1.4 Sanitization — ships now, before any HTML can be stored — 3.5 ed

- [x] **P1-18** `SanitizationService` in `Core/Security/` over HtmlSanitizer with the `Basic` /
  `Extended` / `Developer` profiles [§20.2], including the cross-profile rules (no `<script>`, no
  `<style>`, no `on*`, scheme allowlist, forced `rel="noopener noreferrer"`, CSS allowlist). — 1.5 ed
  *Its contract already exists: `P1-10` added `IContentSanitizer` and `SanitizationProfile` to
  `Shared/Contracts/Security/` because `richText` and `html` had to depend on something. Implement
  against it and register it — nothing resolves the field type registry until you do. `Shared`
  rather than beside the implementation on purpose: field types sanitize on write, renderers on
  read, and the editor preview has to run the identical pipeline, and those three layers do not
  reference each other.*
  *2026-08-13 — `SanitizationService` over `HtmlSanitizer 9.2.995` (MIT), with the allowlists split
  into a **public `SanitizationPolicy`**: [§14.4] wants a banner showing which tags the active
  profile permits, so the boundary has to be readable from outside the sanitizer, and the corpus
  suite can then assert against the list rather than restate it. `AddCmsSanitization()` is the
  registration; `AddCmsFieldTypes()` still fails to resolve without it, as intended.*
  *Four rules the library has no opinion about, which is what this class actually adds. **`data:`
  is a scheme but not a permission** — it is on the allowlist so the image case is reachable, and a
  URI then has to be on an `img`/`source`, base64, an allowlisted raster media type (never
  `image/svg+xml`), and within a 256 KB cap. Size is measured from the base64 length, not decoded;
  the point of a cap is to not hold the payload. **An `iframe` whose `src` fails the host allowlist
  is removed entirely**, not just stripped of its `src`, because a frame with no source frames the
  embedding origin in some browsers. Hosts match in full — a suffix match accepts
  `www.youtube.com.evil.test`. **`rel="noopener noreferrer"` is merged, not assigned**, so an
  author's `nofollow` survives. And **an empty class allowlist means no `class` attribute at all**;
  HtmlSanitizer reads an empty `AllowedClasses` as "all classes", which would let an author hang any
  of the site's own styles off arbitrary content.*
  *The one decision worth arguing about: **unknown elements are unwrapped, code-bearing ones are
  deleted with their contents.** A `<section>` a paste dragged in loses its tag and keeps its
  paragraphs (deleting the subtree is risk R3 arriving as a support ticket), but unwrapping is wrong
  for an element whose children are code — `<script>alert(1)</script>` would unwrap to the visible
  text `alert(1)`. `DeletedOutright` is that list.*
- [x] **P1-19** Markdig pipeline in `Core/Content/Markdown/`: markdown → HTML → sanitize, **identical
  between editor preview and delivery**. — 1 ed
  *Note what `P1-10` deliberately did not do: `richText` in markdown format is stored **exactly as
  authored**, un-sanitized, because the raw HTML markdown permits cannot be cleaned without parsing
  the markdown around it, and rewriting the source to whatever a Markdig round trip produces would
  lose the author's formatting on every save. That makes this task the only thing standing between
  stored markdown and the page — the conversion output must go through the sanitizer on **both**
  paths, with no shortcut for preview.*
  *2026-08-13 — `MarkdownRenderer` over Markdig 1.3.2, behind `IMarkdownRenderer` in
  `Shared/Contracts/Content/` (the backoffice runs in WebAssembly, cannot reference `Core`, and must
  not carry a second copy of the pipeline into the browser). The conversion is a **private** method
  returning a string nothing outside the class sees, so "render markdown without sanitizing" is not
  an available call. Registered once by `AddCmsContent()`, which is what makes `P1 #7` structural.*
  ***`UseAdvancedExtensions()` is deliberately not called** — recorded as
  [`ADR-0016` (D16)](./docs/adr/0016-markdown-extensions-bounded-by-the-sanitization-allowlist.md).
  Most of Markdig's extensions emit markup (`del`, `mark`, `abbr`, `dl`, footnote sections,
  `input type=checkbox`) that no profile in [§20.2] carries, so the syntax would appear to work and
  silently render to nothing — the ADR-0008 failure arriving through the front door. Only
  `PipeTables` and `AutoLinks` are on; an extension and the profile widening that carries it are one
  decision. Raw HTML parsing stays **enabled**, because disabling it escapes an author's paste into
  visible angle brackets rather than cleaning it.*
  *One consequence to know before someone reports it as a bug: `Basic` has no `h1`, so `# Title` in
  a markdown zone is unwrapped to its text. Pinned by a test rather than left to be rediscovered.*
- [x] **P1-20** XSS corpus suite in `Core.Tests/Security/` (OWASP payloads + polyglots) asserting
  neutralization per profile and reporting what was stripped. Wire into CI as a merge gate. — 1 ed
  *2026-08-13 — 52 payloads in eight groups (script elements, event handlers, URL schemes, embedded
  content, CSS, malformed markup, mutation XSS, polyglots), each run against all three profiles. The
  assertion **re-parses the sanitized output and inspects the DOM** rather than grepping for
  substrings: a substring check passes for output containing no literal `<script>` that a browser
  re-parses into one, which is exactly what the mXSS payloads do. Its own CI job, separate from the
  unit suite for the same reason `migrations` is separate — gap #11 should not be one red test among
  two hundred.*
  ***The first version of this suite was a tautology and a mutation test caught it.*** Every
  invariant was "the output conforms to the profile", which passes for any output once someone
  widens the profile: adding `script` to the `Basic` tag list left the whole corpus green. The suite
  now also asserts a hard-coded set of elements and attributes **no profile may ever permit**, and
  `SanitizationPolicyTests` asserts the same against the allowlists themselves — the difference
  between "did this payload survive" and "could a payload of this shape survive". Re-run under four
  mutations (widened tag list, iframe host check disabled, `rel` forcing removed, code-bearing
  elements unwrapped): 43, 5, 2, and 2 failures respectively.*
  ***Reporting what was stripped needed a contract change.*** `IContentSanitizer` gained
  `SanitizeWithReport`, returning `SanitizationResult` (`Html` plus `SanitizationRemoval` rows with
  a kind, a name, the element, and a truncated excerpt). A service that only returns clean markup can
  be verified safe but not verified *non-destructive*, which is risk R3 with no attacker to catch
  it. The corpus job writes every removal to the test log, and `P6-13`'s pre-save warning is the
  other consumer. The reporting path builds its own sanitizer per call — the library's removal
  events carry no per-call context, so a handler on the shared instance would hand one request
  another request's removals; a test asserts both paths produce identical HTML.*

### 1.5 Structure admin (functional, unstyled UI) — 6 ed

- [x] **P1-21** Management API `/api/cms/v1/templates` in `Server/Api/Cms/Structure/` — list, create,
  read, update, revisions. — 0.5 ed
  *2026-08-13 — six endpoints (list, read, create, update, revision list, revision read) over an
  `ITemplateService` in `Core/Structure/`. **No delete**: [§8.5] blocks deleting a template while a
  non-deleted page references it, and there is no `Page` table to ask until `P2-01`; shipping the
  verb now would mean shipping the guard later. It lands with the rest of `P1-32`.*
  ***Three things this task had to settle before it could be built, none of them template-specific.***
  **Authorization**: `CONTRIBUTING.md` requires it in the service layer, and `Core` cannot reference
  ASP.NET Core. `ICmsAuthorization` in `Shared/Contracts/Security/` is that seam — implemented by
  `HttpCmsAuthorization` over the cookie principal, backed by `CmsPermissionMap`, the [§21.1] table
  transcribed once and read by both the endpoint policies and the service checks so the two cannot
  disagree. Reads need `Content.Read`, writes `Structure.Edit`. `P2-21` extends this rather than
  starting it; section ACLs join at the same seam in P7.
  **Antiforgery**: the API is cookie-authenticated, which makes every write forgeable from any page
  a signed-in developer visits. `CmsAntiforgeryFilter` closes that now rather than in `P2-20`,
  because the alternative is a phase of writes with no CSRF defence. It is an endpoint filter, not
  the middleware already in the pipeline — that one only validates endpoints binding **form** data,
  and a JSON body is not covered by it. Token pair from `GET /api/cms/v1/antiforgery-token`.
  **The error contract**: `CmsProblems` maps every service outcome to one status and emits the
  [§22.2] shape with `errors` and `warnings` arrays always present. Diagnostics reuse
  `ValidationResult`, so a structural refusal and a field-type refusal reach the client as the same
  shape. 422 for a broken content-model rule, 409 for a taken key, 404, 403.*
  ***A pipeline bug this turned up, which would have hit every API endpoint.***
  `UseStatusCodePagesWithReExecute` re-executes any body-less error response against `/not-found` —
  so a 403 from the authorization middleware came back to the client as a **400 about the content
  type**, having been re-run through a Razor component endpoint as a JSON POST. Now branched with
  `UseWhen` so nothing under `/api` gets the site's HTML error experience. Caught by the one test
  that asserted a `Viewer` is refused; without it the whole authorization surface would have
  reported the wrong status.*
  *Two smaller decisions. `ContentKeys` holds the key shape for every content-model key, not just
  templates — a key is immutable, so the rules only ever run once and every key admitted is admitted
  forever. And a template created here is `IsOrphaned` from the start: no deployed component claims
  its key yet, which is what the flag means, and `P1-25`'s reconciler clears it when one does.
  17 API integration tests plus `Structure/ContentKeysTests`; the list endpoint is deliberately
  unpaged, since templates are written one per page shape.*
- [x] **P1-22** `/api/cms/v1/templates/{id}/zones` — CRUD with key immutability enforced [§8.5]. — 0.5 ed
  *`P1-12` built `IFieldConfigurationValidator` and registered it; this is the call site [§7.2].
  Block-type property writes in `P1-23` and the schema sync in `P1-26` are the other two.*
  *2026-08-14 — five endpoints (list, read, create, update, delete) over an `IZoneService`, nested
  under the template because a zone key is unique within a template and meaningless outside one.
  This closes the zone half of `P1-32`: **add is free, remove keeps the payload data, a key rename
  is refused, and a field-type change is refused.** Removal is deliberately unguarded — the payload
  is not rewritten, `P1-15` already reports the leftover value as an orphaned-zone **warning**, and
  blocking it while content exists would make a content model unchangeable the moment anyone used
  it.*
  ***What "cuts a revision" means had to be decided here, and it is the task's real content.*** A
  change cuts a `TemplateRevision` when it alters how a stored value is read or judged — add,
  remove, `IsRequired`, configuration — and does not when it only changes a label, matching
  `P1-21`'s rule that a template rename cuts nothing. The consequence worth knowing: the snapshot
  captures `name` and `sortOrder` too, so a revision's copy of them can lag the live zone. That is
  the right trade, since a revision exists to pin **validation**, and cutting one per typo
  correction would bury the changes revisions exist to record.
  ***A field-type change is refused rather than half-implemented.*** [§8.5] wants an explicit
  converter choice plus a background job rewriting drafts; there are no drafts and no job until
  Phase 2, and a "change accepted, values dealt with later" path would be a hole with a date on it —
  the same reasoning that kept template delete out of `P1-21`. `StructureCodes.FieldTypeImmutable`
  is separate from `KeyImmutable` because the remedy differs and a client should be able to offer
  it. The available path today (remove the zone, add it again) is named in the message.
  ***Warnings had to survive a successful save, which the API could not previously express.***
  `P1-12` accepts a `NotEnforcedUntil` setting with a warning naming its phase; `CmsProblems` only
  carries diagnostics on a **failure**, so that warning was being dropped and the setting would have
  gone quiet for months. `ZoneSaveResult` now carries `warnings` beside the zone, which moved
  `ApiDiagnostic` from `Server` into `Shared/Contracts/Api/` (the backoffice runs in WebAssembly and
  reads both shapes) and put the projection behind one `ApiDiagnostics.Project` that `CmsProblems`
  now calls too. Errors block, warnings do not — so the result is judged on `HasErrors`, not on
  whether anything was said.
  *Three smaller decisions. **An empty configuration object is stored as no configuration**, so
  `{}` stays out of every revision snapshot where it would read as a change on a `P1-28` structure
  diff. **Configuration is replaced, not patched** — a merge would leave no way to remove a setting.
  And **a zone id belonging to another template answers 404, not 400**: the pair is the address.*
  ***Concurrency is a real case here, unlike on templates.*** A zone write touches two unique
  indexes, and they mean different things: `(TemplateId, Key)` is a duplicate zone key, while
  `(TemplateId, RevisionNumber)` is someone else changing the same template's structure between
  this request reading it and writing it. The second is a lost update and returns the new
  `StructureCodes.ConcurrentChange` for the client to retry; anything else still rethrows, so an
  unrelated fault is never reported as a conflict a client will retry forever.
  ***One bug the tests caught, which would have corrupted every snapshot.*** Attaching the new zone
  through `context.Zones.Add` sets the foreign key and EF's relationship fixup then appends it to
  the loaded `template.Zones` as well, so a snapshot built from "the collection plus this zone"
  captured it **twice** — every page created afterwards would have validated against a schema with
  a duplicated slot. The zone is now attached through `template.Zones` and the snapshot taken from
  that collection alone. 16 API integration tests; `Core.Tests` 959 and `Data.Tests` 14 still green
  after `TemplateService` moved onto the shared `StructureJson` reader.*
- [x] **P1-23** `/api/cms/v1/block-types` and `/block-types/{id}/properties`. — 0.5 ed
  *2026-08-14 — eleven endpoints over one `IBlockTypeService`: the block type, its revisions, its
  properties, and the compositions composed into it. One service rather than three because every
  one of those writes cuts the same artefact — a `BlockTypeRevision` whose snapshot is the
  **flattened** property set — and splitting them would put the flattening rule in three places and
  let the revision number be computed twice against one block type.*
  ***The property rules are the zone rules, and they are now literally the same code.*** `SlotRules`
  holds what a zone and a block-type property share (labels, the field type binding, key and
  field-type immutability) because `P1-15` already reads them into one `ContentPropertySchema`; two
  copies of "a key is immutable" would drift, and the copy that drifted is the one nobody wrote a
  test for. `ZoneService` was moved onto it, along with a shared `StructureJson.CountSlots` and
  `SlotRules.Clean`.*
  ***Compositions forced the one genuinely new decision*** — recorded as
  [`ADR-0018` (D18)](./docs/adr/0018-compositions-flattened-into-block-type-revisions.md). A block
  instance names a block type and a revision, never a composition, so resolving groups at read time
  would let an edit to a shared group retroactively change every published block. The snapshot
  therefore **flattens** them, own properties first and each group after, and
  `ContentSchemaSnapshot.WriteSlots` records each slot's *effective* index rather than its raw
  `SortOrder` — the two sort orders come from different tables and merging on the number would
  shuffle a group into the middle of a host's own properties.
  *Key collisions are checked in **both** directions, which is the part that would have been missed:
  adding a property that a composed group already contributes, composing a group whose keys the host
  declares, and — the one that matters — adding a property *to a group* that would collide on any
  block type composing it. That last collision is not where the edit is made, and without the check
  it surfaces as a broken editor on a block type nobody was looking at.*
  ***`IsBuiltIn` earned its keep.*** A built-in's property set is frozen: adds, edits, and removals
  are refused with `StructureCodes.BuiltInImmutable`, because the code that renders `rawHtml` expects
  exactly those properties and no editor can repair a renderer. Its **metadata stays editable** —
  renaming "Raw HTML" is nobody's dependency. **There is still no block type delete**, for the reason
  there is no template delete: it must be blocked while content references the type, and there is no
  page table to ask until `P2-01`.*
- [x] **P1-24** `/api/cms/v1/compositions` and `/field-types` (read-only registry introspection). — 0.5 ed
  *`/field-types` serves each field type's configuration JSON Schema via
  `FieldConfigurationSchemaWriter` (`P1-12`), which is what `P1-29`'s configuration form builds its
  controls from.*
  *2026-08-14 — seven composition endpoints plus two read-only field-type ones. **A composition is
  not revisioned** — nothing in a payload addresses it — so every write here recuts every block type
  composing the group, in one transaction, and returns `AffectedBlockTypeKeys` instead of a revision
  number. That is the honest answer to "what did that do", and it is the blast radius a developer
  needs before editing a shared group; the list endpoint carries `BlockTypeCount` for the same
  reason.*
  ***The one delete this phase can honestly ship.*** A composition delete is refused while any block
  type composes it, and the refusal names them (`StructureCodes.InUse`). Unlike a template delete,
  its guard is a join table that exists.*
  ***`/field-types` is read-only with no write verbs at all***, rather than write verbs that refuse:
  a field type arrives with a deployment and there is no state here for a client to change. Backed by
  a singleton `IFieldTypeCatalog` that builds every JSON Schema document once — the registry cannot
  change without a restart, and regenerating a dozen documents per request to describe something
  constant is waste on the one screen a developer refreshes while iterating. Capabilities are sent as
  **names**, not as the enum's numeric value, so inserting a flag cannot silently change what a
  client thinks a field type can do.*
- [x] **P1-25** `TemplateReconciler` in `Core/Structure/`: scan assemblies for `[CmsTemplate]` /
  `[CmsBlockType]`, insert code-only records, mark DB-only records `IsOrphaned`, **never delete**, log a
  diff in Development [§8.4]. — 1 ed
  *2026-08-14 — the two attributes had to be written first. They live in
  `Shared/Contracts/Structure/` rather than beside the component base class in `Rendering`, because
  `Core` sits below `Rendering` in the reference graph and cannot read an attribute declared there.
  They carry only what code owns — the key, and a name and description used as **initial values**.
  Zone definitions stay out of them deliberately [§8.1]: those are data a developer edits in the
  backoffice and promotes as JSON.*
  ***The rule that looks like a bug and is not: name and description are applied on creation only.***
  They are editable in the backoffice, and rewriting them from the attribute on every startup would
  silently undo an editor's rename after each deploy. Asserted by a test that renames a template and
  reconciles again.*
  ***Two things the spec's wording had to be narrowed on.*** [§8.4] says "scans loaded assemblies";
  doing that literally makes the scan depend on whatever the CLR happened to fault in, includes every
  framework assembly, and answers differently under a trimmed publish. `CmsStructureAssemblies` names
  them instead, which also lets a test reconcile against exactly its own fixtures. And **a built-in
  block type is never orphaned** — it is declared by the system, not by a scanned attribute, so
  marking it would degrade the health check on every fresh install.*
  *Two components declaring one key throws at startup rather than picking a winner: a key is written
  into stored payloads, and choosing silently would render half a site with the wrong markup. A
  partially loadable assembly is tolerated the other way — the loadable types are reconciled and the
  rest logged, because an unrelated broken type should not stop a site from starting.*
- [x] **P1-26** `SchemaSyncService`: idempotent, additive-only apply of
  `Server/CmsSchema/*.json` zone/property definitions at startup [§27.1]. — 0.5 ed
  *2026-08-14 — recorded as
  [`ADR-0019` (D19)](./docs/adr/0019-schema-sync-is-additive-and-non-destructive.md), because
  "additive-only" is ambiguous and the reading decides whether a promotion can corrupt content.
  Read strictly it may only insert, and a structure promotion could never promote a change; read
  loosely it may retype a zone, which makes every stored value under that key unreadable **in an
  environment nobody is watching**. The pass therefore creates what is missing, updates labels and
  validation settings, **refuses** a field-type change or a configuration the field type rejects, and
  never removes.*
  *One file per record rather than one manifest — the reason the format exists is source control, and
  a single file makes every structural change a conflict against every other one. The `key` inside
  the document is authoritative, not the filename. The whole pass is one transaction, and files are
  applied compositions → block types → templates so one commit can add a group and the block type
  that composes it.*
  ***Two bugs the tests caught, both of which would have shipped quietly.*** A block type file naming
  a composition the **same pass** had just created was refused, because the lookup queried the
  database and the composition was added but not yet saved — the dependency ordering existed and did
  nothing. And `export` was writing a file for the seeded `rawHtml` block type, which `apply` then
  refused on every subsequent run: permanent drift in the CI check, from a record nobody can change.
  Built-ins are now excluded from export, and the export→diff round trip is asserted to settle to
  nothing.*
- [x] **P1-27** `cms-templates` health check — degrades when an `IsOrphaned` template has non-deleted
  pages [§24.2]. — 0.25 ed
  *2026-08-14 — **`Degraded`, never `Unhealthy`**, which is the whole point per [§8.4]: a bad
  deployment must be visible without taking down a site whose other pages render perfectly well. It
  names the offending keys in its description and its `data`, because a count alone tells an operator
  nothing actionable.*
  ***It is deliberately broader than [§24.2] words it, and this will tighten in Phase 2.*** The spec's
  condition is "an `IsOrphaned` template **has non-deleted pages**", which is the right condition to
  end at — an orphan nobody uses is housekeeping, not an operational matter. There is no page table
  to ask until `P2-01`, so today it fires on orphan existence alone. Worth knowing before someone
  reports it: a template created in the backoffice ahead of its markup is orphaned **by design**
  (`P1-21`), so a developer building a content model early will see this degrade. That is not a false
  positive — such a template cannot render — but narrowing it is the first thing `P2-01` should do.*
  ***Narrowed 2026-08-14 in `P2-01`.*** It now degrades only once an orphaned template has a
  non-deleted page, which is [§24.2]'s condition and was unaskable without the `Page` table. A
  template created ahead of its markup no longer degrades anything. Block types keep the broad
  condition: nothing references one relationally — block instances name it from inside a payload —
  so existence is the only signal available until the reference index can answer for it.*
- [x] **P1-28** CLI verbs in `Server/Cli/`: `cms schema export | diff | apply` [§27.1]. — 0.25 ed
  *2026-08-14 — `dotnet run -- cms schema export|diff|apply [directory]`, handled after `Build()` so
  the verbs use exactly the services the site uses, and before anything is mapped so no request
  pipeline and no startup pass ever runs. A promotion tool that reimplemented the sync would be a
  second definition of what "apply" means; all three verbs are one `ISchemaSyncService`.*
  ***`diff` exits 2 when the files and the database disagree***, distinct from 1 for a command that
  failed — it is the CI drift check, and a check that always succeeds is not a check. **A refusal
  counts as drift** even though nothing is pending: a file asking for something that will never be
  applied should leave the repository rather than be reported on every future run. **A kept-unlisted
  slot does not**, or the check would be impossible to keep green while anyone edits structure in the
  backoffice. Those two judgements are `SchemaSyncReport.HasPendingWork` / `HasProblems` and have
  their own unit tests, since getting either backwards produces a gate that never fires or never
  passes.*
- [x] **P1-29** Plain admin screens under `/admin/structure` in `Client/Components/Admin/Structure/`:
  template list, create, edit zone, edit block type. — 2 ed
  *2026-08-14 — four screens (`/admin/structure/templates`, `…/templates/{id}`,
  `…/block-types`, `…/block-types/{id}`) plus a `SlotForm` shared by zones and properties, since the
  two are one thing at validation time and would otherwise be two copies of one screen. Bootstrap
  classes and `form-floating` per `.claude/rules/blazor.instructions.md`; every screen is
  `InteractiveWebAssembly` with code-behind and no `@code` block.*
  ***The pre-rendering pattern needed a service seam, and that seam is `IStructureClient`.***
  `HttpStructureClient` calls the API from the browser; `ServerStructureClient` calls the structure
  services directly while pre-rendering, so a screen arrives with its content in the HTML instead of
  a spinner the developer watches. The server half deliberately does **not** loop back through its
  own HTTP API — a request to itself would need a cookie it does not have and an antiforgery token
  that has not been issued. Authorization is unaffected: every one of those services checks
  permissions itself, which is exactly why `CONTRIBUTING.md` puts the check in the service layer.*
  *Two moves this forced, both to `Shared`: `AntiforgeryTokenResponse` / `CmsAntiforgeryDefaults`
  (the WASM client cannot reference `Server`, and without them it cannot save anything at all), and
  a `StructureClientResult<T>` that is deliberately **not** `StructureResult<T>` — that type lives in
  `Core`, which the client cannot see, and carries an outcome enum whose only consumer is the HTTP
  status mapping.*
  ***The screens offer no control for the changes [§8.5] forbids, and do not rely on that.*** Key and
  field-type inputs go **read-only** rather than hidden once a slot exists — hiding an immutable field
  makes it look like a missing one — and the service refuses both regardless, with the refusal
  rendered as a diagnostic. Composed properties are listed with their group and given no edit button,
  because editing one there would fork a shared definition into many. The field-type picker and the
  per-field settings hint are built from the `P1-24` JSON Schema, so an extension author's field type
  documents itself here with no change to this screen.*
  *One framework rule learned the hard way: a `[PersistentState]` property **must not** have an
  initializer (`BL0009`) — the initializer runs after the restored state is applied and throws it
  away.*
- [x] **P1-30** Register CMS services and the field type registry in `Server/Program.cs`.
  *(Existing-code change.)* — 0.25 ed
  *2026-08-13 — sanitization, the field types and their registry, the structure services, the
  authorization policies, and the antiforgery header name are registered; `P1-21`'s endpoints are
  mapped. **`AddCmsContent()` is deliberately still not called.** It registers
  `ContentSchemaValidator`, which needs an `IContentSchemaCatalog`, and the only honest
  implementation is the cached database-backed one that arrives with the payload-validating
  endpoints in Phase 2 — an empty catalog would make a deployment start up validating every payload
  against nothing and reporting success. Finish this task there. The markdown renderer rides on the
  same call, so nothing resolves `IMarkdownRenderer` until then either.*
  *2026-08-14 — 1.5 added the rest: the block type, composition, and field-type-catalog services;
  `AddCmsStructureReconciliation(...)` naming the assemblies scanned for `[CmsTemplate]` /
  `[CmsBlockType]`; `SchemaSyncOptions` bound from `Cms:SchemaSync`; the `cms-templates` health
  check; `CmsStructureStartupService`, which reconciles and then syncs, in that order; and
  `ServerStructureClient` for the pre-rendered admin screens. `Program` now returns an exit code so
  `dotnet run -- cms schema …` can fail a CI job. **`AddCmsContent()` is still not called**, for the
  reason above — this task stays open until `P2` supplies a real `IContentSchemaCatalog`.*
  *2026-08-14 — **closed**. `P2-10` needed the catalog to validate a draft save, so
  `DatabaseContentSchemaCatalog` was built: revision snapshots read from the database and held in a
  process-wide `ContentSchemaCache`, which is safe to keep forever because a revision's snapshot is
  written once and a structural change cuts a new revision rather than editing the old one [§8.5].
  `AddCmsContent()` is now called, along with the eight Phase 2 page services.*
  ***One decision inside it worth stating: a cache miss reads synchronously.***
  `IContentSchemaCatalog` is deliberately a synchronous interface — `ContentSchemaValidator` resolves
  a block type revision in the middle of a walk that is itself on a hot path — and making it async
  would put an await on the inner loop of every payload validation to serve a cache that hits
  essentially always. The blocking call happens at most once per revision per process. The catalog
  and therefore the validator are now **scoped** rather than singleton, since resolving a revision
  reads through a database context; the indexer and the markdown renderer hold no state and stay
  singleton.*
- [x] **P1-31** Wire axe-core accessibility checks into CI against the structure screens — the
  continuous a11y gate starts here, not in P9. — 0.25 ed
  *2026-08-14 — its own CI job (`Accessibility (axe)`), separate for the reason `migrations` and
  `xss-corpus` are separate: an a11y regression should not be one red test among many, because the
  usual response to that is to disable the rule. WCAG 2.1 AA plus axe's best-practice pack, which is
  where heading order and landmark structure live — exactly what goes wrong on a screen assembled
  from tables and forms.*
  ***It renders the components rather than driving the running site, which is a real trade.*** A
  browser journey through `/admin` needs a database, a migrated schema, a seeded user, and a login,
  which makes it a nightly job — and a gate that does not run on every push is not a gate. What axe
  inspects is the DOM, so `PrerenderingHtmlRenderer` renders each screen statically and hands the
  markup to a real Chromium. That is also the *right* markup to judge: it is what a user sees before
  the WebAssembly runtime finishes downloading, and a screen that is only accessible after hydration
  is not accessible. **What this leaves for P9** is everything needing the compiled stylesheet —
  colour contrast above all — and focus order across a real navigation.*
  ***Two traps, both closed.*** The renderer must await `QuiescenceTask` or the gate inspects a page
  reading "Loading templates…", finds no violations, and goes green having checked nothing; each case
  therefore asserts a string the loaded screen must contain. And the gate was mutation-tested:
  removing a `<label>` alone does **not** fail it, because `placeholder` supplies an accessible name
  under accname — removing both correctly fails with `label (critical) … #slot-name`.*
- [x] **P1-32** Template evolution rules enforced in the service layer [§8.5]: add zone free; remove
  zone retains payload data as orphaned; **rename key forbidden**; field-type change requires an
  explicit converter choice; template delete blocked while pages reference it. — included above
  *2026-08-14 — the **zone** rules landed with `P1-22`: add free, remove retains, rename refused,
  field-type change refused until the converter and the drafts it rewrites exist. What is left is
  the same set for block-type properties (`P1-23`) and the template delete guard, which cannot be
  written until `Page` exists in `P2-01`.*
  *2026-08-14 — `P1-23` closed the **block-type property** half on the same terms, and `P1-26` made
  the schema sync obey them too. **What is left is one rule**: template delete blocked while a
  non-deleted page references it. It stays open until `P2-01`. The composition delete guard, which
  is the same shape against a join table that already exists, shipped in `P1-24`.*
  *2026-08-14 — **the blocker is cleared**: `P2-01` landed `Page`, so the delete verb and its guard
  can now be written against a real query. Still open because the verb itself does not exist yet —
  it lands with the template endpoints' next revision, alongside `P2-16`.*
  *2026-08-14 — **closed.** `ITemplateService.DeleteAsync` and `DELETE /api/cms/v1/templates/{id}`,
  behind `Structure.Edit` and antiforgery like every other structural write. The template's zones and
  captured revisions go with it, removed explicitly because every structural foreign key is
  `Restrict` — cascading would take the only record of what stored content was validated against.*
  ***The guard is wider than [§8.5]'s wording, and deliberately.*** The spec says a *non-deleted*
  page blocks the delete; this refuses while **any** page references the template, counting the
  recycled ones separately in the message. Two reasons, either of which is sufficient: a page in the
  recycle bin keeps its `TemplateId` and can be restored, so the narrow reading turns a restore into
  a page whose schema no longer exists; and `Page.TemplateId` and `PageVersion.TemplateId` are both
  `Restrict`, so the narrow guard would pass the check and then hand the caller a foreign-key error
  in place of an answer. Emptying the bin is a remedy, so the refusal names it.*
  *The refusal **names the pages** rather than counting them — capped at five plus "and N more",
  since "12 pages use this template" leaves a developer to go and find them and twelve hundred names
  is a wall nobody reads. `Api/Cms/TemplateDeleteApiTests`, six cases: an unused template goes with
  its revisions, a used one is refused by name and is still there afterwards, a recycled page blocks
  it until purged and then does not, an Editor is refused where a Developer is not, a missing
  template is 404, and a delete with no antiforgery token is refused.*
  ***Still no block type delete***, and it is not this task's wording: guarding it means finding
  block *instances* of a type inside stored payloads, and a block type is not a
  `ContentReference` target, so there is no index to ask. It needs either a payload scan or a
  projection that does not exist yet — worth raising when Phase 4 or 8 builds one.*
- [x] **P1-33** ADRs for any Phase 1 decision not already covered by D1–D12.
  *2026-08-14 — Phase 1 produced seven: `D13`–`D16` during 1.1–1.4, and three from 1.5.
  [`D17`](./docs/adr/0017-revisions-cut-only-when-content-is-read-differently.md) — a revision is cut
  only when content would be read differently, which [§8.5] leaves undefined and the API cannot avoid
  answering on every write.
  [`D18`](./docs/adr/0018-compositions-flattened-into-block-type-revisions.md) — compositions are
  flattened into block type revisions and editing one recuts every host, because a block instance
  names a block type and never a composition.
  [`D19`](./docs/adr/0019-schema-sync-is-additive-and-non-destructive.md) — the schema sync refuses
  rather than applies, since a promotion runs where nobody is watching.*

### Acceptance criteria — Phase 1

- [~] **P1 #1** A `Developer` creates a template with four zones of differing field types through the
  admin UI, and the definitions persist.
  *2026-08-14 — every part of this exists and is asserted **except the words "through the admin UI"**.
  `Api/Cms/ZoneApiTests` drives create-template-then-add-zones of differing field types end to end
  against a real database, and `P1-29` ships the screens that call exactly those endpoints. What is
  not covered is a browser actually driving the form, which needs the E2E harness pointed at a
  running site with a login — deliberately deferred with the rest of the full-journey suite (see
  `P1-31` for why the a11y gate does not wait for it). Closing this honestly is one Playwright
  journey once `P2` stands the site up with seeded users.*
- [x] **P1 #2** `ContentSchemaValidator` accepts a valid payload and rejects an invalid one with errors
  identifying the exact zone, block id, and property.
  *2026-08-13 — `Content/ContentSchemaValidatorTests` asserts `ZoneKey`, `BlockId`, and `PropertyKey`
  on the diagnostics themselves, not just the rendered path. The backoffice addresses a block by GUID
  rather than by index, which is what makes this a literal assertion rather than a string match.*
- [x] **P1 #3** Renaming a zone key is refused; renaming a display name succeeds.
  *2026-08-14 — `Api/Cms/ZoneApiTests.RenamingAZoneKeyIsRefusedAndRenamingItsLabelIsNot`, through
  the API rather than against the service: a rule that holds in `ZoneService` and is bypassable by
  an endpoint is not enforced. The same suite asserts the neighbouring rule the criterion does not
  name — a field-type change is refused too.*
- [x] **P1 #4** Removing a zone leaves existing payload data intact and reachable as orphaned content.
  *2026-08-14 — half of this is asserted. `P1-22` proves the definition goes while the revision that
  captured it stays, and `P1-15` already reports an orphaned zone as a **warning** rather than an
  error, which is what makes the value reachable. The literal criterion — a stored payload survives
  the removal and the editor can still see the value — needs a page to store one, so it closes in
  Phase 2 against `ContentSchemaValidator` and the obsolete-content panel.*
  ***Closed 2026-08-14*** — `Api/Cms/OrphanedZoneApiTests`, three cases through the API now that
  Phase 2 supplies the page. The value survives the removal byte for byte; once the page **adopts
  the new revision** the leftover comes back as a `zone.orphaned` warning naming the zone, and is
  still stored after that save; and a page carrying one can still be published, because a warning
  does not block.
  *The revision timing is the part worth knowing, and it is why the first two cases are separate. A
  payload is judged against the revision it **captured**, so a draft that has not moved forward is
  still being validated against a schema in which the zone exists and reports nothing. The orphan
  appears exactly when an editor adopts the structural change — which is also exactly when they are
  in a position to do something about it.*
- [x] **P1 #5** A template defined in code but absent from the database is created at startup; a
  database template with no code component is marked orphaned and degrades `cms-templates`.
  *2026-08-14 — `Structure/StructureStartupTests`, all three halves: a `[CmsTemplate]` fixture with no
  row is created with revision 1, a row no attribute declares is marked orphaned (and **not**
  deleted), and `HealthCheckService` then reports `cms-templates` as `Degraded` naming that key.
  Also asserted: the pass is idempotent, an editor's rename survives it, a template adopted back by a
  returning component clears its flag, and the built-in block type is never orphaned.*
- [x] **P1 #6** Every payload in the XSS corpus is neutralized under each sanitization profile, with the
  stripped content reported.
  *2026-08-13 — `Security/XssCorpusTests`, 52 payloads × 3 profiles, green, running as its own CI
  job. Removals are written to the test log per payload per profile. Also asserted: sanitizing twice
  changes nothing further, which both matches the on-write-and-on-render path (ADR 0008) and is the
  shape a mutation bypass takes.*
- [x] **P1 #7** Markdown rendered by the editor-preview path is byte-identical to the delivery path.
  *2026-08-13 — structural rather than incidental: one `IMarkdownRenderer` registration, one
  conversion method, and the conversion itself is private. What the test actually guards is the
  remaining risk — the preview reads `ToHtmlWithReport` for its strip warning while delivery reads
  `ToHtml`, and those run through separately constructed sanitizers. Asserted equal across the whole
  corpus under all three profiles.*

**Exit gate:** structure can be defined and a payload validated against it; XSS corpus green in CI.
— [ ] met on ____
*2026-08-14 — the gate's own wording is satisfied: structure is definable through the API and the
admin screens, `ContentSchemaValidator` validates a payload against it (`P1 #2`), and the XSS corpus
runs as its own CI job (`P1 #6`). It is left open because two acceptance criteria are only partly
met, and closing the gate over them would lose the distinction: **`P1 #1`** needs a browser driving
the admin form rather than the API beneath it, and **`P1 #4`** needs a stored payload to survive a
zone removal — which needs a page to store one. Both close early in Phase 2; neither blocks starting
it, since `P2-01` is the thing that unblocks them.*
*2026-08-14 — **`P1 #4` is now closed** (`OrphanedZoneApiTests`), and all 33 tasks are done including
`P1-32`'s template delete. **`P1 #1` is the only thing left**, and it is one Playwright journey: a
browser signing in and driving the zone form, rather than the API the form calls. It waits on the
full-journey E2E suite with seeded users — deliberately, per `P1-31` — so the gate stays open on a
single, named, non-architectural item rather than being ticked over it.*

**Risks:** R2 (runtime-schema complexity), R3 (sanitizer over-stripping).

**Raised during Phase 1 — build infrastructure:** `SSH.NET 2025.1.0`, pulled in transitively by
Testcontainers, picked up [GHSA-q939-rpr3-3284](https://github.com/advisories/GHSA-q939-rpr3-3284)
and, under warnings-as-errors, failed every build of the three test projects that use Testcontainers
— nothing to do with any task here. Pinned to `2026.0.0` in `Directory.Packages.props` via transitive
pinning; the Testcontainers suites were re-run against real SQL Server to confirm the bump is
harmless. Remove the pin when Testcontainers resolves a patched version itself. **A NuGet advisory
published against any transitive dependency will break the build the same way** — worth knowing
before it happens on a Friday.

**Also raised during Phase 1 — the RZ1021 trap.** Editing any `.razor` file can poison the Razor
compilation server on SDK 10.0.301: it then misparses component tags inside code blocks
(`@if { <SomeComponent /> }`) and reports dozens of bogus errors in files nobody touched, including
untouched template code. The remedy is `dotnet build-server shutdown` and nothing else — it is
already written up in
[`.claude/rules/blazor.instructions.md`](./.claude/rules/blazor.instructions.md). It cost real time
in 1.5 before that note was read, and it is worth reading first, because every dead end it sends you
down (clean `obj/`, edit the markup, change the SDK pin in `global.json`) looks plausible and the
last one appears to work — switching SDKs happens to start a fresh build server.

---

## Phase 2 — Pages, versioning, and publishing

**Objective:** the core promise — a page has a draft and a published version, and editing the draft does
not disturb what is published. **27 ed** · Entry: Phase 1 exit.

### 2.1 Data — 6.5 ed

- [x] **P2-01** `Page` and `PageVersion` entities + configurations per [§23.2], including the mutual
  `Page.DraftVersionId` / `PageVersion.PageId` FK handling from [§23.5] (`DeleteBehavior.Restrict`,
  `DraftVersionId` set in a second statement inside the creating transaction). — 2 ed
  *2026-08-14 — both, plus `PageVersionStatus` and the `ISoftDeletable` interface `P2-04` needed.
  The mutual reference is three relationships, not two: `DraftVersionId` and `PublishedVersionId`
  are separate navigation-less FKs beside `PageVersion.PageId`, all `Restrict` — cascade would be a
  cycle SQL Server refuses outright, and version history is the thing a soft delete exists to
  preserve.*
  ***One deliberate deviation from [§23.2]: `Path` is `nvarchar(800)`, not 900.*** A nonclustered
  index key is capped at 1700 bytes and silently includes the clustering key; `nvarchar(900)` is
  1800 bytes on its own, so SQL Server would create `IX_Pages_Path` with a warning and then fail
  inserts of long values — an index that works until a site gets deep. `PageTreeService.MaxDepth`
  bounds the real worst case at 551 characters, so the column is provably wide enough.
  *Three smaller decisions. `PageVersion.TemplateId` is a **navigation-less** FK: a version's
  template is a captured coordinate rather than a relationship anyone traverses, but a version whose
  template row is gone can neither be rendered nor diffed, so the constraint stays. `Priority`
  needed `ColumnTypes.SitemapPriority` — the model-wide `decimal` convention is `Money`, and storing
  a sitemap priority as `decimal(18,2)` invites a `0.55` no search engine reads back as written.
  And `Page.PublicId` carries a unique index with no database default, so a service that forgets to
  assign one fails loudly on the second page rather than quietly sharing `Guid.Empty`.*
  ***Also closed here: the `SiteSettings.HomePageId` / `NotFoundPageId` foreign keys that `P1-01`
  deferred to this task**, and the `P1-27` narrowing — `cms-templates` now degrades only once an
  orphaned template has a non-deleted page, which is how [§24.2] words it and was impossible to ask
  until now. Its test asserts all three states: unused orphan healthy, orphan with a live page
  degraded, orphan whose only page is in the recycle bin healthy again.*
- [x] **P2-02** `ContentReference` and `EditLock` entities + configurations, with the two hot indexes
  `(TargetType, TargetId)` and `(SourceType, SourceVersionId)`. — 1 ed
  *2026-08-14 — `ContentReference` reuses `ContentReferenceTargetType` from `Shared` and adds
  `ContentSourceType`; the name collision with the `P1-08` value type is the one [§7] and [§23.2]
  both chose, and the namespaces separate them. **Both ends are polymorphic, so the table carries no
  foreign key at all** — `TargetId` means a page, a media item, or a reusable item depending on
  `TargetType`, which no constraint can express. Every guard built on these rows is therefore a
  query, which is also why `P1-16` over-reports by design.*
  ***`ContentReference` is added to the audit exemption list, beyond the five [§23.5] names.*** It is
  deleted and reinserted wholesale on every draft save — every twenty seconds per open editor —
  which is precisely the unbounded-growth argument that exempts the others, and it records nothing
  the audited payload beside it does not already hold. `CONTRIBUTING.md` already states the rule
  this follows.
  *`EditLock` is keyed on `PageId` with no surrogate: at most one lock exists per page, and a table
  whose primary key **is** that fact cannot hold two. Its FK to `Page` is **the one cascade in this
  schema** — a lock is disposable UX state, and `Restrict` would let a stale heartbeat block a
  permanent delete the recycle bin had already cleared. This finally makes `P1-05`'s registered
  exclusion testable: `EditLocksAreNotWrittenToTheAuditLog` is the test that note asked for.*
- [x] **P2-03** `rowversion` concurrency tokens on `Page` and `PageVersion`; global query filters
  excluding `IsDeleted = 1`; filtered indexes per [§23.5]. — 1 ed
  *2026-08-14 — `rowversion` on both, asserted by two contexts saving the same draft from state each
  loaded before the other wrote. **`PageVersion` deliberately has no query filter of its own**: a
  deleted page's history is the thing the recycle bin exists to preserve, and a matching filter
  would hide exactly the rows a restore has to find. EF warns about that shape (10622), so the
  warning is suppressed in `ApplicationDbContext.OnConfiguring` with the reason — at the model
  rather than at each registration, so the decision travels with what made it.*
  *The tree index `IX_Pages_ParentId_SortOrder_Live` is filtered to `IsDeleted = 0` because the
  recycle bin is the only caller that wants deleted rows and it asks explicitly; `IX_Pages_Path` is
  deliberately **unfiltered**, since restoring a subtree has to find deleted rows by prefix. Note
  that Phase 2's schema has no filtered *unique* index — the first is `PageRoute.UrlHash WHERE
  IsPublished = 1` in `P3-01`.*
- [x] **P2-04** Implement `AuthDbContext.ApplySoftDeletes()` — the virtual hook exists and is empty, so
  a stray `Remove()` on a `Page` would destroy version history. *(Existing-code change.)* — 0.5 ed
  *2026-08-14 — the hook was empty **and never called**; both halves are fixed. It runs before
  fingerprinting and audit capture, so a rewritten delete is stamped and logged as the update it has
  become. An entity already flagged deleted is left `Deleted`, because reaching `Remove` a second
  time is the permanent delete the recycle bin performs deliberately.*
  ***The test found a hole that would have made the net useless in exactly the case it exists for.***
  EF resolves a severed required relationship the instant `Remove` is called, so removing a page
  whose versions happened to be loaded threw **there** — before any `SaveChanges` override could
  rewrite it — while the same call against an unloaded page was caught silently. A safety net whose
  behaviour depends on what the change tracker was holding is not one. `AuthDbContext` now sets
  `CascadeDeleteTiming` and `DeleteOrphansTiming` to `OnSaveChanges`, which changes nothing about
  the SQL finally sent and only decides when the tracker computes it. The test loads the versions
  first, on purpose.*
- [x] **P2-05** `Page.Path` materialization (`/1/8/44/`) and maintenance on insert/move in
  `Core/Content/PageTreeService.cs`; index it for prefix matching [§10.1]. — 1 ed
  *2026-08-14 — `AttachAsync` after insert (the path contains the page's own id, so it is a second
  statement for the same reason `DraftVersionId` is) and `MoveAsync`, which rewrites every
  descendant's prefix in one pass. **Nothing here calls `SaveChanges`**: a move that committed while
  the route rebuild beside it failed would leave the tree and the URLs disagreeing.*
  ***`MaxDepth = 50` is a real rule, not a defensive one.*** It is what bounds `Path` below its
  column and therefore below SQL Server's index key limit, and it is far past any navigable site —
  a tree that deep is a modelling mistake, and refusing it names the mistake where it is made.
  *Two rules the task's wording does not mention and the tree cannot do without. **A page may not be
  moved under its own descendant**: the subtree would still be in the table, reachable from nothing,
  and no query would report it missing. **A deleted page is not an available parent**, since
  adopting one would put a live subtree under a page sitting in the recycle bin. Conversely deleted
  *descendants* do move — the subtree query uses `IgnoreQueryFilters`, or restoring one later puts
  it back into a branch that no longer exists.*
  *Refusals are a plain `PageMoveOutcome` enum rather than a diagnostic-carrying result: each is a
  single fact about the tree with nothing further to say, and the caller is the layer that knows how
  to phrase it. Registered by a new `AddCmsPages()`, kept separate from `AddCmsContent()` because
  these services are scoped and the payload engine is singleton.*
- [x] **P2-06** Migration `AddCmsPages` — migration #3. `Up`/`Down` verified in CI. — 1 ed
  *2026-08-14 — reviewed statement by statement: four `CreateTable`s and no drop-plus-add, with the
  three FKs that close the `Page`/`PageVersion` cycle added after both tables exist. `Down` drops
  those first and then the tables in dependency order; both directions are asserted from empty by
  `MigrationsApplyFromEmptyTests`, which now covers three migrations.*

### 2.2 Services — 16 ed

- [x] **P2-07** `PageService` in `Core/Content/` — create from template (produces a draft version with
  an empty, schema-valid payload), read, metadata patch. — 2 ed
  *2026-08-14 — three operations over `IPageService`, plus `Slugs` (generation and the [§10.2]/[§10.3]
  segment rules) and `PageCodes`. The create is one transaction of three statements, because two of
  its values cannot be known until the rows exist: the path contains the page's own id and
  `DraftVersionId` points at a row that points back [§23.5]. It runs under
  `CreateExecutionStrategy()` — Aspire's `EnrichSqlServerDbContext` turns retries on, and a manual
  transaction without one throws the moment a connection blips — and clears the change tracker on
  entry, since a retry re-runs the whole lambda and would otherwise insert the failed attempt's
  entities a second time.*
  ***`StructureResult<T>` / `StructureOutcome` are now `CmsResult<T>` / `CmsOutcome`** in
  `Core` (199 references, a pure token rename). A page service returning a type named for structure
  is a lie, and a second identical carrier beside it would mean a second `CmsProblems` mapping — two
  places to answer "what status is a conflict", which is exactly how a 404 becomes a 400 in one
  corner of the surface. `NotFound`/`Forbidden` gained an optional code so a page refusal reads
  `page.not-found`; the default stays `structure.*` because those codes have shipped and a shipped
  code does not change [§22.2].*
  ***The metadata patch needed a way to say "not supplied", and that is `Patch<T>`*** in
  `Shared/Contracts/Api/`. Binding a PATCH body to plain nullables collapses `{"ownerUserId": null}`
  (clear it) and an omitted member (leave it) into one value, so a client sending only the field it
  changed silently clears every field it did not — the way an editor's SEO settings disappear when
  someone fixes a title. `System.Text.Json` supplies the reading half free, since the converter runs
  only for a member that is present; the writing half needs `WhenWritingDefault` on the member,
  because a converter cannot suppress a property name its parent already wrote.*
  *Four decisions the task's wording did not settle. **A bad `templateId` or `parentId` is `422`, not
  `404`** — the address of the request is the page collection, and answering 404 tells a client the
  endpoint is not there. **The slug is checked against live siblings only**: a full URL is its
  ancestors' slugs joined, so that is the only way two tree-derived URLs collide, and counting
  deleted siblings would hold a URL hostage to the recycle bin. **A non-ASCII slug is a warning, not
  an error** [§10.3], which made the same errors-block-warnings-do-not rule `P1-22` needed apply
  here. And **`OwnerUserId` is checked against the user table** rather than left to the foreign key,
  because a constraint violation reaches the client as a 500 about a database it should not know
  exists.*
  ***One gap recorded rather than papered over.*** Phase 2's schema has no unique index on
  `(ParentId, Slug)`, so two simultaneous creates can both pass the sibling check. There is no
  constraint for a catch block to re-interpret, and inventing one here would duplicate what
  `PageRoute.UrlHash WHERE IsPublished = 1` does properly in `P3-01`. Said so in
  `SiblingSlugExistsAsync`'s remarks. 13 integration tests in `Server.Tests/Content/PageServiceTests`
  and 28 unit tests in `Core.Tests/Content/SlugsTests`; 1099 green across the four suites.*
- [x] **P2-08** `RecycleBinService` in `Core/Content/` — subtree-aware soft delete/restore, route
  retirement, parent-redirect option, permanent-delete guard against live `ContentReference` rows
  [§14.10]. Restore returns a page as a **draft**, never live. — 1.5 ed
  *2026-08-14 — four operations. Delete and restore both walk the subtree by materialized path,
  which is what `P2-05` denormalised it for; a live child under a deleted parent is a page reachable
  by URL and invisible in the tree, so per-page delete is not an option. **Restore returns drafts** —
  `PublishedVersionId` is left null on every page it touches, so nothing reappears publicly that
  nobody has looked at since it was deleted.*
  ***Two halves of the task's wording cannot be built yet, and the note is the deliverable.***
  "Route retirement" and "parent-redirect option" both need `PageRoute` and `Redirect`, which arrive
  in `P3-01`. Until then a URL is derived from the tree at request time and the query filter already
  makes a deleted page unreachable, so clearing `PublishedVersionId` is the whole of what
  "unpublished" means. Said so at both call sites rather than left for someone to discover.*
  ***The permanent delete is the one irreversible operation in the system, and it is gated three
  ways.*** It needs `Users.Manage` rather than `Content.Delete` — an editor who can empty the bin
  can destroy history — it refuses a page that is not already in the bin, and it refuses while any
  `ContentReference` row targets it, **naming the pages in the way**. A count is not something an
  editor can act on. Because `P1-16` over-reports by design, that refusal is occasionally cautious;
  that is the right direction to be wrong in when the alternative cannot be undone. The delete
  itself runs in a transaction that nulls both version pointers first — `Page` and `PageVersion`
  reference each other and both directions are `Restrict`, so nothing can be removed while either
  pointer is set.*
  *One rule beyond the spec's wording: **a restored page whose former parent is still deleted comes
  back at the root**, with a warning, and its descendants' paths are rewritten to match. [§14.10]
  asks for the root fallback; what it does not say is that leaving the stored path pointing through
  a deleted ancestor produces a subtree queries silently stop finding, which is the failure
  `P2-05` exists to prevent.*
- [x] **P2-09** `DuplicationService` in `Core/Content/` — shallow and deep duplication with
  intra-subtree link rewriting; media referenced, never copied; copy starts at version 1 [§14.12].
  — 1.5 ed
  *2026-08-14 — all three rules hold, and the interesting one needed a contract change.*
  ***Link rewriting forced `IFieldType.RemapReferences`***, the mirror of `ExtractReferences`, with
  `FieldTypeBase` defaulting to "nothing to rewrite" and the six reference-bearing types overriding
  it. The alternatives were both worse: rewriting ids by walking the payload for numbers that happen
  to match would eventually rewrite a `number` field, and rewriting by the path `ExtractReferences`
  reports does not work at all — a single `pageReference` reports a null path pointing at the
  property object, while a multiple one reports `value[1]` pointing at the id, so the two shapes
  need different surgery. The failure being prevented is specific: a field type that reports a
  reference and does not rewrite it makes a duplicated section keep pointing at the originals, which
  reads as working until somebody edits the copy and watches the original change.
  `ReferenceExtractionContractTests` now fails any field type claiming `ReferenceBearing` with no
  remap, including the nested case for containers — the same friction the extraction half already
  had.*
  ***The copy is written in two passes and it has to be.*** A link inside the subtree can only be
  pointed at its copy once every page in the set has an identity, and identities are assigned on
  insert; a single pass would be guessing at ids it was about to create. Pass one inserts in depth
  order so a parent always exists before its children, pass two remaps and reprojects. All of it in
  one transaction.
  *Four smaller decisions. **Links out of the subtree are left alone** — that is the rule that makes
  "duplicate last year's campaign" produce a section navigating to itself rather than back into last
  year, and it falls out of the map lookup missing. **`ExplicitUrl` is dropped**, because an explicit
  URL is unique by construction and copying one gives two pages the same address at the copy's first
  publish. **Only the root gets a free slug and the "(copy)" suffix**; descendants land under a new
  parent, so nothing they could collide with is in scope. And the search for a free slug is bounded
  at a hundred attempts — a loop that cannot terminate is a request that never returns.*
- [x] **P2-10** `DraftService` in `Core/Content/` — load, save (payload + `rowversion` concurrency),
  discard (reset to published), named checkpoint [§11.3]. — 2 ed
  *2026-08-14 — four operations, and **every write mutates the draft row in place**. That is what
  makes `P2 #2` structural rather than a rule somebody has to remember: nothing in this service can
  reach the published row, so an autosave every twenty seconds cannot bury the history an editor
  reads. The one exception is a checkpoint, which inserts an `Archived` row deliberately.*
  ***`ExpectedRowVersion` is applied as EF's `OriginalValue`, not compared in code.*** Comparing
  would check against the value *this request* just read, leaving the window between its read and
  its write unguarded — which is exactly the window two editors saving at once occupy. Setting the
  original value moves the check into the `UPDATE` predicate, where the database arbitrates.
  ***A conflict hands back the stored draft***, which needed `CmsResult.Conflict` to accept a value.
  [§11.8] wants the editor to offer keep-mine / take-theirs / open-diff, and a second round trip to
  fetch the winner would race the same way. The change tracker is cleared before reloading it, or
  the "theirs" copy would be the losing editor's own text read back out of the tracker.*
  ***The envelope is a privilege boundary, not data.*** A payload declares which template and which
  revision judge it, so a client free to name either can pick rules its content happens to satisfy.
  `templateKey` must match the page's template; `templateRevision` must be the draft's own or the
  template's current one — the second being how an editor adopts a structural change [§8.5] — and
  anything else is refused. Without this the draft endpoint changes the content model of a live page,
  which is the [§20.1] mass-assignment hole one level deeper.
  *Two more. **Discard copies field by field rather than repointing `DraftVersionId` at the
  published row**: a draft that *is* the published row would be mutable the moment somebody typed
  into it, which is the one thing the whole model exists to prevent. And **reference rows are
  rewritten on every draft save**, not only on publish, because where-used and the delete guards are
  only as good as the last projection.*
- [x] **P2-11** `PublishingService` in `Core/Publishing/` — validate → snapshot draft into a new
  immutable version → archive the previous published version → repoint `Page.PublishedVersionId` →
  reindex `ContentReference` → enqueue invalidation, **all in one transaction** [§5.5]. — 3 ed
  *2026-08-14 — publish, unpublish, and a dry-run validate that **runs the identical check path**,
  because a separate implementation eventually disagrees and the direction it disagrees in is a
  green check followed by a refused publish.*
  ***The commit is four explicit `SaveChangesAsync` calls inside one transaction, and keeping them
  apart is deliberate.*** Batching them would be marginally faster and would make the failure modes
  indistinguishable; separate steps are what let `P2-12` force a failure at each one. Step 4 is the
  cache-invalidation outbox row [§5.5] — `OutboxMessage` arrives in P8, and until then delivery
  reads through no cache, so the step is kept as a position in the sequence so that adding it later
  is an insertion rather than a restructuring. The whole thing runs under `CreateExecutionStrategy()`
  for the reason `P2-07` gives.*
  ***Validation adds two things the payload walk cannot see.*** Referenced pages are checked to
  still exist — a link to a deleted page is an **error**, because publishing it puts a dead link on
  the site — while a link to a page that merely is not live yet is a **warning**, since publishing a
  section top-down is ordinary work and refusing it would make a landing page unpublishable before
  everything it links to. And a disabled template blocks a publish. Media and reusable content are
  checked the same way once those tables exist; their references are already extracted, so it is two
  more queries rather than a new mechanism.
  *`acknowledgeWarnings` defaults to **false**, so an unattended caller cannot publish past a
  warning a person would have looked at [§14.6]. Unpublish archives the live version and leaves the
  draft alone; re-publishing is an ordinary publish rather than an undo.*
- [x] **P2-12** Fault-injection tests forcing a mid-transaction failure at each step of `PublishingService`,
  asserting all-or-nothing *(mitigates R4 — stop-the-line severity)*. — included above
  *2026-08-14 — `Content/PublishTransactionTests`, one case per step plus a control. The fault is
  injected through an **EF `SaveChangesInterceptor` that throws on the nth save**, not through a seam
  in the service: a production hook existing only for a test is a hook a deployment can trip over,
  and the interceptor also proves more, because it fails at the real database boundary inside the
  real transaction. Each case snapshots the page's pointers, every version row, and the reference
  count, forces the failure, and asserts the snapshot is unchanged.*
  ***The control case is the part worth keeping.*** Without a successful publish asserting all four
  steps applied, an interceptor that broke publishing outright would make every roll-back assertion
  pass for the wrong reason. The arrange half also publishes once first, so the failing publish has
  a previous version to archive and a pointer to move — failing on a page that was never live would
  never exercise step 2 at all.*
  *One wiring note for whoever adds the next interceptor: EF resolves interceptors from the options
  the context was **built** with, so registering one in DI after `AddDbContext` does nothing. The
  workbench re-registers the context with the interceptor attached.*
- [x] **P2-13** `VersionService` in `Core/Publishing/` — history, fetch one version, restore-into-draft
  (copy, never resurrect [§11.5]), retention pruning policy [§11.7]. — 2 ed
  *2026-08-14 — history, read, restore, and the retention sweep. **Restore copies into the draft's
  own row**, which keeps its identity, its number, and its row version; the published version is not
  touched, so the timeline stays forward-moving and the history never gains a cycle (`P2 #7`).
  Restoring the draft onto itself is refused rather than silently doing nothing.*
  ***History reads through `IgnoreQueryFilters` on the page.*** The recycle bin lists the history of
  a deleted page, and the soft-delete filter would hide exactly the rows a restore has to show. This
  is the case `P2-03` deliberately left `PageVersion` unfiltered for.
  ***The retention policy is five clauses and every one protects something an editor would be upset
  to lose***: everything inside the window, the last twenty versions per page, every version that was
  ever published, every named checkpoint, and everything belonging to a page in the recycle bin —
  because a restore that came back with no history is not a restore. The window comes from
  `SiteSettings.VersionRetentionDays`, falling back to ninety. Reference rows are deleted with the
  versions they belong to: a row pointing at a version that no longer exists makes every delete
  guard permanently cautious about a page nothing actually references.*
  *The clock is injected as a `TimeProvider` so the sweep is testable without waiting ninety days;
  the same registration makes edit-lock expiry testable.*
- [x] **P2-14** `ContentDiffService` in `Core/Publishing/` — structural diff with GUID-based block
  matching (reports *moved*, not removed+added), word-level text diff, target-identity diff for
  media/link/reference fields, flat metadata diff [§11.4]. Computed on demand, **never in the publish
  path**. — 3 ed
  *2026-08-14 — all four kinds of comparison, plus `WordDiff` as its own type. **Blocks are matched
  on the stable GUID the `blocks` field type writes**, which is what turns a drag-and-drop reorder
  into one `Moved` entry instead of a wall of removals and additions — the edit people make most,
  and the one a positional comparison is useless on (`P2 #6`).*
  ***Values are rendered by the field type that wrote them***, dispatched on the stored `type`
  discriminator, which keeps this service from knowing any field type's shape. Text comes from
  `ExtractSearchText`; a reference-bearing value renders as the identities it points at instead,
  because "Media 12 → Media 15" *is* the change and the alt text beside it is not. The human labels
  those ids resolve to arrive with the media library in P5.
  ***`WordDiff` is words, not characters***, because the reader is a person checking a paragraph and
  a character diff of a rewritten sentence is a cloud of single letters. Common prefix and suffix are
  stripped before the quadratic step, separators are kept attached to their words so the segments
  reassemble into the original text, and beyond ten thousand words it degrades to one removal plus
  one addition — still correct, and not a request thread tied up on a pasted book. Its unit tests
  assert reassembly on every case, since a diff that renders nicely and has dropped a word gives the
  reader no way to tell.
  *Two smaller decisions. **Metadata is hand-listed rather than reflected over the entity** — a
  reflected walk sweeps in the row version and the audit stamps, which differ between two versions by
  definition, and a diff in which everything always changed says nothing. And **values are compared
  as canonicalised JSON**, because member order inside a stored value is not meaningful; only zone
  order is, and `ContentPayloadBuilder` already preserves that.*
- [x] **P2-15** `EditLockService` in `Core/Content/` — acquire on editor open, 30 s heartbeat, override,
  2-minute expiry reaper. **A lock never blocks editing** [§11.8, D12]. — 1 ed
  *2026-08-14 — acquire, read, release, reap. **Nothing here refuses anything.** Acquiring a page
  somebody else holds succeeds and reports who held it; the caller decides whether to warn and the
  editor decides whether to carry on. The test asserts the write itself still goes through, which is
  the property the whole design turns on — locks that block are locks that get stuck, and a closed
  laptop on a Friday would otherwise take a page out of circulation until somebody with database
  access noticed [D12].*
  *Three details. **Expiry is enforced on read as well as by the reaper**, so a stale row can never
  be shown as live just because nothing swept the table in the last few seconds — and the two use the
  same inclusive boundary, which a test caught: a strict comparison in the reaper left a lock that
  read as expired and was never collected. **A heartbeat leaves `AcquiredOn` alone**, so "opened at
  09:14" keeps meaning what it says over a three-hour session. And **releasing somebody else's lock
  does nothing and is not an error**: the ordinary way to reach it is an editor closing a tab they
  had already been taken over from, and a failure would put an alarming message in front of the
  wrong person.*
  *The holder's navigation is deliberately not loaded on the acquire path — a take-over reassigns
  `UserId`, and EF's relationship fixup would put the old holder's key straight back from the loaded
  navigation.*

### 2.3 API and UI — 4.5 ed

- [x] **P2-16** Page endpoints in `Server/Api/Cms/Pages/` per [§22.1]: `GET /pages`, `GET /pages/tree`,
  `POST /pages`, `GET /pages/{id}`, `PUT /pages/{id}/draft`, `PATCH /pages/{id}/metadata`. — 1 ed
  *2026-08-14 — those six plus three the table does not list. `GET /pages/{id}/draft` is the read pair
  of the `PUT` and is what stamps the `ETag` the `PUT` requires; `POST /pages/{id}/draft/discard` and
  `/draft/checkpoint` are the other two operations `P2-10` built. Without them `DraftService.Discard`
  and `.Checkpoint` would ship dead — [§22.1]'s table is not exhaustive for drafts, and a service
  method no endpoint reaches is a service method nobody is running.*
  ***Two of the six needed a query service that did not exist.*** `IPageService` gained `ListAsync`
  and `TreeAsync`. The list is **ordered by identity**, which is a real constraint rather than a
  preference: keyset pagination needs a total order over a unique indexed column and the primary key
  is the only one Phase 2's schema offers. Ordering by "recently changed", which a browse screen
  would rather have, needs a composite `(ModifiedOn, Id)` keyset over an unindexed column, so every
  page of results would scan; when a screen earns that, the index comes with it. The tree is answered
  from the materialized path in one prefix match with **one extra query for the has-children flags** —
  a correlated subquery per row would make a fifty-page list fifty-one round trips, and the flag is
  what tells a node the fetch stopped at from a leaf.
  ***`rootOnly` is a separate flag from `parentId`.*** A null `parentId` already means "do not filter
  by parent", and one nullable value cannot also mean "at the root of the site".
  ***A filter that cannot be read is refused, not ignored*** (`PageCodes.FilterInvalid`). A dropped
  filter answers with a superset of what was asked for and the caller cannot tell that from an honest
  answer — an unrecognised status reads as "every page is a draft", and a malformed cursor turns a
  paging bug into a loop over the first page. The `tag` filter [§22.1] lists is deliberately **absent**
  rather than stubbed: the tag projection arrives in P8, and a filter that silently matched nothing
  would read as "no pages are tagged".*
- [x] **P2-17** Lifecycle endpoints: `POST /pages/{id}/duplicate`, `DELETE /pages/{id}`,
  `POST /pages/{id}/restore`, `POST /pages/{id}/validate`, `POST /pages/{id}/publish`,
  `POST /pages/{id}/unpublish`. — 0.75 ed
  *2026-08-14 — those six plus `GET /pages/recycle-bin` and `DELETE /pages/{id}/permanent`, for the
  reason the draft additions above give: `RecycleBinService.ListAsync` and `.PurgeAsync` exist and had
  no way in. The permanent delete keeps its `Users.Manage` gate — an editor who can empty the bin can
  destroy history — so it is the one endpoint in the CMS API behind a permission no content role
  holds.*
  *Three response shapes rather than `204`s, each because the operation has something the screen
  cannot otherwise learn: a delete answers with the affected subtree (**how many pages went, and how
  many were live** — the number a confirmation dialog has to show), an unpublish names the version it
  retired, and a purge reports how many version rows it destroyed. `UnpublishResult` and `PurgeResult`
  are named records, not anonymous objects: a wire contract nobody can reference is one no client can
  be written against.*
  ***`PublishPageRequest` is a flag, not [§22.2]'s array of acknowledged codes.*** Listing codes looks
  more precise and is not — the set of warnings can change between the check and the publish, so a
  client would be acknowledging a list it may no longer be looking at. The honest question is whether
  a person saw the warnings *this attempt* produced, and a boolean asks exactly that.
  ***`validate` requires `Content.Read`, not `Content.Publish`.*** It is a dry run that changes
  nothing, and an Author who cannot publish still needs to know whether their page would.*
- [x] **P2-18** Version endpoints: `GET /versions`, `GET /versions/{vid}`,
  `GET /versions/{a}/diff/{b}`, `POST /versions/{vid}/restore`. — 0.5 ed
  *2026-08-14 — nested under the page for the reason zones are nested under their template: the pair
  is the address, so a version of another page answers **404** rather than confirming the existence of
  a row the caller did not ask about. **The history is deliberately unpaged** — `P2-13`'s retention
  caps it at the last twenty plus what the policy protects, so it is bounded by construction and a
  cursor would be ceremony over a set that fits on one screen.*
  ***The diff is a `GET`*** even though it computes rather than reads: no side effects, idempotent,
  and its whole input is two ids, so it is bookmarkable and cacheable. Cost is bounded by `WordDiff`
  degrading past ten thousand words, not by the verb.
  ***Restore answers with the draft's new `ETag`.*** A restore rewrites the draft, so the token the
  editor was holding is stale the instant it returns; without this the next save would be refused as
  a conflict with itself.*
- [x] **P2-19** `POST`/`DELETE /pages/{id}/lock`. — 0.25 ed
  *2026-08-14 — three, with `GET` beside them. **The acquire and the heartbeat are the same `POST`**,
  so the editor sends one request every thirty seconds whether it is opening the page or still on it;
  a separate heartbeat verb would be a second code path exercised four times more often than the
  first. An unheld page answers **204, not 404** — the question "who has this open" has been answered
  and the answer is nobody, and a 404 would be indistinguishable from the page not existing.*
  *Nothing here can refuse anything on the grounds of a lock, and the API test asserts the property
  the design turns on rather than the mechanism: the second editor's **save still goes through** [D12].*
- [x] **P2-20** Cross-cutting API concerns: `ETag`/`If-Match` optimistic concurrency, RFC 9457
  problem-details error contract with the `errors`/`warnings` shape from [§22.2], antiforgery on all
  writes, cursor pagination on collections. — 1 ed
  *2026-08-14 — the problem-details half already existed (`CmsProblems`, `P1-21`); this added the
  other three.*
  ***`If-Match` is mandatory on the draft save and optional on the metadata patch.*** An unconditional
  draft save is a lost update waiting for two editors, so a missing precondition is **428 Precondition
  Required** rather than an accepted write — 428 exists precisely so a server can insist. A patch names
  the members it changes, so two concurrent patches to different fields merge rather than collide, and
  insisting there would make "clear the review date" fail for a client that never read the page.
  Honouring it anyway meant `PatchMetadataAsync` gaining an `expectedRowVersion`, and the row-version
  handling moving out of `DraftService` into a shared `RowVersions` — the two must not drift on the
  one rule that matters, which is that the token is applied as EF's **original value** and never
  compared in code.
  ***A mismatch answers 409, not 412.*** A deliberate departure from the letter of RFC 9110: a 412 is
  conventionally bodiless, and the losing editor needs the winning draft in hand to be offered
  keep-mine / take-theirs / open-diff [§11.8]. Recorded in `ETags`.
  ***Cursor pagination carries no total count.*** A keyset query knows where it is, not how long the
  collection is, and answering with a count means a second full scan per request — the exact cost
  cursor pagination exists to avoid. `Cursor` is Base64Url over the last key rather than a bare
  number, so widening to a composite keyset later is a change to one class rather than to every
  bookmarked URL; it is explicitly **not** a security boundary, since a decoded cursor is only ever a
  `WHERE Id > @cursor` bound inside a query that already applied the caller's permissions.
  ***Antiforgery gained a marker so it can be audited.*** `AddEndpointFilter` leaves no trace in
  `Endpoint.Metadata`, which made "is every write protected?" a question nothing could answer by
  inspection — and that is precisely the question worth answering automatically, because this defence
  fails by a new endpoint simply forgetting it. `RequireCmsAntiforgery()` adds the filter and a
  `CmsAntiforgeryMetadata` marker together; the structure endpoints were moved onto it too.*
- [x] **P2-21** Authorization policies and permission constants in `Server/Authorization/`
  (`Content.Read/Edit/Publish/Delete`, `Structure.Edit`, `Settings.Edit`) — global roles only; section
  ACLs land in P7. — 1 ed
  *2026-08-14 — **the code for this shipped in `P1-21`**, which needed the same seam a phase early:
  `CmsPermissions`, `CmsPermissionMap` ([§21.1] transcribed once), `CmsAuthorizationExtensions`, and
  the request-scoped `HttpCmsAuthorization` the services ask. What Phase 2 owed was the part that
  keeps it true, and that is what landed here — `Api/Cms/ApiContractTests` asserts over the route
  table the application actually builds that **every permission has a policy, every policy has a
  permission, every CMS endpoint carries a named policy**, and that the Phase 2 grants match [§21.1]
  (an Author edits and cannot publish; an Approver publishes and cannot delete). The one exemption is
  `/antiforgery-token`, and the test names it rather than skipping it.*
  ***One thing the backoffice forced.*** The screens run in WebAssembly, where the server's policies
  do not exist, so `[Authorize(Roles = …)]` is all they can say. `CmsRoles` gained `ContentReaders` /
  `ContentEditors` / `ContentPublishers` beside the existing `StructureEditors` — convenient and
  hazardous in equal measure, since a role added to the map and not to the list gets a blank screen
  instead of the page it is entitled to and nothing else would notice. A contract test now asserts
  each list equals the map's roles for the permission it stands in for.*
- [x] **P2-22** Explicit DTOs on every write endpoint so a client cannot mass-assign `Status:
  "Published"`; status transitions only via dedicated endpoints [§20.1]. — included above
  *2026-08-14 — every write binds a record from `Shared.Contracts`, and `AcquireLockRequest`,
  `PublishPageRequest`, and `CheckpointDraftRequest` were added rather than accepting a bare value or
  a query parameter for something that will grow members.*
  ***The rule is enforced structurally, not by review.*** `NoWriteEndpointBindsATypeThatCouldMoveAPagesLifecycle`
  reflects over the route table: for every `POST`/`PUT`/`PATCH`/`DELETE` under `/api/cms`, the
  body-bound parameter must be a request contract, and it must declare no member named `Status`,
  `IsDeleted`, `PublishedVersionId`, `DraftVersionId`, `VersionNumber`, `Path`, `Depth`,
  `CurrentRevision`, or the audit stamps. Which parameter is body-bound is settled by **asking the
  container** rather than by matching on namespace — minimal APIs infer it the same way, and a rule
  that guessed would stop covering a handler the day somebody injects a concrete service.
  *Status transitions are reachable only through publish, unpublish, delete, and restore, each with
  its own permission and its own transaction. A DTO accepting one as data would route around all four.*
- [x] **P2-23** Plain admin screens in `Client/Components/Admin/Pages/`: page list,
  create-from-template, generic zone form, version history, diff viewer. — 1 ed
  *2026-08-14 — four components over a new `IPageClient`, implemented twice per the pre-rendering
  pattern (`HttpPageClient` in the browser, `ServerPageClient` over the services during pre-render),
  exactly as `IStructureClient` is. Reads return bare values and writes return
  `StructureClientResult<T>`, and publishing is the case that makes the asymmetry earn its keep: an
  unfilled required zone has to come back as a list of zones to go and fill in, not as a red banner
  saying 422.*
  ***The zone form is built from the revision the draft captured, never from the template's current
  zones*** [§8.5]. That needed a contract — `CapturedSlot`, with a forgiving reader over the snapshot
  array — because `ZoneDefinition` describes the template *now* and carries database identities a
  revision does not have. A form built from the live definitions would show a control for a zone the
  page has no value for and silently drop one it does, which is authoring against a schema the content
  is not being judged by.
  ***Every zone is a textarea, and the non-text field types are read-only.*** The per-field-type
  editors arrive in P6 behind [ADR-0014](./docs/adr/0014-field-type-components-resolved-by-the-hosting-layer.md);
  inventing a control for a media reference here would mean inventing a shape for its value, and P6's
  first job would be repairing what this one wrote. A value the screen cannot edit is shown as stored
  JSON and **written back verbatim**, so a save made for some other zone cannot damage it.
  ***An emptied control removes the zone rather than storing null.*** Absent means never authored and
  null means deliberately cleared, and `P1-14` kept them apart on purpose; writing null for a box
  nobody filled in would tell the renderer a fallback was declined. Rich text keeps its stored
  `format`, so a save from this screen cannot silently reinterpret an author's HTML as markdown.
  *The publish button relabels itself to "Publish anyway" **only after** a refused attempt has shown
  the warnings, which is [§22.2]'s resubmit-to-acknowledge as one visible decision rather than a
  checkbox nobody reads. The diff viewer renders a `Moved` block as one row saying where it went, and
  uses `<del>`/`<ins>` so the distinction survives for a screen reader and in monochrome —
  `PageScreenAccessibilityTests` asserts both, and puts all three screens under the same axe gate
  `P1-31` built (3 screens + the diff, 0 violations).*

### Tests — Phase 2

- [x] **P2-24** Unit: draft save concurrency, version numbering, retention policy selection.
  *2026-08-14 — three suites, and **two of the three rules had to be lifted out of the service that
  ran them before they could be tested at all**, which is the substance of this task rather than a
  side effect of it. `RetentionPolicy` now holds the [§11.7] clauses as a pure decision returning
  **why** a version was spared (`RetentionReason`), and `VersionNumbers` holds "the highest ever
  issued plus one" — previously duplicated verbatim in `DraftService` and `PublishingService`, which
  is two chances to write "the count plus one" and reissue a number the moment history is pruned.
  `VersionService.PruneAsync` and both mint sites now call them.*
  ***Carrying the reason is what makes the retention clauses assertable one at a time.*** Arranging a
  version protected by exactly one clause and no other takes ninety days of history per case, so the
  database suite can only ever show that the sweep as a whole kept the right rows — not that it kept
  them for the right reason, which is the difference between a policy and a coincidence.
  `RetentionPolicyTests` includes the control the integration suite cannot state either: an ordinary
  old version **is** prunable, without which every other case passes for a sweep that deletes
  nothing. Three clauses caught something worth having: a superseded publish is `Archived` and keeps
  its `PublishedOn`, so reading only the status would prune the entire published history of any page
  published twice; a `CreatedOn` of null means the row's age is unknown, which fails safe; and a
  label of blanks is not a name.*
  *`VersionNumbersTests` pins the join between the two rules — **retention never prunes the newest
  version**, which is the only reason numbering may read `MAX` rather than keep a monotonic counter.
  Both halves of that are asserted, in the file that would break if either moved.*
  ***Draft-save concurrency is `RowVersionsTests`, and it needs no database on purpose.*** The race
  itself is arbitrated by SQL Server and is already asserted there (`PageSchemaTests`,
  `PageApiTests`). What those cannot show is the decision made before any statement is sent: that a
  **malformed** token is refused rather than quietly treated as no precondition, since both spellings
  of that bug produce a successful save on an uncontended row. Also pinned: the token is applied as
  the entry's *original* value and never compared in code, an over-long token is refused rather than
  truncated to its first eight bytes (which would have matched), and absent stays distinguishable
  from unreadable — the draft save insists on one and the metadata patch does not.*
- [x] **P2-25** Unit: diff algorithm — reorder, insert, delete, nested block change.
  *2026-08-14 — the comparison moved into `Core/Publishing/PayloadDiff.cs`, leaving
  `ContentDiffService` with what actually needs a database: the permission check, the two rows, and
  the metadata list. `PayloadDiffTests` drives the algorithm over two payload documents with no page,
  no template, and no publish — 13 cases for what would otherwise be 13 containers spent to reach the
  same method.*
  *All four named cases, and each is asserted at the thing that distinguishes it from its neighbour:
  a rotation is three `Moved` entries carrying before and after indexes; an insertion in the middle
  is one `Added` **plus a `Moved` for what it pushed down and silence for what it did not touch**; a
  deletion is one `Removed` at the index it held and explicitly not a removal-plus-addition; and a
  change inside a block reports that block alone, word-segmented, with its siblings quiet.*
  *Four cases beyond the task's list, all of them things the integration suite has no cheap way to
  arrange. **A block that both moved and changed reports `Changed`** — hiding an edit behind a
  reorder is the wrong way round to be wrong. **Member order is not a change**, so a save that
  happened to re-serialise a block does not read to an editor as an edit. **A cleared zone is
  `Changed` and an absent one is `Removed`**, which is [§6.2]'s absent-vs-null surviving into the
  diff. And **a value whose field type this build no longer has still diffs**, as its raw document —
  reporting "no change" there is the worst of the three available answers.*
  *One behaviour this documented rather than changed: two blocks sharing an id produce a `Changed`
  zone with an **empty** block list, because the second occurrence is dropped and the first is
  identical. It is a malformed payload the `blocks` field type already reports on, and the diff's job
  is to still render — the editor is looking at it to work out what broke.*
- [x] **P2-26** Data integration: filtered unique indexes behave; query filters exclude deleted rows;
  `rowversion` conflicts surface as `DbUpdateConcurrencyException`.
  *2026-08-14 — `Data.Tests/Cms/PageSchemaTests`, eight cases against real SQL Server: the creating
  transaction leaves the page and its draft consistent, a duplicate version number and a duplicate
  `PublicId` are refused, a stray `Remove` retires the page and keeps its history, the query filter
  hides it while `IgnoreQueryFilters` and the version table still find it, a stale draft save raises
  `DbUpdateConcurrencyException`, and neither `EditLock` nor `ContentReference` reaches the audit
  log. Plus `Server.Tests/Content/PageTreeServiceTests` for the six tree rules.*
  *One wording note: **there is no filtered unique index in Phase 2's schema**, so what is asserted
  here is the filtered tree index's predicate (read back from `sys.indexes`) beside the plain unique
  ones. The filtered-unique case the criterion has in mind arrives with `PageRoute.UrlHash WHERE
  IsPublished = 1` in `P3-01`.*
- [x] **P2-27** API integration: authorization, validation, and concurrency behavior for every endpoint.
  *2026-08-14 — closed by `2.3`'s own definition of done rather than as separate work. Three suites in
  `Server.Tests/Api/Cms/`: `PageApiTests` (17), `PageLifecycleApiTests` (9), `PageVersionApiTests` (8),
  plus `ApiContractTests` (6) over the route table. **Every fixture is built through the API** — a
  test whose arrange step inserts its own template and page proves nothing about whether the endpoints
  an editor uses can produce that state, and the arrange step is exactly what would keep passing after
  an endpoint broke.*
  *Authorization is asserted per role and not only per endpoint: a Viewer reads and cannot write, an
  Author edits and cannot publish or delete, an Editor empties the bin and cannot purge it, an
  anonymous caller gets **401 and not 403**. Concurrency is asserted at all three of its statuses —
  428 for a draft save with no precondition, 409 for one that lost a race, 409 for a metadata patch
  with a stale `If-Match`.*
- [x] **P2-28** API integration: publish transactionality under fault injection.
  *2026-08-14 — `Api/Cms/PublishFaultInjectionApiTests`, the same four steps `P2-12` forces but
  driven through `POST /pages/{id}/publish`. **Not a duplicate of the service-layer suite**: the
  endpoint runs in ASP.NET Core's request scope, with its own `ApplicationDbContext`, the audit
  interceptor's saves, and a connection the pipeline disposes on the way out — a transaction that
  rolls back correctly when a test calls the service can still be committed in halves by a request
  that ends differently.*
  ***What the client sees is asserted alongside what the database keeps.*** A failed publish answers
  **500**, not a 2xx and not a problem-details refusal: this is not the editor being told the page is
  unfit to publish, it is the server failing to do what it was asked, and a 2xx over a rolled-back
  transaction produces a page everybody believes is live. The before-and-after state is read **back
  through the API** — the page, its version list, and its draft — because "nothing changed" has to
  mean nothing a client can see, and a row that rolled back while a response still carries a stale
  pointer is a broken page either way. The successful control case is here too, for the reason
  `P2-12` needed one: an interceptor that broke publishing outright would make every roll-back
  assertion pass for the wrong reason.*
- [x] **P2-29** Telemetry: `cms.publish.count` / `.duration` metrics and publish trace spans [§24.1].
  *2026-08-14 — `Core/Telemetry/`: `CmsTelemetry` holds the names ([§24.1]'s meter, the activity
  source, the `cms.publish` span) and `CmsMetrics` the two instruments, built from `IMeterFactory`
  rather than a bare `Meter` so they are scoped to the container. `PublishingService.PublishAsync`
  is now a wrapper over `PublishCoreAsync` that starts the span, times the attempt, and records both
  instruments with one `result` tag. Registered by `AddCmsPages()`, and — the half that is easy to
  forget — **listed with OpenTelemetry in `Program.cs`**, since an unregistered meter records
  measurements no exporter ever collects, which is indistinguishable from code that was never
  instrumented.*
  ***The measurement is taken in a `finally`, and that is the case worth having.*** A publish that
  threw is counted as `failed` rather than not counted at all: an operation that vanishes from its
  own counter when it breaks is worse than no counter, because the graph stays flat and healthy while
  publishing is down — risk R4 arriving invisibly. `refused` is kept distinct from `failed` for the
  same reason in reverse; an editor being told their required zone is empty is not an incident, and
  tagging it as one buries the real thing under ordinary editing noise. The tag values are a closed
  set (`published` / `refused` / `forbidden` / `not-found` / `failed`), never an exception message,
  which is how a metrics bill and a collector both fall over.*
  *`Content/PublishTelemetryTests` asserts all five outcomes and both instruments through **real
  publishes**, since what can go wrong is not the counter but the counter never being reached — an
  early return that skips it, a name that drifted from the one the dashboard queries — and every one
  of those passes a test that records a measurement by hand. Measurements are filtered by the meter's
  `Scope`, so one host cannot count another's. The span case pins the tags and that a refusal sets
  `ActivityStatusCode.Error`: a trace is read to find out why a request did not do what was asked.
  One trap found in writing it — `Activity.Tags` enumerates only string-valued tags, so the integer
  page id and version number are invisible there and must be read through `GetTagItem`.*

### Acceptance criteria — Phase 2

- [x] **P2 #1** Creating a page from a template produces a draft version with an empty, schema-valid
  payload.
  *2026-08-14 — `Content/PageServiceTests`. The payload is checked through the real
  `ContentSchemaValidator` against a catalog built from the template's own revision, with the zone
  marked **required** — the case that must still save, since a required zone blocks only a publish.*
- [x] **P2 #2** Saving the draft mutates the draft version in place and creates no new version row.
  *2026-08-14 — `Content/DraftAndPublishTests`, asserted by counting the rows rather than by
  inspecting the one that came back.*
- [x] **P2 #3** Publishing creates a new immutable version, archives the previous published version, and
  repoints `Page.PublishedVersionId` — all or nothing under a forced mid-transaction failure.
  *2026-08-14 — `Content/PublishTransactionTests`, one case per step of the transaction plus a
  control that asserts a successful publish applied all four. See `P2-12` for why the fault is
  injected through an EF interceptor rather than a seam in the service.*
- [x] **P2 #4** **After publishing, editing the draft leaves the published version byte-for-byte
  unchanged.** *(The requirement's central promise — R-10.)*
  *2026-08-14 — `DraftAndPublishTests.PublishingSnapshotsTheDraftAndLeavesItByteForByteAloneAfterwards`.
  Literal: the published row's `ContentJson` and `RowVersion` are captured, the draft is saved three
  more times, and both are compared again. The row version is part of the assertion on purpose — a
  row whose concurrency token moved was written to, whatever it now says.*
- [x] **P2 #5** Version history lists every version with status, author, and timestamp.
  *2026-08-14 — `Content/VersionAndDiffTests`, newest first, with `IsDraft` and `IsPublished`
  resolved against the page's two pointers rather than inferred from the status column.*
- [x] **P2 #6** The diff between two versions reports a reordered block as *moved*, not as
  removed-plus-added.
  *2026-08-14 — three blocks rotated, and every one of them comes back `Moved` with its before and
  after index. Matched on the stable GUID, which is the whole reason the `blocks` field type writes
  one.*
- [x] **P2 #7** Restoring an old version copies it into the draft and leaves the published version
  untouched.
  *2026-08-14 — the published row's bytes, status, and the page's `PublishedVersionId` are all
  asserted unchanged after the restore.*
- [x] **P2 #8** Two concurrent draft saves: the second receives `409 Conflict` with both payloads.
  *2026-08-14 — the behaviour was asserted at the service layer (the second save returns
  `CmsOutcome.Conflict` carrying the stored draft, so the editor has both copies). **The literal `409`
  is now asserted too**, by `Api/Cms/PageApiTests.TwoConcurrentDraftSavesGiveTheSecondA409CarryingBothPayloads`:
  two saves send the same `If-Match`, the first wins, the second comes back 409 with
  `page.concurrent-change`, and the stored draft is re-read to confirm the winner's text is what
  survived. 409 rather than the protocol's 412 — see `P2-20` for why.*
- [x] **P2 #9** An advisory lock is visible to a second editor and can be overridden; it expires after 2
  minutes of silence.
  *2026-08-14 — `Content/EditLockTests`, all three, on an injected clock. The suite also asserts the
  rule the criterion does not name: the second editor's save goes through regardless, because a lock
  never blocks editing [D12].*
- [x] **P2 #10** Soft-deleting a page hides it from default queries while keeping full history
  retrievable.
  *2026-08-14 — `Content/RecycleBinAndDuplicationTests`: the subtree disappears from the default
  query, is still there under `IgnoreQueryFilters`, and its version history still lists.*
- [x] **P2 #11** Publishing with an unfilled required zone returns `422` naming that zone.
  *2026-08-14 — `Api/Cms/PageLifecycleApiTests.PublishingWithAnUnfilledRequiredZoneAnswers422NamingThatZone`.
  The assertion is on the diagnostic's **path containing the zone key**, not merely on the count: an
  editor has to be told which zone to go and fill in, and "3 errors prevent publishing" is not
  something anyone can act on.*

**Exit gate:** acceptance test **#4** passes — the requirement's central promise is mechanically
verified. — [x] met on **2026-08-14**.
*The gate is the one criterion that could not be faked by careful implementation, and it passes:
publishing snapshots the draft into a separate row, and no operation in `DraftService` can reach it.
**All eleven criteria are now `[x]`** — `P2 #8` and `P2 #11` were held at `[~]` for their literal HTTP
status codes, and `2.3`'s endpoints closed both. `P2 #4` is additionally re-asserted through the API
in `PageLifecycleApiTests`, so the promise holds at the surface an editor actually touches and not
only at the service beneath it.*
*2026-08-14 — **the phase is closed**: `P2-24`, `P2-25`, `P2-28`, and `P2-29` were the last four, and
all 29 tasks are done. Suite totals at close: `Core.Tests` 1054, `Server.Tests` 169, `Data.Tests` 23,
`E2E.Tests` 9 — 1255 green, zero build warnings. Two of those tasks changed production code rather
than only adding tests, both because the rule under test was unreachable where it lived:
`RetentionPolicy` and `VersionNumbers` came out of `VersionService` and the two mint sites (`P2-24`),
and `PayloadDiff` came out of `ContentDiffService` (`P2-25`). `P2-29` added `Core/Telemetry/` and the
OpenTelemetry registration outright, which is Phase 2's only new production surface — the rest of
[§24.1] arrives with the phases that own the operations it measures.*

**Risks:** R4 (publish transaction correctness), R5 (diff complexity).

---

## Phase 3 — Delivery, routing, and preview

**Objective:** the vertical slice closes — published pages are reachable by anonymous visitors at real
URLs, and drafts are previewable but invisible. **22.5 ed** · Entry: Phase 2 exit.

### 3.1 Routing — 7.5 ed

- [ ] **P3-01** `PageRoute`, `Redirect`, `NotFoundLog` entities + configurations, with `binary(32)` URL
  hash columns carrying the unique indexes (URLs exceed SQL Server's 900-byte key limit) [§23.5]. — 0.5 ed
- [ ] **P3-02** Migration `AddCmsRouting` — migration #4, also adding `PreviewToken`. — 0.5 ed
- [ ] **P3-03** `SlugService` in `Core/Routing/` — generation from title, normalization, Unicode/NFC
  handling with homograph warning, reserved-prefix checks (`/admin`, `/api`, `/media`, `/_blazor`,
  `/_framework`, `/account`, `/health`, `/alive`, `/sitemap.xml`, `/robots.txt`, `/preview`) [§10.2–10.3].
  — 1.5 ed
- [ ] **P3-04** `UrlService` in `Core/Routing/` — route materialization, `UseExplicitUrl` support,
  cascade to all descendants on move/rename, single transaction, emits redirects for each old URL
  [§10.4]. — 2 ed
- [ ] **P3-05** `RedirectService` in `Core/Routing/` — automatic creation on URL change, loop detection
  at write and resolve time (max depth 10), chain flattening (`A→B` then `B→C` ⇒ `A→C`), manual
  overrides automatic, **live page wins over a redirect at the same URL**, hit counting [§10.5]. — 1.5 ed
- [ ] **P3-06** Redirect CSV import/export for bulk legacy-site migration. — 0.5 ed
- [ ] **P3-07** Complete the `link` and `pageReference` field types — internal links stored as `pageId`,
  **never as a URL string**, resolved to the current URL at render [D6, §7.1]. — 1 ed

### 3.2 Rendering — 10 ed

- [ ] **P3-08** `ContentManagementSystem.Rendering` infrastructure: `CmsTemplateBase`, `CmsZone`,
  `RenderContext` (with the accumulating `CacheTags` set), `[CmsTemplate]` and `[CmsBlockType]`
  attributes [§15.2]. — 2 ed
  *From [S2](./docs/spikes/s2-dynamic-ssr.md): name the render-mode enum **`CmsRenderMode`** — the
  spec's `RenderMode` collides with `Microsoft.AspNetCore.Components.Web.RenderMode` in every .razor
  file. Keep `CacheTags` per render, never shared across requests. Markers and structural hints must
  be elements or attributes: the Razor compiler strips HTML comments from .razor markup.*
- [ ] **P3-09** Field renderer components in `Rendering/Fields/` for every Phase 1 field type. — 2 ed
  *Carries the renderer half of [`ADR-0014`](./docs/adr/0014-field-type-components-resolved-by-the-hosting-layer.md):
  built-in field types answer null for `RendererComponent`, so this task builds the catalog that maps
  a field type key to its renderer, plus the **startup check that every registered field type
  resolves to one**. Without that check a forgotten registration is invisible until someone looks at
  the page — delivery treats a missing renderer the same way it treats an unknown field type key,
  rendering nothing and logging [§15.3]. Editors are the mirror image in `P6`.*
- [ ] **P3-10** Two reference templates in `Rendering/Templates/` and three reference block types in
  `Rendering/Blocks/`, between them exercising every field type. — 2 ed
- [ ] **P3-11** Per-zone error boundaries and the full fallback matrix from [§15.3]: unknown template
  key, unknown field type, missing media, unpublished reusable content, renderer throwing. — 1 ed
  *From [S2](./docs/spikes/s2-dynamic-ssr.md): derive from **`ErrorBoundaryBase`**, not the stock
  `ErrorBoundary` — overriding `OnErrorAsync` is what gets page id, zone key, version id, and block
  id into the log (`P3 #8`), and the stock fallback text is not acceptable on a public page. Put a
  boundary at **both** levels, per zone and per block.*
- [ ] **P3-12** `PublishedContentService` in `Core/Delivery/` — resolve → load → deserialize → render;
  read-only, cache-ready, filters on `PublishedVersionId` **at the data layer** so drafts cannot leak
  [§20.1]. — 2 ed
- [ ] **P3-13** Delivery endpoint `app.MapGet("/{**slug}", …)` in `Server/Delivery/`, registered
  **after every other endpoint**; 404 page (itself a CMS page); `NotFoundLog` writing [§15.1, §10.6].
  — 1 ed
  *From [S2](./docs/spikes/s2-dynamic-ssr.md): **render to a buffer, then set headers, then write.**
  Cache tags accumulate during the render, so anything that streams sends headers before the tag set
  is complete — producing a page that never invalidates. No public delivery component may opt into
  streaming rendering.*
- [ ] **P3-14** Scope interactive routing to `/admin` in `Server/Components/Routes.razor`; keep public
  pages static SSR — the decision that makes output caching possible [§5.3]. *(Existing-code change.)*
  — 0.5 ed
- [ ] **P3-15** Route-ordering integration tests asserting `/_blazor`, `/_framework`, `/api`, `/admin`,
  `/account`, `/health` are not shadowed by the catch-all *(mitigates R6)*. — 0.5 ed

### 3.3 Preview — 4.5 ed

- [ ] **P3-16** `GET /preview/{pageId}?version=` in `Server/Delivery/Preview/` — authenticated, renders
  **any** version through the shared rendering path, output cache disabled, `X-Robots-Tag: noindex`,
  floating preview toolbar (version label, status, exit) [§12.1]. — 1.5 ed
- [ ] **P3-17** `PreviewToken` entity + hashed-token issuance/validation in `Core/Preview/`: 32 bytes
  CSPRNG, base64url, **only the SHA-256 hash stored**, default 7-day expiry (max 30), `MaxUses`,
  revocation [§12.2]. — 1 ed
- [ ] **P3-18** `GET /preview/s/{token}` anonymous shareable preview; serves exactly one page version,
  always `noindex, nofollow`, excluded from `sitemap.xml`, rate-limited. — 0.5 ed
- [ ] **P3-19** `POST /preview-tokens`, `GET /preview-tokens?pageId=`, `DELETE /preview-tokens/{id}` +
  revocation UI. — 0.5 ed
- [ ] **P3-20** Draft-link resolution inside preview — an internal link to an unpublished page resolves
  to *that page's* draft, clearly badged [§12.3]. — 0.5 ed
- [ ] **P3-21** Device-width preview frame (desktop/tablet/mobile) via a width-constrained iframe. — 0.5 ed

### Tests — Phase 3

- [ ] **P3-22** Unit: slug generation, URL construction, redirect chain flattening and loop detection.
- [ ] **P3-23** bUnit: field renderers, block components, template composition, unknown-type fallbacks.
- [ ] **P3-24** Integration: anonymous delivery of a published page; 404 for an unpublished page.
- [ ] **P3-25** Integration: URL change 301s the page and every descendant.
- [ ] **P3-26** Integration: preview-token expiry, revocation, and non-recoverability from the database.
- [ ] **P3-27** Performance benchmark harness for page render, with CI regression thresholds (starts
  here per the plan's cross-cutting performance workstream).
  *Baselines from the spikes, both **excluding** database access — treat them as the floor, not a
  projected latency: schema validation ~1.2 µs per block ([S1](./docs/spikes/s1-runtime-schema.md)),
  component rendering ~7 µs per block ([S2](./docs/spikes/s2-dynamic-ssr.md)). Warm **every** input
  size before measuring any of them; measuring sizes in sequence made a 200-block page look faster
  per block than a 50-block one, purely tiered-JIT artifact.*
- [ ] **P3-28** Telemetry: `cms.page.render.duration`, `cms.route.resolution.miss` [§24.1].
- [ ] **P3-29** Visual regression baseline (Playwright screenshots) for the two reference templates.
- [ ] **P3-30** Confirm Q8 (legacy URL preservation) is answered and its redirect import path tested.
- [ ] **P3-31** ADR: catch-all route ordering and reserved prefixes.

### Acceptance criteria — Phase 3

- [ ] **P3 #1** A published page is reachable at its URL by an anonymous request and renders its content.
- [ ] **P3 #2** An unpublished page returns 404 to anonymous requests and renders in preview for an
  editor.
- [ ] **P3 #3** **After publishing, further draft edits do not change the anonymous response.**
- [ ] **P3 #4** Changing a published page's slug 301s the old URL to the new one, for the page and all
  descendants.
- [ ] **P3 #5** A redirect chain `A→B`, then `B→C`, is flattened to `A→C`; a cycle is refused at write
  time.
- [ ] **P3 #6** A live page at a URL takes precedence over a redirect with the same `FromUrl`.
- [ ] **P3 #7** An internal link renders the target's *current* URL even after that target has moved.
- [ ] **P3 #8** A template throwing inside one block renders the rest of the page and logs the failure
  with page id, zone key, and version id.
- [ ] **P3 #9** An unknown field type key renders nothing, logs a warning, and does not throw.
- [ ] **P3 #10** A shareable preview link renders for an anonymous browser, expires on schedule, and is
  revocable; the token is not recoverable from the database.
- [ ] **P3 #11** Unresolved URLs are recorded in `NotFoundLog` with an accurate hit count.

**Exit gate — DEMO MILESTONE.** The full loop is demonstrable to a stakeholder: define a template →
create a page → fill zones → save draft → preview → publish → view anonymously → edit draft → confirm
the public page is unchanged → publish again. — [ ] met on ____

**Risks:** R6 (catch-all route ordering), R7 (static SSR + `DynamicComponent`).

---

## Phase 4 — Reusable content

**Objective:** content authored once — footers, banners, carousels — appears on many pages and updates
everywhere in one publish. **12 ed** · Entry: Phase 3 exit. Parallel with Phase 5.

- [ ] **P4-01** `ReusableContent` and `ReusableContentVersion` entities + configurations per [§23.2].
  — 1 ed
- [ ] **P4-02** Migration `AddCmsReusableContent` — migration #5. — 0.5 ed
- [ ] **P4-03** `ReusableContentService` in `Core/Content/` — CRUD plus draft/publish/version lifecycle
  **reusing the Phase 2 publishing primitives** rather than duplicating them. — 2.5 ed
- [ ] **P4-04** `reusable` field type completed: editor picker, renderer, late binding by default,
  optional `pinnedVersionId`, reference extraction [§9.2]. — 1.5 ed
- [ ] **P4-05** Pinned-version UI affordance: badge plus an "update to latest" action. — 0.5 ed
- [ ] **P4-06** `ReusableContentResolver` in `Core/Delivery/` — resolves to the *published* version in
  the delivery path, with a recursion-depth guard and cycle detection. — 1.5 ed
- [ ] **P4-07** `ReferenceQueryService` in `Core/Content/` — impact analysis / where-used over
  `ContentReference`, returning the [§9.4] shape (`affectedPages`, `affectedPageCount`,
  `pinnedPageCount`, `warnings`). — 1.5 ed
- [ ] **P4-08** `/references` endpoints for pages, media, and reusable content. — 0.5 ed
- [ ] **P4-09** `/api/cms/v1/reusable` endpoints mirroring the page endpoints minus URLs and the tree
  (CRUD, versions, publish, references, impact). — 1.5 ed
- [ ] **P4-10** Delete guard: deleting reusable content that is still referenced is **refused**, with an
  accurate where-used list [§9.4]. — included above
- [ ] **P4-11** Plain admin screens in `Client/Components/Admin/Reusable/`: library, editor, where-used
  panel, publish-impact confirmation dialog (required whenever `affectedPageCount > 0`). — 1 ed
- [ ] **P4-12** Audit: record the reusable-content publish **with its impact list**, so "why did 40
  pages change at 14:02?" is answerable [§9.3]. — included above
- [ ] **P4-13** Measure cache-invalidation fan-out cost on a high-reference item; record the baseline for
  P8 tuning *(R8)*.

### Tests — Phase 4

- [ ] **P4-14** Unit: cycle detection and depth guard in `ReusableContentResolver`.
- [ ] **P4-15** Unit: impact analysis counts, split by pinned and late-bound.
- [ ] **P4-16** Integration: publish a reusable item → three referencing pages change without being
  republished.
- [ ] **P4-17** Integration: a pinned page does not change when a newer version publishes.
- [ ] **P4-18** Integration: unpublished reusable content renders nothing, logs, and appears in the
  broken-references report.
- [ ] **P4-19** Integration: delete-while-referenced is refused with the correct list.

### Acceptance criteria — Phase 4

- [ ] **P4 #1** A reusable item is created, published, and referenced from three pages.
- [ ] **P4 #2** **Publishing a new version of the reusable item changes all three published pages
  without republishing them.**
- [ ] **P4 #3** A page pinned to version 3 does not change when version 4 is published, and its UI shows
  a badge plus an "update to latest" action.
- [ ] **P4 #4** The publish-impact dialog reports the correct affected-page count, split by pinned and
  late-bound.
- [ ] **P4 #5** Deleting reusable content that is still referenced is refused, with an accurate
  where-used list.
- [ ] **P4 #6** Unpublishing reusable content renders nothing on dependent pages, logs a warning, and
  appears in the broken-references report.
- [ ] **P4 #7** A reusable item referencing itself (directly or transitively) is refused; a depth guard
  prevents runaway recursion at render time.

**Exit gate:** one reusable publish updates all late-bound pages; pinned pages unchanged. — [ ] met on ____

**Risks:** R8 (invalidation fan-out).

---

## Phase 5 — Media library and image pipeline

**Objective:** editors upload, organize, edit, and reference images safely and with good delivery
performance. **23.5 ed** · Entry: Phase 3 exit. Parallel with Phase 4.

> **Q3 resolved:** SkiaSharp (MIT). **AVIF is not produced in v1** — renditions are WebP plus the
> original format [§13.9.1]. Build the format capability assertion in `P5-08` so an unsupported encode
> fails loudly at startup rather than returning null at runtime.
>
> **Still blocking:** **Q7 (SVG policy)** must be resolved before `P5-06` completes.

### 5.1 Storage and upload — 9 ed

- [ ] **P5-01** `MediaItem`, `MediaFolder`, `MediaRendition` entities + configurations per [§23.3],
  including `UNIQUE (Sha256) WHERE IsDeleted = 0` for deduplication. — 1.5 ed
- [ ] **P5-02** Migration `AddCmsMedia` — migration #6. — 0.5 ed
- [ ] **P5-03** `IMediaStore` abstraction + `FileSystemMediaStore` in `Core/Media/Stores/` —
  path-traversal-guarded, stores **outside `wwwroot`**, keys server-generated from content hashes
  [§13.2]. — 1 ed
- [ ] **P5-04** `AzureBlobMediaStore` against the Azurite resource added in P0. — 1 ed
- [ ] **P5-05** Upload pipeline steps 1–4 in `Core/Media/Upload/` [§13.3]: size limits
  (`RequestSizeLimit` + `FormOptions.MultipartBodyLengthLimit`), extension allowlist, magic-number
  sniffing (declared MIME must match actual bytes), decode-bomb guard (reject `width*height > 100 MP`).
  AVIF **uploads** rejected in v1. — 1.5 ed
- [ ] **P5-06** Upload pipeline step 5 — SVG policy per **Q7**: strict sanitization profile (no
  `<script>`, `<foreignObject>`, external refs, event handlers) **or** outright rejection. *Blocked on
  Q7.* — 0.75 ed
- [ ] **P5-07** Upload pipeline steps 6–10: pluggable `IMalwareScanner` with quarantine, SHA-256 dedupe,
  EXIF orientation via **MetadataExtractor** with `SKCodec.EncodedOrigin` fallback baked into pixels then
  **all metadata stripped** (GPS in a published photo is a privacy incident), persist original, queue
  standard rendition generation. — 1.25 ed
- [ ] **P5-08** Chunked/resumable upload for large files with progress reporting
  (`Server/Api/Cms/Media/` + `Client/`). — 1.5 ed

### 5.2 Image processing — 7.5 ed

- [ ] **P5-09** `IImageProcessor` abstraction + `SkiaSharpImageProcessor` (sole v1 implementation) in
  `Core/Media/Processing/`, with a `SupportedOutputFormats` capability set **asserted at startup**
  [§13.9]. — 2 ed
- [ ] **P5-10** Non-destructive edit model: `MediaItem.EditsJson`, `EditsVersion`, library-scope vs.
  usage-scope edits, revert-to-original. Original bytes never modified [§13.4]. — 2 ed
- [ ] **P5-11** Operation set: `rotate 0|90|180|270`, `flip h|v`, normalized `crop {x,y,w,h}`, resize
  per rendition, normalized `focalPoint {x,y}`. — included above
- [ ] **P5-12** Focal-point cropping math and rendition spec normalization in
  `Core/Media/Processing/`. — 1.5 ed
- [ ] **P5-13** Rendition generation in `Core/Media/Renditions/` — **per-key semaphore** so N concurrent
  cold requests produce one encode, persistence to `MediaRendition`, lazy population. — 2 ed

### 5.3 Delivery — 7 ed

- [ ] **P5-14** Signed rendition endpoint `GET /media/{id}/{w}x{h}/{mode}/{name}.{ext}` in
  `Server/Media/`: HMAC-SHA256 signature validation over the normalized parameter set, allowlisted
  widths (`320, 640, 960, 1280, 1920, 2560`), modes `crop|contain|cover|pad` [§13.5]. — 1.25 ed
- [ ] **P5-15** `Accept`-based WebP negotiation with `Vary: Accept`; **AVIF rejected at the spec-parsing
  layer**, never silently producing an empty response. — 0.75 ed
- [ ] **P5-16** Cache headers `public, max-age=31536000, immutable`; `EditsVersion` folded into the
  signature so a library edit changes every URL and busts client and CDN caches. — 0.5 ed
- [ ] **P5-17** Media serving safety [§20.7]: `Content-Type` pinned to the **sniffed** type,
  `X-Content-Type-Options: nosniff`, `Content-Disposition: inline` for images and `attachment` for
  documents. — 0.25 ed
- [ ] **P5-18** Signing-key rotation with a grace period during which the previous key still validates
  [§20.8]. — 0.25 ed
- [ ] **P5-19** `media` and `mediaList` field types completed: editor picker, inline crop/rotate/focal
  UI, reference extraction. — 1.5 ed
- [ ] **P5-20** Responsive `<picture>` renderer in `Rendering/Fields/`: WebP `<source>`, accurate
  `srcset`/`sizes`, explicit `width`/`height` for CLS, `loading="lazy"` + `decoding="async"` on
  non-LCP images, `loading="eager"` + `fetchpriority="high"` on the first image in the first zone
  [§13.6]. — 1.5 ed
- [ ] **P5-21** Alt-text policy [§13.7]: `AltText` required at upload **or** `IsDecorative = true`;
  usage-level override; **publish-time validation error** when neither is present. — 0.5 ed
- [ ] **P5-22** Media admin in `Client/Components/Admin/Media/`: browser (grid/list, folders, filters),
  detail/metadata panel, image editor, replace-keeping-id, where-used, soft delete + bin. — 0.5 ed
- [ ] **P5-23** Media API endpoints per [§22.1]: `POST /media`, `GET /media`, `GET /media/{id}`,
  `PATCH /media/{id}`, `PUT /media/{id}/edits`, `POST /media/{id}/revert`, `POST /media/{id}/replace`,
  `DELETE /media/{id}`, `GET /media/{id}/references`, `/media/folders…`. — included above
- [ ] **P5-24** Media deletion rules [§13.8]: soft delete first; permanent deletion blocked while
  `ContentReference` rows exist, with a where-used list. — included above
- [ ] **P5-25** `cms-media-store` health check — write/read/delete round trip [§24.2]. — included above

### Tests — Phase 5

- [ ] **P5-26** Unit: focal-point crop math, rendition spec normalization, signature generation.
- [ ] **P5-27** Security: upload type-confusion corpus (HTML renamed `.jpg`, mismatched magic bytes).
- [ ] **P5-28** Security: decode-bomb rejection before decode.
- [ ] **P5-29** Security: unsigned and tampered rendition URLs rejected; path-traversal probes.
- [ ] **P5-30** Integration: 20 concurrent cold requests for one rendition produce exactly one encode.
- [ ] **P5-31** Integration: dedupe returns the existing item on identical bytes.
- [ ] **P5-32** Benchmark NFR-8 — cold 4000 px source → 1280 px WebP under 800 ms p95; telemetry
  `cms.media.rendition.generated` / `.duration` [§24.1].
- [ ] **P5-33** Confirm Q9 (retention/compliance on versions and audit logs) is answered and reflected in
  the retention policy.

### Acceptance criteria — Phase 5

- [ ] **P5 #1** A JPEG upload produces a `MediaItem` with correct dimensions, size, hash, and stripped
  EXIF; GPS data is absent from the stored original.
- [ ] **P5 #2** Re-uploading identical bytes returns the existing item rather than creating a duplicate.
- [ ] **P5 #3** A file whose extension and magic bytes disagree is rejected; an HTML file renamed `.jpg`
  is rejected.
- [ ] **P5 #4** An oversized-dimension decode bomb is rejected before decoding.
- [ ] **P5 #5** SVG uploads follow the configured policy — sanitized to the strict profile, or refused.
- [ ] **P5 #6** Rotating an image in the library updates every usage; the original bytes are unchanged
  and revert-to-original restores it.
- [ ] **P5 #7** A usage-level crop affects only that page; other usages are unchanged.
- [ ] **P5 #8** An unsigned or tampered rendition URL returns 400/403; a valid one returns the image.
- [ ] **P5 #9** A rendition is generated once — twenty concurrent cold requests produce one encode.
- [ ] **P5 #10** `<picture>` output includes a WebP source, an accurate `srcset`, explicit
  `width`/`height`, and `loading="lazy"` on non-LCP images. Requesting AVIF is rejected at the
  spec-parsing layer.
- [ ] **P5 #11** Publishing a page whose image has neither alt text nor a decorative flag fails
  validation.
- [ ] **P5 #12** Permanent deletion of referenced media is refused with a correct where-used list.
- [ ] **P5 #13** A library-level edit bumps `EditsVersion`, changing rendition URLs and thereby busting
  client and CDN caches.

**Exit gate:** safe upload, non-destructive edits, signed responsive renditions. — [ ] met on ____

**Risks:** R11 (rendition CPU cost), R12 (SVG XSS). R10 closed by the SkiaSharp decision.

---

## Phase 6 — Authoring experience

**Objective:** replace the functional admin screens with the editing experience real editors will use
daily — including the edit/preview experience the requirements call out explicitly.
**34.5 ed** · Entry: Phases 4 and 5 exit.

> **R13 — this is the phase most likely to overrun.** Polish is backlogged, not absorbed. The plain UI
> from P1–P5 remains a working fallback. **Trigger:** 20% over budget at the midpoint → cut to the
> acceptance criteria only.

### Shell and navigation — 10 ed

- [ ] **P6-01** Three-pane shell in `Client/Components/Admin/Shell/`: resizable, collapsible,
  responsive down to tablet, layout persisted per user [§14.1]. — 3 ed
- [ ] **P6-02** Content tree in `Client/Components/Admin/Tree/`: lazy-loaded children, virtualized
  sibling lists, status indicators (published / draft-pending / scheduled / unpublished / in-review /
  locked) [§14.2]. — 2 ed
- [ ] **P6-03** Tree drag reorder/reparent **plus keyboard-accessible move controls**, with an explicit
  confirmation showing the URL changes and redirects that will be created. — 1.5 ed
- [ ] **P6-04** Tree context menu (new child, duplicate deep/shallow, copy, move, delete, publish
  branch, unpublish) and inline filter over title/slug/id. — 0.5 ed
- [ ] **P6-05** Editing canvas in `Client/Components/Admin/Canvas/`: zone cards ordered by `SortOrder`,
  grouped by `Zone.Group`, per-zone validation state, sticky action bar. — 3 ed

### Field editors — 14.5 ed

> Built-in field types answer null for `IFieldType.EditorComponent` — `Core` cannot name a component
> in `Client` ([`ADR-0014`](./docs/adr/0014-field-type-components-resolved-by-the-hosting-layer.md)).
> The editors below are mapped to field type keys through the same catalog `P3-09` builds for
> renderers, and the backoffice needs the equivalent startup check: a field type with no editor
> leaves an author with no way to fill a property the schema requires.

- [ ] **P6-06** Block list editor in `Client/Components/Admin/Fields/BlockList/`: add constrained to
  `allowedBlockTypes`, reorder, collapse with a configurable summary line, duplicate, delete-with-undo,
  per-block validation badges [§14.3]. — 3 ed
- [ ] **P6-07** Block list **full keyboard operability** — explicit move up/down controls; drag is an
  enhancement, never the only path [§28]. — 1 ed
- [ ] **P6-08** **Edit/Preview/Split rich-text editor** in `Client/Components/Admin/Fields/RichText/` —
  CodeMirror 6 source mode for Markdown, Quill for the constrained WYSIWYG surface, both as **local
  static assets** (no CDN, so the CSP stays strict) [§14.4]. — 2.5 ed
  *Proven end to end by [S3](./docs/spikes/s3-editor-interop.md), with four requirements: the
  backoffice host page must emit a **per-request style nonce** exposed as `<meta name="csp-nonce">`
  and passed to `EditorView.cspNonce`, or CodeMirror renders silently unstyled ([`D13`](./docs/adr/0013-backoffice-editor-bundle-and-style-nonce.md));
  one shared base class carries the interop plumbing (module import, `DotNetObjectReference`, echo
  suppression, `IAsyncDisposable`); **Quill's toolbar must be removed explicitly** on teardown, since
  Quill has no `destroy()` and appends the toolbar as a sibling; split the bundle per editor
  (696 KB raw / 231 KB gzipped for both).*
- [ ] **P6-09** Preview pane rendered through the **same Markdig → sanitize → site typography pipeline**
  the public site uses, so preview is accurate rather than approximate. — 1 ed
- [ ] **P6-10** Split mode with synchronized scrolling. — 0.75 ed
- [ ] **P6-11** CMS-aware link and image insertion — opens the CMS pickers and inserts internal
  references, never hand-typed URLs. — 0.5 ed
- [ ] **P6-12** Word/character counts with a configurable soft limit. — 0.25 ed
- [ ] **P6-13** HTML editor in `Client/Components/Admin/Fields/Html/` with a persistent banner of
  permitted tags and a **live "these tags will be stripped on save" warning** — silent stripping is the
  number-one "the CMS ate my content" ticket [§14.4]. — 1.5 ed
- [ ] **P6-14** Plain-text inline editing with a live character counter, and a "preview" that renders in
  the template's actual typography. — 0.5 ed
- [ ] **P6-15** Pickers in `Client/Components/Admin/Pickers/`: page (tree), media (browser + inline
  upload), reusable content, and a unified link picker. — 2.5 ed
- [ ] **P6-16** `IAsyncDisposable` on every JS-interop component; verify no listener/editor instance
  leaks *(mitigates R14)*. — included above

### Properties, saving, and feedback — 5 ed

- [ ] **P6-17** Properties panel in `Client/Components/Admin/Properties/`: page metadata, SEO section
  with a **search-result preview widget** and character-count guidance, publishing section, editorial
  fields (owner, review-by, internal notes, tags) [§14.7, §18.1]. — 2 ed
- [ ] **P6-18** Autosave in `Client/Services/`: 20-second idle debounce, save on navigate-away,
  offline-safe queueing, clear save-state indication ("Saved 14:32") [§11.3]. — 1.25 ed
- [ ] **P6-19** Conflict resolution UI on `409`: keep-mine / take-theirs / open-diff. **No path silently
  discards work.** — 0.75 ed
- [ ] **P6-20** Publish dialog: errors and warnings grouped by zone, each deep-linking to the offending
  field; warnings require acknowledgement and resubmit with `acknowledgedWarnings` [§14.6, §22.2]. — 0.5 ed
- [ ] **P6-21** Toasts (reuse the existing `IToastService`), confirmation dialogs, undo affordances,
  empty and loading states. — 0.25 ed
- [ ] **P6-22** ARIA live regions announcing autosave state and validation results [§28]. — 0.25 ed

### Dashboard, bin, and bulk — 5.5 ed

- [ ] **P6-23** Keyboard shortcuts plus a shortcut reference dialog. — 1 ed
- [ ] **P6-24** Dashboard in `Client/Components/Admin/Dashboard/` [§14.9] — **My work** tile (drafts with
  unpublished changes, review assignments, rejected items). — 0.5 ed
- [ ] **P6-25** Dashboard — **Scheduled** tile (publishes/expiries in the next 7 days, failures
  highlighted). — 0.5 ed
- [ ] **P6-26** Dashboard — **Needs attention** tile (past `ReviewByDate`, broken references, images
  missing alt text, top `NotFoundLog` URLs). — 0.5 ed
- [ ] **P6-27** Dashboard — **Recent activity** tile (permission-filtered `AuditLog` view); every tile
  deep-links into a correctly filtered list. — 0.5 ed
- [ ] **P6-28** Recycle bin UI in `Client/Components/Admin/RecycleBin/`: list, filter, subtree-aware
  restore, permanent delete with typed-name confirmation [§14.10]. — 1 ed
- [ ] **P6-29** `BulkOperationService` in `Core/Content/`: selection model, impact preview, background
  execution with progress above 25 items, per-item result reporting, per-item audit logging [§14.11].
  — 1.5 ed

### Tests — Phase 6

- [ ] **P6-30** bUnit: block list editor add/reorder/duplicate/delete, keyboard paths.
- [ ] **P6-31** bUnit: rich-text editor mode switching and preview parity.
- [ ] **P6-31a** E2E: mount and unmount an editor ten times, asserting zero surviving editor DOM
  nodes and created-equals-disposed; and assert CodeMirror's own styling is in effect (a computed
  style differing from the browser default), since a missing CSP nonce fails **silently**
  [[S3](./docs/spikes/s3-editor-interop.md), [`D13`](./docs/adr/0013-backoffice-editor-bundle-and-style-nonce.md)].
- [ ] **P6-32** E2E: full editor journey — create → edit → preview → publish → verify anonymous → edit
  again → verify published unchanged → rollback.
- [ ] **P6-33** E2E: autosave survives a simulated transient network failure without losing input.
- [ ] **P6-34** E2E: save conflict presents all three resolution options.
- [ ] **P6-35** Performance: tree responsive at 5,000 pages with 500 siblings under one parent.
- [ ] **P6-36** axe-core across every backoffice screen — zero critical or serious violations.
- [ ] **P6-37** Manual keyboard-only pass over the whole authoring flow.
- [ ] **P6-38** 200% browser zoom pass.
- [ ] **P6-39** `prefers-reduced-motion` respected; no color-only status encoding in the tree [§28].
- [ ] **P6-40** Add backoffice and content typography layers to `styles/site.scss`.
  *(Existing-code change.)*

### Acceptance criteria — Phase 6

- [ ] **P6 #1** An editor completes create → fill → preview → publish without touching a raw JSON
  payload or a URL bar.
- [ ] **P6 #2** Markdown Edit/Preview/Split all work, and Preview matches the published page's rendering
  exactly.
- [ ] **P6 #3** The HTML editor warns *before* save about content the active profile will strip.
- [ ] **P6 #4** Blocks can be added, reordered, duplicated, and deleted entirely by keyboard; drag is an
  enhancement, never the only path.
- [ ] **P6 #5** Autosave fires on a 20-second idle, shows its state, and survives a transient network
  failure by retrying without losing input.
- [ ] **P6 #6** A save conflict presents keep-mine / take-theirs / open-diff, and no path silently
  discards work.
- [ ] **P6 #7** The tree remains responsive at 5,000 pages with 500 siblings under one parent.
- [ ] **P6 #8** The dashboard surfaces the signed-in user's drafts, review tasks, and overdue content,
  and every tile deep-links into a correctly filtered list.
- [ ] **P6 #9** A deleted page leaves the public site immediately, remains in the recycle bin with full
  history, and restores as a *draft*.
- [ ] **P6 #10** Deleting and restoring a page with children moves the whole subtree, with the count
  shown before confirming.
- [ ] **P6 #11** A deep duplicate rewrites links between pages inside the copied subtree to the new
  copies, while links out of the subtree still point at the originals.
- [ ] **P6 #12** A bulk publish of 100 pages runs as a background job with progress, and a partial
  failure leaves successful items published while reporting the rest individually.
- [ ] **P6 #13** axe-core reports zero critical or serious violations on every backoffice screen.
- [ ] **P6 #14** The whole authoring flow is operable at 200% browser zoom.

**Exit gate:** editors complete the full flow unaided; a11y clean. — [ ] met on ____

**Risks:** R13 (scope elasticity), R14 (JS interop memory leaks).

---

## Phase 7 — Workflow, permissions, and scheduling

**Objective:** more than one person can use the system safely. **16 ed** · Entry: Phase 2 exit.
**Runs in parallel with Phases 4–6.**

- [ ] **P7-01** Seed the seven roles from [§3.2] (`Administrator`, `Developer`, `Editor`, `Author`,
  `Approver`, `MediaManager`, `Viewer`) in `Data/Seeding/`. — 0.5 ed
- [ ] **P7-02** Permission constants + policy provider in `Server/Authorization/`, mapped to roles per
  the [§21.1] matrix; extend `CustomUserClaimsPrincipalFactory`. — 1.5 ed
- [ ] **P7-03** `PageAcl` entity + configuration [§21.2]. — 0.5 ed
- [ ] **P7-04** `AclService` in `Core/Security/`: inheritance via indexed `Page.Path` prefix match,
  **deny beats allow** at the same depth, deeper rule beats shallower, `Administrator` bypass with an
  audit entry. — 2.5 ed
- [ ] **P7-05** Per-request ACL cache to keep deep-tree resolution fast *(mitigates R15)*. — included above
- [ ] **P7-06** Apply ACL checks in the **service layer** for every content and media operation — never
  only at the endpoint, never in the client. — 1.5 ed
- [ ] **P7-07** IDOR integration tests sweeping every content and media endpoint across ACL boundaries
  with guessed ids. — 0.5 ed
- [ ] **P7-08** `WorkflowTask` and `Comment` entities + migration `AddCmsWorkflow` (migration #7, also
  carrying `PageAcl` and `ScheduledJob`). — 1 ed
- [ ] **P7-09** `WorkflowService` in `Core/Workflow/` with the three modes from [§11.9]: `None`,
  `Simple`, `TwoStep` (approver may not be the author). Site-wide setting in v1. — 2 ed
- [ ] **P7-10** Version status transitions wired to workflow: `Draft → InReview → Approved → Published`,
  `Rejected` copying content into a fresh draft with comments preserved [§11.2]. — included above
- [ ] **P7-11** Workflow endpoints: `POST /pages/{id}/submit|approve|reject`,
  `GET /workflow/tasks?assignedTo=me`, `GET`/`POST /pages/{id}/comments`. — included above
- [ ] **P7-12** Review UI in `Client/Components/Admin/Workflow/`: submit/approve/reject, zone-anchored
  threaded comments, task inbox. — 2 ed
- [ ] **P7-13** `ScheduledJob` entity + `PublishSchedulerService` in `Server/HostedServices/`: 30 s
  poll, **atomic `UPDATE … OUTPUT` claiming** so two instances cannot double-publish [§11.6]. — 1.25 ed
- [ ] **P7-14** Scheduled publish runs the identical validation and invalidation path as a manual
  publish; a validation failure marks the job `Failed`, notifies the owner, and does **not** retry
  blindly. — included above
- [ ] **P7-15** `UnpublishOn` handling: clear `PublishedVersionId`, retire public routes, apply the
  configured parent-redirect behavior rather than leaving a 404. — 0.25 ed
- [ ] **P7-16** DST-aware scheduling UI: stored UTC, presented in the site timezone with the offset
  shown explicitly. — 0.5 ed
- [ ] **P7-17** `cms-scheduler` health check (lag > 5 min fails) + `cms.scheduler.lag` gauge. — included above
- [ ] **P7-18** Replace `Server/Components/Email/IdentityNoOpEmailSender.cs` with a real sender per
  **Q5**. *(Existing-code change — workflow notifications and password resets are non-functional
  without it.)* — 1 ed
- [ ] **P7-19** Notification templates + in-app inbox for: submitted, approved, rejected, scheduled
  publish succeeded/failed, edit-lock override, comment mentions [§14.8]. — 0.5 ed
- [ ] **P7-20** Audit log viewer in `Client/Components/Admin/Audit/` with entity / user / date filters,
  backed by `GET /audit?entity=&entityId=&userId=&from=&to=`. — 0.5 ed

### Tests — Phase 7

- [ ] **P7-21** Unit: ACL resolution — inheritance, deny-over-allow, depth precedence, admin bypass.
- [ ] **P7-22** Integration: `Author` publish attempt returns `403` and content stays unpublished.
- [ ] **P7-23** Integration: `TwoStep` mode refuses self-approval.
- [ ] **P7-24** Integration: two server instances, one scheduled job → exactly one publish *(R16)*.
- [ ] **P7-25** Integration: `Content.Read` denial hides a subtree from the content tree entirely.
- [ ] **P7-26** Performance: tree load under 500 ms at depth 10 with ACLs applied *(R15 trigger)*.

### Acceptance criteria — Phase 7

- [ ] **P7 #1** An `Author` cannot publish: the API returns `403` and the content stays unpublished.
- [ ] **P7 #2** Submit → approve → publish works end to end, with email and in-app notifications at each
  step.
- [ ] **P7 #3** In `TwoStep` mode, the author cannot approve their own submission.
- [ ] **P7 #4** A rejection returns the content to a fresh draft with comments preserved and visible.
- [ ] **P7 #5** A user with an ACL on `/products` can edit that subtree and receives `403` on `/about`,
  including on direct API calls with a guessed id.
- [ ] **P7 #6** Denying `Content.Read` on a subtree hides it from the content tree entirely.
- [ ] **P7 #7** A page scheduled for a future time publishes within 60 seconds of it, and only once even
  with two server instances running.
- [ ] **P7 #8** A scheduled publish that fails validation marks the job failed, notifies the owner, and
  does not silently retry.
- [ ] **P7 #9** `UnpublishOn` retires the page and applies the configured redirect behavior.
- [ ] **P7 #10** The audit viewer answers "who unpublished the homepage and when" in under three
  interactions.

**Exit gate:** authors cannot publish; ACLs enforced server-side; scheduling fires once. — [ ] met on ____

**Risks:** R15 (ACL query performance), R16 (duplicate scheduled publishes).

---

## Phase 8 — SEO, caching, navigation, and search

**Objective:** the public site is fast, discoverable, and navigable. **14 ed** · Entry: Phase 3 exit.
**Runs in parallel with Phases 5–6.**

### SEO — 3.5 ed

- [ ] **P8-01** Surface the SEO fields already on `PageVersion` end to end in `Rendering/Seo/`:
  `<title>`, meta description, `<link rel="canonical">`, robots directives [§18.1–18.2]. — 0.75 ed
- [ ] **P8-02** Open Graph and Twitter Card tags, with the OG image rendered through a `1200x630` crop
  rendition. — 0.5 ed
- [ ] **P8-03** JSON-LD: `WebSite` + `Organization` on the home page, `BreadcrumbList` from the content
  tree, `WebPage`/`Article` per page, all overridable via `StructuredDataJson`. — 0.75 ed
- [ ] **P8-04** `sitemap.xml` in `Server/Delivery/Seo/`: published indexable pages only, `<lastmod>` from
  the publish timestamp, configurable `changefreq`/`priority`, **index splitting above 40,000 URLs**,
  cached with the `content` tag. — 1 ed
- [ ] **P8-05** Editable `robots.txt` from `SiteSettings` with a sensible default disallowing `/admin`,
  `/api`, `/preview` and pointing at the sitemap; **non-production serves `Disallow: /`
  unconditionally**. — 0.5 ed

### Caching — 7 ed

- [ ] **P8-06** Output caching in `Server/`: policies, `UseOutputCache()` placed **after**
  `UseAuthentication`/`UseAuthorization`, ETag revalidation, `.NoCache()` on preview and admin routes,
  a base-policy predicate excluding requests carrying an identity cookie [§16.4]. — 1.5 ed
- [ ] **P8-07** Cache-tag accumulation **during render** via `RenderContext.CacheTags` → applied to the
  response: `page:{id}`, `ru:{id}`, `media:{id}`, `tpl:{id}`, `nav:{menuKey}`, `content` [§16.2]. — 1 ed
- [ ] **P8-08** `HybridCache` for published content objects and route lookups in `Core/Delivery/`
  (15 min TTL, tag eviction) [§16.1]. — 1 ed
- [ ] **P8-09** `OutboxMessage` entity + `OutboxProcessorService` in `Server/HostedServices/` (5 s
  cadence) — invalidation enqueued **inside the publish transaction** so a committed publish always
  evicts even if the process dies immediately after [§16.3]. — 1.5 ed
- [ ] **P8-10** `CacheInvalidator` in `Core/Caching/` — fan-out driven by `ContentReference`, using
  `IOutputCacheStore.EvictByTagAsync`. — 1 ed
- [ ] **P8-11** Optional Redis output cache (`AddStackExchangeRedisOutputCache`) behind configuration,
  wired to the Aspire Redis resource; **`IDistributedCache` explicitly not used** for output caching.
  — 0.75 ed
- [ ] **P8-12** Short backstop TTL so any missed invalidation self-heals within an hour *(mitigates
  R17)*. — 0.25 ed
- [ ] **P8-13** `cms-outbox` health check (unprocessed messages older than 5 min) +
  `cms.cache.hit_ratio` metrics. — included above

### Navigation and search — 3.5 ed

- [ ] **P8-14** `NavigationMenu` / `NavigationItem` entities + migration `AddCmsDelivery` — migration #8,
  also carrying `SearchDocument` (+ full-text catalog), `OutboxMessage`, `Tag`, `PageTag`. Handle the
  Azure SQL vs. on-prem raw-SQL differences for full-text catalog creation explicitly. — 0.75 ed
- [ ] **P8-15** Structural navigation generated from the content tree, filtered by
  `Page.ShowInNavigation` and publish state [§10.7]. — 0.5 ed
- [ ] **P8-16** Managed menus (ordered items, internal page reference or external link) + menu admin UI.
  — 0.5 ed
- [ ] **P8-17** `nav:{menuKey}` cache tags invalidated on any publish/unpublish/move. — 0.25 ed
- [ ] **P8-18** `SearchDocument` + SQL Server full-text index + `SearchIndexService` in `Core/Search/`,
  populated via `IFieldType.ExtractSearchText` on save and publish, **asynchronously through the
  outbox** with a nightly reconcile *(mitigates R18)* [§17.1]. — 1 ed
- [ ] **P8-19** Backoffice search UI with filters: template, status, owner, tag, modified date range,
  "has unpublished changes," "past review date." — 0.5 ed
- [ ] **P8-20** `tags` field type completed + `Tag`/`PageTag` management. — included above

### Tests — Phase 8

- [ ] **P8-21** Integration: publishing a page evicts exactly its own cache entry and its dependents,
  and nothing else.
- [ ] **P8-22** Integration: an invalidation enqueued in a transaction that then **fails** is not
  dispatched; one in a committed transaction is dispatched even if the process is killed immediately
  after commit.
- [ ] **P8-23** Integration: two instances with Redis — a publish on A invalidates B.
- [ ] **P8-24** Integration: an authenticated editor's request is never served from the anonymous cache,
  and vice versa.
- [ ] **P8-25** Performance: backoffice search returns by title, body, and slug across 50,000 seeded
  pages in under 500 ms.

### Acceptance criteria — Phase 8

- [ ] **P8 #1** Every public page emits a correct `<title>`, meta description, canonical link, robots
  directive, and OG/Twitter tags; JSON-LD validates against Google's Rich Results test.
- [ ] **P8 #2** `sitemap.xml` contains exactly the published, indexable pages, and refreshes on publish.
- [ ] **P8 #3** Staging serves `Disallow: /` regardless of the configured `robots.txt`.
- [ ] **P8 #4** A cached page is served from the output cache, and publishing it evicts the entry
  immediately.
- [ ] **P8 #5** Publishing reusable content evicts every dependent page and nothing else.
- [ ] **P8 #6** An authenticated editor's request is never served from the anonymous cache, and vice
  versa.
- [ ] **P8 #7** With Redis configured and two instances running, a publish on instance A invalidates
  instance B.
- [ ] **P8 #8** An invalidation enqueued in a transaction that then fails is not dispatched; one in a
  committed transaction is dispatched even if the process is killed immediately after commit.
- [ ] **P8 #9** Navigation reflects publish state within one cache generation; unpublishing removes the
  item.
- [ ] **P8 #10** Backoffice search returns a page by title, body text, and slug across 50,000 seeded
  pages in under 500 ms.

**Exit gate:** publish invalidates exactly the right cache entries; SEO output correct. — [ ] met on ____

**Risks:** R17 (cache invalidation correctness — highest-severity functional risk), R18 (full-text index
maintenance cost).

> **Scheduling constraint:** decide during this phase whether multi-site is plausible within 18 months.
> Adding a `SiteId` discriminator is dramatically cheaper before v2 adds tables than after.
> - [ ] **P8-26** Multi-site assessment recorded as an ADR. — 0 ed

---

## Phase 9 — Hardening, accessibility, and launch

**Objective:** verify the non-functional requirements and make the system operable. **14 ed** ·
Entry: all prior phases exit.

### Security — 5.5 ed

- [ ] **P9-01** CSP with per-request nonces: the strict public policy from [§20.5], and a separate
  `/admin` policy carrying `wasm-unsafe-eval` and `frame-ancestors 'self'`. Nonce propagation added to
  `Server/Components/App.razor`; public and admin head content split. *(Existing-code change.)* — 1 ed
- [ ] **P9-02** `Strict-Transport-Security`, `X-Content-Type-Options: nosniff`,
  `Referrer-Policy: strict-origin-when-cross-origin`, minimal `Permissions-Policy`. — 0.5 ed
- [ ] **P9-03** Rate limiting across all endpoint groups per [§20.6] — login/register/reset 5 per 15 min
  per IP; API writes 100/min per user; uploads 20/min per user; renditions 300/min per IP; preview
  tokens 30/min per token; public pages 600/min per IP. — 1 ed
- [ ] **P9-04** Identity hardening in `Server/Program.cs`: minimum 12-character password, breached-password
  screening, mandatory 2FA for `Administrator` / `Developer` / `Approver`, and the self-registration
  decision from **Q10**. *(Existing-code change — current settings are template defaults.)* — 1 ed
- [ ] **P9-05** Verify secrets handling [§20.8]: the Aspire `sql-password` dev default never reaches
  production; media-signing HMAC key sourced from key vault/environment and rotatable. — 0.25 ed
- [ ] **P9-06** Penetration-test pass: XSS corpus against **live rendering**, IDOR sweep, upload fuzzing,
  unsigned rendition URLs, preview-token enumeration, CSRF. — 1.75 ed

### Accessibility — 2.5 ed

- [ ] **P9-07** axe-core across all screens, backoffice and public output. — 0.5 ed
- [ ] **P9-08** Manual keyboard pass and screen-reader passes (NVDA + VoiceOver). — 1 ed
- [ ] **P9-09** 200% zoom and `prefers-reduced-motion` verification. — 0.25 ed
- [ ] **P9-10** Authored-output accessibility rules [§28]: heading structure validated (`h2`–`h6` only in
  rich text; skipped-level warning at publish), link-text warnings ("click here", bare URLs), `<th
  scope>` emitted by the table tool, `color` field constrained to design-system tokens, `lang` from
  `SiteSettings.Culture`. — 0.5 ed
- [ ] **P9-11** Remediation of all findings to zero critical/serious. — 0.25 ed

### Performance — 3 ed

- [ ] **P9-12** Seed a 50,000-page / 100,000-media dataset for load testing. — 0.5 ed
- [ ] **P9-13** k6 load tests against NFR-1 (cached TTFB < 200 ms p95), NFR-2 (uncached < 800 ms p95),
  NFR-7 (publish < 2 s), NFR-9 (scale). — 1 ed
- [ ] **P9-14** Profile and fix the top three findings. — 0.5 ed
- [ ] **P9-15** Lighthouse CI on representative templates; Core Web Vitals remediation to NFR-3 (≥ 90
  mobile) and NFR-4. — 1 ed
- [ ] **P9-16** 8-hour editor soak test for JS interop memory growth *(R14 — fail if browser memory grows
  more than 50% over 2 hours)*. — included above
- [ ] **P9-17** Chaos test for NFR-11: cached public content continues serving during a backoffice
  outage. — included above

### Operations and documentation — 3 ed

- [ ] **P9-18** Backup/restore drill **including a media-store restore**, timed against the RTO;
  documented runbook [§24.3]. — 1 ed
- [ ] **P9-19** Operational documentation: deployment, configuration reference, health checks,
  dashboards, alert thresholds, incident runbooks. — 1 ed
- [ ] **P9-20** Verify every health check has a monitor and an alert threshold: `cms-database`,
  `cms-media-store`, `cms-templates`, `cms-scheduler`, `cms-outbox`. — included above
- [ ] **P9-21** User documentation: editor guide, developer template-authoring guide, admin guide. — 1 ed
- [ ] **P9-22** Update `README.md` with CMS setup, template authoring, and the schema sync CLI.
  *(Existing-code change.)* — included above
- [ ] **P9-23** Switch migration policy to roll-forward-only after launch; retain `Down` methods as
  documentation only. — 0 ed
- [ ] **P9-24** Browser matrix verification for NFR-13 (last 2 versions of Chrome, Edge, Firefox,
  Safari). — included above

### Acceptance criteria — Phase 9

- [ ] **P9 #1** Zero critical or high findings from the security pass; all mediums triaged with owners
  and dates.
- [ ] **P9 #2** WCAG 2.2 AA verified on backoffice and public output; zero critical/serious axe
  violations.
- [ ] **P9 #3** NFR-1, NFR-2, NFR-7, and NFR-9 met under load with a 50,000-page dataset.
- [ ] **P9 #4** Lighthouse mobile performance ≥ 90 on all reference templates.
- [ ] **P9 #5** A full restore from backup — database and media — produces a working site, timed against
  the RTO.
- [ ] **P9 #6** Every health check has a monitor and an alert threshold.
- [ ] **P9 #7** An editor unfamiliar with the system completes create → publish using only the written
  guide.

**Exit gate:** NFRs met; security and accessibility signed off; runbooks in place. — [ ] met on ____

---

## Launch checklist

From [`plan.md` §22](./plan.md#22-launch-and-rollout). Not counted in the phase totals.

### Pre-launch

- [ ] **L-01** Content freeze on any legacy system being migrated.
- [ ] **L-02** Structure promotion: templates and zones applied to production via `cms schema apply`,
  verified with `cms schema diff`.
- [ ] **L-03** Content migration dry run in staging, with an unresolved-links report reviewed.
- [ ] **L-04** Redirect import from the legacy URL map; verify a sample of 100 old URLs resolve.
- [ ] **L-05** Full backup/restore drill.
- [ ] **L-06** Load test against production-equivalent infrastructure.
- [ ] **L-07** Editor training and guide handover.

### Launch

- [ ] **L-08** Blue/green or slot-based cutover; previous version retained warm.
- [ ] **L-09** Confirm the rollback plan: migrations through launch are additive-only and backward
  compatible, so an application rollback needs no database rollback.

### Post-launch

- [ ] **L-10** First 48 h — monitor `NotFoundLog` hourly, create redirects for anything with real
  traffic; watch cache hit ratio, publish success rate, error rate.
- [ ] **L-11** First 2 weeks — daily editor check-in; triage friction into a backlog; verify
  search-console coverage.
- [ ] **L-12** First month — review R3 (over-stripping), R11 (rendition CPU), R13 (deferred UI polish);
  re-baseline NFR measurements against real traffic.
- [ ] **L-13** Ongoing — quarterly restore drill, quarterly dependency and security review, monthly
  review of content past its review date.

---

## Cross-cutting workstreams

Budgeted **inside** each phase's estimates, not added on top. These are standing obligations, not
one-time tasks — review them at every phase exit.

| Workstream | Obligation | Starts |
|---|---|---|
| **Testing** | Every task ships with tests; coverage gates enforced in CI | P0 |
| **Security** | Sanitization from P1, authorization from P2, threat-model review at each phase exit | P1 |
| **Accessibility** | axe-core in CI; manual keyboard pass at each UI phase exit | P1 |
| **Performance** | Benchmarks added alongside features; regression thresholds in CI | P3 |
| **Documentation** | ADRs written when decisions are made, not reconstructed at the end | P0 |
| **Observability** | Metrics and traces added with each service, never retrofitted in P9 | P1 |

### Merge gates (every PR)

- [ ] Build clean — `TreatWarningsAsErrors` already enabled solution-wide.
- [ ] All fast-lane suites green: unit, EF integration, API integration, bUnit, security corpus, axe,
  migration up/down verification.
- [ ] Line coverage ≥ 80% in `Core`; **≥ 90% on `PublishingService`, `SanitizationService`,
  `UrlService`, `RedirectService`, and `AclService`**.
- [ ] Zero new critical/serious axe violations.
- [ ] No new high or critical findings from dependency and secret scanning.
- [ ] Any migration in the diff carries a reviewer sign-off.

---

## Database migration sequence

Additive, applied in this order by the existing Aspire `ef-migrations` resource. Every migration is
verified in CI to apply cleanly against a database restored from the previous one, and has a tested
`Down` **through Phase 8**.

| # | Migration | Phase | Task | Contents | Done |
|---|---|---|---|---|:--:|
| 1 | `InitialDatabase` | — | P0-01 | Existing Identity + `AuditLog` schema | [x] |
| 2 | `AddCmsStructure` | 1 | P1-06 | `Template`, `TemplateRevision`, `Zone`, `BlockType`, `BlockTypeRevision`, `BlockTypeProperty`, `Composition`, `CompositionProperty`, `BlockTypeComposition`, `SiteSettings` | [x] |
| 3 | `AddCmsPages` | 2 | P2-06 | `Page`, `PageVersion`, `ContentReference`, `EditLock` (+ the `SiteSettings` home / not-found FKs deferred from P1-01) | [x] |
| 4 | `AddCmsRouting` | 3 | P3-02 | `PageRoute`, `Redirect`, `NotFoundLog`, `PreviewToken` | [ ] |
| 5 | `AddCmsReusableContent` | 4 | P4-02 | `ReusableContent`, `ReusableContentVersion` | [ ] |
| 6 | `AddCmsMedia` | 5 | P5-02 | `MediaFolder`, `MediaItem`, `MediaRendition` | [ ] |
| 7 | `AddCmsWorkflow` | 7 | P7-08 | `WorkflowTask`, `Comment`, `PageAcl`, `ScheduledJob` | [ ] |
| 8 | `AddCmsDelivery` | 8 | P8-14 | `NavigationMenu`, `NavigationItem`, `SearchDocument` (+ full-text catalog), `OutboxMessage`, `Tag`, `PageTag` | [ ] |

**Rules:** data backfills are separate, idempotent, resumable, and batched — never inline in a schema
migration. Full-text catalog creation in migration 8 requires raw SQL and must handle Azure SQL vs.
SQL Server on-prem syntax differences explicitly.

---

## Changes to existing code

The CMS is additive, but these existing files are modified. Tracked separately so nothing surprises a
reviewer.

| File | Change | Phase | Task | Done |
|---|---|---|---|:--:|
| `Data/Models/AuthDbContext.cs` | Exclude high-churn tables from `AddLogging()` audit capture | 1 | P1-05 | [x] |
| `Data/Models/AuthDbContext.cs` | Implement `ApplySoftDeletes()` — the virtual hook exists, is empty, and is never called | 2 | P2-04 | [x] |
| `Data/Models/AuthDbContext.cs` | Defer cascade and orphan timing to `SaveChanges`, without which the soft-delete net is bypassed whenever the dependents happen to be loaded | 2 | P2-04 | [x] |
| `Data/Models/ApplicationDbContext.cs` | Suppress EF warning 10622: `PageVersion` deliberately carries no soft-delete filter, so a deleted page's history stays retrievable | 2 | P2-03 | [x] |
| `Data/Models/ApplicationDbContext.cs` | Register CMS `DbSet`s; apply configurations from the assembly | 1 | P1-04 | [x] |
| `Server/Program.cs` | Register CMS services, field type registry, output cache, rate limiting, security headers, background services; delivery endpoint registered **last** | 1–8 | P1-30, P3-13 | [ ] |
| `Server/Program.cs` | Tighten the Identity password policy; decide self-registration | 9 | P9-04 | [ ] |
| `Server/Components/Email/IdentityNoOpEmailSender.cs` | Replace with a real sender | 7 | P7-18 | [ ] |
| `Server/Components/App.razor` | CSP nonce propagation; split public and admin head content | 8–9 | P9-01 | [ ] |
| `Server/Components/Routes.razor` | Scope interactive routing to `/admin`; keep public pages static SSR | 3 | P3-14 | [ ] |
| `aspire/…AppHost/AppHost.cs` | Add Azurite and optional Redis resources | 0 | P0-13, P0-14 | [ ] |
| `Directory.Packages.props` | Add HtmlSanitizer, Markdig, SkiaSharp, MetadataExtractor, HybridCache, rate limiting, Testcontainers, bUnit, Playwright, k6 tooling | 0–5 | P0-07, P0-12 | [ ] |
| `Shared/Common/FieldLengths.cs` | Add CMS field length constants | 1 | P1-03 | [x] |
| `styles/site.scss` | Add backoffice and content typography layers | 6 | P6-40 | [ ] |
| `README.md` | Document CMS setup, template authoring, schema sync CLI | 9 | P9-22 | [ ] |
| `ContentManagementSystem.slnx` | Add Core, Rendering, and four test projects | 0 | P0-07…P0-11 | [ ] |

---

## Definition of done

A **task** is done when:

- [ ] Code is merged to `main` with a passing pipeline.
- [ ] Unit tests cover the happy path plus the failure and boundary cases.
- [ ] Integration tests cover the endpoint's authorization, validation, and concurrency behavior.
- [ ] Any new UI passes axe with zero critical/serious violations and is fully keyboard operable.
- [ ] Any editor-facing HTML path is covered by the XSS corpus.
- [ ] Telemetry (metric or trace) exists for anything that can be slow or can fail.
- [ ] Public API surface has XML documentation; non-obvious decisions have an ADR.
- [ ] The feature is demonstrable to a non-engineer without a debugger.

A **phase** is done when every acceptance criterion is a passing **automated** test — not a manual
check — except where explicitly marked as a manual audit (P9 accessibility and security passes).

---

## Requirements traceability

Every statement in [`requirements.md`](./requirements.md) mapped to the tasks that deliver it. This is
the checklist for verifying the delivered system against the original ask.

| # | Requirement | Spec | Tasks | Acceptance | Done |
|---|---|---|---|---|:--:|
| R-1 | "Create templates that let them specify data zones" | [§8] | P1-01…P1-02, P1-21…P1-22, P1-25, P1-29 | P1 #1 | [ ] |
| R-2 | "Specify what type of data can be used in a zone (plain text, reusable content, html/markdown, etc)" | [§7], [§8.3] | P1-08…P1-12 | P1 #1, #2 | [ ] |
| R-3 | "In zones that are plain text or html/markdown … inline editing … 'edit/preview' editor experience" | [§14.4] | P6-08…P6-14 | P6 #2, #3 | [ ] |
| R-4 | "Reusable content … specified once but then reused in multiple (common footers, image carousels)" | [§9] | P4-01…P4-11 | P4 #1, #2 | [ ] |
| R-5 | "content editors should be able to create pages from those templates" | [§10.1], [§22.1] | P2-07, P2-16, P2-23 | P2 #1 | [x] 2026-08-14 |
| R-6 | "populate the 'placeholder' areas with actual content" | [§6.2], [§14.3] | P2-10, P2-23, P6-05, P6-06 | P2 #2, P6 #1 | [ ] |
| R-7 | "Pages … need to have a url specified so that end users would be able to navigate to the pages" | [§10.2]–[§10.4] | P3-01…P3-06, P3-13 | P3 #1 | [ ] |
| R-8 | "pages in draft mode before they get published out" | [§11.1], [§11.2] | P2-10, P2-11, P3-16 | P2 #3, P3 #2 | [ ] |
| R-9 | "pages should be versioned" | [§11.1]–[§11.5] | P2-11, P2-13, P2-14 | P2 #5, #6, #7 | [ ] |
| R-10 | "a published page could still be visible to unauthenticated users while content editors are making changes that only they can see internally" | [§11.1], [§12] | P2-11, P3-12, P3-16 | **P2 #4, P3 #3** — the central promise | [~] `P2 #4` met 2026-08-14; `P3 #3` awaits delivery |
| R-11 | "image management functionality … upload images" | [§13.3] | P5-01…P5-08 | P5 #1–#5 | [ ] |
| R-12 | "resize and rotate those images" | [§13.4], [§13.5] | P5-09…P5-13 | P5 #6, #7 | [ ] |
| R-13 | "'reference' those images inside the pages they are creating" | [§13.6], [§7.1] `media` | P5-19, P5-20 | P5 #10 | [ ] |
| R-14 | "do plenty of research and add elements that are clearly missing" | [§4.2] — 30 gaps | see below | per gap | [ ] |

### Gap coverage (R-14)

The 30 gaps from [§4.2], mapped to the tasks that close them.

| Gap | Item | Tasks | Done |
|---|---|---|:--:|
| #1 | URL management | P3-03, P3-04 | [ ] |
| #2 | Redirects | P3-05, P3-06 | [ ] |
| #3 | SEO metadata | P8-01…P8-03, P6-17 | [ ] |
| #4 | `sitemap.xml` & `robots.txt` | P8-04, P8-05 | [ ] |
| #5 | Scheduled publish/unpublish | P7-13…P7-16 | [ ] |
| #6 | Approval workflow | P7-08…P7-12 | [ ] |
| #7 | Granular permissions | P7-01…P7-07 | [ ] |
| #8 | Shareable preview links | P3-17…P3-19 | [ ] |
| #9 | Version diff & rollback | P2-13, P2-14 | [ ] |
| #10 | Soft delete & recycle bin | P2-08, P6-28 | [ ] |
| #11 | HTML sanitization / XSS defense | P1-18…P1-20, P9-06 | [ ] |
| #12 | Upload validation & safe serving | P5-05…P5-07, P5-17 | [ ] |
| #13 | Alt text enforced | P5-21 | [ ] |
| #14 | Focal point / smart cropping | P5-12 | [ ] |
| #15 | Renditions, `srcset`, WebP | P5-13…P5-16, P5-20 | [ ] |
| #16 | Where-used / link integrity | P4-07, P4-08 | [ ] |
| #17 | Output caching + invalidation | P8-06…P8-13 | [ ] |
| #18 | Concurrency control | P2-03, P2-15, P6-19 | [ ] |
| #19 | Backoffice search & content tree | P6-02…P6-04, P8-18, P8-19 | [ ] |
| #20 | Audit trail surfaced in the UI | P7-20 | [ ] |
| #21 | Template change / schema evolution safety | P1-25, P1-26, P1-32 | [ ] |
| #22 | Public site search | **v2** — index built by P8-18 | [-] |
| #23 | Localization | **out of scope** — Q1 resolved, [§19] | [-] |
| #24 | Navigation/menu management | P8-14…P8-17 | [ ] |
| #25 | Forms / lead capture | **v2** | [-] |
| #26 | Headless read API + webhooks | **v2** | [-] |
| #27 | Import/export & environment promotion | P1-26, P1-28 (structure, v1); content bundles **v2** | [ ] |
| #28 | Rate limiting & brute-force protection | P9-03, P9-04 | [ ] |
| #29 | Editorial metadata | P6-17 | [ ] |
| #30 | Broken-link & orphaned-media reporting | **v2** — nightly jobs in P8/P9, UI deferred | [-] |

---

## Post-v1 backlog

Ordered by expected value, not effort. Not started; listed so nothing is lost.

| # | Item | Spec | Size |
|---|---|---|---|
| 1 | In-context (on-page) editing | [§14.5] | 12 ed |
| 2 | Public site search UI and analytics | [§17.2] | 5 ed |
| 3 | Headless read API + webhooks | [§29.3] | 10 ed |
| 4 | Forms and lead capture | [§29.3] | 12 ed |
| 5 | Content import/export bundles | [§27.2] | 8 ed |
| 6 | Broken-link and orphaned-media reporting UI | gap #30 | 4 ed |
| 7 | Nested blocks beyond one level | [§29.3] | 5 ed |
| 8 | Per-template workflow configuration | [§11.9] | 4 ed |
| 9 | Multi-site support | [§29.3] | 25 ed — **assess in Phase 8 (P8-26) before v2 locks the schema** |

Localization is **not** on this list — it was removed from scope entirely (Q1, [§19]). If it ever
returns it is a re-planning event costing ~25–35 ed, not a backlog item.

---

## Risk register — live status

Carried from [`plan.md` §20](./plan.md#20-risk-register). Update the status column as phases land.

| ID | Risk | Sev | Phase | Trigger for the contingency | Status |
|---|---|---|---|---|---|
| R1 | A Phase 0 spike fails | High | 0 | Spike exceeds its box by 50% | Open |
| R2 | Runtime-defined schema too complex to validate cleanly | High | 1 | Validator cannot identify the offending field | Open |
| R3 | Sanitizer strips content editors legitimately need | Med | 1 | >3 editor complaints in the first month | Open |
| R4 | Publish transaction leaves inconsistent state | **Critical** | 2 | Any occurrence — stop the line | Open |
| R5 | Diff algorithm slow or noisy | Low | 2 | Diff over 2 s on a typical page | Open |
| R6 | Catch-all route shadows framework/admin paths | High | 3 | Any framework path 404s in testing | Open |
| R7 | `DynamicComponent` under static SSR misbehaves | High | 0/3 | S2 no-go | Open |
| R8 | Invalidation fan-out slow for a reusable item on 10,000 pages | Med | 4/8 | Publish exceeds NFR-7 (2 s) | Open |
| R9 | Testcontainers unreliable in CI | Med | 0 | Flake rate above 5% | Open |
| R10 | ~~Six Labors licensing stalls Phase 5~~ | — | 5 | **Closed** — SkiaSharp selected; residual is the silent-null AVIF encode, mitigated by P5-09 | Closed |
| R11 | Rendition generation saturates CPU | High | 5 | CPU above 70% sustained during load test | Open |
| R12 | SVG sanitization bypassed | **Critical** | 5 | Any bypass found → disable SVG | Open |
| R13 | Phase 6 scope expands | Med | 6 | 20% over budget at the midpoint → cut to acceptance criteria only | Open |
| R14 | JS interop leaks memory in long sessions | Med | 6/9 | Browser memory grows >50% over 2 hours | Open |
| R15 | ACL resolution slow on a deep tree | Med | 7 | Tree load exceeds 500 ms at depth 10 | Open |
| R16 | Duplicate scheduled publishes under scale-out | Med | 7 | Any duplicate observed | Open |
| R17 | Cache invalidation misses a dependent page | **High** | 8 | Any stale page reported after publish | Open |
| R18 | Full-text index degrades write throughput | Med | 8 | Save latency exceeds NFR-6 | Open |
| R19 | Requirements shift mid-build (multi-site, multilingual) | **High** | any | Either raised → stop and re-plan | Open |
| R20 | Key-person dependency on Blazor/EF expertise | Med | 1–3 | Either engineer unavailable >1 week | Open |
