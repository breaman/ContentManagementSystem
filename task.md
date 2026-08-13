# Content Management System — Implementation Task List

**Status:** In progress — Phase 0 complete; Phase 1 section 1.1 done, 1.2 in progress (`P1-11` next)
**Version:** 1.0
**Last updated:** 2026-08-13
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
| [1 — Content structure](#phase-1--content-structure) | 33 | 10 | 28.0 | In progress — 1.1 complete; 1.2 value field types done, `P1-11` next | — |
| [2 — Pages, versioning, publishing](#phase-2--pages-versioning-and-publishing) | 29 | 0 | 27.0 | Not started | — |
| [3 — Delivery, routing, preview](#phase-3--delivery-routing-and-preview) | 31 | 0 | 22.5 | Not started | — |
| [4 — Reusable content](#phase-4--reusable-content) | 19 | 0 | 12.0 | Not started | — |
| [5 — Media library & image pipeline](#phase-5--media-library-and-image-pipeline) | 33 | 0 | 23.5 | Not started | — |
| [6 — Authoring experience](#phase-6--authoring-experience) | 41 | 0 | 34.5 | Not started | — |
| [7 — Workflow, permissions, scheduling](#phase-7--workflow-permissions-and-scheduling) | 26 | 0 | 16.0 | Not started | — |
| [8 — SEO, caching, navigation, search](#phase-8--seo-caching-navigation-and-search) | 26 | 0 | 14.0 | Not started | — |
| [9 — Hardening, accessibility, launch](#phase-9--hardening-accessibility-and-launch) | 24 | 0 | 14.0 | Not started | — |
| **v1 total** | **281** | **29** | **203.5** | | |

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
- [ ] **P1-11** Stub reference-bearing field types to their contract, completed in later phases:
  `media` (P5), `link`/`pageReference` (P3), `reusable` (P4), `tags` (P8); implement `blocks` fully
  here. — 1 ed
- [ ] **P1-12** Per-field-type configuration JSON Schema + validation on zone save, in
  `Core/Fields/Configuration/` [§7.2]. — 1 ed
- [ ] **P1-13** Contract test asserting **every registered field type returns references for a
  representative populated value** — the omission that silently produces stale content [§7.3].
  — included above
  *Widened by [S1](./docs/spikes/s1-runtime-schema.md): the test as originally worded passes for a
  `blocks` field type that reports only its top level and silently drops every reference nested
  inside it. **Add a second case** — a container field type must return the references of a nested
  populated value.*

### 1.3 Payload engine — 5 ed

- [ ] **P1-14** `ContentPayload` model + envelope + `System.Text.Json` converters in `Shared/Content/`,
  with explicit **absent-vs-null** semantics [§6.2]. — 1.5 ed
- [ ] **P1-15** `ContentSchemaValidator` in `Core/Content/` — walks zone/block-property definitions,
  dispatches to field types, returns structured errors keyed by zone / block id / property. — 2 ed
  *Four constraints proven by [S1](./docs/spikes/s1-runtime-schema.md): stay on
  `JsonDocument`/`JsonElement` (no intermediate CLR model, or absent-vs-null is lost); build the
  error path from a push/pop stack so nothing allocates on the happy path; give every diagnostic a
  stable `code` alongside its message; make draft-vs-publish a validator parameter, not a filter
  applied to the results.*
- [ ] **P1-16** `ReferenceIndexer` in `Core/Content/` — extracts `ContentReference` rows via
  `IFieldType.ExtractReferences`. — 1 ed
- [ ] **P1-17** Snapshot tests pinning the payload envelope format in `Core.Tests/Content/`. — 0.5 ed

### 1.4 Sanitization — ships now, before any HTML can be stored — 3.5 ed

- [ ] **P1-18** `SanitizationService` in `Core/Security/` over HtmlSanitizer with the `Basic` /
  `Extended` / `Developer` profiles [§20.2], including the cross-profile rules (no `<script>`, no
  `<style>`, no `on*`, scheme allowlist, forced `rel="noopener noreferrer"`, CSS allowlist). — 1.5 ed
  *Its contract already exists: `P1-10` added `IContentSanitizer` and `SanitizationProfile` to
  `Shared/Contracts/Security/` because `richText` and `html` had to depend on something. Implement
  against it and register it — nothing resolves the field type registry until you do. `Shared`
  rather than beside the implementation on purpose: field types sanitize on write, renderers on
  read, and the editor preview has to run the identical pipeline, and those three layers do not
  reference each other.*
- [ ] **P1-19** Markdig pipeline in `Core/Content/Markdown/`: markdown → HTML → sanitize, **identical
  between editor preview and delivery**. — 1 ed
  *Note what `P1-10` deliberately did not do: `richText` in markdown format is stored **exactly as
  authored**, un-sanitized, because the raw HTML markdown permits cannot be cleaned without parsing
  the markdown around it, and rewriting the source to whatever a Markdig round trip produces would
  lose the author's formatting on every save. That makes this task the only thing standing between
  stored markdown and the page — the conversion output must go through the sanitizer on **both**
  paths, with no shortcut for preview.*
- [ ] **P1-20** XSS corpus suite in `Core.Tests/Security/` (OWASP payloads + polyglots) asserting
  neutralization per profile and reporting what was stripped. Wire into CI as a merge gate. — 1 ed

### 1.5 Structure admin (functional, unstyled UI) — 6 ed

- [ ] **P1-21** Management API `/api/cms/v1/templates` in `Server/Api/Cms/Structure/` — list, create,
  read, update, revisions. — 0.5 ed
- [ ] **P1-22** `/api/cms/v1/templates/{id}/zones` — CRUD with key immutability enforced [§8.5]. — 0.5 ed
- [ ] **P1-23** `/api/cms/v1/block-types` and `/block-types/{id}/properties`. — 0.5 ed
- [ ] **P1-24** `/api/cms/v1/compositions` and `/field-types` (read-only registry introspection). — 0.5 ed
- [ ] **P1-25** `TemplateReconciler` in `Core/Structure/`: scan assemblies for `[CmsTemplate]` /
  `[CmsBlockType]`, insert code-only records, mark DB-only records `IsOrphaned`, **never delete**, log a
  diff in Development [§8.4]. — 1 ed
- [ ] **P1-26** `SchemaSyncService`: idempotent, additive-only apply of
  `Server/CmsSchema/*.json` zone/property definitions at startup [§27.1]. — 0.5 ed
- [ ] **P1-27** `cms-templates` health check — degrades when an `IsOrphaned` template has non-deleted
  pages [§24.2]. — 0.25 ed
- [ ] **P1-28** CLI verbs in `Server/Cli/`: `cms schema export | diff | apply` [§27.1]. — 0.25 ed
- [ ] **P1-29** Plain admin screens under `/admin/structure` in `Client/Components/Admin/Structure/`:
  template list, create, edit zone, edit block type. — 2 ed
- [ ] **P1-30** Register CMS services and the field type registry in `Server/Program.cs`.
  *(Existing-code change.)* — 0.25 ed
- [ ] **P1-31** Wire axe-core accessibility checks into CI against the structure screens — the
  continuous a11y gate starts here, not in P9. — 0.25 ed
- [ ] **P1-32** Template evolution rules enforced in the service layer [§8.5]: add zone free; remove
  zone retains payload data as orphaned; **rename key forbidden**; field-type change requires an
  explicit converter choice; template delete blocked while pages reference it. — included above
- [ ] **P1-33** ADRs for any Phase 1 decision not already covered by D1–D12.

### Acceptance criteria — Phase 1

- [ ] **P1 #1** A `Developer` creates a template with four zones of differing field types through the
  admin UI, and the definitions persist.
- [ ] **P1 #2** `ContentSchemaValidator` accepts a valid payload and rejects an invalid one with errors
  identifying the exact zone, block id, and property.
- [ ] **P1 #3** Renaming a zone key is refused; renaming a display name succeeds.
- [ ] **P1 #4** Removing a zone leaves existing payload data intact and reachable as orphaned content.
- [ ] **P1 #5** A template defined in code but absent from the database is created at startup; a
  database template with no code component is marked orphaned and degrades `cms-templates`.
- [ ] **P1 #6** Every payload in the XSS corpus is neutralized under each sanitization profile, with the
  stripped content reported.
- [ ] **P1 #7** Markdown rendered by the editor-preview path is byte-identical to the delivery path.

**Exit gate:** structure can be defined and a payload validated against it; XSS corpus green in CI.
— [ ] met on ____

**Risks:** R2 (runtime-schema complexity), R3 (sanitizer over-stripping).

---

## Phase 2 — Pages, versioning, and publishing

**Objective:** the core promise — a page has a draft and a published version, and editing the draft does
not disturb what is published. **27 ed** · Entry: Phase 1 exit.

### 2.1 Data — 6.5 ed

- [ ] **P2-01** `Page` and `PageVersion` entities + configurations per [§23.2], including the mutual
  `Page.DraftVersionId` / `PageVersion.PageId` FK handling from [§23.5] (`DeleteBehavior.Restrict`,
  `DraftVersionId` set in a second statement inside the creating transaction). — 2 ed
- [ ] **P2-02** `ContentReference` and `EditLock` entities + configurations, with the two hot indexes
  `(TargetType, TargetId)` and `(SourceType, SourceVersionId)`. — 1 ed
- [ ] **P2-03** `rowversion` concurrency tokens on `Page` and `PageVersion`; global query filters
  excluding `IsDeleted = 1`; filtered indexes per [§23.5]. — 1 ed
- [ ] **P2-04** Implement `AuthDbContext.ApplySoftDeletes()` — the virtual hook exists and is empty, so
  a stray `Remove()` on a `Page` would destroy version history. *(Existing-code change.)* — 0.5 ed
- [ ] **P2-05** `Page.Path` materialization (`/1/8/44/`) and maintenance on insert/move in
  `Core/Content/PageTreeService.cs`; index it for prefix matching [§10.1]. — 1 ed
- [ ] **P2-06** Migration `AddCmsPages` — migration #3. `Up`/`Down` verified in CI. — 1 ed

### 2.2 Services — 16 ed

- [ ] **P2-07** `PageService` in `Core/Content/` — create from template (produces a draft version with
  an empty, schema-valid payload), read, metadata patch. — 2 ed
- [ ] **P2-08** `RecycleBinService` in `Core/Content/` — subtree-aware soft delete/restore, route
  retirement, parent-redirect option, permanent-delete guard against live `ContentReference` rows
  [§14.10]. Restore returns a page as a **draft**, never live. — 1.5 ed
- [ ] **P2-09** `DuplicationService` in `Core/Content/` — shallow and deep duplication with
  intra-subtree link rewriting; media referenced, never copied; copy starts at version 1 [§14.12].
  — 1.5 ed
- [ ] **P2-10** `DraftService` in `Core/Content/` — load, save (payload + `rowversion` concurrency),
  discard (reset to published), named checkpoint [§11.3]. — 2 ed
- [ ] **P2-11** `PublishingService` in `Core/Publishing/` — validate → snapshot draft into a new
  immutable version → archive the previous published version → repoint `Page.PublishedVersionId` →
  reindex `ContentReference` → enqueue invalidation, **all in one transaction** [§5.5]. — 3 ed
- [ ] **P2-12** Fault-injection tests forcing a mid-transaction failure at each step of `PublishingService`,
  asserting all-or-nothing *(mitigates R4 — stop-the-line severity)*. — included above
- [ ] **P2-13** `VersionService` in `Core/Publishing/` — history, fetch one version, restore-into-draft
  (copy, never resurrect [§11.5]), retention pruning policy [§11.7]. — 2 ed
- [ ] **P2-14** `ContentDiffService` in `Core/Publishing/` — structural diff with GUID-based block
  matching (reports *moved*, not removed+added), word-level text diff, target-identity diff for
  media/link/reference fields, flat metadata diff [§11.4]. Computed on demand, **never in the publish
  path**. — 3 ed
- [ ] **P2-15** `EditLockService` in `Core/Content/` — acquire on editor open, 30 s heartbeat, override,
  2-minute expiry reaper. **A lock never blocks editing** [§11.8, D12]. — 1 ed

### 2.3 API and UI — 4.5 ed

- [ ] **P2-16** Page endpoints in `Server/Api/Cms/Pages/` per [§22.1]: `GET /pages`, `GET /pages/tree`,
  `POST /pages`, `GET /pages/{id}`, `PUT /pages/{id}/draft`, `PATCH /pages/{id}/metadata`. — 1 ed
- [ ] **P2-17** Lifecycle endpoints: `POST /pages/{id}/duplicate`, `DELETE /pages/{id}`,
  `POST /pages/{id}/restore`, `POST /pages/{id}/validate`, `POST /pages/{id}/publish`,
  `POST /pages/{id}/unpublish`. — 0.75 ed
- [ ] **P2-18** Version endpoints: `GET /versions`, `GET /versions/{vid}`,
  `GET /versions/{a}/diff/{b}`, `POST /versions/{vid}/restore`. — 0.5 ed
- [ ] **P2-19** `POST`/`DELETE /pages/{id}/lock`. — 0.25 ed
- [ ] **P2-20** Cross-cutting API concerns: `ETag`/`If-Match` optimistic concurrency, RFC 9457
  problem-details error contract with the `errors`/`warnings` shape from [§22.2], antiforgery on all
  writes, cursor pagination on collections. — 1 ed
- [ ] **P2-21** Authorization policies and permission constants in `Server/Authorization/`
  (`Content.Read/Edit/Publish/Delete`, `Structure.Edit`, `Settings.Edit`) — global roles only; section
  ACLs land in P7. — 1 ed
- [ ] **P2-22** Explicit DTOs on every write endpoint so a client cannot mass-assign `Status:
  "Published"`; status transitions only via dedicated endpoints [§20.1]. — included above
- [ ] **P2-23** Plain admin screens in `Client/Components/Admin/Pages/`: page list,
  create-from-template, generic zone form, version history, diff viewer. — 1 ed

### Tests — Phase 2

- [ ] **P2-24** Unit: draft save concurrency, version numbering, retention policy selection.
- [ ] **P2-25** Unit: diff algorithm — reorder, insert, delete, nested block change.
- [ ] **P2-26** Data integration: filtered unique indexes behave; query filters exclude deleted rows;
  `rowversion` conflicts surface as `DbUpdateConcurrencyException`.
- [ ] **P2-27** API integration: authorization, validation, and concurrency behavior for every endpoint.
- [ ] **P2-28** API integration: publish transactionality under fault injection.
- [ ] **P2-29** Telemetry: `cms.publish.count` / `.duration` metrics and publish trace spans [§24.1].

### Acceptance criteria — Phase 2

- [ ] **P2 #1** Creating a page from a template produces a draft version with an empty, schema-valid
  payload.
- [ ] **P2 #2** Saving the draft mutates the draft version in place and creates no new version row.
- [ ] **P2 #3** Publishing creates a new immutable version, archives the previous published version, and
  repoints `Page.PublishedVersionId` — all or nothing under a forced mid-transaction failure.
- [ ] **P2 #4** **After publishing, editing the draft leaves the published version byte-for-byte
  unchanged.** *(The requirement's central promise — R-10.)*
- [ ] **P2 #5** Version history lists every version with status, author, and timestamp.
- [ ] **P2 #6** The diff between two versions reports a reordered block as *moved*, not as
  removed-plus-added.
- [ ] **P2 #7** Restoring an old version copies it into the draft and leaves the published version
  untouched.
- [ ] **P2 #8** Two concurrent draft saves: the second receives `409 Conflict` with both payloads.
- [ ] **P2 #9** An advisory lock is visible to a second editor and can be overridden; it expires after 2
  minutes of silence.
- [ ] **P2 #10** Soft-deleting a page hides it from default queries while keeping full history
  retrievable.
- [ ] **P2 #11** Publishing with an unfilled required zone returns `422` naming that zone.

**Exit gate:** acceptance test **#4** passes — the requirement's central promise is mechanically
verified. — [ ] met on ____

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
| 3 | `AddCmsPages` | 2 | P2-06 | `Page`, `PageVersion`, `ContentReference`, `EditLock` | [ ] |
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
| `Data/Models/AuthDbContext.cs` | Implement `ApplySoftDeletes()` — the virtual hook exists and is empty | 2 | P2-04 | [ ] |
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
| R-5 | "content editors should be able to create pages from those templates" | [§10.1], [§22.1] | P2-07, P2-16, P2-23 | P2 #1 | [ ] |
| R-6 | "populate the 'placeholder' areas with actual content" | [§6.2], [§14.3] | P2-10, P2-23, P6-05, P6-06 | P2 #2, P6 #1 | [ ] |
| R-7 | "Pages … need to have a url specified so that end users would be able to navigate to the pages" | [§10.2]–[§10.4] | P3-01…P3-06, P3-13 | P3 #1 | [ ] |
| R-8 | "pages in draft mode before they get published out" | [§11.1], [§11.2] | P2-10, P2-11, P3-16 | P2 #3, P3 #2 | [ ] |
| R-9 | "pages should be versioned" | [§11.1]–[§11.5] | P2-11, P2-13, P2-14 | P2 #5, #6, #7 | [ ] |
| R-10 | "a published page could still be visible to unauthenticated users while content editors are making changes that only they can see internally" | [§11.1], [§12] | P2-11, P3-12, P3-16 | **P2 #4, P3 #3** — the central promise | [ ] |
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
