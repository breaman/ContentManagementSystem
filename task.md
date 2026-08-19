# Content Management System — Implementation Task List

**Status:** In progress — **Phase 9 is under way**: its security, accessibility, and operations
sections are done (17 of 24 tasks), leaving the performance section, two gates that need a person and
an environment, and one deferred to after launch by its own terms. **Phase 8's 26 tasks are all done**
and its exit gate is met; nine of its ten criteria are met, and the tenth (`P8 #10`, search latency)
is asserted but has only ever run on an engine without full-text. **Phase 7 is complete**: all 26 tasks, all 10 acceptance criteria, and
its exit gate. **Phase 6 is built out**, with 12 of its 14 criteria met and its gate open
on the browser journeys and the manual keyboard pass alone. Phase 0 complete; **Phase 1's 33 tasks all done**, its exit gate open on
`P1 #1` alone, which needs a browser driving the admin form; **Phase 2 complete**; **Phase 3's three
sections all finished** and all 11 criteria met, with the perf harness (`P3-27`), visual regression
(`P3-29`), and Q8 (`P3-30`) still open. **Phase 4 is complete** — all 19 tasks, all 7 acceptance
criteria, and its exit gate. **Phase 5 is complete** — 32 of 33 tasks, all 13 acceptance criteria,
and its exit gate; the one task left open is `P5-33`, which asks for a confirmation Legal has not
given. Media is uploaded through the ten-step pipeline (`P5-05` to `P5-07`) into a store that is a
local directory or a blob container behind one interface (`P5-03`, `P5-04`), with the schema and
migration #6 beneath it (`P5-01`, `P5-02`). Uploads are judged by their bytes rather than their
names, capped before they are decoded, stripped of every metadata block, and deduplicated by content
hash. SkiaSharp is the sole processor and asserts its own encoders at startup (`P5-09`); renditions
are signed, allowlisted, lazily generated behind a per-key semaphore, and served with a year of
immutable caching, `nosniff`, and a pinned content type (`P5-13` to `P5-18`, `P5-25`). The API an
editor drives exists (`P5-23`): browse and search, metadata, non-destructive edits and revert
(`P5-10`), replace-keeping-id, the folder tree, soft delete with a bin, and a permanent delete
guarded by where-used (`P5-24`). **A rendition URL that was not signed by this site is refused, a
library edit changes every URL the site emits without a purge, and a URL signed against a superseded
edit is refused with `410` rather than served the newer picture under the old cache key.** The
authoring surface closed it out: the `media` picker and its publish-time settings (`P5-19`), the
responsive `<picture>` renderer (`P5-20`), the admin library and image editor (`P5-22`), and
resumable chunked upload (`P5-08`) — a transport in front of the same pipeline, not a second way
into the library. **The one honest gap Phase 5 leaves behind is audit-log retention**, which has no
implementation and is recorded against `P9-25` rather than absorbed here.
**Phase 6 is built out**: every feature task is done, and **12 of its 14 acceptance criteria are
met**. The three-pane shell, the real content tree, moving pages, and the tree's context menu, filter
and clipboard are done (`P6-01` to `P6-04`) — **"publish branch" included**, now that there is a bulk
service to build it on. Moving a page is new server work as well as new UI: `IPageService.MoveAsync`
makes the tree position, the sibling order, and the route rebuild one transaction, and its **preview
is the move itself, rolled back**, so the confirmation an editor approves cannot differ from what
then happens. The editing canvas is done (`P6-05`): zones are cards, grouped and ordered by the
revision the draft was authored against — which is why the schema snapshot now captures a zone's
grouping and help text — each carrying its own validation state, under a sticky action bar. **The
field editors that fill those cards are done** (`P6-06` to `P6-16`): one catalog maps a field type
key to the component that fills it (`ADR-0014`), one host dispatches through it, and the page canvas,
a block's property row, and the reusable editor therefore cannot disagree about what a `richText`
looks like. Every editor binds to the stored value as its whole JSON envelope, which puts each field
type's storage shape in the one component that understands it. **All eighteen built-in field types
have an editor**, not only the ten the tasks name, because `P6 #1` asks an author to fill a page
without touching raw JSON and one `number` zone would otherwise fail it. CodeMirror and Quill are
locally bundled and split per editor, behind the per-request style nonce `D13` calls load-bearing;
the preview pane renders through the server's one Markdig-and-sanitize pipeline rather than a second
copy in the browser, and reports what publishing will remove — which is also the HTML editor's live
strip warning. **Properties, saving, and feedback are done** (`P6-17` to `P6-22`): the right-hand
pane edits everything about a page that is not its content and sends only the fields an editor
touched, autosave writes the draft twenty seconds after the typing stops and on the way out, a lost
race opens keep-mine / take-theirs / open-diff instead of being retried, and the publish dialog
groups what is wrong by zone with a link into each card. Two server promises had to be made real for
that: **the `409` now carries the draft that won** — `ETags` has claimed since `P2-20` that this is
why a mismatch answers 409 rather than 412, but the problem mapper dropped it — and a new
`POST /pages/{id}/draft/diff` compares an unsaved payload against the stored draft, which the version
diff cannot do because both copies of a contested draft are the same version row.
**The dashboard, the recycle bin, the shortcuts, and bulk operations closed the phase out**
(`P6-23` to `P6-29`). `/admin` is a route for the first time, and it is the four tiles of [§14.9] over
one read-only service: what the signed-in editor has in progress, what publishes or expires in the
next week with a failed schedule called out, what has rotted — an overdue review, a live page
pointing at a deleted one, a picture nobody described, the 404s still taking traffic — and what has
been done lately. Every tile links into **the same query at a larger limit**, which is what makes
`P6 #8`'s "correctly filtered" structural. `BulkOperationService` runs one operation over many pages
without reimplementing any of them: each item goes through the same publish, delete, or patch a
single request does, in a scope of its own, so validation, permissions, and audit rows are the same —
and a batch over twenty-five items runs after the response has been written, carrying the caller's
identity with it. The recycle bin lists subtree roots rather than deleted rows, and its one
irreversible operation asks for the page's name to be typed. Keyboard shortcuts are one table read by
both the listener and the reference dialog, and every one of them is an accelerator for a button that
is also on the screen. **The test gates found real defects rather than confirming a clean bill**: the
axe pass (`P6-36`) turned up three landmark and heading faults in the new screens, and the 200% zoom
pass (`P6-38`) found four screens whose tables could not reflow. Both are fixed. **What is left is
what no assertion supplies**: `P6-32` to `P6-34` need the whole application running in a browser —
a Kestrel address, the WebAssembly runtime booting against it, and a database — which is a harness
that does not exist yet, and `P6-37`'s keyboard-only pass is a thing a person does. The phase's exit
gate is open on those four alone.
**Phase 7 makes the system safe for more than one person.** Permissions are the [§21.1] matrix, held
by seven seeded roles whose ids are part of the contract because a role-scoped rule stores one, and
narrowed by section ACLs: rules hang on a page, reach its descendants through an indexed prefix match
on `Page.Path`, and resolve by deeper-beats-shallower then deny-beats-allow. **One allow anywhere
turns a permission into an allowlist for that principal**, which is the clause that makes an ACL
narrow rather than only widen — an editor given `/products` is thereby refused `/about`. The
resolution happens once per request and every node after that is a string comparison, which is what
keeps a depth-10 tree inside its budget (`R15`). Enforcement is in the **service layer**, on the id
the caller supplied, and the IDOR sweep walks nineteen entry points to say so; a refusal of
`Content.Read` answers *not found*, because a 403 a 404 would not have produced tells the caller the
page is there. `Administrator` bypasses the rules, and a bypass is logged when — and only when — a
rule would otherwise have refused.
**Review is three verbs rather than a status field a client could patch.** `None`, `Simple`, and
`TwoStep` come from `SiteSettings` and are read per request; in `TwoStep` the approver may not be the
author, and publishing an unapproved version is refused as well, or the rule would be one button
press away from nothing. **The draft is frozen while it is under review** — a save against an
`InReview` version is refused — because an approval has to be a statement about the content that then
publishes. A rejection keeps the refused version exactly as it was refused and hands the author an
editable copy of it, with the comment thread untouched: comments hang off the page, so they survive
by construction. Which buttons an editor sees is computed server-side and shipped as three flags, so
the self-approval clause has one implementation rather than two.
**Scheduling turns on a single statement.** A job leaves `Pending` only through an atomic
`UPDATE … OUTPUT`, so every instance can poll and only one can claim (`R16`, closed); a job runs as
the editor who scheduled it, rebuilt from the identity tables, because a publish by nobody would be
refused by the same service-layer check everything else passes through. A validation failure is
terminal and notifies its owner rather than retrying every thirty seconds forever. The scheduling UI
states the exact instant a wall-clock box means, offset and all, before anything is saved. **Mail is
no longer a no-op**: `Q5` is answered as configuration — SMTP, which every candidate provider speaks
— with a logging fallback that says what it would have sent and a health check that reports the
difference. **The scheduler test found a real defect**: `new SqlParameter("@pending", 0)` binds to
the `SqlDbType` overload, because the literal zero converts to any enum, and the claim query failed
saying a parameter it had been given was never supplied.
**Phase 8 makes the public site fast, discoverable, and navigable.** The SEO head, `sitemap.xml`, and
`robots.txt` come from one builder, so preview and delivery cannot emit different heads for one
version, and staging serves `Disallow: /` from the environment name rather than from a setting a
copied production database could carry with it. **Caching is opt-in per endpoint** rather than a base
policy with exclusions, which is the version somebody adding a route cannot undo; what a page depends
on is recorded as cache tags *while it renders*, so evicting a reusable item reaches every page
showing it with no query at all. Invalidation is enqueued **inside the publish transaction** and
applied by every instance, because each node has its own in-process cache to evict.
**Navigation is two mechanisms with one filter**: generated from the content tree and hand-managed,
both dropping anything an anonymous visitor could not reach, and both taking a `nav:` tag on the
pages that render them — so unpublishing a page removes it from the menu on every *other* page within
a cache generation. **Search is asynchronous by construction.** A save enqueues an id, and the outbox
rebuilds the document afterwards, so extracting text from every zone is never on the path an editor
waits on; the index describes *working* content, with published state as a column, which is what
makes an editor able to find the paragraph they wrote this morning and what makes v2's public search
a filter rather than a second index. The outbox now carries two message types, and they claim
differently on purpose: cache eviction runs on every node and claims nothing, while the index handler
claims its row, because one is per-node memory and the other is a shared table.
**The arm64 full-text question raised in Phase 0 is answered**: the service probes for the engine and
falls back to a scan, so Azure SQL Edge is a supported deployment rather than a broken test
environment — only the 500 ms budget is gated on the index existing. **Tags landed as page metadata,
not payload**, which corrects a note left on `TagsFieldType` in Phase 1: two writers would mean a tag
removed on the properties panel reappearing on the next payload save. That closes `P6-17`'s open
half. **Multi-site was assessed and declined for v1** ([ADR 0025](./docs/adr/0025-single-site-in-v1-no-siteid-discriminator.md)):
the column was never the cost — the twenty-odd uniqueness rules are, each of them a product question
nobody has been asked, and a discriminator nothing filters by has a failure mode no single-site test
can catch.
**The `Content-Security-Policy` header is now switched on** — see Phase 9 below, which is where the
inline-`style` problem noted here was resolved.
**Phase 9 hardens what the earlier phases built, and it is the phase whose tests found the most.**
The CSP went on as **three profiles selected from endpoint metadata**, strictest as the default, so a
route that says nothing is strict and a route that needs more says so
([ADR 0026](./docs/adr/0026-three-content-security-policies-public-carries-no-nonce.md)); the public
policy deliberately carries **no nonce**, because a public response is cached and replayed and a
per-request value in one is a constant an attacker can quote. Rate limiting is six named policies on
endpoint groups rather than one limiter in front of the site, two of which decide per request whether
they apply at all. Identity now asks for twelve characters and no character classes — the classes are
what produce `Password1!` — screens against a breach list, and **will not let an `Administrator`,
`Developer`, or `Approver` do anything but enrol a second factor**. `CmsSecretsGuard` refuses to start
a deployment holding a development secret, because every one of those appears to work when it is
wrong.
**What the phase's gates turned up is the part worth reading.** A refused request was reaching clients
as a `404`, because the site's status-code pages re-execute any body-less error response. The
`cms-database` health check of [§24.2] **did not exist** — Aspire registers one under the context's
full type name, which no alert rule can refer to. And the version retention sweep has implemented all
five clauses of [§11.7] since `P2-13` with **nothing ever calling it**, so every deployment kept every
version of every page forever while a policy that said otherwise sat in the code. The XSS corpus now
runs against **live rendering** as well as against the sanitizer, the accessibility gate covers the
public document as well as the backoffice, and both come with negative controls, because an assertion
that nothing is wrong passes just as well against a page that rendered nothing.
**Version:** 1.0
**Last updated:** 2026-08-19
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
| [3 — Delivery, routing, preview](#phase-3--delivery-routing-and-preview) | 31 | 28 | 22.5 | All three sections done; all 11 criteria met. `P3-27`, `P3-29`, `P3-30` remain | — |
| [4 — Reusable content](#phase-4--reusable-content) | 19 | 19 | 12.0 | Complete — all 19 tasks and all 7 acceptance criteria | 2026-08-16 |
| [5 — Media library & image pipeline](#phase-5--media-library-and-image-pipeline) | 33 | 32 | 23.5 | Complete — all 13 acceptance criteria. `P5-33` open: it needs an answer from Legal (**Q9**), and the gap it names is `P9-25` | 2026-08-16 |
| [6 — Authoring experience](#phase-6--authoring-experience) | 41 | 36 | 34.5 | Built out — every feature task done; **12 of 14 criteria met**. Open: `P6-32`…`P6-34` (browser journeys, which need a hosted-app harness), `P6-37` (a pass a person performs), and `P6-17`'s tags and share image, which wait on `P8-20`/`P8-02` | — |
| [7 — Workflow, permissions, scheduling](#phase-7--workflow-permissions-and-scheduling) | 26 | 26 | 16.0 | Complete — all 26 tasks and all 10 acceptance criteria | 2026-08-18 |
| [8 — SEO, caching, navigation, search](#phase-8--seo-caching-navigation-and-search) | 26 | 26 | 14.0 | All 26 tasks done; **9 of 10 criteria met**. `P8 #10`'s 500 ms budget is asserted but has only run on an engine without full-text (arm64 Azure SQL Edge), so it waits on the same CI agent `P0 #3` does | — |
| [9 — Hardening, accessibility, launch](#phase-9--hardening-accessibility-and-launch) | 24 | 17 | 14.0 | In progress — **security, accessibility, and operations are done** (`P9-01`…`P9-07`, `P9-09`…`P9-11`, `P9-19`…`P9-22`, `P9-24`, `P9-25`). Open: the performance section (`P9-12`…`P9-17`), `P9-08` and `P9-18`, which need a person and an environment, and `P9-23`, which is deferred to after launch by its own terms | — |
| **v1 total** | **281** | **265** | **203.5** | | |

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
- [x] **Q5** — Which email provider replaces `IdentityNoOpEmailSender`?
  *Owner: Ops · Needed by: Phase 1 (implemented P7)* · **Answer: whichever one Ops wants — it is
  configuration.** `P7-18` sends over SMTP (`CmsEmailOptions`), and every candidate — SendGrid,
  Mailgun, SES, Microsoft 365, a corporate relay — offers an SMTP endpoint, so choosing one is a host
  and a credential rather than a code change or a NuGet package chosen on Ops' behalf. With nothing
  configured the deployment runs `LoggingCmsEmailSender`, which writes what it would have sent and is
  reported as unconfigured rather than appearing to work. If a provider SDK is later wanted for
  deliverability reporting it is a second `ICmsEmailSender`, not a change to anything that calls it.
- [ ] **Q6** — Is a CDN in front of the site? Changes cache headers, adds a purge integration.
  *Owner: Ops · Needed by: Phase 6* · **Answer:** _pending_
- [ ] **Q7** — Is SVG upload permitted at all? Safest answer is no.
  *Owner: Security · Needed by: Phase 5* · **Answer:** _pending — but no longer blocking._ `P5-06`
  ships both branches behind `MediaUploadOptions.SvgPolicy` and **defaults to `Reject`**, which is
  the safe reading. Answering Q7 now changes one line of configuration rather than any code; a
  "yes" means setting `Sanitize` and accepting the strict profile in `SvgSanitizer`.
- [ ] **Q8** — Existing site to migrate, and must its URL structure be preserved?
  *Owner: Product · Needed by: Phase 3* · **Answer:** _pending_
- [ ] **Q9** — Retention/compliance obligations on content versions and audit logs?
  *Owner: Legal · Needed by: Phase 5* · **Answer:** _pending — but no longer blocking, and `P5-33`
  says exactly what an answer would change._ The **version** half is already configuration rather
  than code: `RetentionPolicy` implements the five clauses of [§11.7] and reads its window from
  `SiteSettings.VersionRetentionDays`, so a legal answer sets a number. The **audit-log** half has
  no policy at all — `AuditLog` grows without bound and nothing prunes it, which is a defensible
  default for a compliance question nobody has answered and is not one to carry to launch. Whichever
  way Q9 lands, an audit retention sweep is work that does not exist yet; it is recorded against
  `P9` rather than absorbed into Phase 5.
- [ ] **Q10** — Does self-service registration stay enabled, and with what default role?
  *Owner: Security · Needed by: Phase 1 (enforced P9)* · **Answer:** _pending — but no longer
  blocking._ `P9-04` ships both branches behind `CmsIdentityOptions.SelfRegistration` and **defaults
  to `Disabled`**, which is the safe reading of [§20.3]: the registration routes answer `404` rather
  than `403`, because a refusal a 404 would not have produced tells the caller the door is there.
  The other branch, `NoRole`, is the registration pages as they already behave — nothing in this
  application grants a role on registration — and it exists so that "we chose this" and "nobody has
  decided" are different states of the file. Answering Q10 now changes one line of configuration
  rather than any code. `ResendEmailConfirmation` stays open under both, because an account an
  administrator created still has an address to confirm.

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

**Raised during Phase 0 — decided in Phase 8:** the arm64 test fallback runs **Azure SQL Edge**,
which has no full-text search, so `P8-18`'s tests could not run on an arm64 developer machine under
the current fallback. **Answered 2026-08-18, and by none of the three options as stated:** the
service asks the same question the migration does — `SERVERPROPERTY('IsFullTextInstalled')` plus the
index's existence — and falls back to a `LIKE` scan, so Azure SQL Edge became a supported deployment
rather than a test-only compromise. The correctness suite therefore runs everywhere; only `P8-25`'s
500 ms budget is gated, and it skips with a message naming `CMS_TEST_SQL_IMAGE` for an arm64
developer who wants to run it under emulation.

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

- [x] **P3-01** `PageRoute`, `Redirect`, `NotFoundLog` entities + configurations, with `binary(32)` URL
  hash columns carrying the unique indexes (URLs exceed SQL Server's 900-byte key limit) [§23.5]. — 0.5 ed
  *2026-08-14 — four entities, not three: `PreviewToken` ships here too so migration #4 is one
  migration rather than two against the same tables. **`SiteUrls` in `Shared/Common/` is the piece
  worth knowing about.** Normalization and hashing live in one class and `Hash` normalizes first,
  because a hash taken over an unnormalized string indexes a URL nobody will ever ask for —
  `/About/` and `/about` would occupy two rows and defeat the very index they carry. Added
  `ColumnTypes.Sha256Hash` (`binary(32)`, fixed-width rather than `varbinary`: every value is
  exactly 32 bytes).*
  ***One EF behaviour cost a regenerated migration.*** `PageRoute` needs two indexes on `UrlHash` —
  the filtered unique one for published routes and a plain one preview resolves draft routes
  through — and EF Core hands back the **same index builder** for a repeated property set, so the
  second unnamed `HasIndex` silently reconfigured the first instead of adding anything. Caught by
  reading the generated migration; the plain index is now named explicitly. A missed index is
  invisible until somebody reads a query plan.*
  *Delete behaviour differs per table and each one is a decision: routes and preview tokens
  **cascade** (derived data with no life of their own, and Restrict would make a purge fail on rows
  it would only have had to delete), `Redirect.ToPageId` is **Restrict** so a missed rewrite in the
  purge path is a loud failure rather than an administrator's rule quietly vanishing, and
  `PreviewToken.PageVersionId` is **Restrict** so version retention cannot strand a shared link.
  `Cms/RoutingSchemaTests` asserts all of it against real SQL Server — 7 tests.*
- [x] **P3-02** Migration `AddCmsRouting` — migration #4, also adding `PreviewToken`. — 0.5 ed
  *2026-08-14 — reviewed statement by statement: four `CreateTable`s, no drop-plus-add, nine
  indexes, `Down` drops all four. `Up` and `Down` are both asserted from empty by
  `MigrationsApplyFromEmptyTests`, which now covers four migrations;
  `dotnet ef migrations has-pending-model-changes` is clean.*
- [x] **P3-03** `SlugService` in `Core/Routing/` — generation from title, normalization, Unicode/NFC
  handling with homograph warning, reserved-prefix checks (`/admin`, `/api`, `/media`, `/_blazor`,
  `/_framework`, `/account`, `/health`, `/alive`, `/sitemap.xml`, `/robots.txt`, `/preview`) [§10.2–10.3].
  — 1.5 ed
  ***Already landed, in P2, as `Core/Content/Slugs.cs`*** — `Generate` (accent folding, NFC,
  truncation that will not split a surrogate pair), `Validate` (format, length, reserved first
  segments, the homograph **warning** rather than an error), and `Normalize`. Sibling-uniqueness is
  in `PageService`, and explicit-URL format and reserved-prefix checking are there too. **No second
  service was written**: a `SlugService` in `Core/Routing/` would be a second copy of rules that
  already have one home, and the copy that drifts is the one nobody wrote a test for. Covered by
  `Core.Tests/Content/SlugsTests`.*
- [x] **P3-04** `UrlService` in `Core/Routing/` — route materialization, `UseExplicitUrl` support,
  cascade to all descendants on move/rename, single transaction, emits redirects for each old URL
  [§10.4]. — 2 ed
  *2026-08-14 — `SyncAsync` rebuilds a whole subtree in one pass: ancestors resolved from the
  materialized path in a single query, descendants walked in depth order so each parent's URL is
  computed before its children need it. **Nothing here calls `SaveChanges`**, following
  `PageTreeService` — a move, its route rebuild, and the redirects it emits are one atomic act and
  the caller owns the transaction. `IUrlService.Build` is a static: a page with `UseExplicitUrl`
  ignores its ancestor, and its descendants still build on it, so opting out relocates a branch
  rather than detaching what is under it.*
  ***Two routes per page, not one.*** A draft route (`IsPublished = 0`) exists from the moment a
  page is created, so preview can address it by URL before it is ever published; the published route
  exists only while it is, and is the one the filtered unique index governs. That is what lets an
  editor prepare a replacement at the URL a live page is still serving — asserted at both the schema
  and the service level.*
  ***Collisions are checked rather than left to the index***, for two reasons: a constraint violation
  reaches the client as a 500 naming nothing actionable, and a collision *inside* the subtree being
  rebuilt never reaches the database at all — two descendants computing one URL would be written in
  a single batch the index rejects wholesale. The refusal names the page holding the URL.
  `PublishingService` asks the same question on its shared check path so the dry run reports it too.
  *Wired into every path that can move a URL: `PageService.CreateAsync` (inside the creating
  transaction) and `PatchMetadataAsync` (only when a slug or explicit URL actually changed — a
  review-date patch must not walk a subtree), `PublishingService` publish and unpublish, and
  `RecycleBinService` delete, restore, and purge. A recycled page keeps its **draft** route and
  loses its published one; a restore refreshes the draft route, because a page restored at the root
  has a different URL from the one it had under its old parent and a stale draft route is a preview
  link that opens somebody else's page.*
- [x] **P3-05** `RedirectService` in `Core/Routing/` — automatic creation on URL change, loop detection
  at write and resolve time (max depth 10), chain flattening (`A→B` then `B→C` ⇒ `A→C`), manual
  overrides automatic, **live page wins over a redirect at the same URL**, hit counting [§10.5]. — 1.5 ed
  *2026-08-14 — all six behaviours, plus `IRouteResolver`, which is where "a live page wins" actually
  lives: routes are asked first, then a canonical-form correction, then redirects. **That ordering is
  the task's real content** — reversed, retiring a page and later reusing its URL would be impossible
  forever and nothing would report why.*
  ***Loop detection walks forward from the destination*** rather than checking only for the trivial
  self-reference, because the case that happens is `A→B` and `B→C` already stored and somebody adding
  `C→A`. Bounded by `MaxChainDepth` so a cycle already in the data cannot make the check itself run
  forever. At resolve time a cycle returns **null**, not the last hop before it closed: serving
  somewhere arbitrary is worse than the 404, and the cycle is logged for whoever has to fix it.*
  *`RecordHitAsync` is a single relative `ExecuteUpdate`, so concurrent hits add up rather than
  overwriting each other, and any failure is logged and swallowed — a redirect must never be less
  reliable than the page it points at. `RecordAutomaticAsync` leaves a **manual** redirect at the
  same source exactly as it is: a person made a decision about that URL and a tree move is not an
  argument against it.*
  *Also added the `IRouteResolver` canonical-form answer: `/About/` resolves to the page **and**
  reports the canonical URL, so P3-13 can 301 rather than serve the same content at two addresses.*
- [x] **P3-06** Redirect CSV import/export for bulk legacy-site migration. — 0.5 ed
  *2026-08-14 — service plus six endpoints under `/api/cms/v1/redirects` (list, create, patch,
  delete, import, export). Writes require **`Content.Publish`**, not `Content.Edit`: a redirect
  reaches anonymous visitors the instant it is saved, with no draft, no preview, and no publish step
  in between. There is deliberately no PATCH of `fromUrl` — a redirect *is* its source URL, and an
  edit that quietly deleted one rule and created another would leave the original URL serving
  nothing with no record it ever did. The import body is read as raw text, since what an operator
  has is a file.*
  ***A skipped row is a warning, never a failed file.*** A legacy list is thousands of rows and always
  has a handful of bad ones; refusing the document leaves somebody editing a spreadsheet with no
  report of what was wrong. Each skip carries its line number, and those warnings survive the 200.
  *The CSV reader is hand-written (four columns of URLs; a CSV package in `Core` for one method is
  not worth the dependency) and handles quoted cells and doubled quotes.*
  ***The round-trip test caught a real defect.*** The first version refused to update a **manual**
  redirect on import, meaning an export of this table could not be re-imported — which is the one
  thing the pair exists for. The rule is now that an import updates a row whatever its origin: an
  operator uploading a file has the same authority as the person who typed the row. Imported rows are
  marked manual, so a later tree move still leaves them alone.*
- [x] **P3-07** Complete the `link` and `pageReference` field types — internal links stored as `pageId`,
  **never as a URL string**, resolved to the current URL at render [D6, §7.1]. — 1 ed
  *2026-08-14 — `ILinkResolver` is the resolution half of D6, and it is **batched**: a page with a
  related-articles list and a navigation block resolves dozens of ids, and one query per link is the
  classic N+1 that only shows up under real content. It answers `Url`, `IsPublished`, and `Title`.*
  ***`IsPublished` and "has a URL" are deliberately different questions.*** Inside preview an
  unpublished target resolves to its draft URL and is badged; on the public site the same target
  resolves to nothing and the link degrades to text. Collapsing the two would either leak a draft URL
  to an anonymous visitor or make preview useless for walking an unreleased section [§12.3]. The
  title comes from whichever version the audience is looking at, for the same reason.*
  ***The two `notEnforcedUntil: "P3"` settings are now enforced, in two different places, and that
  split is structural.*** `allowedKinds` is a pure value check and lives in `LinkFieldType`.
  `allowedTemplates` cannot: answering "what template does page 44 use" needs the database, and a
  field type is a stateless singleton without one [§7]. It is a **publish check** in
  `PublishingService`, on the same seam that already checks a link target still exists — so the draft
  still saves (an editor must be able to store work in progress) and the publish is refused with the
  new `field.reference.notAllowed`. Zone-level references only; a reference inside a block needs the
  block's own captured revision resolved, which is P4's walk.*
  *An id naming no page is **absent from the resolver's result**, not an error — delivery renders
  plain text and logs [§15.3]. Throwing would take a whole page down because one card points at
  something somebody deleted.*

### 3.2 Rendering — 10 ed

- [x] **P3-08** `ContentManagementSystem.Rendering` infrastructure: `CmsTemplateBase`, `CmsZone`,
  `RenderContext` (with the accumulating `CacheTags` set), `[CmsTemplate]` and `[CmsBlockType]`
  attributes [§15.2]. — 2 ed
  *From [S2](./docs/spikes/s2-dynamic-ssr.md): name the render-mode enum **`CmsRenderMode`** — the
  spec's `RenderMode` collides with `Microsoft.AspNetCore.Components.Web.RenderMode` in every .razor
  file. Keep `CacheTags` per render, never shared across requests. Markers and structural hints must
  be elements or attributes: the Razor compiler strips HTML comments from .razor markup.*
  *2026-08-14 — the S2 shapes, plus `CmsBlockBase` and `CmsFieldRendererBase` (the parameter
  contracts `CmsZone` and `P3-09`'s renderers are written against) and `CmsPageHost`, the root that
  cascades the context and resolves the template. The two attributes already existed — `P1-25` needed
  them before anything could render — so what this adds is the other half of them: a
  `CmsComponentCatalog` that turns a stored `templateKey` or `blockTypeKey` back into a component.
  All four `CmsRenderMode` / per-render / attribute-marker constraints from the spike are honoured.*
  ***The scan is now one scan.*** `TemplateReconciler` had a private copy; the catalog would have
  been a second, and the duplicate-key rule would then have lived in two places — with the render
  path free to pick a winner the reconciler had refused. Both now go through
  `CmsComponentScanner` in `Core/Structure/`, registered by the new `AddCmsComponentScanning(...)`
  that `AddCmsStructureReconciliation` calls. The catalog additionally **refuses a declaration on a
  type that is not a component**, at startup: rendering it would fail one page at a time, in
  production, on whichever request first reached that content.
  ***Two deviations from the [§15.2] record, both deliberate.*** `RenderContext.Page` is a
  **`RenderPage`**, not the spec's `PublishedPage`: preview renders *any* version through this same
  pipeline [§12.1], so a type whose name asserts its contents are published would be a lie on every
  preview request — and the kind someone eventually leans on by skipping a check the name already
  seemed to make. Whether a version is live is the explicit `IsPublished` flag; the guarantee that
  anonymous delivery never loads a draft stays in the query, where [§20.1] puts it. And `CacheTags`
  is a **`CacheTagSet`** rather than a bare `ISet<string>`, so a renderer adds a dependency through
  `CacheTags.Media(id)` instead of concatenating a string — a hand-built tag that disagrees with the
  eviction side by one character is exactly the failure the tag scheme exists to prevent, and the two
  sides ship phases apart. The set also **seeds `page:{id}` and `tpl:{id}` in its constructor**: a
  tag that has to be remembered is one that gets forgotten on some path and leaves a stale page live.
  ***`RenderContext` also carries the captured `ContentSchema`, which the spec's four members do
  not.*** Without it a renderer cannot see its own configuration, and `P3-09` would have had to
  reshape the context one task later. The rule it introduced is worth knowing: **the renderer is
  chosen by the payload's stored `type` discriminator, never by the schema** — a value has to be read
  by whatever wrote it — and the schema supplies configuration *only when the two agree on the field
  type*. Configuration belonging to a different field type is worse than none: it parses, and the
  value renders under bounds nobody chose for it. A mutation that dispatched on the schema instead
  fails two tests.
  *23 bUnit and unit tests in `Core.Tests/Rendering/`, written against `RenderTreeBuilder` because
  the unit project is a plain library rather than a Razor one — which costs nothing here, since what
  is under test is the dispatch rather than the markup. The composition is asserted four levels deep
  (host → template → zone → renderer), along with the [§15.3] non-events this task can already
  reach: an unknown template key logs an error and renders nothing, an unknown field type key logs a
  warning and renders nothing, and neither throws. **The empty render for an unknown template is
  temporary** — `P3-11` replaces it with the fallback layout carrying the page's text content, and
  no endpoint serves any of this until `P3-13`.*
  ***One build-time trap, already documented in `.claude/rules/blazor.instructions.md`.*** The first
  build of these components failed with a wall of `RZ1021` and bogus `CS` errors in untouched files;
  it is a poisoned Razor build server on SDK 10.0.301, not the markup. `dotnet build-server shutdown`
  and rebuild.*
- [x] **P3-09** Field renderer components in `Rendering/Fields/` for every Phase 1 field type. — 2 ed
  *Carries the renderer half of [`ADR-0014`](./docs/adr/0014-field-type-components-resolved-by-the-hosting-layer.md):
  built-in field types answer null for `RendererComponent`, so this task builds the catalog that maps
  a field type key to its renderer, plus the **startup check that every registered field type
  resolves to one**. Without that check a forgotten registration is invisible until someone looks at
  the page — delivery treats a missing renderer the same way it treats an unknown field type key,
  rendering nothing and logging [§15.3]. Editors are the mirror image in `P6`.*
  *2026-08-15 — all eighteen, plus `BuiltInFieldRenderers` (the key → component table ADR-0014 says
  the hosting layer must own) and `FieldRendererCatalog`. **The catalog is built from the field type
  registry, not from that table**, which is what makes it describe this deployment: a field type
  somebody removed has no renderer, so content authored against it renders nothing and logs, exactly
  as [§15.3] requires. Seeding from the table instead would keep rendering values whose field type
  was deliberately retired. Each field type is asked for its own renderer first and answered for only
  when it declines, so an extension author above `Rendering` in the reference graph needs no entry
  anywhere. `CmsRenderingStartupService` resolves both catalogs while the host starts — a declaration
  on a non-component becomes a deployment-time throw rather than a production request — and **reports
  a field type with no renderer rather than throwing**: unlike a duplicate key it has a defensible
  outcome and every other page is unaffected, so what had to be fixed was it being silent.*
  ***One task-shaped gap this had to close first.*** A block's structured properties had no way to
  reach a renderer at all — `CmsBlockBase.Text` covers the text-shaped ones and nothing covered an
  image, a link, or nested blocks — so `P3-10` could not have exercised every field type inside a
  block. Added `CmsBlockProperty`, the block-level `CmsZone`, fed by a `BlockRenderContext` the
  `blocks` renderer cascades around each item; the zone and block dispatch now share one
  `FieldValueDispatch`, since a zone and a block property are the same thing at render time exactly
  as they are at validation time.
  ***Cache tags are the half of `media` and `reusable` that is finished, and deliberately so.*** P5
  and P4 own the picture and the item store, but a page rendered before them would be invisible to
  invalidation forever — nothing goes back and re-renders it — so `media:{id}` and `ru:{id}` are
  declared now, against things that do not exist yet. `link` and `pageReference` tag `page:{id}`
  **including ids that fail to resolve**, because a reference to a page that is not published yet
  must re-render when it is. Media renders [§15.3]'s placeholder-with-alt-text, which is already the
  right answer for an item nothing can resolve.
  *Six decisions worth knowing before someone reports one as a bug. **`json` renders nothing,
  silently** — it is developer-only data for a block's markup to read, and a "useful" default grows
  into printing authored data inside a `<script>` block; it is not logged because it is intended,
  unlike everything else that renders nothing. **`number` is emitted exactly as stored**, so
  precision survives and no page depends on the server's culture. **`date` and `dateTime` format
  under `InvariantCulture`**, and a `dateTime` is shown in UTC and says so — a cached page is served
  to everybody and cannot be built from anything that varies by reader. **`color` carries
  `data-color` and never an inline `style`**, which would be the one place [§20.5]'s CSP had to be
  relaxed for authored content. **`choice` and `pageReference` branch on the shape that is stored,
  not on the `multiple` setting**, because a property narrowed to single selection still has pages
  holding arrays. And **`html`'s renderer is `RawHtmlRenderer`**: `HtmlRenderer` is already
  `Microsoft.AspNetCore.Components.Web`'s, and a collision inside a `.razor` file resolves to
  whichever `@using` came last.*
  ***Sanitization runs again on render, on all three paths*** (ADR-0008) — markdown through the one
  `IMarkdownRenderer` so preview and delivery stay byte-identical, HTML rich text under the
  configured profile, `html` under `Developer`. `link` re-applies the scheme allowlist too, because
  the field type's write-time check does not cover rows that arrived by import or restore, and
  `javascript:` in an `href` is stored XSS. An unrecognised `profile` falls back to `Basic`: a
  mistyped setting may only ever strip more than intended.
  ***One bug the Server suite caught that no unit test could.*** `FieldRendererCatalog` had two
  public constructors — one over the registry, one over an explicit `IEnumerable<IFieldType>` for
  tests — and the container registers every field type as `IFieldType`, so both were resolvable,
  neither was a superset, and **the host refused to start** with an ambiguous-constructor error that
  named constructors rather than renderers. The test path is now the static `For(...)`. Worth
  recording because the same shape will recur on any catalog built over a registered collection.
  *58 new tests in `Core.Tests/Rendering/` (132 across the folder), driven through `CmsZone` and the
  real catalog rather than by instantiating components — a value reaching the wrong renderer and a
  renderer drawing the wrong thing look identical on the page. The one asserted for every field type
  is that a malformed, absent, or stale value renders nothing and never throws. `Core.Tests` 1154
  green; `Server.Tests` 216 of 217, the one failure being `VersionAndDiffTests.RetentionKeepsWhat
  AnEditorWouldBeUpsetToLose`, **which fails identically on a clean checkout of `main`** and is
  unrelated to rendering.*
  ***That failure is now understood and fixed — it was never a retention defect, and it was never
  ours to blame on rendering.*** Entity timestamps were stamped from `DateTimeOffset.UtcNow` inside
  `AuthDbContext`'s `SaveChanges` interceptor, while `RetentionPolicy` computed its cutoff from the
  registered `TimeProvider`. Two clocks, so a suite that advanced the fake one moved the cutoff and
  left the rows where they were, and **whether the test passed depended on the real calendar date it
  ran on** — it was green until 2026-08-15 09:00 UTC and red from that minute. `AuthDbContext` now
  reads every stamp it writes (`CreatedOn`, `ModifiedOn`, `DeletedOn`, and the `AuditLog` row) from
  the injected clock, and `FakeTimeProvider` starts at the real current instant so the offset cannot
  reopen. Recorded in [Changes to existing code](#changes-to-existing-code). Worth knowing for the
  next phase that ages a row out: scheduling (`P7`) and purging need exactly this seam.
  ***And it was hiding a second test.*** `NothingIsPrunedFromAPageInTheRecycleBin` asserts
  `after == before`, which passed for free while nothing anywhere was prunable. It only began
  testing the recycle-bin exclusion once the clocks agreed — a green test proving nothing, kept
  honest by the red one beside it.*
- [x] **P3-10** Two reference templates in `Rendering/Templates/` and three reference block types in
  `Rendering/Blocks/`, between them exercising every field type. — 2 ed
  *2026-08-15 — `marketing-landing` and `article`; `hero-banner`, `rich-text`, and `feature-grid`.
  The split is deliberate: the landing page is almost entirely block lists and the article is almost
  entirely single values, so between them every one of the eighteen renderers has a zone or property
  it is actually reached through. **`ReferenceContentTests` asserts that against the field type
  registry rather than against a list restated in the test**, so a field type added in a later phase
  fails until the reference content gives it a home — which is the only thing that makes shipping
  reference content worth the files.*
  ***`feature-grid` is a container on purpose.*** Its `items` property is a `blocks` value, so the
  render path goes zone → blocks → block → block property → blocks → block. That is the shape that
  breaks if the block context is cascaded rather than scoped per block, or if a nested block's
  captured revision is resolved against the outer block's schema, and nothing else in the reference
  set can reach it.
  *Two smaller decisions. **The zone definitions are not in these files and cannot be** — they are
  database rows a `Developer` owns [§8.1] and promotes as JSON [§27.1] — so each component's XML doc
  carries a table of the *intended* field type per zone, which is what the tests are written against.
  And the class is `RichTextSection`, not `RichText`: a type of that name one namespace from
  `RichTextRenderer` is exactly the pair that gets imported by mistake in a `.razor` file where
  `@using` order picks the winner.*
  ***One thing this broke, which is worth knowing before it recurs.*** Four Server tests were using
  `article`, `marketing-landing`, and `hero-banner` as fixture keys. Those keys now belong to
  deployed components, so the startup reconciler inserts them before the fixture does and the insert
  collides on the unique index. Renamed to `news-story`, `campaign-landing`, and `promo-banner`;
  `PageWorkbench.AddTemplateAsync` now says in its documentation that its key must be one no
  component declares, and `UseTemplateAsync` is the method for a test that needs a template which
  actually renders.*
- [x] **P3-11** Per-zone error boundaries and the full fallback matrix from [§15.3]: unknown template
  key, unknown field type, missing media, unpublished reusable content, renderer throwing. — 1 ed
  *From [S2](./docs/spikes/s2-dynamic-ssr.md): derive from **`ErrorBoundaryBase`**, not the stock
  `ErrorBoundary` — overriding `OnErrorAsync` is what gets page id, zone key, version id, and block
  id into the log (`P3 #8`), and the stock fallback text is not acceptable on a public page. Put a
  boundary at **both** levels, per zone and per block.*
  *2026-08-15 — `CmsErrorBoundary` over `ErrorBoundaryBase`, wired into `CmsZone` (per zone) and
  `BlocksRenderer` (per block), plus `CmsFallbackTemplate` for the matrix's first row. All three
  spike constraints honoured. Asserted against all three failure shapes — lifecycle, mid-
  `BuildRenderTree`, post-await — and the half-written case has its own test: a renderer that emits
  an element and then throws leaves nothing behind, because Blazor discards the failing subtree
  rather than flushing what it had.*
  ***The marker is the boundary's own default, not an `ErrorContent` at each call site.*** The
  templated-component route would have meant writing `Context="_"` on a component that already has a
  `Context` cascading parameter, which is at best confusing and at worst ambiguous to the Razor
  compiler; and a marker supplied per call site is a marker two call sites will eventually spell
  differently. It is an element with attributes rather than an HTML comment, because the Razor
  compiler strips comments out of `.razor` markup — the trap S2 found.
  ***`CmsFallbackTemplate` asks the field types for the page's text rather than reading the JSON.***
  Each zone's value is dispatched to the field type its own `type` discriminator names and reduced
  through `ExtractSearchText` — the same method the search index is built from. A walk written here
  would have to know that rich text hides its words inside markup and that a block list nests them
  two levels down, and it would be wrong about the next field type somebody adds. It carries **no
  `[CmsTemplate]` attribute**, so it cannot be chosen in the create-page picker and the reconciler
  cannot write a row for it; a test asserts that.
  *One test-harness fact worth recording: under bUnit the post-await failure needs a
  `WaitForAssertion`, because the boundary's own re-render is queued behind the continuation.
  Delivery does not have to care — `HtmlRenderer.RenderComponentAsync` waits for quiescence before
  the markup is read at all — and `TheDeliveryRenderPathSeesTheBoundaryMarkerToo` asserts that
  through a real `HtmlRenderer` rather than assuming it.*
- [x] **P3-12** `PublishedContentService` in `Core/Delivery/` — resolve → load → deserialize → render;
  read-only, cache-ready, filters on `PublishedVersionId` **at the data layer** so drafts cannot leak
  [§20.1]. — 2 ed
  *2026-08-15 — one query, no tracking, no writes, returning an immutable `PublishedContent`.
  **The projection selects through `page.PublishedVersion` and never mentions `DraftVersion`**, which
  is what makes `P3 #3` a property of the SQL rather than of a reviewer noticing: the draft row is
  not in the result set to be picked by mistake. The soft-delete query filter is left in place, so a
  recycled page is not published content — `IgnoreQueryFilters` here would make the recycle bin a way
  to keep serving a page nobody can see in the tree.*
  ***Resolution is deliberately not folded in.*** `IRouteResolver` already owns the ordering that
  makes a live page outrank a redirect at the same URL [§10.5], and a second entry point into it here
  would be a second copy of that decision. The interface is one method taking a page id.
  *Two shapes had to be settled. **`PublishedContent` is one flat record**, page facts and payload and
  captured schema together, because a version *is* those things together and they are read in one
  query and will be cached as one entry [§16.1]; `RenderPage.From` narrows it to what a renderer may
  read, which is the point of keeping the two types — a renderer must not be able to reach the SEO
  metadata or the payload wholesale. And **`RenderContext.For(content, mode)` is the one supported
  way to start a render**, so the three things that must travel together cannot be assembled from
  different sources by a caller in a hurry.*
- [x] **P3-13** Delivery endpoint `app.MapGet("/{**slug}", …)` in `Server/Delivery/`, registered
  **after every other endpoint**; 404 page (itself a CMS page); `NotFoundLog` writing [§15.1, §10.6].
  — 1 ed
  *From [S2](./docs/spikes/s2-dynamic-ssr.md): **render to a buffer, then set headers, then write.**
  Cache tags accumulate during the render, so anything that streams sends headers before the tag set
  is complete — producing a page that never invalidates. No public delivery component may opt into
  streaming rendering.*
  *2026-08-15 — `DeliveryEndpoint`, `CmsPageRenderer`, and `CmsDeliveryDocument`, mapped last by
  `MapCmsDelivery()`. The spike's ordering is honoured through `HtmlRenderer`-to-string rather than
  `RazorComponentResult`: the latter works today only because the response happens to be buffered,
  and one `[StreamRendering]` attribute anywhere below would silently break it.*
  ***`CmsDeliveryDocument` is a separate document from `App.razor`, and that is the whole of [§5.3]
  in one file.*** It carries no `@rendermode`, no `blazor.web.js`, and no `ImportMap`; serving the
  backoffice shell to an anonymous reader would download the editor to every visitor and undo the
  reason the two front doors exist. `DeliveryTests` asserts the absence of `blazor.web.js` on a
  public page, which is the assertion output caching depends on.
  ***`NotFoundLog` writing is an upsert, update-first.*** The overwhelming majority of 404s repeat a
  URL already in the table — that is the premise of the report — so the common path is one relative
  `ExecuteUpdate` that cannot lose a concurrent increment. An insert race is caught and retried as an
  update rather than surfaced, and **a request carrying no referrer does not erase the one a previous
  request supplied**: "who is still linking to this" is what the column is for, and one live example
  is enough. The referrer is attacker-controlled, so it is truncated rather than allowed to fail the
  write.
  ***A reserved prefix is a bare 404, not the site's 404 page*** — recorded as
  [`ADR-0020` (D20)](./docs/adr/0020-catch-all-route-ordering-and-reserved-prefixes.md). `GET
  /api/cms/v1/no-such-thing` matches no API endpoint and therefore reaches the catch-all, which was
  cheerfully serving HTML; a JSON client then reports a parse failure somewhere else entirely, which
  is the same misdirection `UseStatusCodePagesWithReExecute` produced in `P1-21`. The prefix list is
  **read from `Slugs.Reserved`**, not restated, so a page cannot be created at an address delivery
  then declines to serve.
  *Also here: `ETag` computed over the finished document (not derived from the version id — a link
  target moving or a reusable item republishing changes the page without changing its version
  [§16.4]), `Last-Modified` from the publish timestamp, `Cache-Control` per [§16.1], and a 404 that
  is never cached anywhere, since a dead URL is very often one somebody is about to publish at.
  **`CacheOutput` is deliberately not applied** — that is P8, and a response cached before there is
  anything to evict it would make every publish look broken — but the accumulated tags are published
  on `HttpContext.Items` under `DeliveryEndpoint.CacheTagsItemKey` for the policy that will read them.*
- [x] **P3-14** Scope interactive routing to `/admin` in `Server/Components/Routes.razor`; keep public
  pages static SSR — the decision that makes output caching possible [§5.3]. *(Existing-code change.)*
  — 0.5 ed
  *2026-08-15 — most of this fell out of `P3-13`: public pages no longer reach the Blazor router at
  all, since delivery renders its own document. What was left was the invariant, and it is now
  **enforced rather than documented**: `InteractiveRoutingTests` reflects over the routable
  components in the Server and Client assemblies and fails if any component carrying a
  `RenderModeAttribute` has a route outside `/admin`. That turns ADR-0002 from a convention into
  something a test holds. It has a second test asserting the scan finds the backoffice pages, because
  a test that asserts an empty set passes just as well when the scan finds nothing at all.*
  *The one violation when the rule was written was `ClientHello` at `/client-hello` — scaffolding,
  interactive, sitting in the public route space. Moved to `/admin/client-hello`. Note the scaffolding
  `Home.razor` still claims `/`, so the site root does not reach the CMS; [§10.3] gives `/` to a CMS
  page, so a real deployment deletes that component. Left in place because it is template scaffolding
  rather than CMS code, and recorded in ADR-0020 because it is otherwise discovered as "why does my
  home page not publish".*
- [x] **P3-15** Route-ordering integration tests asserting `/_blazor`, `/_framework`, `/api`, `/admin`,
  `/account`, `/health` are not shadowed by the catch-all *(mitigates R6)*. — 0.5 ed
  *2026-08-15 — `Server.Tests/Delivery/RouteOrderingTests`, 10 tests. **They assert outcomes, not
  registration order**: precedence is one way to get this right and order is another, and what must
  not change is that these paths reach the endpoints that own them whatever anybody does to
  `Program.cs` later. The assertions are on the thing that distinguishes the two answers — the API
  returns `application/json`, `/health` returns the literal text `Healthy`, the backoffice document
  carries `blazor.web.js` and the delivery document never does.*
  *The last test is the one that matters most and looks least important: an ordinary content URL
  **does** reach the catch-all. Without it, deleting the catch-all entirely would pass every other
  assertion in the file.*
  *`/_blazor` is reserved but not mapped — it is the SignalR endpoint for interactive **server**
  rendering, which this solution never uses (ADR-0002). Keeping it reserved costs nothing and means
  turning that mode on later cannot collide with a published page. Recorded in ADR-0020.*

### 3.3 Preview — 4.5 ed

**Complete 2026-08-15.** One thing [§12.3] lists that this section has no task for, recorded so it is
not lost by being between two lists: **compare mode** — published and draft side by side. The
comparison an editor actually makes is served today by the version diff of `P2-14` at
`/admin/pages/{id}/versions`, which compares any two versions and preselects published against draft.
A rendered side-by-side is a different thing and would belong in `P6` with the rest of the authoring
experience; it is not a Phase 3 acceptance criterion and nothing here waits on it.

- [x] **P3-16** `GET /preview/{pageId}?version=` in `Server/Delivery/Preview/` — authenticated, renders
  **any** version through the shared rendering path, output cache disabled, `X-Robots-Tag: noindex`,
  floating preview toolbar (version label, status, exit) [§12.1]. — 1.5 ed
  *2026-08-15 — `PreviewContentService` in `Core/Preview/` plus `PreviewEndpoint`, `PreviewChrome`,
  `CmsPreviewDocument`, and `PreviewToolbar` in `Server/Delivery/Preview/`, mapped by
  `MapCmsPreview()` before the catch-all. Requires `Content.Read`, which is the permission that
  already means "may see unpublished content" [§21.1], enforced by a **policy** rather than only in a
  service: there is no service call on the refusal path here to make the check in.*
  ***The chrome and the page are two documents, and that is the task's real content.*** The toolbar
  lives in an outer document and the page is rendered into an `iframe` by the same `CmsPageRenderer`,
  the same `CmsDeliveryDocument`, and the same components that serve an anonymous visitor. There is
  no "but this is a preview" branch anywhere below, so preview fidelity is a property of the code
  rather than a promise somebody has to keep up [§12.1] — and it is what gives `P3-21` a real
  viewport to constrain. A toolbar injected into the rendered markup would have been the one
  difference between preview and delivery, sitting in the middle of the thing preview verifies.
  ***`PreviewContentService` is a separate service from `PublishedContentService`, deliberately.***
  The whole value of the published one is that its projection selects through
  `page.PublishedVersion` and never mentions the draft, which is what makes `P3 #3` a property of the
  SQL; an "include drafts" flag on it would put the draft row back into the result set of the query
  the public site runs, one boolean away from being served [§20.1]. **It also authorizes nothing** —
  two callers reach it and prove their right to be there in completely different ways, so a check in
  there would have to be satisfiable by the anonymous path, which means bypassable, which means not a
  check.
  *Three smaller decisions. **Headers are applied on entry to every handler**, not at each write, so
  a path that returns early — including every refusal — cannot be the one that forgets `no-store` or
  `X-Robots-Tag`; the failure being prevented is not one cached preview but an unpublished page in a
  shared cache, which nothing the CMS does can evict. **`/preview` is excluded from
  `UseStatusCodePagesWithReExecute`**, the same fix `P1-21` made for `/api`: preview writes its own
  refusal documents ("this link has expired", "this preview is no longer available"), which are the
  whole of what a stakeholder with no account has to go on, and re-executing a body-less 403 through
  the site's error page reported it as a 404 besides. And **`cms.page.render.duration` now records
  only `Live` renders** — preview traffic is a handful of editors rendering deliberately unusual
  versions, and folding it in would move the percentiles of a series watched for regressions, in a
  direction nobody could attribute.*
- [x] **P3-17** `PreviewToken` entity + hashed-token issuance/validation in `Core/Preview/`: 32 bytes
  CSPRNG, base64url, **only the SHA-256 hash stored**, default 7-day expiry (max 30), `MaxUses`,
  revocation [§12.2]. — 1 ed
  *2026-08-15 — the entity shipped with migration #4 in `P3-01`; this is `PreviewTokens` (the secret)
  and `PreviewTokenService` (issue, list, revoke, revoke-in-bulk, check, redeem). Issuing needs
  **`Content.Edit`, not `Content.Publish`**: sharing work for review is the ordinary act of whoever
  is doing the work, and a link that needed the publish permission would mean an author could not get
  their own draft looked at, which is the entire feature [§21.1].*
  ***The hash is taken over the decoded bytes, not the encoded string.*** base64url has spellings
  that differ as text and decode identically, so hashing the string would make one secret hash to
  several values and the lookup would depend on which spelling a mail client passed through. Pinned
  by a test that asserts the two are different, because the symptom is a link that issues
  successfully and never works — with nothing left to compare against, since the token is stored
  nowhere.
  ***Validating and recording a use are one operation, and there are two entry points on purpose.***
  `RedeemAsync` re-states the `MaxUses` guard *inside* the `ExecuteUpdate`, so two simultaneous
  requests for a single-use link do not both pass — which is the whole meaning of a single-use link.
  `CheckAsync` is the non-consuming half the chrome uses: **a use is a view of the content, not a
  request for the furniture around it**, and a `MaxUses = 1` link that spent its one view on the
  toolbar would never show anybody a page.
  *Four smaller decisions. **Shape is checked before the database is asked**, so a crawler walking
  `/preview/s/{anything}` is answered without a query — the cheap half of `P3-18`'s rate limit.
  **A revoked token and a token that never existed get one answer**, because confirming that a string
  was once real narrows the search for anybody probing and the person holding a revoked link has to
  go back to whoever sent it either way. **A recycled page is a distinct outcome**, reached with
  `IgnoreQueryFilters`, because "this preview is no longer available" and "this link is invalid" send
  the reviewer to different people — and the use is *not* spent on it, so the link still works if the
  page comes back. **An expiry over thirty days is refused rather than clamped**: a link somebody
  believes lasts a year and which actually lasts thirty days is a support ticket on day thirty-one,
  and the request is the last moment the misunderstanding is visible.*
  ***One thing the spec's wording overpromises, now recorded in the contract.*** Pinning a version id
  stops a link following the page to a *different* version, however many times it publishes. It
  cannot freeze the draft, because the draft is the one version a page is allowed to keep editing
  [§11.1] — so a link shared against the draft shows the latest save. Both behaviours are useful and
  the sender chooses between them by picking a version; `ALinkSharingTheDraftFollowsTheDraftRow`
  asserts it rather than leaving it to be discovered.
- [x] **P3-18** `GET /preview/s/{token}` anonymous shareable preview; serves exactly one page version,
  always `noindex, nofollow`, excluded from `sitemap.xml`, rate-limited. — 0.5 ed
  *2026-08-15 — two routes (`/preview/s/{token}` and `/preview/s/{token}/content`) sharing every line
  of the editor path except how the caller proved they may be there. **The version is carried by the
  token and never by the query string**, so nothing in the URL a reviewer holds can be edited to
  reach a different one — which is what "serves exactly one page version" has to mean.*
  ***Rate limiting is a fixed window partitioned by address, 60 requests a minute, on these two
  routes alone.*** Anonymous by design means there is no account to key on; an unknown address falls
  into one shared bucket rather than being exempt, because unlimited is the wrong side to fail on for
  a link that reads unpublished content. `429`, not the framework's default `503`: a link being
  clicked too fast is the client's problem to slow down about, and `503` tells every intermediary the
  site itself is unhealthy. A limiter in front of the whole site would be a denial-of-service tool
  pointed at its own visitors, and `TheSharedPreviewIsRateLimitedAndTheRestOfTheSiteIsNot` asserts
  both halves.
  ***`sitemap.xml` exclusion needed no code, and that is the point.*** The Phase 8 sitemap is built
  from published `PageRoute` rows; a preview URL is a token or a page id under a reserved prefix and
  is not a route. The exclusion is structural, which is the only kind that survives somebody writing
  the sitemap generator without having read the preview code. Recorded in `PreviewEndpoint`'s remarks
  so the reader who goes looking for the filter finds out why there isn't one.
  *Status codes are part of the contract here: `410 Gone` for a link that worked and has stopped —
  expired or used up — and `404` for one that never worked or whose page is gone. The difference is
  what an intermediary is told, and what the person reading is told to do next.*
- [x] **P3-19** `POST /preview-tokens`, `GET /preview-tokens?pageId=`, `DELETE /preview-tokens/{id}` +
  revocation UI. — 0.5 ed
  *2026-08-15 — four endpoints (the three above plus `DELETE /preview-tokens?pageId=` for the bulk
  revocation [§12.2] asks for), and `/admin/pages/{id}/preview-links` over an extended `IPageClient`.
  **`DELETE` revokes; it does not delete.** The row is stamped and kept, because "this link was
  revoked on the 3rd, by this person" is the answer somebody needs when a stakeholder reports that a
  link stopped working — and it is the only record of who could once read an unpublished page, which
  a verb that removed the row would destroy at the exact moment it starts mattering.*
  ***The secret has no member to travel on.*** `PreviewTokenSummary` carries no token, so the list
  and the revoke responses could not leak it even if a caller wanted them to;
  `IssuedPreviewToken` exists only as the body of the creating response. `NoResponseButTheCreating
  OneEverCarriesTheSecret` asserts that against the **raw** response text rather than through the
  typed shape, since deserializing into a record with no token member would hide a token that was on
  the wire.
  *The screen is built around the one thing that cannot be undone — the secret is shown once, and the
  banner says so rather than leaving somebody to find out by closing the tab. Revoked and expired
  links stay in the table: the question it is opened to answer is usually "why did the link I sent
  stop working", and a list that filtered them out could only answer "there is no such link", which
  reads as a bug in the CMS rather than as an expiry.*
- [x] **P3-20** Draft-link resolution inside preview — an internal link to an unpublished page resolves
  to *that page's* draft, clearly badged [§12.3]. — 0.5 ed
  *2026-08-15 — the resolution half already existed: `ILinkResolver.ResolveAsync` takes
  `includeUnpublished`, `LinkRenderer` and `PageReferenceRenderer` pass `Context.IsPreview`, and
  `P3-07` made "is published" and "has a URL" deliberately different questions. What this task found
  missing was the **badge**, and the fix is the interesting part.*
  ***`cms-link-draft` was a class and nothing more, which is not "clearly badged".*** The document a
  reader sees is the delivery document, styled by the *site's* stylesheet — written by whoever built
  the site, and knowing nothing about the CMS's class names. A badge that were only a class would
  therefore be invisible on every deployment nobody had told to add a rule for it. `CmsDraftBadge`
  emits visible text inside the anchor, so a screen reader announces "Enterprise, draft" and a sighted
  reviewer sees it whatever the stylesheet does; the class stays, for a site that wants to style it.
  *A component rather than a string in two renderers, so the wording, the class, and the tooltip are
  one decision. Asserted three ways: a draft target is badged, a **published** one is not (a badge on
  everything says nothing, and a reviewer would stop reading it within a page), and a mixed
  `pageReference` list badges exactly its unpublished entry. The end-to-end case is in
  `PreviewTests` — the same link, at the same moment, resolving to `/unreleased-section` with a badge
  in preview and to plain text with no URL on the live page.*
- [x] **P3-21** Device-width preview frame (desktop/tablet/mobile) via a width-constrained iframe. — 0.5 ed
  *2026-08-15 — `?device=desktop|tablet|mobile`, three CSS classes in `wwwroot/css/preview.css`, and
  the toolbar's buttons are **plain links**. That keeps the whole preview chrome static SSR: no render
  mode, no circuit, and not a byte of JavaScript on a document whose job is to show what a reader
  would see.*
  ***The constraint is on the `iframe`, which is why the frame exists at all.*** An iframe's width
  *is* the viewport its content reads, so a media query inside behaves as it would on the device. A
  `div` with a `max-width` would make the layout narrow while leaving every breakpoint reporting the
  desktop width — a preview that agrees with the real thing right up until the moment it matters.
  *Two smaller decisions. **The device is not passed down to the framed page**: if it were, a page
  could render differently inside preview, which is the one thing preview must not permit. And **an
  unreadable value falls back rather than failing** — the parameter is a view preference in a URL
  people paste to each other, and a mangled one must produce a preview at the default width, not an
  error page. The widths are classes rather than inline styles because authored content already
  renders under a policy that forbids those [§20.5], and the preview chrome must not be the one page
  that needs the policy relaxed.*

### Tests — Phase 3

- [x] **P3-22** Unit: slug generation, URL construction, redirect chain flattening and loop detection.
  *2026-08-14 — slug generation was already covered by `Core.Tests/Content/SlugsTests` (P2). Added
  `Core.Tests/Routing/SiteUrlsTests` for normalization, hashing, joining, and segment-aware
  containment — 27 cases, almost all of them one assertion said several ways: two spellings of an
  address must produce one hash, because a normalizer that misses a case does not fail loudly, it
  produces a second route row the index accepts and no request ever resolves to. The one that would
  be got wrong by a plain `StartsWith` is pinned explicitly: `/new` does not contain `/news`.*
  *Chain flattening and loop detection are in `Server.Tests/Routing/RedirectServiceTests` rather
  than here — both are database facts (a unique index on a hash, a chain assembled across rows), and
  asserting them against a fake would be asserting that the fake works. 15 tests.*
- [x] **P3-23** bUnit: field renderers, block components, template composition, unknown-type fallbacks.
  *2026-08-15 — the field renderers themselves were covered by `P3-09`. Added `Rendering/
  ReferenceContentTests` (9) over the two reference templates and three block types, and
  `Rendering/CmsErrorBoundaryTests` (8) over the boundaries, and rewrote the two `CmsPageHostTests`
  cases the fallback template changed. All of it drives `CmsPageHost` and the **real** component
  catalog — scanned over the Rendering assembly, so the composition under test is the one a
  deployment runs — rather than instantiating components, because a value reaching the wrong renderer
  and a renderer drawing the wrong thing look identical on the page.*
  *Each renderer assertion looks for markup only that renderer emits (`<time class="cms-date"`,
  `data-color=`, `<li class="cms-tag">`), not merely for the text somewhere on the page. 1172 green
  in `Core.Tests`, up from 1154.*
- [x] **P3-24** Integration: anonymous delivery of a published page; 404 for an unpublished page.
  *2026-08-15 — `Server.Tests/Delivery/DeliveryTests`, 9 tests over the real HTTP pipeline against
  SQL Server: content arranged through the real page, draft, and publishing services and then
  requested by an `HttpClient` carrying no identity. Covers `P3 #1`, the anonymous half of `P3 #2`,
  `P3 #3`, `P3 #4`, and `P3 #11`, plus the canonical-form 301, the configured CMS 404 page, ETag
  revalidation, and that a 404 is never cached.*
  ***`P3 #3` is asserted byte-for-byte***, not as "the response does not contain the draft text": the
  weaker form passes for a response that changed in some other way nobody looked at. Three draft
  saves between the two requests.
  *This needed one addition to `PageWorkbench`: `CreateClient()`, so content can be arranged and then
  requested against the **same** application and database. Its permissive `ICmsAuthorization` is in
  force for those requests too — irrelevant to delivery, which authorizes nothing, but it would be
  wrong to write an authorization test through that client, and the method says so.*
- [x] **P3-25** Integration: URL change 301s the page and every descendant.
  *2026-08-14 — `Server.Tests/Routing/UrlServiceTests`, 11 tests driven through the real page,
  publishing, and recycle-bin services against SQL Server. The headline one renames a grandparent
  and asserts all three published URLs moved **and** that all three old ones resolve to redirects
  with a 301. Also here: the criteria P3 #6 (a live page reusing a vacated URL outranks the redirect
  the vacating created) and the half of P3 #7 that is about a page-target redirect following its
  page through a second move in one hop.*
  *Two of these tests initially failed because the scenarios collided on P2's sibling-slug rule
  before ever reaching a URL collision — which is itself worth recording: **the only way two pages
  can collide on a URL without being siblings is an explicit URL**, so that is the case the URL
  check exists for and the case the tests now use.*
- [x] **P3-26** Integration: preview-token expiry, revocation, and non-recoverability from the database.
  *2026-08-15 — `Server.Tests/Delivery/PreviewTests`, 23 tests over the real HTTP pipeline against
  SQL Server, plus `Api/Cms/PreviewTokenApiTests` (5) for the management contract and
  `Core.Tests/Preview/PreviewTokensTests` (8) for the secret itself. Content is arranged through the
  real page, draft, and publishing services, then requested with a client carrying an editor's roles
  for the authenticated path and **no identity at all** for the shared-link one.*
  *The three the task names: **expiry** advances the fake clock to six days (still good) and then to
  eight (`410 Gone`) — a link that expired immediately would pass an assertion that only checked the
  second half; **revocation** asserts the link stops working *and* that the row survives it; and
  **non-recoverability** reads the stored row and asserts the column is 32 bytes that are neither the
  token nor any encoding of it, then asserts it really is `SHA-256` of the token, so the first
  assertion is not passing vacuously.*
  *Beyond the wording: a version belonging to another page is refused under this page's URL (the pair
  is the address), `Content.Read` is required and `MediaManager` is refused, a single-use link is
  spent by the content request and not by the chrome, a recycled page answers "no longer available"
  without spending a use, and the rate limit answers `429` while the rest of the site is untouched.
  `PreviewTests` needed one addition to `PageWorkbench`: `CreateClient` now takes roles, since these
  are the first tests where the **endpoint policy** rather than the service check is what refuses.*
- [ ] **P3-27** Performance benchmark harness for page render, with CI regression thresholds (starts
  here per the plan's cross-cutting performance workstream).
  *Baselines from the spikes, both **excluding** database access — treat them as the floor, not a
  projected latency: schema validation ~1.2 µs per block ([S1](./docs/spikes/s1-runtime-schema.md)),
  component rendering ~7 µs per block ([S2](./docs/spikes/s2-dynamic-ssr.md)). Warm **every** input
  size before measuring any of them; measuring sizes in sequence made a 200-block page look faster
  per block than a 50-block one, purely tiered-JIT artifact.*
  ***Left open when 3.2 closed on 2026-08-15.*** It is the start of a cross-cutting workstream rather
  than a test of the rendering pipeline, and it needs a benchmark project, a committed baseline, and
  CI thresholds — none of which rendering produced or blocks on. What it can now be built against
  exists: the reference content of `P3-10`, and `cms.page.render.duration` from `P3-28`, which gives
  the same measurement from a running site.*
- [x] **P3-28** Telemetry: `cms.page.render.duration`, `cms.route.resolution.miss` [§24.1].
  *2026-08-15 — both on `CmsMetrics`, recorded from the delivery path, asserted by
  `Server.Tests/Delivery/DeliveryTelemetryTests` through real requests rather than by calling the
  recorder — what goes wrong is never the instrument, it is the instrument not being reached, and a
  test that records a measurement by hand passes for every one of those.*
  *Two cardinality decisions. The render histogram is tagged **`template`** and not page id: pages
  are not slow, templates are, and an untagged histogram averages the one expensive layout into
  invisibility — while one series per page is how a metrics bill falls over. The miss counter is
  **untagged**, deliberately not carrying the requested URL, since that population is entirely
  crawler- and attacker-supplied; which URLs missed is `NotFoundLog`'s question, and this counter
  answers the different one of whether the rate has changed. `cache_hit` is recorded from the first
  release although output caching is P8, because a histogram whose meaning silently changes when
  caching is switched on is worse than one that could always say which of the two it measured.*
- [ ] **P3-29** Visual regression baseline (Playwright screenshots) for the two reference templates.
  ***Left open when 3.2 closed on 2026-08-15**, and now unblocked: the two reference templates exist
  (`P3-10`) and a published page is reachable over HTTP (`P3-13`), which are the two things it was
  waiting for. What it still needs is its own decisions — seeded content the screenshots are taken
  of, a stylesheet worth photographing, and a baseline-image policy that survives being run on a
  different platform from CI. The markup itself is pinned by `P3-23` in the meantime.*
- [ ] **P3-30** Confirm Q8 (legacy URL preservation) is answered and its redirect import path tested.
- [x] **P3-31** ADR: catch-all route ordering and reserved prefixes.
  *2026-08-15 — [`ADR-0020` (D20)](./docs/adr/0020-catch-all-route-ordering-and-reserved-prefixes.md).
  Records the two failures that are actually distinct — a path an endpoint owns being matched by the
  catch-all, which routing precedence already prevents, and a path an endpoint owns matching nothing
  so the catch-all answers anyway, which it does not — and the four decisions that close them: map
  last, refuse reserved prefixes with a bare 404, keep the prefix list in `Slugs.Reserved` alone, and
  scope interactivity to `/admin`. Also records the two things a reader will otherwise trip over:
  `/_blazor` is reserved but not mapped, and the scaffolding home page still owns `/`.*

### Acceptance criteria — Phase 3

- [x] **P3 #1** A published page is reachable at its URL by an anonymous request and renders its content.
  *2026-08-15 — `DeliveryTests.APublishedPageIsReachableAtItsUrlByAnAnonymousRequest`. Asserts the
  document as well as the status: a doctype, the page's title, the template's own marker, and the
  authored text — plus that `blazor.web.js` is **not** on the page, which is the static-SSR half of
  [§5.3] and the precondition for output caching.*
- [x] **P3 #2** An unpublished page returns 404 to anonymous requests and renders in preview for an
  editor.
  *2026-08-15 — the anonymous half is met by
  `DeliveryTests.AnUnpublishedPageReturnsNotFoundToAnAnonymousRequest`, and it is a 404 because the
  published route the resolver looks up does not exist, not because of a check somebody could forget
  to write. The preview half is `PreviewTests.AnUnpublishedPageRendersInPreviewForAnEditor`, which
  asserts **both at once**: the same page, at the same moment, is a 404 to a client with no identity
  and readable to one carrying an editor's roles. Asserting them together is what makes it a
  statement about the page rather than two statements about two fixtures.*
- [x] **P3 #3** **After publishing, further draft edits do not change the anonymous response.**
  *2026-08-15 — `DeliveryTests.DraftEditsAfterAPublishDoNotChangeTheAnonymousResponse`, comparing the
  two responses **byte for byte** across three intervening draft saves. The mechanism is in the SQL:
  `PublishedContentService` projects through `page.PublishedVersion` and never mentions the draft, so
  the draft row is not in the result set to be picked by mistake.*
- [x] **P3 #4** Changing a published page's slug 301s the old URL to the new one, for the page and all
  descendants.
  *2026-08-15 — the descendant cascade and the stored redirect rows were already asserted at the
  service level by `P3-25`. `DeliveryTests.AChangedSlugRedirectsTheOldUrlPermanently` adds the half
  that was missing: the endpoint actually serves them, with a 301 and the new `Location`.*
- [x] **P3 #5** A redirect chain `A→B`, then `B→C`, is flattened to `A→C`; a cycle is refused at write
  time.
  *2026-08-14 — `RedirectServiceTests.AChainIsFlattenedOnWriteRatherThanWalkedOnEveryRequest` asserts
  the stored row, not just the resolution, so a resolver that merely walked the chain would fail it.
  Cycles are covered twice: the trivial self-reference, and the one that actually happens — `C→A`
  closing a loop through two flattened rows neither of which mentions `C`.*
- [x] **P3 #6** A live page at a URL takes precedence over a redirect with the same `FromUrl`.
  *2026-08-14 — `UrlServiceTests.ALivePageAtAUrlOutranksARedirectWithTheSameSource` does the whole
  sequence: publish at `/offers`, move away leaving a redirect, then publish new content back at
  `/offers` and assert the resolver answers with the new page.*
- [x] **P3 #7** An internal link renders the target's *current* URL even after that target has moved.
  *2026-08-14 — `LinkResolutionTests.AStoredPageIdResolvesToThatPagesCurrentUrlAfterItHasMovedTwice`.
  Twice rather than once on purpose: one move can be passed by a resolver that happens to read a
  redirect, two cannot. Nothing rewrote the payload — the id was always the stored value.
  **Rendering this through a component is P3-09**; what is asserted here is the resolution the
  renderer will call.*
- [x] **P3 #8** A template throwing inside one block renders the rest of the page and logs the failure
  with page id, zone key, and version id.
  *2026-08-15 — `CmsErrorBoundaryTests`. The isolation is asserted for all three shapes a renderer can
  fail in (lifecycle, mid-`BuildRenderTree`, post-await) and at both levels, with a three-block list
  whose middle block throws and whose siblings still render — the case a zone-level boundary alone
  would fail. The log assertion is literal about the four facts, because a stack trace names a
  component and not which of four hundred pages built on it was being rendered.*
- [x] **P3 #9** An unknown field type key renders nothing, logs a warning, and does not throw.
  *2026-08-15 — asserted in `P3-08` for the zone case and `P3-09` for the block-property case, and
  extended here by `ReferenceContentTests.ABlockTypeNoComponentDeclaresIsSkippedAndItsSiblingsStill
  Render`, which is the same rule one level down: the retired block is skipped and logged and the
  list around it is unaffected.*
- [x] **P3 #10** A shareable preview link renders for an anonymous browser, expires on schedule, and is
  revocable; the token is not recoverable from the database.
  *2026-08-15 — four tests in `PreviewTests`, one per clause. **Renders for an anonymous browser**:
  the client carries no cookie, no role header, and no identity of any kind — the token is the whole
  of its authority. **Expires on schedule**: still good at six days, `410 Gone` at eight.
  **Revocable**: individually and in bulk, with the row kept and stamped both times.
  **Not recoverable**: the stored column is 32 bytes that are neither the token nor any encoding of
  it, and separately are exactly `SHA-256` of it — so whoever can read that table holds a hash and no
  way to turn it back into a working link. `PreviewTokenApiTests` adds the client's side of the same
  question: no response but the creating one ever carries the secret, asserted against raw response
  text rather than through a typed shape that has no member to put it in.*
- [x] **P3 #11** Unresolved URLs are recorded in `NotFoundLog` with an accurate hit count.
  *2026-08-15 — `DeliveryTests.AnUnresolvedUrlIsRecordedOnceWithAnAccurateHitCount`: three requests
  for one dead URL, one row, `HitCount` 3. One row per URL rather than one per request is what keeps
  a crawler from making this the largest table on the site [§10.6].*

**Exit gate — DEMO MILESTONE.** The full loop is demonstrable to a stakeholder: define a template →
create a page → fill zones → save draft → preview → publish → view anonymously → edit draft → confirm
the public page is unchanged → publish again. — [~] every step built and asserted **2026-08-15**;
the demonstration itself has not been performed.
*Every step of the loop now exists and is covered end to end by automated tests against SQL Server
over real HTTP: the structure API for the template, `PageApiTests` for creation and zone filling,
`DraftAndPublishTests` for the draft, **`PreviewTests` for preview**, and `DeliveryTests` for the
publish, the anonymous view, the draft edit that changes nothing, and the second publish. What is
open is only the demo: nobody has yet sat a stakeholder in front of it and walked the ten steps,
which is what this gate actually asks for. Nothing is known to be missing — but "a test asserts it"
and "a person watched it work" are different claims, and this gate makes the second one.*

**Risks:** ~~R6 (catch-all route ordering)~~ **closed 2026-08-15** — the catch-all is mapped last,
reserved prefixes are refused from `Slugs.Reserved` alone, and `RouteOrderingTests` asserts the
outcome for `/api`, `/admin`, `/Account/Login`, `/health`, `/alive`, and `/_framework`
([ADR-0020](./docs/adr/0020-catch-all-route-ordering-and-reserved-prefixes.md)).
R7 (static SSR + `DynamicComponent`) closed at the Phase 0 gate on the S2 spike, and is now running
in shipped code: `CmsPageHost` down through four levels of `DynamicComponent`, with no interactive
render mode anywhere beneath it.

---

## Phase 4 — Reusable content

**Objective:** content authored once — footers, banners, carousels — appears on many pages and updates
everywhere in one publish. **12 ed** · Entry: Phase 3 exit. Parallel with Phase 5.

- [x] **P4-01** `ReusableContent` and `ReusableContentVersion` entities + configurations per [§23.2].
  — 1 ed
  *2026-08-16 — the same shape as `Page`/`PageVersion` with the address removed: a draft pointer, a
  published pointer, an unfiltered unique `Key`, and a soft-delete filter. Two decisions are worth
  reading. The key index is **unfiltered** while the library index is filtered, because a deleted
  item still owns its key — a filtered unique index would let a second item take the key of one in
  the recycle bin and make the first unrestorable. And `Status` reuses `PageVersionStatus` rather
  than getting an identical enum of its own, since [§23.2] gives `WorkflowTask` a nullable key to
  each kind of version precisely because one approval flow serves both.*
- [x] **P4-02** Migration `AddCmsReusableContent` — migration #5. — 0.5 ed
  *2026-08-16 — `Up` and `Down` both apply from empty, asserted continuously by
  `MigrationsApplyFromEmptyTests`. The storage guarantees the model depends on — the unfiltered key
  index, the filtered library index, the version-number uniqueness a pin relies on, `rowversion`
  conflicts, and restrict-on-delete from `BlockType` — are asserted against real SQL Server by
  `ReusableContentSchemaTests`.*
- [x] **P4-03** `ReusableContentService` in `Core/Content/` — CRUD plus draft/publish/version lifecycle
  **reusing the Phase 2 publishing primitives** rather than duplicating them. — 2.5 ed
  *2026-08-16 — one service where pages have three, because with the tree, the URL, the redirects,
  and the SEO panel removed what is left of each is a handful of methods over the same two rows.
  Nothing here is a second implementation of anything: version numbering is `VersionNumbers`
  (extended with `NextForReusableAsync`, kept there because the rule — highest ever issued, never
  the count — is load-bearing for a **pinned** placement in a way it is not for a page), payload
  checking is `IContentSchemaValidator`, reference projection is `IContentReferenceProjector` with
  `ContentSourceType.ReusableContentVersion`, and impact is `IReferenceQueryService`.*
  ***The decision that made that reuse possible*** is recorded on `ReusableContentVersion.ContentJson`
  and in `ReusableContentSchema`: an item's payload is an **ordinary content payload envelope** whose
  `templateKey`/`templateRevision` carry the block type key and revision and whose `zones` object
  holds the block's properties. A zone and a block-type property are the same thing to every reader
  of a payload — a keyed value carrying the field type that wrote it — and `ContentSchema` and
  `BlockTypeSchema` are both lists of `ContentPropertySchema`. Storing it any other way would have
  meant a parallel validator, indexer, diff, and remapper, each aware that a reusable item is
  shaped like a block. Recorded as
  [`ADR-0021` (D21)](./docs/adr/0021-reusable-content-stored-as-a-payload-envelope.md), with the
  alternatives that were weighed and the one honest cost — the envelope's `templateKey` member now
  sometimes holds a block type key.*
- [x] **P4-04** `reusable` field type completed: editor picker, renderer, late binding by default,
  optional `pinnedVersionId`, reference extraction [§9.2]. — 1.5 ed
  *2026-08-16 — `ReusableRenderer` resolves through `IReusableContentResolver` and renders the item
  through the component its **block type** declares, so a reusable item and an inline block of the
  same type produce identical markup. `allowedTypes` is now enforced, at the publish check rather
  than in the field type, for the reason `allowedTemplates` is: a field type is a stateless singleton
  with no database and "what shape is item 3" is not answerable from the stored value. That needed
  `ContentSlots`, which resolves the schema slot behind a reference **at any depth** — and closed a
  gap on the way, since `allowedTemplates` had been enforced for zone-level page references only and
  silently ignored inside blocks. `pinnedVersionId` now travels on the `ContentReference` row rather
  than staying in the payload, which is what lets the impact check split forty pages into the ones
  that change and the two that do not without opening a single payload.*
  *Also added: the **`rawHtml` block component**. The database has seeded a built-in `rawHtml` block
  type since P1 so reusable content has a shape without a developer defining one, and no component
  declared the key — so that seeded row was orphaned from the moment it was inserted and the
  commonest reusable shape rendered nothing.*
  ***The picker is still the shared plain control.*** Every non-text field type in the admin is a
  read-only JSON textarea until P6 supplies the field editors; a bespoke picker for this one field
  type would be the only one, and the first thing P6 would replace.*
- [x] **P4-05** Pinned-version UI affordance: badge plus an "update to latest" action. — 0.5 ed
  *2026-08-16 — two halves in two places, which is where the pin actually lives. `CmsReusableBadge`
  renders inside **preview** and carries the state as `data-` attributes rather than a control,
  because the previewed page is static SSR with no interactivity beneath it [§5.3]. The action is
  `PinnedPlacements` on the **page** editor: a pin is a property of the placement, so the person who
  can clear it is the person editing the page, not the person publishing the item. It clears every
  pin at once — a page with several almost always got that way from one duplication — and writes the
  draft without publishing, since adopting a newer shared version on a live page is a publish
  somebody performs.*
- [x] **P4-06** `ReusableContentResolver` in `Core/Delivery/` — resolves to the *published* version in
  the delivery path, with a recursion-depth guard and cycle detection. — 1.5 ed
  *2026-08-16 — as narrow as `IPublishedContentService` and for the same reason: the published filter
  is in the query, not in a check afterwards. Both guards live on `ReusableResolutionChain`, an
  **immutable** linked list pushed as the renderer descends — a shared mutable set would report the
  second of two sibling placements of the same item as a cycle, which it is not. Cycles are refused
  at write time; this is the backstop for content that arrived by import, restore, or hand edit,
  where the only acceptable answer on a public request is to stop, log, and render the rest.*
- [x] **P4-07** `ReferenceQueryService` in `Core/Content/` — impact analysis / where-used over
  `ContentReference`, returning the [§9.4] shape (`affectedPages`, `affectedPageCount`,
  `pinnedPageCount`, `warnings`). — 1.5 ed
  *2026-08-16 — three round trips **per level of nesting**, not per referencing page, which is what
  keeps a footer on ten thousand pages from being ten thousand queries at publish time.*
  *Two rules in it are not obvious and both were found by a failing test. **A pin protects the edge
  it sits on and nothing beneath it**: a page pinning a footer to v3 does not change when the footer
  changes, but v3 of that footer still places a banner late-bound, so the page *does* change when the
  banner does — a transitive arrival therefore overrides a direct pin. And **only a page's live
  versions count**: a page that used to place an item late-bound and now pins it has reference rows
  for both, and counting the archived one reported the pinned page as changing.*
  *Two members beyond the spec's shape: `affectedReusableItems`, because an item nothing places
  directly can still be on the whole site through another item; and `isTruncated`, because the counts
  are exact while the list is capped at 100 so a confirmation dialog is not a download.*
- [x] **P4-08** `/references` endpoints for pages, media, and reusable content. — 0.5 ed
  *2026-08-16 — one route per target kind rather than one taking a type parameter, so a client cannot
  ask a question the system has no answer for by mistyping a string. An entity nothing points at
  answers `200` with an empty impact rather than `404`: "nothing uses this" is the answer the delete
  button needs, and distinguishing it from "no such entity" would put an existence probe for every
  id behind a read permission that grants no such thing. The media route ships now although the
  library is P5 — its answer is honest today, and shipping it with the others means the where-used
  panel was written once.*
- [x] **P4-09** `/api/cms/v1/reusable` endpoints mirroring the page endpoints minus URLs and the tree
  (CRUD, versions, publish, references, impact). — 1.5 ed
  *2026-08-16 — deliberately shaped like the page routes, because an item is a page's twin with the
  address removed and an editor who has learned one should not have to learn the other. `If-Match` is
  mandatory on the draft save and honoured-but-optional on the metadata patch, exactly as for pages.
  `ReusableContentApiTests` asserts the statuses, the permissions, and — the one that matters — that
  an unacknowledged publish comes back `422` **with the blast radius in `warnings`**, since that
  refusal *is* the confirmation dialog's content.*
- [x] **P4-10** Delete guard: deleting reusable content that is still referenced is **refused**, with an
  accurate where-used list [§9.4]. — included above
  *2026-08-16 — refused at the **soft** delete, not only at a purge, and refused for a reference held
  only by a draft. A deleted item is invisible to the resolver, so cascading would blank a zone on
  every page holding it, discovered by a visitor; and a draft placement becomes a broken published
  one the moment that page is published. The refusal names the pages, because "replace the
  placements first" is not actionable without them.*
- [x] **P4-11** Plain admin screens in `Client/Components/Admin/Reusable/`: library, editor, where-used
  panel, publish-impact confirmation dialog (required whenever `affectedPageCount > 0`). — 1 ed
  *2026-08-16 — `ReusableLibrary`, `ReusableEditor`, and `WhereUsedPanel`, with `IReusableClient`
  implemented twice (HTTP in WebAssembly, services directly on the server) so the screens pre-render
  with real content. The confirmation is staged rather than trusted: the screen re-reads the impact
  immediately before the irreversible click rather than using the one it loaded on open, since an
  editor who left the tab open would otherwise be shown an hour-old number. **The dialog is not the
  guard** — the server refuses an unacknowledged publish whose blast radius is non-zero — so a screen
  that skipped it, or a script that never had one, still cannot change forty pages silently.*
  *The payload/textarea rules moved into a shared `PlainSlotValues`, used by the page editor and this
  one: a zone and a block-type property are the same thing to a payload, and two copies would
  eventually disagree about what an emptied box means. Both screens are under the axe gate.*
- [x] **P4-12** Audit: record the reusable-content publish **with its impact list**, so "why did 40
  pages change at 14:02?" is answerable [§9.3]. — included above
  *2026-08-16 — an explicit `AuditLog` row written **inside the publish transaction**, with its own
  `Type` rather than one of `AuditType`'s three. The change interceptor structurally cannot answer
  this question: a publish's consequence is on rows it did not touch, and the interceptor will
  faithfully log a new version row and a changed pointer while explaining nothing. Page **ids** are
  stored rather than titles, because an id is the part that is still true when somebody reads the
  entry back months later.*
- [x] **P4-13** Measure cache-invalidation fan-out cost on a high-reference item; record the baseline for
  P8 tuning *(R8)*.
  *2026-08-16 — **≈ 2.8 ms** for one item on 40 published pages, warm, recorded in
  [`docs/phase-4-fanout-baseline.md`](./docs/phase-4-fanout-baseline.md) and guarded by
  `ReferenceFanOutTests`. The number is less interesting than the shape it demonstrates: query count
  is bounded by nesting depth (5) and not by page count, so the eviction list is cheap to compute and
  the cost in P8 will be the eviction itself. The threshold in the test is an order-of-magnitude
  tripwire at two seconds, not a tolerance — it guards the one regression that would hurt, the walk
  becoming per-page.*

### Tests — Phase 4

- [x] **P4-14** Unit: cycle detection and depth guard in `ReusableContentResolver`.
  *2026-08-16 — `ReusableRendererTests`, thirteen cases through `CmsZone` rather than by calling the
  renderer, so what is asserted is the dispatch as delivery performs it. The depth case builds a
  chain of **distinct** items one longer than the ceiling, so it is the depth guard under test and
  not the cycle guard, and asserts the exact level that renders and the exact one that does not —
  one more would mean the guard counted wrong, one fewer that legitimate content stopped short.*
- [x] **P4-15** Unit: impact analysis counts, split by pinned and late-bound.
  *2026-08-16 — asserted where it is true, against SQL Server: `APinnedPageDoesNotChangeWhenANewer
  VersionPublishes` checks `AffectedPageCount` 2 and `PinnedPageCount` 1 on the same publish whose
  rendered output it also checks. The counts come from reference rows, so a unit test over a double
  would assert the arithmetic and skip the part that was actually wrong twice while writing it.*
- [x] **P4-16** Integration: publish a reusable item → three referencing pages change without being
  republished.
  *2026-08-16 — `PublishingANewVersionChangesEveryLateBoundPageWithoutRepublishingThem`, and both
  halves are asserted: every page's **rendered document** over real HTTP shows the new text, and
  every page's `PublishedVersionId` is byte-identical to what it was before. The second half is the
  one that makes it goal G4 rather than a fan-out somebody wrote.*
- [x] **P4-17** Integration: a pinned page does not change when a newer version publishes.
  *2026-08-16 — asserted on the rendered document, since that is where an auditor would look.*
- [x] **P4-18** Integration: unpublished reusable content renders nothing, logs, and appears in the
  broken-references report.
  *2026-08-16 — `UnpublishingRendersNothingOnDependentPagesAndSaysSo` for the first clause, and
  `ReusableRendererTests` for the log, which is asserted for its **reason** and not merely its level:
  "not published", "deleted", and "pinned version gone" have different remedies, and the renderer
  names the remedy in the line.*
  *The broken-references **report screen** does not exist yet — it is a backoffice surface that
  belongs with the dashboard in P6. What it will be built from does: every unresolved placement logs
  a distinct reason, and `ReferenceQueryService` answers the query it will list.*
- [x] **P4-19** Integration: delete-while-referenced is refused with the correct list.
  *2026-08-16 — `DeletingAReferencedItemIsRefusedWithTheWhereUsedList`, asserting the conflict, the
  code, that the message names every page, and that the item is still there afterwards. Its
  companion asserts the other direction: an item nothing places is deleted and stops resolving,
  which the soft-delete query filter makes true without the resolver asking.*

### Acceptance criteria — Phase 4

- [x] **P4 #1** A reusable item is created, published, and referenced from three pages.
  *2026-08-16 — `AnItemIsCreatedPublishedAndReferencedFromThreePages`, asserted through the
  where-used query rather than by counting the payloads that were written: the query is what every
  guard and every count in the phase is built on, and a payload the indexer failed to walk would
  pass the first kind of check and fail every promise made on the second.*
- [x] **P4 #2** **Publishing a new version of the reusable item changes all three published pages
  without republishing them.**
  *2026-08-16 — `P4-16`. The mechanism is an absence: nothing in `ReusableContentService` touches a
  page. Publishing repoints `ReusableContent.PublishedVersionId` and stops, and every late-bound
  placement reads that pointer at the moment the page is served.*
- [x] **P4 #3** A page pinned to version 3 does not change when version 4 is published, and its UI shows
  a badge plus an "update to latest" action.
  *2026-08-16 — the content half is `P4-17`; the UI half is `P4-05`'s two components, both under the
  axe gate with a **stale** pin in the fixture, since a pin that matched the published version would
  render the panel with the branch that matters unchecked.*
- [x] **P4 #4** The publish-impact dialog reports the correct affected-page count, split by pinned and
  late-bound.
  *2026-08-16 — `P4-15` for the counts, `ReusableContentApiTests` for the `422` that carries them.*
- [x] **P4 #5** Deleting reusable content that is still referenced is refused, with an accurate
  where-used list.
  *2026-08-16 — `P4-19`, at the service and again at `409` over HTTP.*
- [x] **P4 #6** Unpublishing reusable content renders nothing on dependent pages, logs a warning, and
  appears in the broken-references report.
  *2026-08-16 — `P4-18`. The pages still **serve** — one retired fragment must not 404 a page — and
  the space where the item was is simply empty [§15.3]. The report screen is P6's, as noted there.*
- [x] **P4 #7** A reusable item referencing itself (directly or transitively) is refused; a depth guard
  prevents runaway recursion at render time.
  *2026-08-16 — both clauses, and the transitive case is the one a direct self-reference check would
  miss entirely: `AnItemThatPlacesItselfThroughAnotherItemIsAlsoRefused` closes the loop through a
  row that mentions neither end. The render-time guard is `P4-14`.*

**Exit gate:** one reusable publish updates all late-bound pages; pinned pages unchanged. — [x] met on
2026-08-16, by `P4-16` and `P4-17` together: the same publish that changes two pages' rendered
documents leaves the third's alone, with no page republished.

**Risks:** R8 (invalidation fan-out) — **measured, not closed.** Computing the eviction list is cheap
and bounded by nesting depth rather than page count ([`P4-13`](#phase-4--reusable-content)); what
remains untested is evicting the entries, which has no implementation until P8.

---

## Phase 5 — Media library and image pipeline

**Objective:** editors upload, organize, edit, and reference images safely and with good delivery
performance. **23.5 ed** · Entry: Phase 3 exit. Parallel with Phase 4.

> **Q3 resolved:** SkiaSharp (MIT). **AVIF is not produced in v1** — renditions are WebP plus the
> original format [§13.9.1]. Build the format capability assertion in `P5-08` so an unsupported encode
> fails loudly at startup rather than returning null at runtime.
>
> **Q7 (SVG policy) no longer blocks.** `P5-06` ships both branches behind
> `MediaUploadOptions.SvgPolicy` and defaults to `Reject`, the safe reading of an unanswered
> question. Answering Q7 changes configuration, not code.

### 5.1 Storage and upload — 9 ed

- [x] **P5-01** `MediaItem`, `MediaFolder`, `MediaRendition` entities + configurations per [§23.3],
  including `UNIQUE (Sha256) WHERE IsDeleted = 0` for deduplication. — 1.5 ed
- [x] **P5-02** Migration `AddCmsMedia` — migration #6. — 0.5 ed
- [x] **P5-03** `IMediaStore` abstraction + `FileSystemMediaStore` in `Core/Media/Stores/` —
  path-traversal-guarded, stores **outside `wwwroot`**, keys server-generated from content hashes
  [§13.2]. — 1 ed
- [x] **P5-04** `AzureBlobMediaStore` against the Azurite resource added in P0. — 1 ed
- [x] **P5-05** Upload pipeline steps 1–4 in `Core/Media/Upload/` [§13.3]: size limits
  (`RequestSizeLimit` + `FormOptions.MultipartBodyLengthLimit`), extension allowlist, magic-number
  sniffing (declared MIME must match actual bytes), decode-bomb guard (reject `width*height > 100 MP`).
  AVIF **uploads** rejected in v1. — 1.5 ed
  *Done in `MediaUploadService` + `MediaTypeSniffer` + `MediaTypeCatalog`. The service enforces its
  own size ceilings so the CLI and tests get them too. 2026-08-16 — the ASP.NET half landed with
  `P5-23`: a `MediaBodySizeLimit` on the endpoint (so Kestrel refuses an oversized body before any
  middleware reads it, including the antiforgery filter) plus `FormOptions.MultipartBodyLengthLimit`
  on the form read, both derived from the same `MediaUploadOptions` the pipeline reads.*
- [x] **P5-06** Upload pipeline step 5 — SVG policy per **Q7**: strict sanitization profile (no
  `<script>`, `<foreignObject>`, external refs, event handlers) **or** outright rejection. — 0.75 ed
  *Both branches ship. `SvgUploadPolicy` defaults to `Reject`; `SvgSanitizer` implements the strict
  profile over AngleSharp's DOM for deployments that opt in, and stores its output rather than the
  upload. Q7 now selects a setting rather than gating code.*
- [x] **P5-07** Upload pipeline steps 6–10: pluggable `IMalwareScanner` with quarantine, SHA-256 dedupe,
  EXIF orientation via **MetadataExtractor** with `SKCodec.EncodedOrigin` fallback baked into pixels then
  **all metadata stripped** (GPS in a published photo is a privacy incident), persist original, queue
  standard rendition generation. — 1.25 ed
  *Rendition generation is lazy rather than queued (ADR 0007): warming six sizes of every upload
  would encode far more than any page asks for. Quarantined bytes are kept under a key with no
  extension and no row pointing at it.*
- [x] **P5-08** Chunked/resumable upload for large files with progress reporting
  (`Server/Api/Cms/Media/` + `Client/`). — 1.5 ed
  *2026-08-16 — `ChunkedUploadService` plus five endpoints under `/media/uploads`. **It is a
  transport, not a second way into the library**: parts are staged under an `incoming` prefix and
  the assembled bytes go through the identical `IMediaUploadService` call the single-request route
  uses, so every refusal applies to both — `AFileWhoseBytesDisagreeWithItsNameIsRefusedWhenTheSessionIsFinished`
  is the test that holds that line. Session state is a JSON manifest in the store beside its own
  fragments rather than a table: an in-progress upload is transient data with exactly the lifetime
  of the parts it describes, so abandoning one is a delete instead of a row and a sweep that could
  disagree — and it needs no migration, leaving #7 to `P7-08` as planned. Parts arrive in order and
  the session reports the index it wants next, which is what makes an interrupted upload resumable
  rather than merely restartable; progress is reported from the **server's** count of bytes it
  holds, because a bar driven by what the client wrote moves forward through failures.*

### 5.2 Image processing — 7.5 ed

- [x] **P5-09** `IImageProcessor` abstraction + `SkiaSharpImageProcessor` (sole v1 implementation) in
  `Core/Media/Processing/`, with a `SupportedOutputFormats` capability set **asserted at startup**
  [§13.9]. — 2 ed
- [x] **P5-10** Non-destructive edit model: `MediaItem.EditsJson`, `EditsVersion`, library-scope vs.
  usage-scope edits, revert-to-original. Original bytes never modified [§13.4]. — 2 ed
  *2026-08-16 — write side landed with `P5-23`. `MediaEdits` moved to `Shared/Contracts/Media/` so
  the editor, the request body, the column, and the processor all name one type. `PUT
  /media/{id}/edits` and `POST /media/{id}/revert` both increment `EditsVersion` — the revert too,
  or caches would go on serving the cropped version under URLs the site still emits.
  `EditingAnItemLeavesItsStoredOriginalByteForByteIdentical` fetches the original through its signed
  URL before and after an edit and compares the bytes.*
- [x] **P5-11** Operation set: `rotate 0|90|180|270`, `flip h|v`, normalized `crop {x,y,w,h}`, resize
  per rendition, normalized `focalPoint {x,y}`. — included above
- [x] **P5-12** Focal-point cropping math and rendition spec normalization in
  `Core/Media/Processing/`. — 1.5 ed
- [x] **P5-13** Rendition generation in `Core/Media/Renditions/` — **per-key semaphore** so N concurrent
  cold requests produce one encode, persistence to `MediaRendition`, lazy population. — 2 ed
  *`RenditionKeyLocks` is a reference-counted per-key lock registered as a singleton; the unique
  index on `(MediaItemId, SpecHash)` is the backstop for a race between two instances. `P5-30` now
  proves the twenty-concurrent-requests case end to end, counting the `cms.media.rendition.generated`
  measurements rather than a stub.*

### 5.3 Delivery — 7 ed

- [x] **P5-14** Signed rendition endpoint `GET /media/{id}/{w}x{h}/{mode}/{name}.{ext}` in
  `Server/Media/`: HMAC-SHA256 signature validation over the normalized parameter set, allowlisted
  widths (`320, 640, 960, 1280, 1920, 2560`), modes `crop|contain|cover|pad` [§13.5]. — 1.25 ed
- [x] **P5-15** `Accept`-based WebP negotiation with `Vary: Accept`; **AVIF rejected at the spec-parsing
  layer**, never silently producing an empty response. — 0.75 ed
- [x] **P5-16** Cache headers `public, max-age=31536000, immutable`; `EditsVersion` folded into the
  signature so a library edit changes every URL and busts client and CDN caches. — 0.5 ed
  *2026-08-16 — hardened once `P5-10`'s write side made stale URLs reachable. A URL signed against a
  superseded generation is validly signed, so it had to be refused on the strength of the version it
  names: `RenditionService` answers `RenditionFailure.Stale` and the endpoint returns `410`. Serving
  it would have rendered the item's current edits and cached them under the old version's key —
  a permanently wrong picture at a URL that says `immutable`.*
- [x] **P5-17** Media serving safety [§20.7]: `Content-Type` pinned to the **sniffed** type,
  `X-Content-Type-Options: nosniff`, `Content-Disposition: inline` for images and `attachment` for
  documents. — 0.25 ed
- [x] **P5-18** Signing-key rotation with a grace period during which the previous key still validates
  [§20.8]. — 0.25 ed
- [x] **P5-19** `media` and `mediaList` field types completed: editor picker, inline crop/rotate/focal
  UI, reference extraction. — 1.5 ed
  *2026-08-16 — the three picker settings lost their `notEnforcedUntil` and are enforced on the
  **publish path**, beside `allowedTemplates` and the reusable `allowedTypes`, for the structural
  reason those two are there: a field type is a stateless singleton with no database, and "how wide
  is item 812" is not a question it can ask. `MediaContentValidator` judges them against the picture
  the page will **show** — library edits and this placement's crop applied first — which is what
  makes a 4:3 photograph legal in a 16:9 slot once an editor has cropped it there. **Q's syntax is
  settled**: `aspectRatio` is `W:H` or a decimal, matched within one percent, because a normalized
  crop applied to whole pixels lands a pixel or two off every time. The picker is `MediaSlotEditor`
  in the page editor — the first structured field type with a real control before Phase 6, and it
  earns it: a media reference cannot be typed. Everything it writes is usage-scope.*
- [x] **P5-20** Responsive `<picture>` renderer in `Rendering/Fields/`: WebP `<source>`, accurate
  `srcset`/`sizes`, explicit `width`/`height` for CLS, `loading="lazy"` + `decoding="async"` on
  non-LCP images, `loading="eager"` + `fetchpriority="high"` on the first image in the first zone
  [§13.6]. — 1.5 ed
  *2026-08-16 — `IMediaResolver` (the media counterpart of `ILinkResolver`, batched for the same
  N+1 reason) plus `ResponsiveImages`, which is the arithmetic, plus the renderer, which is the
  markup. **Every number emitted is the resolved one, not the requested one**: the pipeline never
  upscales, so a 900 px original asked for at 1280 comes back at 900, and markup claiming 1280 would
  reserve a box the picture never fills — the exact layout shift the attributes exist to prevent.
  The same rule makes each `w` descriptor the width the browser will actually receive. "First image
  in the first zone" is settled by `RenderContext.ClaimLcpImage`, claimed **before the first await**
  so the claim happens in document order rather than in query-completion order. `sizes` is a new
  configuration setting defaulting to `100vw` — overstating it would fetch a file too small for a
  full-width hero with no symptom but a soft picture. An SVG or GIF has no rendition and is shown at
  its signed original; a document is a link, because an `<img>` at a PDF is a broken image where a
  label belongs.*
- [x] **P5-21** Alt-text policy [§13.7]: `AltText` required at upload **or** `IsDecorative = true`;
  usage-level override; **publish-time validation error** when neither is present. — 0.5 ed
  *Upload-time half done and configurable (`RequireAltTextOnUpload`), and `PATCH /media/{id}`
  enforces the same rule — without it an editor could satisfy the upload check and then clear the
  field. 2026-08-16 — the publish-time half landed with `P5-19`'s validator, on both the page and
  the reusable-content publish paths: an item placed on forty pages must not be the way an
  undescribed picture reaches all of them. Three sources satisfy the rule and the **placement's
  override is one of them**, because an image whose library description is wrong for one page's
  context is precisely what an override is for; a rule that ignored it would force a choice between
  an accurate library and a publishable page. `MediaValidationOptions.MissingAltTextSeverity`
  carries the spec's migration escape hatch and defaults to `Error` — a rule that made ten thousand
  imported pages unpublishable at once would be turned off wholesale rather than worked through.*
- [x] **P5-22** Media admin in `Client/Components/Admin/Media/`: browser (grid/list, folders, filters),
  detail/metadata panel, image editor, replace-keeping-id, where-used, soft delete + bin. — 0.5 ed
  *2026-08-16 — `MediaBrowser`, `MediaFolderBranch`, `MediaLibrary`, `MediaItemEditor`, over a new
  `IMediaClient` with the usual two implementations. **The browser is one component behind two
  screens** — the library page and the picker a `media` field opens — because they are the same
  screen, and two copies would drift first in the picker, which is the one an editor uses most.
  Thumbnails are **fetched, not built**: a client cannot sign a rendition URL, so a new
  `GET /media/links?ids=` signs a batch per page of results. That is a separate contract from
  `MediaDetail` on purpose — signing is a delivery concern and the library service has no key, and
  folding URLs into the metadata record would have coupled the two halves the DI split keeps apart.
  The image editor is numeric: the operation set and its storage are complete, and a drag-and-drop
  crop surface is authoring experience, which is Phase 6. The where-used list sits beside the delete
  buttons because it is the answer to the question they raise.*
- [x] **P5-23** Media API endpoints per [§22.1]: `POST /media`, `GET /media`, `GET /media/{id}`,
  `PATCH /media/{id}`, `PUT /media/{id}/edits`, `POST /media/{id}/revert`, `POST /media/{id}/replace`,
  `DELETE /media/{id}`, `GET /media/{id}/references`, `/media/folders…`. — included above
  *2026-08-16 — `Server/Api/Cms/Media/MediaEndpoints.cs` over `IMediaLibraryService`,
  `IMediaFolderService`, and the upload pipeline. Also `POST /media/{id}/restore` and
  `DELETE /media/{id}/permanent`. The upload and replace routes take `HttpRequest` and read the
  multipart body themselves, which is what lets both body limits — Kestrel's
  `IHttpMaxRequestBodySizeFeature` and `FormOptions.MultipartBodyLengthLimit` — be set from
  `MediaUploadOptions` before a byte is buffered, finishing the HTTP half of `P5-05`. Replace runs
  the identical screening as upload (one `ScreenAsync`, shared), because a replace path that sniffed
  less would be a back door into the library that looked like a working feature.*
- [x] **P5-24** Media deletion rules [§13.8]: soft delete first; permanent deletion blocked while
  `ContentReference` rows exist, with a where-used list. — included above
  *2026-08-16 — `DELETE /media/{id}` bins, `POST /media/{id}/restore` brings it back, and
  `DELETE /media/{id}/permanent` refuses unless the item is already in the bin and nothing points at
  it. The soft delete is deliberately **not** reference-guarded: it is reversible, so the right
  answer to "this is on twelve pages" is to hide it and let the editor find them.*
- [x] **P5-25** `cms-media-store` health check — write/read/delete round trip [§24.2]. — included above

### Tests — Phase 5

- [x] **P5-26** Unit: focal-point crop math, rendition spec normalization, signature generation.
  *`RenditionGeometryTests`, `MediaUrlSignerTests`, `SkiaSharpImageProcessorTests` — 139 assertions,
  none of which needs a database.*
- [x] **P5-27** Security: upload type-confusion corpus (HTML renamed `.jpg`, mismatched magic bytes).
  *`MediaTypeSnifferTests` and `SvgSanitizerTests`. Asserted against the sniffer and the sanitizer
  rather than an endpoint, so the decision is proven once for every route that shares it.*
- [x] **P5-28** Security: decode-bomb rejection before decode.
  *`ADecodeBombIsRefusedFromItsHeaderRatherThanDecoded` posts a real, tiny PNG whose `IHDR` has been
  rewritten to declare 40,000 × 40,000 — under 4 KB on the wire, six gigabytes decoded — and asserts
  `media.dimensions-too-large`. Built by patching a genuine file rather than hand-assembling one, so
  the codec reads the header exactly as it would read a hostile upload.*
- [x] **P5-29** Security: unsigned and tampered rendition URLs rejected; path-traversal probes.
  *`MediaUrlSignerTests` covers unsigned, tampered width/quality/crop, another site's key, and the
  rotation grace period; `MediaStorageKeyTests` covers traversal, absolute paths, and a root inside
  `wwwroot`.*
- [x] **P5-30** Integration: 20 concurrent cold requests for one rendition produce exactly one encode.
  *`MediaRenditionTests` fires twenty concurrent GETs at one signed URL through the whole pipeline and
  asserts the `cms.media.rendition.generated` counter reads 1 and exactly one `MediaRendition` row
  exists. The counter rather than a stubbed processor: it is the instrument an operator watches, so a
  test that counted something else could pass while the dashboard stayed at zero.*
- [x] **P5-31** Integration: dedupe returns the existing item on identical bytes.
  *`ReuploadingIdenticalBytesReturnsTheExistingItemRatherThanCreatingASecond` — identical pixels
  under two different file names. The second upload answers `200` rather than `201` with
  `deduplicated: true`, because nothing was created and the editor needs to be told which file they
  already had.*
- [x] **P5-32** Benchmark NFR-8 — cold 4000 px source → 1280 px WebP under 800 ms p95; telemetry
  `cms.media.rendition.generated` / `.duration` [§24.1].
  *`RenditionBenchmarkTests` measures twenty **cold** renditions through the endpoint, not through
  the processor: reading the original out of the store, taking the per-key lock, writing the result
  back, and recording the row are what turn a fast encoder into a slow first request, and they are
  exactly what a benchmark of `IImageProcessor` would miss. Each sample varies the crop in the
  fourth decimal — the precision the spec canonicalizes to — so no two requests name the same
  rendition and none can be served from storage. One warm request is discarded first, because the
  native decoder loading and the first database connection are per-process costs NFR-8 is not about.
  The source is noise rather than a flat fill, which compresses to nothing and would report a number
  no real upload could reproduce. Both instruments are asserted alongside the stopwatch: a benchmark
  that passed while the dashboard stayed flat would leave the requirement unobservable the day it
  starts being missed.*
- [~] **P5-33** Confirm Q9 (retention/compliance on versions and audit logs) is answered and reflected in
  the retention policy.
  *2026-08-16 — **Q9 is still unanswered and no longer blocks**, on the same reasoning that
  unblocked Q7: what an answer would change is written down, and it is not code. The version half
  already obeys [§11.7] through `RetentionPolicy` and reads its window from
  `SiteSettings.VersionRetentionDays`, so a legal answer sets a number and nothing is rebuilt. The
  audit half is the honest gap: `AuditLog` has **no** retention at all and nothing prunes it, which
  is the right default for an unanswered compliance question and the wrong state to launch in. That
  is recorded against Phase 9 rather than absorbed here, because an audit retention sweep is work
  that does not exist yet and pretending otherwise is how a compliance gap reaches production inside
  a ticked box. Left `[~]` deliberately: the confirmation this task asks for cannot be made until
  Legal answers.*

### Acceptance criteria — Phase 5

- [x] **P5 #1** A JPEG upload produces a `MediaItem` with correct dimensions, size, hash, and stripped
  EXIF; GPS data is absent from the stored original.
  *`APhotographsGpsCoordinatesAreGoneFromTheStoredOriginal` uploads a fixture carrying orientation 6
  and GPS, then fetches the stored original back through its own signed URL — what the site would
  hand a visitor, not a copy the test kept in memory. The recorded size is the upright 600×800, so
  the rotation is in the pixels, and no `GpsDirectory` survives.*
- [x] **P5 #2** Re-uploading identical bytes returns the existing item rather than creating a duplicate.
- [x] **P5 #3** A file whose extension and magic bytes disagree is rejected; an HTML file renamed `.jpg`
  is rejected. *Proven at the sniffer in `P5-27` and now end to end through the upload endpoint.*
- [x] **P5 #4** An oversized-dimension decode bomb is rejected before decoding. *`P5-28`.*
- [x] **P5 #5** SVG uploads follow the configured policy — sanitized to the strict profile, or refused.
  *Both branches are proven: `SvgSanitizerTests` covers the strict profile, and
  `AnSvgUploadFollowsTheDeploymentsPolicyWhichDefaultsToRefusingIt` covers the shipped default. Q7
  now selects a setting rather than gating code.*
- [x] **P5 #6** Rotating an image in the library updates every usage; the original bytes are unchanged
  and revert-to-original restores it.
  *The original is byte-for-byte identical across an edit, and revert restores it and moves the
  counter again. The third half closed with `P5-19`:
  `RotatingAnImageInTheLibraryChangesWhatEveryPageShowingItResolvesTo` puts one picture on two
  published pages, rotates it once in the library, and asserts that both pages resolve to the
  swapped dimensions **with neither payload changed and neither page republished** — which is the
  claim, and which is only true because a placement stores an id and nothing else about the file.*
- [x] **P5 #7** A usage-level crop affects only that page; other usages are unchanged.
  *Proven at both ends. `AUsageLevelCropAffectsOnlyThatPageAndLeavesTheOtherUsageUntouched` puts one
  item on two pages, crops one of them, and asserts the library row is untouched — no edit document,
  generation counter still zero — while the two placements resolve to different pictures. At the
  renderer, `AUsageCropChangesOnlyTheUrlsOfThePlacementThatCarriesIt` asserts the two placements
  share **no** URL at all, because the crop is inside the signature.*
- [x] **P5 #8** An unsigned or tampered rendition URL returns 400/403; a valid one returns the image.
  *`MediaUrlSignerTests` at the unit level; `ARenditionUrlWithATamperedWidthIsRefusedWithoutEncodingAnything`
  end to end, which also asserts the refusal cost zero encodes — a signature that refused *after*
  generating would have moved the denial of service rather than prevented it.*
- [x] **P5 #9** A rendition is generated once — twenty concurrent cold requests produce one encode. *`P5-30`.*
- [x] **P5 #10** `<picture>` output includes a WebP source, an accurate `srcset`, explicit
  `width`/`height`, and `loading="lazy"` on non-LCP images. Requesting AVIF is rejected at the
  spec-parsing layer.
  *`MediaPictureRendererTests`, against the markup rather than the arithmetic, because half of what
  §13.6 asks for is attributes: a perfect `srcset` on an `<img>` with no `width` still shifts the
  layout. The descriptors are asserted as the widths the browser will actually receive —
  `320, 640, 960, 1280, 1920, 2000` from a 2000 px source, where the last is a 2560 request clamped
  back down. `NoSourceEverOffersAvifBecauseNothingCouldProduceIt` covers the format half at the
  renderer; `RenditionRequestParser` already refused it at the endpoint.*
- [x] **P5 #11** Publishing a page whose image has neither alt text nor a decorative flag fails
  validation.
  *`PublishingAPageWhoseImageHasNeitherAltTextNorADecorativeFlagFailsValidation`, with the three
  ways out proven beside it: the item's own text, the decorative flag, and the placement's override.
  The migration downgrade is proven too — a deployment may make it a warning and publish, which is
  what stops the rule being turned off wholesale during an import.*
- [x] **P5 #12** Permanent deletion of referenced media is refused with a correct where-used list.
  *The guard and the endpoint shipped in `P5-24`; `P5-19` made the refusal reachable.
  `PermanentDeletionOfAPlacedImageIsRefusedAndTheWhereUsedListNamesThePage` places a picture on a
  page, publishes it, bins the item — which succeeds, because a soft delete is deliberately not
  reference-guarded — and then finds the purge refused with `media.still-referenced` and the
  where-used list naming the page that caused it.*
- [x] **P5 #13** A library-level edit bumps `EditsVersion`, changing rendition URLs and thereby busting
  client and CDN caches.
  *`ALibraryEditBumpsEditsVersionAndRevertingBumpsItAgain` from the API side and
  `ALibraryEditChangesTheRenditionUrlAndRetiresTheOldOne` from the delivery side — the new URL serves
  and the old one is `410`, so a superseded link cannot quietly deliver the new picture under the old
  cache key.*

**Exit gate:** safe upload, non-destructive edits, signed responsive renditions. — [x] met on
2026-08-16. All thirteen acceptance criteria pass. The one item left open is `P5-33`, which asks for
a confirmation Legal has not given; it is recorded rather than closed, and the gap it names — audit
log retention — is Phase 9 work that does not exist yet.

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

- [x] **P6-01** Three-pane shell in `Client/Components/Admin/Shell/`: resizable, collapsible,
  responsive down to tablet, layout persisted per user [§14.1]. — 3 ed
  *2026-08-16 — `AdminShell` takes the three panes as render fragments, so the tree, canvas, and
  properties panel know nothing about each other. Resizing runs in a collocated JS module and
  reports one width per gesture rather than one per `pointermove`; **keyboard resizing stays in .NET**
  (arrows, Shift for a coarse step, Home/End), because a separator that can only be dragged is a pane
  a keyboard user cannot resize [§28]. Geometry is per editor in `localStorage` — not a server
  preference, since the same person on a laptop and a 34-inch monitor wants two different layouts.
  Below 62em the panes stack in reading order rather than becoming overlays. Component-scoped CSS is
  switched on here for the first time; note that `wwwroot` is gitignored build output in this repo,
  so every interop module is a collocated `.razor.js` static web asset.*
- [x] **P6-02** Content tree in `Client/Components/Admin/Tree/`: lazy-loaded children, virtualized
  sibling lists, status indicators (published / draft-pending / scheduled / unpublished / in-review /
  locked) [§14.2]. — 2 ed
  *2026-08-16 — `ContentTree` fetches one level per expansion and holds them in a dictionary keyed on
  parent id, so a move or a delete re-reads one level rather than rebuilding the graph. `Virtualize`
  past 60 siblings, against the pane's own scrollbar. Arrow-key navigation with a roving `tabindex`,
  so Tab steps past the whole tree; the row is the focusable `treeitem` and holds no buttons, since a
  control inside one is another Tab stop per row. Every state is an icon **and** a word (P6-39),
  never a colour alone, with the padlock shown beside the publishing state rather than instead of it.
  Two facts the tree needed did not exist: `PageSummary` now carries `ScheduledPublishOn` and
  `LockedBy`, the latter from one batched query over live `EditLock` rows.*
- [x] **P6-03** Tree drag reorder/reparent **plus keyboard-accessible move controls**, with an explicit
  confirmation showing the URL changes and redirects that will be created. — 1.5 ed
  *2026-08-16 — Dropping onto a row reparents; dropping into the strip between two rows reorders.
  Both are reachable from the keyboard with Alt and the arrow keys — up/down reorder, right moves
  into the sibling above, left moves out to the grandparent — which is acceptance criterion P6 #4 for
  the tree. **A move that changes any URL is confirmed first**, listing every affected address and
  the redirects; a reorder, which changes none, goes straight through rather than training editors to
  dismiss the dialog that matters. This needed new server work: `IPageService.MoveAsync` owns the
  tree position, the sibling order, and the route rebuild as one transaction, and **a preview is the
  move** — run and then rolled back — so the dialog cannot promise something the button then does
  differently. `ModalDialog` arrived here too (it is also P6-21's confirmation dialog): Bootstrap's
  markup without Bootstrap's JavaScript, with a real focus trap and focus restoration.*
- [x] **P6-04** Tree context menu (new child, duplicate deep/shallow, copy, move, delete, publish
  branch, unpublish) and inline filter over title/slug/id. — 0.5 ed
  *2026-08-16 — Menu opens on right-click and equally on **Shift+F10 or the Context Menu key**, with
  arrow-key navigation and Escape; entries that cannot act are omitted rather than disabled. Copy and
  move are one clipboard with a mode: paste-as-copy is a deep duplicate into the target, paste-as-move
  is the same move a drag makes, **confirmation and all** — which is why paste is built on the
  existing operations rather than a second implementation. Delete states what it takes **before** it
  takes it (acceptance criterion P6 #10), from a new read-only `IRecycleBinService.DescribeAsync` and
  `GET /pages/{id}/delete-impact`. The filter replaces the tree with a flat site-wide result list
  rather than pruning it — a lazily loaded tree can only hide what it has already fetched, so a
  pruning filter would answer "no results" for most of the site — and the backoffice search now
  matches a page id, which is what an editor arriving from a log line or a ticket is holding.*
  *2026-08-16 — **"publish branch" closed out** once `P6-29` existed to build it on. It is offered
  only on a page that has children, and it is one selection the server resolves rather than a walk of
  the tree: the tree lazily loads a level at a time, so a branch it has never opened looks like a
  leaf and any count it produced itself would be wrong. The confirmation therefore says "you selected
  1 page, this will publish 41", the batch reports per page, and a branch over
  `BulkLimits.BackgroundThreshold` runs on the server rather than tying up the browser. Closing the
  dialog does not cancel anything, and it says so — a background job belongs to the server the moment
  it is accepted.*
- [x] **P6-05** Editing canvas in `Client/Components/Admin/Canvas/`: zone cards ordered by `SortOrder`,
  grouped by `Zone.Group`, per-zone validation state, sticky action bar. — 3 ed
  *2026-08-16 — `EditingCanvas` owns the card frame and nothing inside it: the body comes from a
  `ZoneEditorContext` render fragment, because ADR-0014's editor catalog should be built with the
  editors it maps (`P6-06` onwards) rather than have its parameter contract guessed at now. Two rules
  decide the layout — **a named group is reopened** so its zones are drawn together wherever their
  sort orders scattered them, while **a run of ungrouped zones is not merged** with the ungrouped
  zones elsewhere, since merging on the absence of a name would drag a page's footer up above the SEO
  group it was numbered after. **Grouping had to become part of the captured revision**: the canvas
  must lay a draft out from the revision it was authored against [§8.5], so `ContentSchemaSnapshot`
  now writes `group` and `description` and `CapturedSlot` reads them; snapshots cut before this read
  as one ungrouped canvas, which is the layout those pages already had. Validation is sorted onto the
  cards by the payload path each diagnostic carries, and **anything with no card to land on — a URL
  collision, or a zone the revision no longer declares — is reported above them** rather than
  bucketed under a key nothing renders. Each card is `id="zone-{key}"` with `tabindex="-1"`, which is
  what `P6-20` will deep-link to. `PageEditor` is now a consumer of it rather than a second zone
  form; the plain textareas moved into `PlainZoneEditor` and stay as R13's fallback. Two fixes fell
  out of the rewiring: a publish refused only by warnings now shows the warnings it wants
  acknowledged [§22.2], and editing a zone retires the last check instead of leaving a stale green
  badge over content nobody has checked.*

### Field editors — 14.5 ed

> Built-in field types answer null for `IFieldType.EditorComponent` — `Core` cannot name a component
> in `Client` ([`ADR-0014`](./docs/adr/0014-field-type-components-resolved-by-the-hosting-layer.md)).
> The editors below are mapped to field type keys through the same catalog `P3-09` builds for
> renderers, and the backoffice needs the equivalent startup check: a field type with no editor
> leaves an author with no way to fill a property the schema requires.
>
> *2026-08-16 — the catalog is `IFieldEditorCatalog` in `Client/Components/Admin/Fields/`, the exact
> mirror of `Rendering`'s renderer half, and the startup check is `CmsEditorStartupService` — which
> can only live in `Server`, the one project that can see both the catalog in `Client` and the field
> type registry in `Core`. **Three decisions shaped everything below.** First, the parameter contract
> P6-05 deliberately left open is `FieldEditorBase`: `Field`, `Value`, `ValueChanged`, dispatched by
> name through one `FieldEditorHost`, so the page canvas, a block's property row, and the reusable
> editor cannot disagree about what fills a field type. Second, **every editor binds to the stored
> value as JSON text — the whole envelope, not the text inside it** — which moves each field type's
> storage shape into the one component that understands it and reduces `PlainSlotValues` to a raw
> round trip; it also means an editor rewrites the members it owns and leaves the rest, so a crop
> written by the media screen survives somebody editing the alt text. Third, `ZoneEditorContext`
> became `FieldEditorContext` and moved to `Fields/`, because a zone card and a block property are
> the same thing to an editor. **All eighteen built-in field types have an editor**, not only the ten
> the tasks below name: acceptance criterion `P6 #1` says an editor fills a page without touching a
> raw JSON payload, and a template with one `number` zone would otherwise fail it.*

- [x] **P6-06** Block list editor in `Client/Components/Admin/Fields/BlockList/`: add constrained to
  `allowedBlockTypes`, reorder, collapse with a configurable summary line, duplicate, delete-with-undo,
  per-block validation badges [§14.3]. — 3 ed
  *2026-08-16 — **Every block is drawn from the revision it was authored against**, never the block
  type's current one [§8.5], so two blocks of the same type in one zone can be laid out differently —
  which is correct rather than a defect, since drawing a control for a property that did not exist
  when the block was written invites an author to fill in a schema their content is not judged by.
  Schemas are fetched once per distinct type-and-revision the payload names, so twelve cards of one
  type cost one request. The collapsed summary renders the block type's `SummaryTemplate` against the
  block's own content, because "Hero banner" twelve times is a list nobody dares collapse; a token
  naming an empty or undeclared property resolves to nothing rather than printing `{headline}` at an
  author. Delete keeps the block **and its index** and offers it back inline until the next change —
  an inline bar rather than a toast, since `IToastService` has no action button and a toast times out
  on the one action here worth taking back after reading the screen. Diagnostics are narrowed twice,
  onto the block and then onto the property, so a twelve-block zone says which one is wrong instead
  of "3 problems". A block naming a type this build no longer carries draws a note and stays movable
  and removable [§15.3]; an orphaned type is not offered for adding at all.*
- [x] **P6-07** Block list **full keyboard operability** — explicit move up/down controls; drag is an
  enhancement, never the only path [§28]. — 1 ed
  *2026-08-16 — Add, move up, move down, duplicate, collapse, and delete are all buttons, each with
  an `aria-label` naming the block it acts on. **The drag grip is `aria-hidden` and takes no Tab
  stop**, precisely because it can do nothing the buttons cannot — a handle a keyboard user could
  focus and not use is worse than no handle. Dragging ends in the same write the arrow buttons do, so
  a list reordered either way produces the same payload, ids included. The bUnit suite drives only
  the buttons: a test that covered the pointer path would pass on a build where the buttons had been
  removed, which is exactly what acceptance criterion `P6 #4` forbids.*
- [x] **P6-08** **Edit/Preview/Split rich-text editor** in `Client/Components/Admin/Fields/RichText/` —
  CodeMirror 6 source mode for Markdown, Quill for the constrained WYSIWYG surface, both as **local
  static assets** (no CDN, so the CSP stays strict) [§14.4]. — 2.5 ed
  *Proven end to end by [S3](./docs/spikes/s3-editor-interop.md), with four requirements: the
  backoffice host page must emit a **per-request style nonce** exposed as `<meta name="csp-nonce">`
  and passed to `EditorView.cspNonce`, or CodeMirror renders silently unstyled ([`D13`](./docs/adr/0013-backoffice-editor-bundle-and-style-nonce.md));
  one shared base class carries the interop plumbing (module import, `DotNetObjectReference`, echo
  suppression, `IAsyncDisposable`); **Quill's toolbar must be removed explicitly** on teardown, since
  Quill has no `destroy()` and appends the toolbar as a sibling; split the bundle per editor
  (696 KB raw / 231 KB gzipped for both).*
  *2026-08-16 — All four requirements met. `App.razor` emits `<meta name="csp-nonce">` from a scoped
  `IStyleNonce` (128 bits, `RandomNumberGenerator`, generated lazily so the API and media requests
  that never render a host page pay nothing), and `EditorView.cspNonce` reads it. `JsEditorComponentBase`
  carries the plumbing; `SourceEditor` and `WysiwygEditor` are the two wrappers. **Split into two
  bundles, and the split pays for itself**: 501 KB raw / 173 KB gzipped for CodeMirror and 201 KB /
  59 KB for Quill, against S3's 696 KB / 231 KB for the two together — so a page with only markdown
  zones downloads a quarter less than the combined bundle, a page with only formatted-text zones
  downloads a quarter of it, and a page with only plain-text zones downloads neither. Quill's stylesheet is added by its own module as a same-origin `<link>` on
  first mount rather than from the host page, so an anonymous visitor never fetches 24 KB of editor
  CSS. `BuildEditorBundles` runs esbuild as part of `Build`, incrementally, which is what D13 means by
  a missing bundle failing the build rather than the page.
  **The `Content-Security-Policy` header itself is deliberately not switched on here.** The nonce it
  needs now exists and is wired end to end, but the policy is [§20.5]'s and turning it on today would
  break working screens: `style-src-attr` is not relaxed by D13, and P5-19's media control — among
  others — positions with inline `style` attributes. That is a Phase 9 hardening job with its own
  sweep, and it is recorded here rather than left as a surprise.*
- [x] **P6-09** Preview pane rendered through the **same Markdig → sanitize → site typography pipeline**
  the public site uses, so preview is accurate rather than approximate. — 1 ed
  *2026-08-16 — The backoffice runs in WebAssembly and `Core` is not loaded there, so the source goes
  to the server and the markup comes back: `POST /api/cms/v1/markup-preview`, calling the same
  `IMarkdownRenderer` and `IContentSanitizer` singletons `RichTextRenderer` calls. A second Markdig in
  the browser would satisfy the screen and break the promise `P6 #2` makes, on the first upgrade of
  either side. `POST` for a read because the source is a zone's worth of prose, which has no business
  in a query string or in every access log the request passes through. **The response carries what
  was removed as well as what was kept**, which is what makes a preview also a warning and what
  `P6-13`'s banner is built on. A mistyped profile is refused rather than defaulted — falling back
  would show an author a preview stripped harder than their zone will be — and the `Developer`
  profile is gated on the role that can author against it. The typography half is a `.cms-content`
  layer in `site.scss`, shared with the public site rather than scoped to the component, because a
  preview is a `MarkupString` full of elements Blazor's scoped CSS never attributed.*
- [x] **P6-10** Split mode with synchronized scrolling. — 0.75 ed
  *2026-08-16 — **As a fraction of scrollable height, never a pixel offset.** The two panes render
  different content at different heights — one line of markdown becomes a picture — so matching
  pixels drift further apart the further down a long document an author scrolls, which is exactly
  where the feature is worth having. CodeMirror reports its position rAF-coalesced, so a scroll
  gesture is one interop call per frame rather than dozens per second, and the subscription is only
  made while split mode is showing. The preview does not report back: a follower that announced its
  own position would put the two panes in a feedback loop neither could settle out of. Below 62em the
  panes stack, matching the shell's own breakpoint, because two columns are each too narrow to read
  on a tablet.*
- [x] **P6-11** CMS-aware link and image insertion — opens the CMS pickers and inserts internal
  references, never hand-typed URLs. — 0.5 ed
  *2026-08-16 — Link and picture are **absent from Quill's toolbar on purpose** and are the editor's
  own buttons, opening `LinkPicker` and `MediaPicker`. What an author never does is type an address.
  **The honest limit is worth stating**: a `link` property stores a `pageId` and resolves it at render
  time (`ADR-0006`), but markdown and HTML zones are text and an anchor in text has an `href` in it —
  so for prose the picker's choice is resolved through a new `GET /pages/{id}/link` and the resolved
  URL is what lands in the document. A prose link still goes stale when its target moves, and the
  redirect the move creates is what catches it; a property-valued link does not go stale at all. That
  is the guarantee `ADR-0006`'s own consequence line asks of the editing UI, and the wider one it does
  not. Images insert the signed preview rendition with the library's alternative text, and a picture
  marked decorative inserts an empty `alt`, which is the correct markup for one.*
- [x] **P6-12** Word/character counts with a configurable soft limit. — 0.25 ed
  *2026-08-16 — "Configurable" needed a setting to configure, so `softLimit` was added to
  `TextFieldTypeBase` and `RichTextFieldType` — configuration is closed (`ADR-0015`), so a setting no
  field type declares is refused on the next structure save. **It is advisory and nothing on the
  server reads it**, which is the whole distinction from `maxLength`: "a meta description over 160
  characters gets truncated in results" is guidance an author wants while typing, not a rule that
  should stop them publishing, and the counter says each in the words that match. The running numbers
  are deliberately **not** in the live region — a count that announced itself on every keystroke would
  talk over the letters being typed — while a visually-hidden status holds text only once a threshold
  is crossed, so it speaks on the crossing. The names that cross the `Core`/`Client` boundary now live
  in `FieldSettingNames` in `Shared`, so a rename cannot leave an editor silently ignoring a setting
  the structure screen still offers.*
- [x] **P6-13** HTML editor in `Client/Components/Admin/Fields/Html/` with a persistent banner of
  permitted tags and a **live "these tags will be stripped on save" warning** — silent stripping is the
  number-one "the CMS ate my content" ticket [§14.4]. — 1.5 ed
  *2026-08-16 — **The check runs in every mode, including Write, and does not depend on the preview
  pane being open.** That costs a second request while split mode shows, both of them an in-memory
  sanitize on the server; the alternative is a warning that appears only once an author thinks to look
  at the preview, which is precisely the author who will not — and acceptance criterion `P6 #3` says
  *before* save. The banner lists what the profile keeps, fetched from a new
  `GET /markup-preview/profiles` rather than duplicated in the browser, because a second copy of the
  allowlist is a banner that eventually lies. A failed check leaves the previous account showing
  rather than clearing it: "nothing will be removed" is the one thing this control must never say
  without having asked. Removal excerpts are attacker-influenced text by construction and are rendered
  encoded, never through `MarkupString`.*
- [x] **P6-14** Plain-text inline editing with a live character counter, and a "preview" that renders in
  the template's actual typography. — 0.5 ed
  *2026-08-16 — A single-line `input` rather than a textarea, because `plainText` refuses line breaks
  and a control an author can press Enter in invites a value the validator rejects a screen later.
  The `maxlength` attribute is deliberately **not** the configured maximum: set to the real limit it
  silently swallows the keystrokes past it, so the author types a longer headline, sees a shorter one,
  and has no idea why — the counter tells them instead, and the attribute is only a stop far enough
  out that nothing but a pasted document reaches it. The multiline preview splits on both line-ending
  conventions and joins with `<br>`, which is the rule `MultilineTextRenderer` follows rather than a
  CSS approximation of it: `white-space: pre-wrap` would also preserve runs of spaces the page
  collapses.*
- [x] **P6-15** Pickers in `Client/Components/Admin/Pickers/`: page (tree), media (browser + inline
  upload), reusable content, and a unified link picker. — 2.5 ed
  *2026-08-16 — The page picker offers **both a lazy tree and a server-side search**, because editors
  arrive holding two different things: somebody linking to a sibling browses, and somebody working
  from a ticket searches — and a lazily loaded tree can only filter what it has already fetched. A
  page the slot forbids is shown and disabled rather than hidden, so an editor told to link to it
  learns it is refused rather than missing. The media picker is a dialog around `MediaBrowser`, which
  P5-22 already built as both the library screen and the field control, so the inline uploader P6-15
  asks for came with it. The reusable picker **asks about the pin at the moment of placement** and
  resolves it to a version row id there, since that is the only point at which an author is thinking
  about whether the placement should follow the item. The link picker is one dialog for two callers —
  a `link` property, which stores what it returns verbatim, and a rich-text editor, which turns it
  into an anchor — so the two can never offer different destinations.*
- [x] **P6-16** `IAsyncDisposable` on every JS-interop component; verify no listener/editor instance
  leaks *(mitigates R14)*. — included above
  *2026-08-16 — On the base class rather than on each wrapper, which is the point: S3 found three
  things that must all be right and only the first is obvious — the editor's own teardown, Quill's
  toolbar (a sibling it never removes), and `DotNetObjectReference.Dispose()`, without which the JS
  registry keeps the component alive for the life of the page. Two of the three are handled once in
  `JsEditorComponentBase` so no wrapper can forget them. A subclass that subscribes to something else
  — split mode's scroll listener — passes the base's **existing** reference rather than creating a
  second one to forget, and releases the listener before the editor is destroyed so a scroll fired by
  the DOM being torn down cannot call into a half-disposed component. The JS registry counts created
  against disposed and exposes DOM counts, which is what `P6-31a` asserts on — **and now does**: ten
  mount/unmount cycles of each editor in Chromium, created equal to disposed, the registry empty, and
  no surviving `.cm-editor`, `.ql-editor`, or `.ql-toolbar`. What that still cannot reach is R14's own
  trigger, which is browser memory over two hours (`P9-16`).*

### Properties, saving, and feedback — 5 ed

- [~] **P6-17** Properties panel in `Client/Components/Admin/Properties/`: page metadata, SEO section
  with a **search-result preview widget** and character-count guidance, publishing section, editorial
  fields (owner, review-by, internal notes, tags) [§14.7, §18.1]. — 2 ed
  *2026-08-16 — `PropertiesPanel` edits a `PageProperties` model beside the immutable `PageDetail` it
  was handed and **sends the difference**, which is the whole reason the request contract is built out
  of `Patch<T>`: a panel that patched all twenty fields on every save would reinstate its own copy of
  the nineteen nobody touched over whatever a colleague changed in the meantime, silently, because
  they look right on the screen that sent them. It owns no save button — an edit here is an edit, and
  title and the SEO fields live on the draft version, so P6-18's autosave writes them. The
  search-result widget is deliberately **not** a pixel-accurate forgery of any one engine: it exists to
  show the two rules a counter cannot state — a blank meta title falls back to the page title, and both
  fields are truncated rather than refused — and a convincing imitation would invite trust in details
  no engine guarantees. Two facts had to be added to make the owner field real: `PageDetail` now
  carries `OwnerName` (resolved server-side, as `LockedBy` already was, because "Owner: 42" is a field
  nobody can read), and `GET /api/cms/v1/me` reports the signed-in editor's own id — which the
  WebAssembly backoffice otherwise cannot know, since the serialized authentication state carries the
  name and role claims only. That endpoint is `P6-24`'s "my work" tile as well.
  ***The panel is rendered beside the canvas rather than inside `AdminShell`***, which is still
  unmounted by any route. It takes no position of its own — it is a component with parameters, exactly
  as `P6-01` built the shell to expect — so composing the three panes later moves where it is rendered
  and changes nothing else.*
  **Open: tags, and the share image.** Neither exists to write to. `Tag`/`PageTag` is `P8-20`'s, and
  `OgImageMediaId` has no member on the metadata patch — the Open Graph output that needs it is
  `P8-02`. Both are stated on the panel rather than drawn as dead controls.
- [x] **P6-18** Autosave in `Client/Services/`: 20-second idle debounce, save on navigate-away,
  offline-safe queueing, clear save-state indication ("Saved 14:32") [§11.3]. — 1.25 ed
  *2026-08-16 — `AutosaveController` owns when a save is due and nothing about what is saved, which is
  handed in as a delegate. **There is no queue of payloads, and that is the design rather than a
  shortcut**: a queue would be a queue of stale ones. The delegate reads the editor's current state at
  the moment it runs, so a failed attempt followed by more typing saves the later text once; what is
  queued is the *intent*, held across a failure, a retry, and an editor going offline and coming back.
  A transient failure retries with a doubling backoff capped at 30 s; **a refusal does not retry at
  all**, because repeating a request the server has already reasoned about every twenty seconds buries
  the message explaining it, and a conflict needs a decision from a person. Any unexpected exception is
  treated as transient — an autosave that died on an unobserved exception would leave an editor typing
  into a screen that has quietly stopped saving. The clock is a `TimeProvider`, so the twenty seconds
  are advanced in a test rather than waited out. Navigating away flushes through
  `RegisterLocationChangingHandler`, **registered in `OnAfterRender` rather than `OnInitialized`**: a
  location-changing handler takes a navigation lock, which only an interactive renderer has, and
  registering it during the server pre-render throws before a zone reaches the browser. Closing the tab
  is outside .NET's reach entirely, so `SaveStateIndicator` arms the browser's own `beforeunload`
  prompt while there is unsaved work.*
- [x] **P6-19** Conflict resolution UI on `409`: keep-mine / take-theirs / open-diff. **No path silently
  discards work.** — 0.75 ed
  *2026-08-16 — **Two things the server promised and did not deliver had to be built first.** `ETags`
  has said since `P2-20` that a mismatch answers 409 rather than 412 precisely so "the losing editor
  needs the winning draft in its hands" — but `CmsProblems` dropped the result's value, so the body
  carried diagnostics and nothing else. It now writes a `conflict` member, omitted rather than nulled
  when nothing won, and `StructureClientResult.Refused` is the one failure shape that carries a value
  (`IsSuccess` stays false, so nothing that only asks "did it work" is fooled). And **the version diff
  cannot compare a conflict**: both copies are the same version row and the losing one was never
  written, so there is no second id to name — hence `POST /pages/{id}/draft/diff`, which sends the
  unsaved payload and reuses the same `PayloadDiff` the version history reads, with the stored draft as
  the earlier side because the question being asked is "what would mine change". In the dialog, keeping
  mine is one click (nothing it overwrites is lost — the history holds it), **taking theirs asks
  twice** because what it replaces exists nowhere else, and closing decides nothing. The reassurance
  that the editor's text is still here is stated *above* the buttons, not under the one they did not
  choose.*
- [x] **P6-20** Publish dialog: errors and warnings grouped by zone, each deep-linking to the offending
  field; warnings require acknowledgement and resubmit with `acknowledgedWarnings` [§14.6, §22.2]. — 0.5 ed
  *2026-08-16 — The dialog opens on a **fresh check over a flushed draft**: the server checks what it
  holds, so a dialog opened over unsaved edits would report on the paragraph before the one the editor
  is looking at and then publish the one they are looking at. Groups are named as the canvas names them
  and ordered as the canvas orders them, so the dialog reads down the page the way the page reads;
  anything naming no zone — a URL collision, a missing meta description — is grouped under the page
  rather than dropped, the same rule `CanvasDiagnostics` follows. Each group is a link that closes the
  dialog and moves focus to `#zone-{key}`, which `P6-05` made addressable and `tabindex="-1"`; a link
  that scrolled a card into view behind a modal would go somewhere the editor still cannot type into.
  The acknowledgement is unticked on every opening — consent to a list nobody is looking at is not
  consent — and it is what turns [§22.2]'s resubmit into one visible decision.
  ***A defect this dialog could not have worked around was found underneath it.*** A publish stopped
  by warnings alone answers `422` with an **empty** `errors` array and the warnings in it — and
  `HttpPageClient` read the errors alone, so that body became a bare `http.422` with the warnings
  discarded: a screen telling an editor their page was refused and refusing to say what for. Any
  response carrying diagnostics of either severity is now read as one, and `HttpPageClientTests`
  pins it along with the conflict body.*
- [x] **P6-21** Toasts (reuse the existing `IToastService`), confirmation dialogs, undo affordances,
  empty and loading states. — 0.25 ed
  *2026-08-16 — Toasts for what an editor has just done and is already looking at (an explicit save, a
  publish); the state that outlives a toast lives in the status bar instead. Unpublishing is confirmed
  through `ModalDialog` and says what visitors will see and what is **not** deleted. Its undo is an
  inline bar rather than a toast — a toast times out on the one action on this screen worth taking back
  after reading it — and "put it back" runs the ordinary publish path rather than a second one, so it
  cannot go live past a check the button beside it would have stopped. `FieldMessages` is new and small:
  `DiagnosticList` says how many problems a write had, and a form of twenty boxes also needs each
  message beside the box it is about, or an editor matches them to fields by reading property names and
  matches them wrongly.*
- [x] **P6-22** ARIA live regions announcing autosave state and validation results [§28]. — 0.25 ed
  *2026-08-16 — `LiveRegion` is in the document from the first render and empty when there is nothing to
  say, because a region added at the moment it has something to announce announces nothing. Two
  urgencies, and the difference is deliberate: **autosave is polite** — interrupting somebody mid-word
  to say "saved" is worse than saying it a second later — while **a validation result is assertive**,
  because a person pressed a button and is waiting to hear the answer. What neither does is announce on
  every render: the message is set on a phase crossing or a check completing, never derived from state
  that redraws on every keystroke, and `Pending` and `Saving` pass silently so the region does not
  narrate the typing. Announcements are phrased as the outcome rather than the count — "3" is not an
  answer to "can I publish this".*

### Dashboard, bin, and bulk — 5.5 ed

- [x] **P6-23** Keyboard shortcuts plus a shortcut reference dialog. — 1 ed
  *2026-08-16 — **One table, read by both halves**: `EditorShortcuts.All` is what the listener matches
  against and what the reference dialog renders, so a chord that works undocumented and a chord that
  is documented and does nothing are both unwritable without deleting one of the two uses. The
  listener is on the **document**, not on a div — an editor's focus is usually inside something the
  component tree does not own (a CodeMirror instance, a link in the properties panel), and a shortcut
  that worked in one pane is one nobody trusts. Two rules keep it from being a nuisance: a
  modifier-less chord inside a text field belongs to the field, which only the document can know and
  is therefore the script's one judgement; and `preventDefault` is called only for a chord .NET
  actually claimed, so Ctrl+F, Ctrl+T, and the browser's own find still belong to the browser. Alt is
  matched as "not held" rather than ignored, because it is the tree's move modifier and composes
  characters on several layouts. The chords are conservative — Ctrl/⌘+S, Ctrl/⌘+K, Ctrl/⌘+Shift+P,
  Ctrl/⌘+E, and `?` for the list — and **every one is an accelerator for a button that is also on the
  screen**, which is [§28]'s rule and is stated at the top of the dialog rather than assumed. An
  editor who may not write is not offered the shortcuts that write, the same way the toolbar hides
  the buttons.*
- [x] **P6-24** Dashboard in `Client/Components/Admin/Dashboard/` [§14.9] — **My work** tile (drafts with
  unpublished changes, review assignments, rejected items). — 0.5 ed
  *2026-08-16 — "Mine" is deliberately two things — pages I own and pages I was last to touch —
  because ownership alone leaves a new editor's own unfinished draft off their own dashboard, which
  is the one row they came for. **The review-assignment list is not drawn and the tile says why**:
  assignment arrives with the workflow in `P7`, and an empty "assigned to you" reads as "nothing is
  waiting on you" rather than as "this has not shipped". What can be reported honestly today is what
  the version statuses already record — content in review, and content sent back.*
- [x] **P6-25** Dashboard — **Scheduled** tile (publishes/expiries in the next 7 days, failures
  highlighted). — 0.5 ed
  *2026-08-16 — The overdue rows are the reason the tile exists. A scheduled publish whose moment
  passed while the page is still unpublished is a job that did not run, and it is invisible
  everywhere else in the backoffice because the page looks exactly like an ordinary draft. It is
  drawn differently **and** said in words, never by colour alone (`P6-39`).*
- [x] **P6-26** Dashboard — **Needs attention** tile (past `ReviewByDate`, broken references, images
  missing alt text, top `NotFoundLog` URLs). — 0.5 ed
  *2026-08-16 — Four lists, each a thing nobody would think to look for. The broken-reference sweep
  reads **published** versions only: a draft pointing at a page nobody has created yet is work in
  progress, while a live page pointing at a deleted one is a link a visitor is meeting now, and
  mixing the two buries the second in the first. It checks all three target kinds through the global
  query filters, so "gone" means the same thing here as it does to a visitor, and it over-reports by
  the same design `ContentReference` does [§7.3] — the right direction to be wrong in when the
  alternative is silence.*
- [x] **P6-27** Dashboard — **Recent activity** tile (permission-filtered `AuditLog` view); every tile
  deep-links into a correctly filtered list. — 0.5 ed
  *2026-08-16 — Narrowed to the content tables rather than the whole audit log: this is an editorial
  feed, and an identity table's rows are neither interesting here nor safe to show everyone who may
  read content. Beyond that the filter **is** the tile's entry condition — `Content.Read` — because
  v1 has no per-page permissions to filter by; those arrive with `P7` and this query narrows with
  them rather than being rewritten. The deep link is the same server query at a larger limit
  (`GET /dashboard/{tile}`), not a second screen that resembles it: two definitions of "needs
  attention" would drift, and the tile would then advertise a list that did not contain what it
  promised.*
- [x] **P6-28** Recycle bin UI in `Client/Components/Admin/RecycleBin/`: list, filter, subtree-aware
  restore, permanent delete with typed-name confirmation [§14.10]. — 1 ed
  *2026-08-16 — **It lists subtree roots, not deleted rows.** Deleting a section deletes everything
  under it, and a bin showing all forty rows would ask an editor to restore one delete forty times,
  in an order that matters — a child restored before its parent comes back at the site root. The
  roots are what was deleted; the count beside each is what goes with it. Permanent deletion asks for
  the page's name to be typed and is Administrator-only, with the button **absent** rather than
  disabled for anybody else; a refusal — content elsewhere still pointing at the page — closes the
  dialog rather than leaving a box open over a message it cannot act on. The filter is a predicate
  over a list already in memory rather than a search, which is why it is not debounced the way the
  tree's is.*
- [x] **P6-29** `BulkOperationService` in `Core/Content/`: selection model, impact preview, background
  execution with progress above 25 items, per-item result reporting, per-item audit logging [§14.11].
  — 1.5 ed
  *2026-08-16 — **Nothing here reimplements an operation.** Each item runs through the same
  `IPublishingService`, `IRecycleBinService`, or `IPageService` a single-item request runs through,
  in a scope of its own — which is what makes a bulk publish subject to the same validation, the same
  permission checks, and the same audit rows as forty individual publishes, and why per-item audit
  logging needed no code at all. A scope per item rather than per batch is failure isolation: a
  failed item leaves nothing tracked behind for the next one to save on its behalf. **A background
  job outlives the request that asked for it**, so the caller is captured while there is still one to
  capture and each item's scope is given a synthetic request carrying that principal
  (`IBulkOperationScopeFactory`, implemented over `HttpContext` in `Server`) — without it the job
  would be refused on item one or, worse, recorded as having been done by nobody. Three shapes are
  deliberate: a **delete** shows its whole subtree in the preview and queues only the selected roots,
  since the recycle bin is subtree-aware already and queueing the descendants would report forty "no
  such page" failures for a batch that worked; a **stale selection** warns and runs the rest, because
  one page deleted while the editor read the dialog is no reason to drop the other thirty-nine; and a
  job whose items all failed still reports `Completed`, because every one was attempted and every one
  has a reason attached. Job state is **in memory for the life of the process**, which is stated
  rather than hidden: a poll after a restart gets `page.job-not-found`, and a scaled-out deployment
  (**Q4**) can only poll the instance that accepted the batch — a change to one class, not to its
  callers. Tags are absent from the operation set on purpose: `Tag`/`PageTag` is `P8-20`'s, and an
  operation that silently matched nothing would read as "these pages have no tags".*

### Tests — Phase 6

- [x] **P6-30** bUnit: block list editor add/reorder/duplicate/delete, keyboard paths.
  *2026-08-16 — `BlockListEditorTests`. Every case drives a button and none drives the drag, which is
  the point: dragging ends in the same write, so a suite that covered only the pointer path would
  pass on a build where the buttons had been removed. Also covers the summary line falling back to
  the type name, a badge landing on the block a diagnostic actually names, and a block type this
  build no longer carries staying movable. `FieldDiagnosticsTests` pins the narrowing underneath it —
  in particular that a path stops matching at a member or index boundary, so `zones.hero` cannot
  claim what was said about `zones.heroine` and `items[1]` cannot claim `items[10]`.*
- [x] **P6-31** bUnit: rich-text editor mode switching and preview parity.
  *2026-08-16 — `RichTextFieldEditorTests`. Mode switching, the surface following the value's stored
  format rather than the property's configuration, and the preview being asked for the right format
  and profile. **Parity itself is asserted where it can be**: byte-identity is `P1 #7`, already met,
  and `MarkupPreviewApiTests` shows this endpoint reaching the same two singletons the delivery path
  uses. What bUnit cannot see is CodeMirror and Quill themselves, which never mount without a
  browser — that is `P6-31a`'s.*
- [x] **P6-31a** E2E: mount and unmount an editor ten times, asserting zero surviving editor DOM
  nodes and created-equals-disposed; and assert CodeMirror's own styling is in effect (a computed
  style differing from the browser default), since a missing CSP nonce fails **silently**
  [[S3](./docs/spikes/s3-editor-interop.md), [`D13`](./docs/adr/0013-backoffice-editor-bundle-and-style-nonce.md)].
  *2026-08-16 — `EditorTeardownTests`, in Chromium. The built bundles are served from a synthetic
  `https://` origin rather than from `file://`, because a module graph cannot be imported across
  origins and because a real origin is somewhere a `Content-Security-Policy` **header** can be
  attached — which is the whole exercise. Ten cycles of each editor: created equals disposed, the
  registry is empty, and no `.cm-editor`, `.ql-editor`, or `.ql-toolbar` survives — the last of those
  being S3's finding, since Quill appends its toolbar as a sibling and a teardown that clears the
  container accumulates one per mount. The styling half is asserted **both ways**: with the nonce
  wired, CodeMirror's injected theme is honoured and its computed `display` is `flex`; with the meta
  tag removed and the same strict policy, it is `block` and nothing throws. That negative control is
  the point — a suite asserting only the positive case would go green on a build with no nonce at
  all. What this cannot see is the .NET half of teardown, since CodeMirror and Quill never mount
  under bUnit; `JsEditorComponentBase`'s `DotNetObjectReference.Dispose()` is `P6-16`'s.*
- [ ] **P6-32** E2E: full editor journey — create → edit → preview → publish → verify anonymous → edit
  again → verify published unchanged → rollback.
- [ ] **P6-33** E2E: autosave survives a simulated transient network failure without losing input.
- [ ] **P6-34** E2E: save conflict presents all three resolution options.
  *2026-08-16 — **These three are open together, and for one reason.** Each needs the whole
  application running in a browser: a real Kestrel address rather than `TestServer`, the WebAssembly
  runtime booting against it, a signed-in editor, and a database behind it. The E2E project is
  deliberately `Client`-only today — it renders components statically and drives Playwright over the
  markup — so this is a piece of harness that does not exist rather than three tests somebody forgot
  to write. Everything they gate is asserted a level down and said so in the criteria: `P6 #5` and
  `P6 #6` are met by `AutosaveControllerTests`, `PageEditorSavingTests`, `ConflictDialogTests`, and
  `PageApiTests`, and the journey's server half is `DraftAndPublishTests` and `DeliveryTests`. What
  is missing is the only thing those cannot do — failing the way a real network fails, and racing the
  way two real browsers race.*
- [x] **P6-35** Performance: tree responsive at 5,000 pages with 500 siblings under one parent.
  *2026-08-16 — `ContentTreeScaleTests`. The criterion's two halves are answered by two different
  mechanisms, so they are asserted separately rather than by timing one big render and hoping. **5,000
  pages** is answered by lazy loading, and what is asserted is the request count — a tree that
  quietly fetched depth 5 would still look fast against a fixture and would not be on a real site.
  **500 siblings** is answered by virtualization, and what is asserted is that the document holds a
  bounded number of rows rather than 500, because rendering 500 rows is the slow thing and no
  millisecond threshold is stable across machines. A wall-clock budget is asserted too, deliberately
  loose: it exists to catch an accidental walk of every sibling per sibling, not to police hardware.
  The third test is the one that makes the first two matter — at this size the filter has to be a
  server-side search, because a tree holding twenty of five thousand pages would answer "no results"
  for 99.6% of the site.*
- [x] **P6-36** axe-core across every backoffice screen — zero critical or serious violations.
  *2026-08-16 — Extended to the screens Phase 6 added: the dashboard, a dashboard tile, and the
  recycle bin, with the content tree audited separately **as the pane it actually is** — running it
  through the whole-page theory would report "page should contain a level-one heading" against a
  component that is not a page. **It found three real defects rather than confirming a clean bill**:
  four `section` elements inside one card were four landmarks announced by the same name (the groups
  are now plain `div`s under headings, which is what they are); the tile screen skipped from `h1` to
  `h3`, so the group heading level is now a parameter — the same list sits at two different depths;
  and the tree pane needed the heading its host screen supplies. The gate runs `wcag2a`, `wcag2aa`,
  `wcag21a`, `wcag21aa`, and best-practice over rendered markup, and asserts `results.Passes` is
  non-empty so a document that rendered nothing cannot pass by having nothing to complain about.*
- [~] **P6-37** Manual keyboard-only pass over the whole authoring flow.
  *2026-08-16 — Written up rather than performed, in [`docs/phase-6-keyboard-pass.md`](./docs/phase-6-keyboard-pass.md):
  what the automated suites already prove (block operability, the tree's menu and arrows, keyboard
  moves, pane resizing, the shortcut table, and the axe gate), and the six things a person still has
  to do with the pointer physically unplugged. **A keyboard pass is not a check that every control is
  reachable — it is a check that reaching them is bearable**, and none of what makes it unbearable
  fails an assertion. It has to be run again once the three-pane shell is mounted, since pane order
  and the Tab path between panes are exactly what that changes.*
- [x] **P6-38** 200% browser zoom pass.
  *2026-08-16 — `ZoomTests`. 200% zoom is **a viewport of half the width**, not a screenshot scaled
  up: a browser at 200% on a 1280-pixel display reports 640 CSS pixels, so the failure it produces is
  a layout one. Every screen is rendered with the site's own `site.css` and measured for horizontal
  overflow, which is WCAG 1.4.10's rule — content must reflow rather than make somebody scroll in two
  directions to read one line. **It found four screens that did not**: the page list, the version
  history, the preview links, and the reusable library, all wide tables that could not narrow. Each
  now scrolls inside its own `table-responsive` container, which is the difference between one
  element scrolling and the page doing it. A negative control asserts that something known to be too
  wide does fail the measurement, because an overflow check passes for the wrong reason the moment
  the stylesheet fails to load.*
- [x] **P6-39** `prefers-reduced-motion` respected; no color-only status encoding in the tree [§28].
  *2026-08-16 — `ReducedMotionTests`. The motion half is asserted against the stylesheets themselves:
  `prefers-reduced-motion` is a media query, so a rendering test would have to emulate the preference
  and compute a style, and bUnit has no browser to compute one in — what can be checked is the thing
  that actually goes wrong, an `animation` or `transition` added without a guard beside it. Bootstrap's
  own components carry their guard inside the framework, so only this repository's stylesheets are
  judged, and a second test fails if the file walk found none, since the first would otherwise pass
  vacuously. The colour half drives `PageStatusIndicator` through every state and asserts each is a
  **word** — and not merely present but announced, since the icon is `aria-hidden` and the
  visually-hidden text is all a screen reader has.*
- [x] **P6-40** Add backoffice and content typography layers to `styles/site.scss`.
  *(Existing-code change.)*
  *2026-08-16 — Brought forward because `P6-09` and `P6-14` both need it: a preview is a
  `MarkupString` full of elements Blazor's scoped CSS never attributed, so `.cms-content` has to be a
  site-wide layer or it styles the wrapper and nothing inside it. It is the same class the public
  site's prose uses, which is what makes "the template's actual typography" true rather than
  approximate. The backoffice layer beside it holds the field editors, the block list, and the
  pickers — shared rather than scoped for the same reason three surfaces draw the same controls.*

### Acceptance criteria — Phase 6

- [x] **P6 #1** An editor completes create → fill → preview → publish without touching a raw JSON
  payload or a URL bar.
  *2026-08-16 — The JSON half was met when the eighteenth field editor landed (`P6-06`…`P6-16`):
  every built-in field type has a control, so no zone falls back to a raw envelope. The **URL-bar**
  half needed one more thing, and it was missing until now — `/admin` was not a route at all, so an
  editor arriving at the backoffice reached the page list by typing its address. `P6-24`'s dashboard
  is that route, and it carries the section bar of [§14.1] (content, media, reusable, structure,
  recycle bin) because `AdminShell` is still mounted by nothing; when the shell is composed the bar
  moves into it and nothing else changes. Create, fill, preview, and publish are then reachable by
  clicking: dashboard → content → new page → the canvas → Preview draft → Publish.
  **The browser journey that walks it end to end is `P6-32`, still open.***
- [x] **P6 #2** Markdown Edit/Preview/Split all work, and Preview matches the published page's rendering
  exactly.
  *2026-08-16 — The three modes are `P6-08` and `P6-10`. "Exactly" holds because there is one
  pipeline and the preview reaches it rather than reimplementing it (`P6-09`): byte-identity for the
  same source is `P1 #7`, already met and tested, and `MarkupPreviewApiTests` shows the endpoint
  calling the same `IMarkdownRenderer` and `IContentSanitizer` singletons `RichTextRenderer` calls.
  The full create-to-published comparison is `P6-32`'s journey and is still open.*
- [x] **P6 #3** The HTML editor warns *before* save about content the active profile will strip.
  *2026-08-16 — `P6-13`. The check runs in Write mode with no preview open, which is where an author
  pasting an embed actually is, and it goes through the same sanitizer the save runs rather than a
  client-side approximation of the allowlist. Asserted at both levels: `HtmlFieldEditorTests` for the
  warning appearing before save, `MarkupPreviewApiTests` for the endpoint reporting the removal.*
- [x] **P6 #4** Blocks can be added, reordered, duplicated, and deleted entirely by keyboard; drag is an
  enhancement, never the only path.
  *2026-08-16 — `P6-06` and `P6-07`, pinned by `BlockListEditorTests`, which drives only buttons. The
  drag grip is `aria-hidden` and takes no Tab stop, so there is no control a keyboard user can reach
  and cannot use.*
- [x] **P6 #5** Autosave fires on a 20-second idle, shows its state, and survives a transient network
  failure by retrying without losing input.
  *2026-08-16 — `P6-18`, pinned by `AutosaveControllerTests` on a driven clock: nineteen seconds saves
  nothing, a keystroke restarts the countdown rather than extending it, a screen nobody types into is
  never written, and a transient failure retries and saves **the text as it stands at the retry**, not
  the text that failed. `PageEditorSavingTests` asserts the same through the screen, including that the
  state reads "Saved 14:30" rather than "Saved". **The browser half is still open**: doing this over a
  genuinely flaky connection is `P6-33`, and only a real network can fail the way a real network does.*
- [x] **P6 #6** A save conflict presents keep-mine / take-theirs / open-diff, and no path silently
  discards work.
  *2026-08-16 — `P6-19`. `ConflictDialogTests` covers all three being offered and, more to the point,
  the second half of the criterion: taking theirs asks twice, backing out disarms it, and closing
  decides nothing. `PageEditorSavingTests` shows a losing save opening the dialog rather than being
  retried, and keep-mine resending **the same text** with the winner's token. `PageApiTests` pins the
  server end — the 409 body carries the draft that won, and a refusal nothing won carries no `conflict`
  member at all. `P6-34` remains: the same three options in a browser.*
- [x] **P6 #7** The tree remains responsive at 5,000 pages with 500 siblings under one parent.
  *2026-08-16 — `P6-35`, pinned by `ContentTreeScaleTests` on the two mechanisms that make it true
  rather than on a stopwatch: one fetch per expansion however large the site, and a bounded number of
  rows in the document however crowded the level. The third assertion is the one that keeps the
  first two honest — the filter searches the server, because a tree holding twenty of five thousand
  pages could only ever filter the twenty.*
- [x] **P6 #8** The dashboard surfaces the signed-in user's drafts, review tasks, and overdue content,
  and every tile deep-links into a correctly filtered list.
  *2026-08-16 — `P6-24`…`P6-27`. `DashboardTests` drives the server half — the editor's own drafts,
  an overdue review, an undescribed image, the top 404s, a broken reference that appears only once it
  is live, and a schedule whose moment came and went — and `DashboardScreenTests` drives the screen,
  including that every tile links to its own list. The link opens the **same query at a larger
  limit**, which is what makes "correctly filtered" structural rather than a promise. The one part of
  the criterion that is not drawn is review assignment, which has nothing to write to until `P7`; the
  tile says so rather than showing an empty list that reads as good news.*
- [x] **P6 #9** A deleted page leaves the public site immediately, remains in the recycle bin with full
  history, and restores as a *draft*.
  *2026-08-16 — The behaviour is `P2-08`'s and has been pinned by `RecycleBinAndDuplicationTests`
  since Phase 2: the published pointer is retired inside the delete, the version rows and reference
  rows stay, and a restore sets `PublishedVersionId` to null so nothing comes back live. What Phase 6
  adds is the screen that makes it reachable (`P6-28`) and `RecycleBinScreenTests` over it.*
- [x] **P6 #10** Deleting and restoring a page with children moves the whole subtree, with the count
  shown before confirming.
  *2026-08-16 — The count comes first, from `IRecycleBinService.DescribeAsync` rather than from
  anything the tree counted for itself, and the confirmation states both numbers — how many pages go,
  and how many of them are live and leave the public site at once (`P6-04`, `ContentTreeMenuTests`).
  The bin's own list carries the other half: each entry is the subtree root with its descendant count
  beside it, so a restore is one act on the section rather than forty in an order that matters.*
- [x] **P6 #11** A deep duplicate rewrites links between pages inside the copied subtree to the new
  copies, while links out of the subtree still point at the originals.
  *2026-08-16 — `P2-09`'s `DuplicationService`, pinned by
  `ADeepDuplicateRewritesLinksInsideTheSubtreeAndLeavesLinksOutOfItAlone`. Phase 6 reaches it from
  the tree in two ways — "duplicate with children", and paste-as-copy, which is deliberately deep
  because pasting half a section is never what was meant (`P6-04`).*
- [x] **P6 #12** A bulk publish of 100 pages runs as a background job with progress, and a partial
  failure leaves successful items published while reporting the rest individually.
  *2026-08-16 — `P6-29`. `BulkOperationTests` covers both halves against a real database: a batch over
  `BulkLimits.BackgroundThreshold` is accepted, answered `202` with a job to poll, and finishes after
  the request has been answered; and a branch publish where one page has an unfilled required zone
  leaves the other two published, names the page that failed, and carries the reason the single-item
  publish would have given. A job whose items all failed still reports `Completed` — every one was
  attempted — which is the distinction between a partial failure and a batch that stopped.*
- [x] **P6 #13** axe-core reports zero critical or serious violations on every backoffice screen.
  *2026-08-16 — `P6-36`, over eleven screens and the content tree pane, at `wcag2a` through
  `wcag21aa` plus best-practice. It found three defects on the way in, all of them Phase 6's own and
  all now fixed — which is the argument for the gate rather than against it.*
- [x] **P6 #14** The whole authoring flow is operable at 200% browser zoom.
  *2026-08-16 — `P6-38`, measured as WCAG 1.4.10 defines it: a 640-pixel viewport and no horizontal
  overflow. Four screens failed it and were fixed. "Operable" beyond reflow — every control still
  reachable by keyboard at that size — is the part a measurement cannot make, and it is step 6 of the
  manual pass in `P6-37`.*

**Exit gate:** editors complete the full flow unaided; a11y clean. — [ ] met on ____
*2026-08-16 — **Twelve of fourteen criteria met**, and the a11y half of the gate is clean:
`P6 #13` passes over every screen, `P6 #14` passes after four fixes, and `P6 #4` was met when the
block list shipped. What the gate still waits on is the half that says *unaided*: `P6-32` to `P6-34`
walk the full journey, a flaky network, and a save conflict **in a browser**, and `P6-37`'s
keyboard-only pass has to be performed by a person. All four need something no assertion supplies —
a running application and somebody using it — which is why the gate is left open rather than
declared met on the strength of the layer beneath.*

**Risks:** R13 (scope elasticity), R14 (JS interop memory leaks).

---

## Phase 7 — Workflow, permissions, and scheduling

**Objective:** more than one person can use the system safely. **16 ed** · Entry: Phase 2 exit.
**Runs in parallel with Phases 4–6.**

- [x] **P7-01** Seed the seven roles from [§3.2] (`Administrator`, `Developer`, `Editor`, `Author`,
  `Approver`, `MediaManager`, `Viewer`) in `Data/Seeding/`. — 0.5 ed
  *(`CmsRoleSeedData`, through `HasData` so the rows arrive with the migration that creates the
  table. **The ids are part of the contract** — a role-scoped `PageAcl` stores one.)*
- [x] **P7-02** Permission constants + policy provider in `Server/Authorization/`, mapped to roles per
  the [§21.1] matrix; extend `CustomUserClaimsPrincipalFactory`. — 1.5 ed
  *(Three permissions added where the matrix distinguishes what Phase 7 needs: `Content.Submit`,
  `Content.Approve` — which `Editor` deliberately does **not** hold — and `Audit.View`. The factory
  now stamps one `cms:permission` claim per grant, which is a display convenience for the
  WebAssembly client and never the check; `ApiContractTests` asserts the map against the matrix.)*
- [x] **P7-03** `PageAcl` entity + configuration [§21.2]. — 0.5 ed
- [x] **P7-04** `AclService` in `Core/Security/`: inheritance via indexed `Page.Path` prefix match,
  **deny beats allow** at the same depth, deeper rule beats shallower, `Administrator` bypass with an
  audit entry. — 2.5 ed
  *(Precedence lives in `AclFilter`, so the rule one service applies to one page and the rule the
  tree applies to a hundred siblings are the same code. One **allow** anywhere turns the permission
  into an allowlist for that principal, which is what makes an ACL narrow rather than only widen —
  `P7 #5` is that clause. A bypass is logged only when a rule would otherwise have refused.)*
- [x] **P7-05** Per-request ACL cache to keep deep-tree resolution fast *(mitigates R15)*. — included above
  *(Scoped, and the scoping is load-bearing: an administrator revoking a rule takes effect on the
  next request, not when a cache entry expires.)*
- [x] **P7-06** Apply ACL checks in the **service layer** for every content and media operation — never
  only at the endpoint, never in the client. — 1.5 ed
  *(Pages, drafts, versions, diffs, publishing, locks, duplication, the recycle bin, preview tokens,
  and the dashboard's four tiles. A refusal of `Content.Read` answers **not found** rather than
  forbidden, because a 403 a 404 would not have produced tells the caller the page is there.)*
- [x] **P7-07** IDOR integration tests sweeping every content and media endpoint across ACL boundaries
  with guessed ids. — 0.5 ed
  *(`IdorSweepTests`, at the service layer rather than over HTTP: an authorization bug is almost
  never a missing check on the screen an editor uses, it is a service that reads an id and trusts
  it. Nineteen entry points, every one refused, and the page unchanged afterwards.)*
- [x] **P7-08** `WorkflowTask` and `Comment` entities + migration `AddCmsWorkflow` (migration #7, also
  carrying `PageAcl` and `ScheduledJob`). — 1 ed
  *(`Notification` joined the same migration — an in-app inbox needs a table and `P7-19` had none —
  along with `SiteSettings.RedirectToParentOnUnpublish`, which `P7-15` needs to be configuration
  rather than a coded default.)*
- [x] **P7-09** `WorkflowService` in `Core/Workflow/` with the three modes from [§11.9]: `None`,
  `Simple`, `TwoStep` (approver may not be the author). Site-wide setting in v1. — 2 ed
- [x] **P7-10** Version status transitions wired to workflow: `Draft → InReview → Approved → Published`,
  `Rejected` copying content into a fresh draft with comments preserved [§11.2]. — included above
  *(**The draft is frozen while it is under review** — `DraftService` refuses a save against an
  `InReview` version — because otherwise an approval is a statement about content that no longer
  exists. Publishing returns it to `Draft`, so the next edit cannot inherit an approval nobody
  gave it.)*
- [x] **P7-11** Workflow endpoints: `POST /pages/{id}/submit|approve|reject`,
  `GET /workflow/tasks?assignedTo=me`, `GET`/`POST /pages/{id}/comments`. — included above
  *(Three verbs rather than a status field: a client able to `PATCH` a version to `Approved` could
  approve its own submission by editing a field.)*
- [x] **P7-12** Review UI in `Client/Components/Admin/Workflow/`: submit/approve/reject, zone-anchored
  threaded comments, task inbox. — 2 ed
  *(Which buttons appear is decided server-side — `PageWorkflowState` carries may-submit, may-decide,
  and may-publish — so the self-approval clause has one implementation rather than two. Comment
  bodies are rendered as text, never through `MarkupString`.)*
- [x] **P7-13** `ScheduledJob` entity + `PublishSchedulerService` in `Server/HostedServices/`: 30 s
  poll, **atomic `UPDATE … OUTPUT` claiming** so two instances cannot double-publish [§11.6]. — 1.25 ed
  *(The decisions are in `ScheduledJobRunner`, which a test drives directly; the hosted service is a
  timer and an exception boundary. A job runs **as the editor who scheduled it**, rebuilt from the
  identity tables by `HttpJobIdentityScopeFactory` — a publish by nobody would be refused by the
  service-layer check, and if it were not, would be audited as user 0.)*
- [x] **P7-14** Scheduled publish runs the identical validation and invalidation path as a manual
  publish; a validation failure marks the job `Failed`, notifies the owner, and does **not** retry
  blindly. — included above
- [x] **P7-15** `UnpublishOn` handling: clear `PublishedVersionId`, retire public routes, apply the
  configured parent-redirect behavior rather than leaving a 404. — 0.25 ed
  *(In `PublishingService.UnpublishAsync` rather than in the scheduler, so pressing the button and
  asking for it to be pressed at midnight do the same thing. Off by default: a redirect the system
  invents is a URL the site then promises to serve forever.)*
- [x] **P7-16** DST-aware scheduling UI: stored UTC, presented in the site timezone with the offset
  shown explicitly. — 0.5 ed
  *(The panel states the exact instant a box means, offset included, before anything is saved. A time
  that happens twice takes the earlier offset; a time that does not exist takes the one in force
  before the transition rather than being refused.)*
- [x] **P7-17** `cms-scheduler` health check (lag > 5 min fails) + `cms.scheduler.lag` gauge. — included above
  *(Two ways to be unhealthy, and they are different failures: lag past the threshold, and **silence**
  — no pass completed in four poll intervals, which is the stopped loop that otherwise has no symptom
  until somebody notices a page that never went live.)*
- [x] **P7-18** Replace `Server/Components/Email/IdentityNoOpEmailSender.cs` with a real sender per
  **Q5**. *(Existing-code change — workflow notifications and password resets are non-functional
  without it.)* — 1 ed
  *(SMTP, which is the answer to **Q5** rather than a way round it: every candidate provider offers
  an SMTP endpoint, so a deployment picks one by filling in a host. With none configured it falls
  back to `LoggingCmsEmailSender`, which says what it would have sent instead of discarding it, and
  the registration confirmation link is now shown only in Development as well as only when
  unconfigured.)*
- [x] **P7-19** Notification templates + in-app inbox for: submitted, approved, rejected, scheduled
  publish succeeded/failed, edit-lock override, comment mentions [§14.8]. — 0.5 ed
  *(The inbox row is committed first and the mail attempted after, so a dead relay cannot turn a
  successful approval into a failed one. Mentions are matched against user names that exist, so a
  typo notifies nobody rather than failing a send.)*
- [x] **P7-20** Audit log viewer in `Client/Components/Admin/Audit/` with entity / user / date filters,
  backed by `GET /audit?entity=&entityId=&userId=&from=&to=`. — 0.5 ed

### Tests — Phase 7

- [x] **P7-21** Unit: ACL resolution — inheritance, deny-over-allow, depth precedence, admin bypass.
  *(`AclFilterTests`, without a database: the precedence rules are arithmetic over a handful of rows.
  Deny-over-allow is asserted with the rows in both orders, because an answer that depended on the
  query plan would not be an answer.)*
- [x] **P7-22** Integration: `Author` publish attempt returns `403` and content stays unpublished.
- [x] **P7-23** Integration: `TwoStep` mode refuses self-approval.
  *(Both halves: approving is refused, and so is publishing — a rule one button press away from
  nothing is not a rule.)*
- [x] **P7-24** Integration: two server instances, one scheduled job → exactly one publish *(R16)*.
  *(Two passes over the same rows with no coordination, which is what two servers do. **The suite
  found a real defect**: `new SqlParameter("@pending", 0)` binds to the `SqlDbType` overload, because
  the literal zero converts to any enum — the claim query failed at run time saying a parameter it
  had been given was never supplied.)*
- [x] **P7-25** Integration: `Content.Read` denial hides a subtree from the content tree entirely.
- [x] **P7-26** Performance: tree load under 500 ms at depth 10 with ACLs applied *(R15 trigger)*.

### Acceptance criteria — Phase 7

- [x] **P7 #1** An `Author` cannot publish: the API returns `403` and the content stays unpublished.
- [x] **P7 #2** Submit → approve → publish works end to end, with email and in-app notifications at each
  step. *(Asserted as far as the transport boundary: the inbox rows are read back from the database
  and the mail sender is asked to send to both the approver and the author. Anything beyond that is a
  mail server's behaviour rather than this system's.)*
- [x] **P7 #3** In `TwoStep` mode, the author cannot approve their own submission.
- [x] **P7 #4** A rejection returns the content to a fresh draft with comments preserved and visible.
- [x] **P7 #5** A user with an ACL on `/products` can edit that subtree and receives `403` on `/about`,
  including on direct API calls with a guessed id.
- [x] **P7 #6** Denying `Content.Read` on a subtree hides it from the content tree entirely.
- [x] **P7 #7** A page scheduled for a future time publishes within 60 seconds of it, and only once even
  with two server instances running. *(The 30-second poll is what bounds the 60 seconds; the claim is
  what bounds it to once.)*
- [x] **P7 #8** A scheduled publish that fails validation marks the job failed, notifies the owner, and
  does not silently retry.
- [x] **P7 #9** `UnpublishOn` retires the page and applies the configured redirect behavior.
- [x] **P7 #10** The audit viewer answers "who unpublished the homepage and when" in under three
  interactions. *(The query behind it is asserted — one filter over the entity and its id reaches the
  answer, newest first. The interaction count is a property of the screen and is what `P9-13`'s
  keyboard pass looks at.)*

**Exit gate:** authors cannot publish; ACLs enforced server-side; scheduling fires once. — [x] met on 2026-08-18

**Risks:** R15 (ACL query performance), R16 (duplicate scheduled publishes).

---

## Phase 8 — SEO, caching, navigation, and search

**Objective:** the public site is fast, discoverable, and navigable. **14 ed** · Entry: Phase 3 exit.
**Runs in parallel with Phases 5–6.**

### SEO — 3.5 ed

- [x] **P8-01** Surface the SEO fields already on `PageVersion` end to end in `Rendering/Seo/`:
  `<title>`, meta description, `<link rel="canonical">`, robots directives [§18.1–18.2]. — 0.75 ed
  *(`SeoMetadataBuilder` resolves every [§18.1] fallback and every URL to absolute form; `CmsSeoHead`
  writes them and does nothing else, so preview and delivery cannot emit different heads for one
  version. The absolute address comes from `ISiteAddress` — configured `Cms:Seo:PublicBaseUrl` first,
  the request second — because behind a proxy the request's own host is an internal address. Preview
  is forced to `noindex, nofollow` whatever the page says.)*
- [x] **P8-02** Open Graph and Twitter Card tags, with the OG image rendered through a `1200x630` crop
  rendition. — 0.5 ed
  *(JPEG rather than WebP: the crawlers fetching a share image are not browsers. 1200 joined
  `RenditionSpec.AllowedWidths` without joining the new `ResponsiveWidths`, so it is requestable
  without adding a candidate to every `srcset` on the site. The card type degrades to `summary` when
  no image resolved, since `summary_large_image` with no image renders as an empty box.)*
- [x] **P8-03** JSON-LD: `WebSite` + `Organization` on the home page, `BreadcrumbList` from the content
  tree, `WebPage`/`Article` per page, all overridable via `StructuredDataJson`. — 0.75 ed
  *(A hand-authored document **replaces** the generated set rather than joining it — two `WebPage`
  nodes at one URL is the failure. Editor-authored JSON is re-parsed and re-serialized through the
  same encoder as the generated documents, which is what stops a `</script>` in stored text.
  Breadcrumbs walk the materialized `Page.Path` in one query and skip unpublished ancestors.)*
- [x] **P8-04** `sitemap.xml` in `Server/Delivery/Seo/`: published indexable pages only, `<lastmod>` from
  the publish timestamp, configurable `changefreq`/`priority`, **index splitting above 40,000 URLs**,
  cached with the `content` tag. — 1 ed
  *(The exclusions are in the query rather than applied afterwards: no published route, `noindex`, or
  the configured 404 page. Above the threshold the response becomes a `sitemapindex` over
  `/sitemap-{n}.xml`, each a page of the same URL-ordered query so a crawler fetching file 3 an hour
  later is not handed a reshuffled set; a file past the end is a 404, not an empty `urlset`.)*
- [x] **P8-05** Editable `robots.txt` from `SiteSettings` with a sensible default disallowing `/admin`,
  `/api`, `/preview` and pointing at the sitemap; **non-production serves `Disallow: /`
  unconditionally**. — 0.5 ed
  *(The environment override is not a setting and cannot be reached by editing the text box — the
  environment name is the one fact a copied production database cannot carry with it. A hand-written
  body keeps its own rules and gains the `Sitemap:` line if it has none, since that is the line whose
  absence is silent.)*

### Caching — 7 ed

- [x] **P8-06** Output caching in `Server/`: policies, `UseOutputCache()` placed **after**
  `UseAuthentication`/`UseAuthorization`, ETag revalidation, `.NoCache()` on preview and admin routes,
  a base-policy predicate excluding requests carrying an identity cookie [§16.4]. — 1.5 ed
  *(`CmsPageCachePolicy`, applied by name to the delivery endpoint and the sitemap. Caching is
  **opt-in per endpoint** rather than a base policy with exclusions, so preview, `/admin`, and `/api`
  need no `.NoCache()` — they are never cached at all, which is the version somebody adding a route
  cannot undo. A request is refused the cache when it is authenticated, sends an `Authorization`
  header, or carries any `.AspNetCore.*` cookie; a response is refused storage when it sets one.)*
- [x] **P8-07** Cache-tag accumulation **during render** via `RenderContext.CacheTags` → applied to the
  response: `page:{id}`, `ru:{id}`, `media:{id}`, `tpl:{id}`, `nav:{menuKey}`, `content` [§16.2]. — 1 ed
  *(The accumulation shipped in P3; this is the half that reads it. `CacheTags` moved from
  `Rendering` to `Core/Caching/`, so the side that spells a tag and the side that evicts it are not
  two files. A response that published no tags is stored under `content` rather than untagged, so a
  render that forgot its dependencies is at least reachable by a purge-all.)*
- [x] **P8-08** `HybridCache` for published content objects and route lookups in `Core/Delivery/`
  (15 min TTL, tag eviction) [§16.1]. — 1 ed
  *(Two decorators in `Core/Caching/`. **`HybridCache` serializes everything it stores** — the
  `[ImmutableObject(true)]` optimization applies to reads — and a `PublishedContent` carries a live
  `JsonElement` plus a captured schema whose unset configurations are `default(JsonElement)`, which
  `System.Text.Json` refuses to write at all; so the *stored row* is cached and the payload is
  parsed per request. The route cache stores only a plain page hit: a miss has no tag that could
  evict it, a redirect has to be counted, and a non-canonical spelling has to keep its 301.)*
- [x] **P8-09** `OutboxMessage` entity + `OutboxProcessorService` in `Server/HostedServices/` (5 s
  cadence) — invalidation enqueued **inside the publish transaction** so a committed publish always
  evicts even if the process dies immediately after [§16.3]. — 1.5 ed
  *(`ICacheInvalidationQueue` adds a row to the caller's `DbContext` and saves nothing — publish,
  unpublish, move, recycle, restore, reusable publish, and every media library write. **Every
  instance applies every message**, tracked by an in-memory watermark rather than an exclusive
  claim: each node has its own in-process cache to evict, and a claimed-once message would leave the
  other nodes serving what they had. Eviction is idempotent, so N nodes is N−1 no-ops.)*
- [x] **P8-10** `CacheInvalidator` in `Core/Caching/` — fan-out driven by `ContentReference`, using
  `IOutputCacheStore.EvictByTagAsync`. — 1 ed
  *(Both stores in one place: the output cache holds the finished HTML and the hybrid cache holds the
  content it was rendered from, and evicting only the first re-renders from a payload that has just
  changed — which looks exactly like an invalidation that did not work. **The fan-out is not driven
  by `ContentReference`**, deliberately: every renderer already declared what it used as a cache tag
  while rendering, so evicting `ru:{id}` reaches every page showing that item with no query at all.
  The reference table still answers where-used and the delete guards. The one real fan-out query
  left is which managed menus name a page.)*
- [x] **P8-11** Optional Redis output cache (`AddStackExchangeRedisOutputCache`) behind configuration,
  wired to the Aspire Redis resource; **`IDistributedCache` explicitly not used** for output caching.
  — 0.75 ed
  *(Registered when `ConnectionStrings:outputcache` is present, which is what `Cms:UseRedisOutputCache`
  in the AppHost supplies. No `IDistributedCache` is registered anywhere: beyond the atomicity reason
  in [§16.3], one would silently give `HybridCache` a second level with serialization requirements
  the cached types were never designed for.)*
- [x] **P8-12** Short backstop TTL so any missed invalidation self-heals within an hour *(mitigates
  R17)*. — 0.25 ed
  *(An hour on the output cache, fifteen minutes on the content and route caches, all under
  `Cms:Cache`.)*
- [x] **P8-13** `cms-outbox` health check (unprocessed messages older than 5 min) +
  `cms.cache.hit_ratio` metrics. — included above
  *(Two ways to be unhealthy, and they are different failures: a backlog, and a poller gone quiet for
  six intervals. An instance with the outbox switched off reports **degraded** rather than healthy —
  unlike the scheduler, there is no configuration in which not draining it is fine. The ratio is a
  gauge over hit and miss counters recorded by the cache policy itself, counting only the requests
  that were eligible for caching.)*

### Navigation and search — 3.5 ed

- [x] **P8-14** `NavigationMenu` / `NavigationItem` entities + migration `AddCmsDelivery` — migration #8,
  also carrying `SearchDocument` (+ full-text catalog), `OutboxMessage`, `Tag`, `PageTag`. Handle the
  Azure SQL vs. on-prem raw-SQL differences for full-text catalog creation explicitly. — 0.75 ed
  *(The catalog and index are raw SQL guarded on `SERVERPROPERTY('IsFullTextInstalled')`: SQL Server
  and Azure SQL Database get them, and **Azure SQL Edge — which the arm64 test container runs — has
  no full-text engine at all**, where an unguarded `CREATE FULLTEXT CATALOG` fails the whole
  migration. A navigation item's "page or URL, never both" is a check constraint written as a count,
  since T-SQL has no exclusive-or over predicates.)*
- [x] **P8-15** Structural navigation generated from the content tree, filtered by
  `Page.ShowInNavigation` and publish state [§10.7]. — 0.5 ed
  *(`NavigationService` reads the whole tree in one query and assembles it in memory, because a
  query per level is the N+1 that only appears once a site has a real tree. The two filters are
  different switches — `ShowInNavigation` is an editor saying "not in the menu", `PublishedVersionId`
  is the site saying "not yet" — and a page failing either takes its subtree with it, since an entry
  whose parent cannot be reached is a link into a hole. Depth is clamped at four: beyond that it is a
  sitemap, and building one on every render is a cost nobody asked for.)*
- [x] **P8-16** Managed menus (ordered items, internal page reference or external link) + menu admin UI.
  — 0.5 ed
  *(`NavigationMenuService` + `/api/cms/v1/navigation/menus` + the `/admin/navigation` screens. Writes
  need **`Content.Publish`**, not `Content.Edit`, for the reason redirects do: a menu reaches
  anonymous visitors the moment it is saved, with no draft and no publish step in between [§21.1].
  An entry whose page is not published resolves to nothing and is dropped along with anything nested
  under it — a dead link is the failure an editor is least likely to notice.)*
- [x] **P8-17** `nav:{menuKey}` cache tags invalidated on any publish/unpublish/move. — 0.25 ed
  *(Every page eviction carries `nav:*tree` with it, since publish state is what the generated menu
  is filtered by, plus a query for the managed menus naming that page — a footer linking to a page
  renders its title, so changing the page changes every page showing the footer. `CmsNavigation` adds
  its tag **before** the read rather than after, so a menu that resolved to nothing still leaves the
  page depending on it; otherwise the first entry added to an empty menu would evict nothing.)*
- [x] **P8-18** `SearchDocument` + SQL Server full-text index + `SearchIndexService` in `Core/Search/`,
  populated via `IFieldType.ExtractSearchText` on save and publish, **asynchronously through the
  outbox** with a nightly reconcile *(mitigates R18)* [§17.1]. — 1 ed
  *(The outbox gained a second message type and `OutboxRunner` now dispatches through
  `IOutboxMessageHandler` rather than knowing one payload. The two handlers differ in a way worth
  stating: **cache eviction runs on every instance and claims nothing** — each node has its own
  in-process cache — while **the index handler claims its row first**, because the index is one
  shared table and N nodes rebuilding the same document is N−1 wasted passes and a real chance of two
  inserts racing on the unique key. The message carries ids, not text, so a message applied late
  indexes what the page says now rather than what it said when it was saved. The index describes
  **working content**: an editor looking for the paragraph they wrote this morning would not find it
  in an index of what is live, so `IsPublished` is a column instead — which is also what makes the v2
  public search a filter rather than a second index. **The Phase 0 arm64 question is answered here**:
  `SearchCapabilities` probes `SERVERPROPERTY('IsFullTextInstalled')` plus the index's existence once
  per process and falls back to a `LIKE` scan, so Azure SQL Edge is a supported deployment rather
  than a broken one, and the correctness suite runs on both engines. `SearchResults.FullText` reports
  which path answered, because "search is slow" should be visible on the screen rather than measured
  with a stopwatch.)*
- [x] **P8-19** Backoffice search UI with filters: template, status, owner, tag, modified date range,
  "has unpublished changes," "past review date." — 0.5 ed
  *(`/admin/search`, over `GET /api/cms/v1/search`. The filters are query-string parameters bound with
  `[SupplyParameterFromQuery]`, so a search is a URL an editor can bookmark and send to somebody —
  most of what "saved search" would otherwise have to be built for. **Every page-only filter narrows
  the result to pages by construction** rather than by a separate clause: asking for a template and a
  media item at once is a query with no answer, and it says so by returning nothing. Access rules cut
  the hits after the page is taken, the way every list endpoint does it, so a page of results can come
  back shorter than the count beside it — the alternative is translating "deeper beats shallower, deny
  beats allow" into SQL and keeping two copies of it in step.)*
- [x] **P8-20** `tags` field type completed + `Tag`/`PageTag` management. — included above
  *(**Tags are page metadata, not payload** — [§14.7] lists them beside owner, review date, and
  internal notes — so `PatchPageMetadataRequest.Tags` is the one writer and `ITagService` owns the
  rows. This **corrects the note on `TagsFieldType`**, which said the rows would be projected from the
  field's values: two writers would mean a tag removed on the properties panel reappearing the next
  time somebody saved the payload. The field type keeps contributing searchable text. Slug is
  identity and name is label, which is what makes "Product" and "product" one tag and what makes a
  rename onto an existing name a **merge** rather than a duplicate-key error — refusing it would leave
  an editor merging by hand on every page. `/admin/tags` does the housekeeping a free-form vocabulary
  needs, with each page count linking into the search screen's tag filter so "what is actually tagged
  this" is one click from the decision to rename or delete it. The properties panel's tag box
  completes against the existing vocabulary and commits on Enter only — not on blur, because an
  editor clicking away from a half-typed word has not decided to tag the page with it. This closes
  `P6-17`'s open half. **Registering the tag box's client uncovered a stale gate**: the page editor's
  axe and 200%-zoom passes (`P6-36`, `P6-38`) had been failing on a missing `IWorkflowClient`
  registration since `P7-12` — the review panel resolves it as a property, so the screen threw
  mid-render rather than degrading — which means neither gate had actually judged that screen. Both
  fakes are registered now and all 34 render gates pass, tag chips included.)*

### Tests — Phase 8

- [x] **P8-21** Integration: publishing a page evicts exactly its own cache entry and its dependents,
  and nothing else.
  *(`CachingTests`. The negative half is the interesting one: a bystander page's stored content is
  rewritten behind the cache's back, so its response can only stay the same if its entry survived —
  which no positive assertion can show.)*
- [x] **P8-22** Integration: an invalidation enqueued in a transaction that then **fails** is not
  dispatched; one in a committed transaction is dispatched even if the process is killed immediately
  after commit.
  *(The second half is a runner built with a fresh `OutboxState` — a process that started after the
  commit, with no memory of what came before — dispatching the message anyway.)*
- [x] **P8-23** Integration: two instances with Redis — a publish on A invalidates B.
  *(`RedisOutputCacheTests`, over a real Redis container and one shared database. It asserts the
  store type on both hosts first: without that, the suite would pass against two in-memory caches
  and prove nothing. Both instances drain the outbox, which is the deployment shape — the shared
  store covers the rendered HTML, each node's own poller covers its in-process content cache.)*
- [x] **P8-24** Integration: an authenticated editor's request is never served from the anonymous cache,
  and vice versa.
- [x] **P8-25** Performance: backoffice search returns by title, body, and slug across 50,000 seeded
  pages in under 500 ms.
  *(`SearchPerformanceTests`. The 50,000 documents are one set-based insert rather than 50,000
  publishes: this measures the query, and creating them through the services would measure the
  writer and take longer than the rest of the suite. **The correctness half runs on both engines and
  the 500 ms budget is asserted only where a full-text index exists** — holding the fallback scan to a
  full-text budget would be asserting the fallback is something never claimed for it. On an engine
  with the index, the test waits for `CHANGE_TRACKING AUTO` to finish populating before starting the
  clock, so what is timed is the query rather than the crawl; on Azure SQL Edge it reports the
  elapsed time and skips with a message naming `CMS_TEST_SQL_IMAGE`.)*

### Acceptance criteria — Phase 8

- [x] **P8 #1** Every public page emits a correct `<title>`, meta description, canonical link, robots
  directive, and OG/Twitter tags; JSON-LD validates against Google's Rich Results test.
  *(The tags and the JSON-LD are asserted by `SeoTests`. The Rich Results test itself is a hosted
  page a person submits, and is recorded on the `P9` launch checklist rather than claimed here.)*
- [x] **P8 #2** `sitemap.xml` contains exactly the published, indexable pages, and refreshes on publish.
- [x] **P8 #3** Staging serves `Disallow: /` regardless of the configured `robots.txt`.
- [x] **P8 #4** A cached page is served from the output cache, and publishing it evicts the entry
  immediately.
- [x] **P8 #5** Publishing reusable content evicts every dependent page and nothing else.
- [x] **P8 #6** An authenticated editor's request is never served from the anonymous cache, and vice
  versa.
- [x] **P8 #7** With Redis configured and two instances running, a publish on instance A invalidates
  instance B.
- [x] **P8 #8** An invalidation enqueued in a transaction that then fails is not dispatched; one in a
  committed transaction is dispatched even if the process is killed immediately after commit.
- [x] **P8 #9** Navigation reflects publish state within one cache generation; unpublishing removes the
  item.
  *(`NavigationTests` asserts it on a **different** page from the one unpublished: the menu is
  rendered by every page, so the removal has to be visible on a neighbour's cached response rather
  than only in a fresh query.)*
- [~] **P8 #10** Backoffice search returns a page by title, body text, and slug across 50,000 seeded
  pages in under 500 ms.
  *The three lookups are asserted at that scale by `SearchPerformanceTests`, on both engines. **The
  timing half has not been observed on a full-text engine yet**: the development machine is arm64 and
  runs Azure SQL Edge, which has none, so the budget assertion is live but has only ever been skipped
  here. It runs unskipped on CI's amd64 agent — see `P0 #3`, which is the same "no GitHub runner has
  executed this yet" gap.*

**Exit gate:** publish invalidates exactly the right cache entries; SEO output correct. — [x] met on
**2026-08-18** — both halves are asserted by `CachingTests`, `RedisOutputCacheTests`, and `SeoTests`;
the one criterion still open (`P8 #10`) is about search latency and bears on neither.

**Risks:** R17 (cache invalidation correctness — highest-severity functional risk), R18 (full-text index
maintenance cost).

> **Scheduling constraint:** decide during this phase whether multi-site is plausible within 18 months.
> Adding a `SiteId` discriminator is dramatically cheaper before v2 adds tables than after.
> - [x] **P8-26** Multi-site assessment recorded as an ADR. — 0 ed
>   *([ADR 0025](./docs/adr/0025-single-site-in-v1-no-siteid-discriminator.md).* ***v1 stays
>   single-site.*** *The column was never the cost — the ~20 uniqueness rules are, and each one is a
>   product question nobody has been asked: is `/about` one page or one per site, are templates shared
>   infrastructure or site content, is the taxonomy shared. A discriminator nothing sets and nothing
>   filters by is not insurance; it is a column every query must remember for two years, whose failure
>   mode — a missing site filter leaking another site's content — no single-site test can catch. The
>   ADR records the reversal in full, and the three properties that keep it affordable: `ISiteAddress`
>   is the only place the site's address is decided, `SiteSettings` is a row rather than
>   configuration, and every URL read or write goes through `RouteResolver`/`UrlService`.)*

---

## Phase 9 — Hardening, accessibility, and launch

**Objective:** verify the non-functional requirements and make the system operable. **14 ed** ·
Entry: all prior phases exit.

### Security — 5.5 ed

- [x] **P9-01** CSP with per-request nonces: the strict public policy from [§20.5], and a separate
  `/admin` policy carrying `wasm-unsafe-eval` and `frame-ancestors 'self'`. Nonce propagation added to
  `Server/Components/App.razor`; public and admin head content split. *(Existing-code change.)* — 1 ed
  *Three profiles rather than two, selected from **endpoint metadata** with the strictest as the
  default, so a route that says nothing is strict and a route that needs more says so
  ([ADR 0026](./docs/adr/0026-three-content-security-policies-public-carries-no-nonce.md)). Preview is
  the third: it frames its own rendered content to apply a device width, and `frame-ancestors 'none'`
  refuses same-origin framing like any other. **The public policy carries no nonce** — a public
  response is output-cached and replayed (`P8-06`), so a per-request value in one is a constant an
  attacker can quote, and the public document has no inline script to spend a nonce on: its only
  `<script>` elements are `application/ld+json` data blocks, which the parser never executes and CSP
  never consults. `script-src 'self'` alone is the stronger policy. The backoffice nonce now covers
  Blazor's inline import map as well as CodeMirror's injected theme, and is base64url so the header
  and the attribute hold the same bytes rather than the same value after a `&#x2B;` is decoded.
  `frame-src` is generated from `SanitizationOptions.AllowedIframeHosts`, so the list that decides
  whether an authored `iframe` is stored is the list that decides whether the browser loads it.
  Two things had to change for the policy to hold: `style-src-attr 'unsafe-inline'`, which ADR-0013
  ruled out and six backoffice components need — the sanitizer's CSS property allowlist is what makes
  it acceptable — and Bootstrap Icons, which was a `<link>` to jsDelivr and is now copied out of
  `node_modules` beside the Bootstrap bundle.*
- [x] **P9-02** `Strict-Transport-Security`, `X-Content-Type-Options: nosniff`,
  `Referrer-Policy: strict-origin-when-cross-origin`, minimal `Permissions-Policy`. — 0.5 ed
  *HSTS configured rather than left at the framework's 30-day default: a year, subdomains included,
  and no `preload` — submitting to that list is an operational commitment that is hard to walk back
  and is not this application's to make. The `Permissions-Policy` denies everything except
  `publickey-credentials-get`/`-create` and `fullscreen`, all `self`: a blanket denial would turn
  signing in with a passkey into a `NotAllowedError` nobody would attribute to a header.*
- [x] **P9-03** Rate limiting across all endpoint groups per [§20.6] — login/register/reset 5 per 15 min
  per IP; API writes 100/min per user; uploads 20/min per user; renditions 300/min per IP; preview
  tokens 30/min per token; public pages 600/min per IP. — 1 ed
  *Named policies on endpoint groups, not a global limiter: one of those counts the WebAssembly
  runtime's forty asset requests and the health probe against the same budget as the traffic it
  exists to shape. **Two policies decide per request whether they apply at all.** The credential
  policy sits on a Razor component endpoint that answers both the `GET` rendering the sign-in form
  and the `POST` attempting it, and five of those a quarter-hour is right for the attempt and absurd
  for the form — a single failed sign-in requests it twice. The API policy is on the whole versioned
  group and exempts reads, so a write added later is covered without anyone remembering to. Uploads
  are limited on the three routes that **begin** one rather than on every request one makes: a
  resumable 50 MB document is thirteen four-megabyte parts, and counting each as an upload would put
  the budget at a file and a half a minute. The preview limit was already in place from `P3-18` and is
  left as it is — 60 requests per address per minute is 30 page views, which is §20.6's number in the
  units it means, and partitioning by address rather than by token is what makes it defeat
  enumeration, which per-token partitioning cannot do by construction.*
  *The test found a real defect in something older: **a refused request was reaching the client as a
  404**. `UseStatusCodePagesWithReExecute` re-executes any error response carrying no body, and the
  page it re-executes through sets its own status — so a body-less `429` lost its status, its reason,
  and its `Retry-After` on the way out. The rejection handler now writes one: a problem document
  under `/api`, a sentence anywhere else.*
- [x] **P9-04** Identity hardening in `Server/Program.cs`: minimum 12-character password, breached-password
  screening, mandatory 2FA for `Administrator` / `Developer` / `Approver`, and the self-registration
  decision from **Q10**. *(Existing-code change — current settings are template defaults.)* — 1 ed
  *Twelve characters and **no character-class rules**, which is a decision rather than an omission:
  requiring a digit, a capital, and a symbol is what produces `Password1!`. Length and the breach
  screen do the work instead, and lockout now applies to new users too — the template excluded them,
  which is the one account an attacker is definitely trying. **Breach screening is two
  implementations behind one seam**, the shape `ADR-0024` used for mail: a common-password list held
  in the process, which always runs and costs nothing, and Have I Been Pwned's k-anonymity range API,
  which is what makes the claim literally true and is off unless a deployment turns it on, because it
  puts a third party on the path of every password change. An unreachable service accepts by default
  and logs — failing closed would stop every password reset during the incident that prompted them.
  A second validator refuses a password built out of the account's own name or address.*
  ***Mandatory 2FA is a request-time gate, not a sign-in check.*** A check at sign-in refuses an
  account that is already in this state and leaves it unable to fix itself, and says nothing about
  the account granted `Administrator` while its session is open — which is how most accounts arrive
  here. A privileged principal with no second factor may reach account management and nothing else:
  a redirect for a document, a `403` for an API call, because a redirect to an HTML page reads as a
  parse error to the backoffice's fetches. It reads a `cms:2fa` claim rather than the database, which
  made one existing-code change necessary: `EnableAuthenticator` did not call `RefreshSignInAsync`,
  so an administrator who had just enrolled would have been sent back to enrol for up to half an hour.*
  ***Q10 is answered as configuration, defaulting to closed*** — see the decision entry above.
- [x] **P9-05** Verify secrets handling [§20.8]: the Aspire `sql-password` dev default never reaches
  production; media-signing HMAC key sourced from key vault/environment and rotatable. — 0.25 ed
  *Both halves already held on inspection: `AddParameter`'s `publishValueAsDefault` is false, so the
  development password is a run-mode value and is not written to the deployment manifest, and
  `MediaSigningOptions` has implemented two keys and a grace period since `P5-18`. What was missing
  was the assertion. `CmsSecretsGuard` runs beside `AssertCmsMediaCapabilities` and **refuses to start
  a non-Development deployment** that has no signing key, has a half-finished rotation with no end
  date, or carries the Aspire password in its connection string. All three already *work* when they
  are wrong, which is why a startup refusal rather than a health check: an unconfigured key generates
  a per-process one, so every image renders correctly on the instance that signed the URL and `403`s
  on the next. The parameter is also marked `secret: true` so it stays out of the dashboard and the
  logs. Development is exempt on purpose — the point is not to make a first run require a key vault.*
- [x] **P9-06** Penetration-test pass: XSS corpus against **live rendering**, IDOR sweep, upload fuzzing,
  unsigned rendition URLs, preview-token enumeration, CSRF. — 1.75 ed
  *Six areas, four of them newly covered and two already held. The **corpus against live rendering**
  is the one that could not be faked: the same 52 payloads that the unit suite puts through
  `SanitizationService` are now stored through the real draft service, published, and read back over
  HTTP, with the delivered document re-parsed and its content region inspected — which is what would
  catch a field renderer reaching for `MarkupString` on a stored value, a bug every assertion in the
  unit corpus passes. The corpus moved to `TestSupport` so both suites read one list. All 53 cases
  pass, including the page title, whose only defence is escaping because it is plain text and is
  written into the `<title>` and into every Open Graph `content` attribute. **Upload fuzzing** sends
  eight payloads carrying image extensions and image content types — HTML, an SVG with script, a PHP
  tag behind a real `GIF89a` header, a shell script, an empty file, a truncated JPEG, a double
  extension, a traversal file name — and asserts both that each is refused as a client error rather
  than a 500 and that **the library is still empty afterwards**, which the status codes cannot say.
  Unsigned and forged rendition URLs and guessed preview tokens are refused, and the preview refusals
  are asserted to be **byte-identical to one another**, because a body that distinguished "no such
  token" from "expired" is an oracle confirming a guess landed. CSRF is asserted behaviourally across
  three verbs in addition to the structural check `ApiContractTests` already makes. The **IDOR
  sweep** is `P7-07`'s and is not restated; the live pass names it rather than duplicating it.*
  *The live corpus runs in the `XSS corpus` CI job rather than among the integration tests, for the
  reason that job exists at all: "can stored content execute in a visitor's browser" is not a failure
  to find among two hundred other red tests.*

### Accessibility — 2.5 ed

- [x] **P9-07** axe-core across all screens, backoffice and public output. — 0.5 ed
  *The backoffice half has existed since `P1-31` and was extended screen by screen through `P2-23`
  and `P6-36`; what P9 adds is the public half, which is the one with the wider audience. It renders
  **`CmsDeliveryDocument` itself** rather than a re-creation — a gate that judged a look-alike would
  go green on a shell nobody serves, and the shell is where most of what an audit looks at lives: the
  `lang` attribute, the landmarks, the navigation, and the single `h1`. Everything the render reaches
  for is the real implementation except the navigation reader, which is a database query; what the
  menu contains is content, and what the component makes of it is what is judged. Two cases, because
  they fail differently: a full page of awkward content (a captioned table with row and column
  headers, a nested list, an h2/h3 sequence) and a page with most of its zones empty, where the
  article template still renders every wrapper. **Zero violations on both.** It joined the
  `Accessibility (axe)` CI job, which is a required check; the compiled-stylesheet passes — contrast
  and reflow — stay in the E2E job, which builds the stylesheet.*
  ***The browser matrix found a defect in this gate before the gate found one in the site.*** The
  fixture put its table in a `richText` value, which the `Basic` profile strips — so the audit had
  been judging a page with no table on it, and passing, because a stripped table leaves its caption
  behind as ordinary text and the assertion was looking for the caption. `P9-24` noticed only because
  it measures the table's bounding box and there was none. The table is now stored as `html`, which is
  the field type one is really stored in (there is no table tool — see `P9-10`), and the assertion
  looks for `<table` and a `scope` attribute rather than for words.*
- [!] **P9-08** Manual keyboard pass and screen-reader passes (NVDA + VoiceOver). — 1 ed
  *Blocked on a person, the way `P6-37` is. Nothing here is a thing an assertion supplies: the
  question is whether a screen reader's announcement makes sense to somebody using it, and there is
  no test that answers it. What the automated gates do cover is recorded against `P9-07` and `P9-09`.*
- [x] **P9-09** 200% zoom and `prefers-reduced-motion` verification. — 0.25 ed
  *Both extended from the backoffice to the public page, and both measured in a browser rather than
  read out of the stylesheet. The reflow pass loads the **whole delivery document** rather than a
  fragment in a wrapper, because the document writes its own viewport meta tag and that tag is half
  of what makes a page reflow at all. Reduced motion is measured by asking a browser context that
  requests it for every element whose computed style still has a running animation or transition — a
  media query that exists and does not cover the animation somebody added last week passes a grep and
  fails the reader. Both passes carry a negative control, for the reason the `P6-38` one does: an
  assertion that nothing is too wide, or that nothing is moving, passes just as well against a page
  that rendered nothing.*
- [x] **P9-10** Authored-output accessibility rules [§28]: heading structure validated (`h2`–`h6` only in
  rich text; skipped-level warning at publish), link-text warnings ("click here", bare URLs), `<th
  scope>` emitted by the table tool, `color` field constrained to design-system tokens, `lang` from
  `SiteSettings.Culture`. — 0.5 ed
  *Three of the section's rules are checks on markup and are answered by parsing one document, so
  they are one service on the publish path: skipped heading levels, link text that says nothing, and
  tables with no usable headers. **Which values to parse is asked of the field type registry rather
  than of a list of keys** — `Sanitizable` already means "this is markup an author wrote" — and the
  payload is walked recursively, because a rich-text property inside a block is exactly as visible as
  one in a zone and a top-level walk would report a clean bill for a page built out of blocks. The
  heading sequence runs across zones rather than restarting in each, since a reader moving by heading
  does not know where a zone ends. **Every diagnostic is a warning**; the one accessibility rule that
  blocks is alt text, because an undescribed picture is invisible rather than merely awkward.*
  *The other three rules turned out to be structural and already true, which is worth recording
  rather than implementing twice: `h1` is absent from every sanitization profile, so the rich-text
  editor cannot offer one; the `color` field type takes a `palette` of design-system tokens and
  refuses anything outside it; and there is **no table tool to fix** — the Quill toolbar is the short
  list of `P6-08`, so a table can only arrive through the HTML source editor or a paste, which is
  exactly what the header warnings are for. `lang` is the one that was genuinely missing: it was
  hard-coded `en`, and now comes from `SiteSettings.Culture` through the head builder, on the settings
  read the head already makes.*
- [x] **P9-11** Remediation of all findings to zero critical/serious. — 0.25 ed
  *Nothing to remediate, and that is a result rather than an omission: the backoffice findings were
  found and fixed when the gates were written (`P6-36` turned up three landmark and heading faults,
  `P6-38` four screens that could not reflow), and the public output passed axe, the reflow pass, and
  the reduced-motion pass on their first green run. The finding this phase did produce was in the
  authored-content rules, which had no implementation at all — `P9-10`.*

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

- [~] **P9-18** Backup/restore drill **including a media-store restore**, timed against the RTO;
  documented runbook [§24.3]. — 1 ed
  *The runbook is written ([`docs/runbooks/backup-restore.md`](./docs/runbooks/backup-restore.md));
  **the drill itself needs an environment to restore into** and is what stays open. The runbook is
  built around the sentence the drill exists to prove: **restoring the database does not restore the
  site.** Content is in SQL Server and pictures are in a blob container, and a drill that restores
  only the first produces a site whose every page renders and whose every image is a broken icon —
  which passes any check that only asks whether pages load. The verification steps are ordered so
  each rules out a different failure, and the media step is called out as the one a database-only
  restore fails. Renditions are backed up rather than left to regenerate, because a site restored
  without them re-encodes every image on the first view of each, at the moment it is under the most
  scrutiny it will ever get.*
- [x] **P9-19** Operational documentation: deployment, configuration reference, health checks,
  dashboards, alert thresholds, incident runbooks. — 1 ed
  *[`docs/operations.md`](./docs/operations.md), written for whoever is on call rather than whoever
  wrote the code: each section says what breaks, what the symptom looks like from outside, and what to
  do about it. Five runbooks for the five failures with no other symptom — invalidation that has
  stopped, a signing key that differs between instances, a privileged account meeting the 2FA gate
  mid-session, a build serving against a schema missing its migration, and an index that is behind
  rather than wrong. The configuration reference lists only settings that change behaviour in
  production, and the retention windows are called out as **`SiteSettings` columns rather than
  configuration**, because how long an organisation's records last is its decision and not its
  deployment's. It also carries the proxy warning the rate limiter needs: behind an ingress every
  visitor shares one bucket, which turns the public limit into a site-wide one.*
- [x] **P9-20** Verify every health check has a monitor and an alert threshold: `cms-database`,
  `cms-media-store`, `cms-templates`, `cms-scheduler`, `cms-outbox`. — included above
  *Verifying turned up that **`cms-database` did not exist**. Aspire's `EnrichSqlServerDbContext`
  registers a connectivity check under the context's full type name, which no runbook and no alert
  rule refers to — a check nobody can name is a check nobody has a monitor on. It is now a real check
  that asks two questions rather than one: connectivity, which is the loud failure, and **whether the
  schema is the one this build expects**, which is the quiet one — an instance that connects to a
  database missing its migration starts, serves, and fails on whichever request first touches the new
  column. That is *degraded* rather than unhealthy, because it is the state a rolling deployment
  passes through. `HealthCheckContractTests` asserts the registered set is exactly the documented set,
  that no CMS check is tagged `live`, and — reading the file from disk — that every one is **named in
  the operations document**, since an alert rule is written from a name.*
- [x] **P9-21** User documentation: editor guide, developer template-authoring guide, admin guide. — 1 ed
  *Three guides in [`docs/guides/`](./docs/guides/). The editor guide is the one `P9 #7` is measured
  against, so it is written as a path from an empty tree to a published page and says plainly that a
  place somebody gets stuck is a defect in the document. It explains the two things editors reliably
  get wrong — that saving is not publishing, and the difference between an error and a warning in the
  publish dialog — and gives the reason behind each accessibility warning rather than only the rule.
  The template guide carries the three rules that are easy to get wrong (the `h1` is the title,
  navigation belongs to the shell, nothing may stream) and the field type table. The admin guide
  covers roles, the three that require a second factor, the ACL clause that surprises people
  (`ADR-0023`), retention, and a closing list of the things only an administrator can break.*
- [x] **P9-22** Update `README.md` with CMS setup, template authoring, and the schema sync CLI.
  *(Existing-code change.)* — included above
  *Rewritten from the template leftovers it still was — bootstrap, `dotnet ef`, and `aspire run`. It
  now opens with a map of the other documents, and covers the content model, a worked template, the
  three CLI verbs and what `diff` exiting non-zero is for, and the three security rules worth knowing
  before writing anything.*
- [-] **P9-23** Switch migration policy to roll-forward-only after launch; retain `Down` methods as
  documentation only. — 0 ed
  *Deferred by its own terms: it is a policy change that happens **after** launch, and doing it now
  would remove a test that is currently earning its keep. `MigrationsApplyFromEmptyTests` applies and
  reverts every migration against a real container on every build, and it is what catches a rename
  EF has modelled as drop-plus-add. Recorded in the launch checklist rather than left as a phase task
  somebody closes early; migration #9's `Down` is tested like the eight before it.*
- [x] **P9-24** Browser matrix verification for NFR-13 (last 2 versions of Chrome, Edge, Firefox,
  Safari). — included above
  *Four browsers, **three engines**: Chrome and Edge are both Blink and differ by a shell rather than
  a renderer. Playwright bundles all three, so `BrowserMatrixTests` runs the real public document
  through Chromium, Firefox, and WebKit — which is Safari's engine — with the compiled stylesheet.
  What is asserted is layout, because the public site is static HTML and CSS with no script of its
  own, so "does it work" reduces to "does it come out the same shape": the title is present and has a
  height, the page does not overflow its viewport, and the authored table stays inside the content
  column. The height assertion is the one that catches a font that did not load, which is otherwise
  invisible to a test. **The backoffice is deliberately not in the matrix** — driving a WebAssembly
  application needs the hosted-app harness `P6-32`…`P6-34` are also waiting on.*
- [x] **P9-25** Audit-log retention: a nightly sweep and a configurable window, matching what
  `RetentionPolicy` already does for versions [§11.7]. — 0.5 ed
  *Raised by `P5-33`. `AuditLog` grew without bound and nothing pruned it, which is a defensible
  default for an unanswered compliance question (**Q9**) and the wrong state to launch in — the
  interceptor writes a row for every tracked change, so the table grows with editorial activity
  forever, on the same transaction as the content, and eventually every save waits for it. The window
  is `SiteSettings.AuditLogRetentionDays` (migration #9), **zero meaning keep everything**, which is
  the part Legal decides; the sweep is the part that is the same either way. It deletes in batches of
  two thousand under a fifty-batch ceiling, because SQL Server escalates row locks to a table lock at
  around five thousand and a table lock on `AuditLog` blocks every `SaveChanges` in the application.
  There are **no exceptions to the age rule**, unlike the five clauses that spare a version: a log
  with holes in it is worse evidence than a shorter one.*
  ***The task turned up a second, larger finding.*** The version sweep has implemented all five
  clauses of [§11.7] since `P2-13` and **nothing ever called it** — it was reachable from a test and
  from nowhere else, so every deployment kept every version of every page forever while a policy that
  said otherwise sat in the code. The new `RetentionService` runs both, each in its own scope and each
  caught separately, and `AuditRetentionTests` asserts the host registers it so the same thing cannot
  happen twice.*

### Acceptance criteria — Phase 9

- [x] **P9 #1** Zero critical or high findings from the security pass; all mediums triaged with owners
  and dates.
  *`P9-06`'s pass produced no critical or high finding, and the two mediums it did produce were fixed
  rather than triaged: a refused request reaching the client as a `404` because the site's status-code
  pages re-execute any body-less error response, and `cms-database` not existing under a name any
  alert rule could refer to (`P9-20`). Everything the pass asserts is a standing test rather than a
  one-time review, which is the property that matters — the corpus, the IDOR sweep, the upload fuzz,
  the signature and token refusals, and CSRF all run on every build.*
- [x] **P9 #2** WCAG 2.2 AA verified on backoffice and public output; zero critical/serious axe
  violations.
  *Zero on both, and the public half is new (`P9-07`). Reflow at 200% and `prefers-reduced-motion` are
  verified on both surfaces too, each with a negative control. **The automated half is what is
  claimed here**; `P9-08`'s keyboard and screen-reader passes are still open and are the part no
  assertion supplies.*
- [ ] **P9 #3** NFR-1, NFR-2, NFR-7, and NFR-9 met under load with a 50,000-page dataset.
- [ ] **P9 #4** Lighthouse mobile performance ≥ 90 on all reference templates.
- [ ] **P9 #5** A full restore from backup — database and media — produces a working site, timed against
  the RTO. *The runbook is written (`P9-18`); the drill needs an environment to restore into.*
- [x] **P9 #6** Every health check has a monitor and an alert threshold.
  *All five of [§24.2] now exist — `cms-database` did not until `P9-20` — and each has a row in
  [`docs/operations.md`](./docs/operations.md) saying what makes it degraded, what makes it unhealthy,
  and which of those pages somebody. `HealthCheckContractTests` asserts the registered set is exactly
  the documented set and reads the document from disk to check each is named in it, because an alert
  rule is written from a name.*
- [ ] **P9 #7** An editor unfamiliar with the system completes create → publish using only the written
  guide. *The guide is written (`P9-21`); the criterion is the person, and it has not been run.*

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
- [ ] **L-14** Switch the migration policy to roll-forward-only, retaining `Down` methods as
  documentation (`P9-23`). Deferred here rather than closed in Phase 9 because it happens **after**
  launch, and doing it early removes `MigrationsApplyFromEmptyTests` — the check that catches a rename
  EF has modelled as drop-plus-add — while it is still earning its keep.

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
| 4 | `AddCmsRouting` | 3 | P3-02 | `PageRoute`, `Redirect`, `NotFoundLog`, `PreviewToken` | [x] |
| 5 | `AddCmsReusableContent` | 4 | P4-02 | `ReusableContent`, `ReusableContentVersion` | [x] |
| 6 | `AddCmsMedia` | 5 | P5-02 | `MediaFolder`, `MediaItem`, `MediaRendition` | [x] |
| 7 | `AddCmsWorkflow` | 7 | P7-08 | `WorkflowTask`, `Comment`, `PageAcl`, `ScheduledJob`, `Notification` (+ the seven seeded roles and `SiteSettings.RedirectToParentOnUnpublish`) | [x] |
| 8 | `AddCmsDelivery` | 8 | P8-14 | `NavigationMenu`, `NavigationItem`, `SearchDocument` (+ full-text catalog), `OutboxMessage`, `Tag`, `PageTag` | [x] |

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
| `Data/Models/AuthDbContext.cs` | Read every stamped timestamp (`CreatedOn`, `ModifiedOn`, `DeletedOn`) from the injected `TimeProvider` rather than `DateTimeOffset.UtcNow`. **The retention sweep compares its cutoff to these columns**, so while the two clocks were independent no test could move one without the other, and the result turned on the real calendar date | 3 | P3-09 | [x] |
| `Data/Models/AuditEntry.cs` | `ToAuditLog` takes the clock, so an audit row and the fingerprints on the entity it describes carry one instant | 3 | P3-09 | [x] |
| `Data/Models/ApplicationDbContext.cs` | Constructor overload carrying `TimeProvider`, greedily selected by the container. The existing overloads stay and default to `TimeProvider.System`, so a host that registers no clock is unchanged | 3 | P3-09 | [x] |
| `Data/Models/ApplicationDbContext.cs` | Suppress EF warning 10622: `PageVersion` deliberately carries no soft-delete filter, so a deleted page's history stays retrievable | 2 | P2-03 | [x] |
| `Data/Models/ApplicationDbContext.cs` | Register CMS `DbSet`s; apply configurations from the assembly | 1 | P1-04 | [x] |
| `Server/Program.cs` | Register CMS services, field type registry, output cache, rate limiting, security headers, background services; delivery endpoint registered **last** | 1–9 | P1-30, P3-13, P9-01…P9-04, P9-20, P9-25 | [x] |
| `Server/Program.cs` | Tighten the Identity password policy; decide self-registration. Twelve characters, no character-class rules, lockout for new users, and a breach screen; registration answers 404 until **Q10** says otherwise | 9 | P9-04 | [x] |
| `Server/Components/Email/IdentityNoOpEmailSender.cs` | Replaced by `IdentityCmsEmailSender` over the CMS's own transport, and deleted. `RegisterConfirmation.razor.cs` showed the confirmation link whenever the sender was the no-op one; it now shows it only when mail is unconfigured **and** the environment is Development — the old condition would have handed every visitor a working confirmation link on a production deployment that forgot to configure SMTP | 7 | P7-18 | [x] |
| `Data/Interceptors/AuditLogInterceptor.cs` | Exclude `ScheduledJob` and `Notification` from audit capture — both are written by services rather than by a person, and the publish a job performs is audited by the ordinary path | 7 | P7-08 | [x] |
| `Shared/Contracts/Security/ICmsAuthorization.cs` | Expose the caller's role names, which the ACL resolver needs to find the rules addressed to any of their roles | 7 | P7-04 | [x] |
| `Core/Publishing/PublishingService.cs` | The workflow gate (`TwoStep` refuses an unapproved version), the draft returning to `Draft` after a publish, and the configured parent redirect on unpublish | 7 | P7-10, P7-15 | [x] |
| `Core/Dashboard/DashboardService.cs` | Redact rows naming pages the caller may not read. The dashboard is the one screen that reads across the whole site, so it is the likeliest place for a hidden branch to reappear as a title — and `TotalCount` is reduced with it, or the branch leaks as a number instead | 7 | P7-06 | [x] |
| `Client/…/Pages/PageEditor.razor` | Carry the review, schedule, and comment panels beside the properties pane. Each renders nothing when the caller cannot use it | 7 | P7-12, P7-16 | [x] |
| `Client/…/Dashboard/DashboardScreen.razor` | Section links for review, notifications, and audit, each behind the role that can use it | 7 | P7-12, P7-19, P7-20 | [x] |
| `Server/Components/Account/Pages/Manage/EnableAuthenticator.razor.cs` | Call `RefreshSignInAsync` after enabling the authenticator. Without it the `cms:2fa` claim the mandatory-2FA gate reads stays false until the security stamp is revalidated, so an administrator who has just enrolled is sent back to enrol for up to half an hour | 9 | P9-04 | [x] |
| `Server/Components/App.razor` | CSP nonce propagation; split public and admin head content. The split turned out to be structural rather than a head to divide: the public site is rendered by `CmsDeliveryDocument` and never sees this file, so the nonce, the import map, and `wasm-unsafe-eval` are all backoffice-only by construction (`ADR-0026`). Bootstrap Icons moved from jsDelivr to this origin, which `default-src 'self'` required | 6, 8–9 | P6-08, P9-01 | [x] |
| `Server/Components/Routes.razor` | Scope interactive routing to `/admin`; keep public pages static SSR | 3 | P3-14 | [ ] |
| `aspire/…AppHost/AppHost.cs` | Add Azurite and optional Redis resources | 0 | P0-13, P0-14 | [ ] |
| `Directory.Packages.props` | Add HtmlSanitizer, Markdig, SkiaSharp, MetadataExtractor, HybridCache, rate limiting, Testcontainers, bUnit, Playwright, k6 tooling | 0–5 | P0-07, P0-12 | [ ] |
| `Shared/Common/FieldLengths.cs` | Add CMS field length constants | 1 | P1-03 | [x] |
| `styles/site.scss` | Add backoffice and content typography layers | 6 | P6-40 | [x] |
| `Server/package.json`, `Server/…Server.csproj` | Add esbuild and the two editor bundles to the front-end build, so a missing bundle fails the build rather than the page (`D13`) | 6 | P6-08 | [x] |
| `Server/package.json`, `Server/…Server.csproj` | Add Bootstrap Icons to the front-end build. It was a CDN `<link>`, which the strict `default-src 'self'` refuses; the alternative was a third-party host in the policy, for a font | 9 | P9-01 | [x] |
| `Core/Fields/Types/TextFieldTypeBase.cs`, `RichTextFieldType.cs` | Declare the `softLimit` setting the counter honours. Configuration is closed (`ADR-0015`), so an undeclared setting is refused on save | 6 | P6-12 | [x] |
| `Client/Components/Admin/PlainSlotValues.cs` | Reduced to a raw envelope round trip: each field type's storage shape now lives in its own editor rather than in a switch shared by every form | 6 | P6-06…P6-15 | [x] |
| `Server/Program.cs`, `Client/Program.cs` | Register the dashboard service and its two client halves, and replace `Core`'s identity-free bulk scope factory with the one that captures the signed-in editor — without which a background batch is refused on its first item or recorded as having been done by nobody | 6 | P6-24…P6-29 | [x] |
| `Server/Api/Cms/CmsApiEndpoints.cs` | Map the dashboard and bulk endpoint groups | 6 | P6-27, P6-29 | [x] |
| `Client/…/PageList.razor`, `PageVersions.razor`, `PagePreviewLinks.razor`, `ReusableLibrary.razor` | Wrap each wide table in `table-responsive`. All four overflowed a 640-pixel viewport, which is what 200% zoom reports on a 1280-pixel display — found by the zoom pass rather than by looking | 6 | P6-38 | [x] |
| `Client/…/Dashboard/DashboardGroupList.razor` | Groups are `div`s under headings rather than labelled `section`s, and the heading level is a parameter. Four labelled sections in one card are four landmarks announced by the same name; a fixed level makes one of the two screens skip one — both found by the axe gate | 6 | P6-36 | [x] |
| `README.md` | Document CMS setup, template authoring, schema sync CLI. Rewritten from the template leftovers it still was — bootstrap, `dotnet ef`, `aspire run` — and now opens with a map of the other documents | 9 | P9-22 | [x] |
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
| R-3 | "In zones that are plain text or html/markdown … inline editing … 'edit/preview' editor experience" | [§14.4] | P6-08…P6-14 | P6 #2, #3 | [x] 2026-08-16 — both criteria met. Edit/Preview/Split over markdown and over the WYSIWYG surface, with the preview rendered by the server's one pipeline rather than a second copy in the browser, and the HTML editor warning about what will be stripped while the author is still writing |
| R-4 | "Reusable content … specified once but then reused in multiple (common footers, image carousels)" | [§9] | P4-01…P4-11 | P4 #1, #2 | [x] 2026-08-16 |
| R-5 | "content editors should be able to create pages from those templates" | [§10.1], [§22.1] | P2-07, P2-16, P2-23 | P2 #1 | [x] 2026-08-14 |
| R-6 | "populate the 'placeholder' areas with actual content" | [§6.2], [§14.3] | P2-10, P2-23, P6-05, P6-06 | P2 #2, P6 #1 | [x] 2026-08-16 — both criteria met. A zone is a card with the control its field type declares, and every built-in type has one, so filling a page never falls back to a raw payload; the layout comes from the revision the draft was authored against rather than the template's current one |
| R-7 | "Pages … need to have a url specified so that end users would be able to navigate to the pages" | [§10.2]–[§10.4] | P3-01…P3-06, P3-13 | P3 #1 | [x] 2026-08-15 |
| R-8 | "pages in draft mode before they get published out" | [§11.1], [§11.2] | P2-10, P2-11, P3-16 | P2 #3, P3 #2 | [x] 2026-08-15 |
| R-9 | "pages should be versioned" | [§11.1]–[§11.5] | P2-11, P2-13, P2-14 | P2 #5, #6, #7 | [ ] |
| R-10 | "a published page could still be visible to unauthenticated users while content editors are making changes that only they can see internally" | [§11.1], [§12] | P2-11, P3-12, P3-16 | **P2 #4, P3 #3** — the central promise | [x] `P2 #4` 2026-08-14; `P3 #3` 2026-08-15 |
| R-11 | "image management functionality … upload images" | [§13.3] | P5-01…P5-08 | P5 #1–#5 | [x] 2026-08-16 — `P5 #1`–`#5` all met; `P5-08` (chunked upload) is a comfort feature over a working upload, not a gate |
| R-12 | "resize and rotate those images" | [§13.4], [§13.5] | P5-09…P5-13 | P5 #6, #7 | [x] 2026-08-16 — both met, and non-destructively: the stored original is byte-for-byte identical across an edit, a library rotate reaches every page showing the image without republishing one, and a usage crop reaches only its own placement |
| R-13 | "'reference' those images inside the pages they are creating" | [§13.6], [§7.1] `media` | P5-19, P5-20 | P5 #10 | [x] 2026-08-16 — a placement stores an id and its own edits, nothing about the file, which is what makes `R-12`'s library rotate propagate |
| R-14 | "do plenty of research and add elements that are clearly missing" | [§4.2] — 30 gaps | see below | per gap | [ ] |

### Gap coverage (R-14)

The 30 gaps from [§4.2], mapped to the tasks that close them.

| Gap | Item | Tasks | Done |
|---|---|---|:--:|
| #1 | URL management | P3-03, P3-04 | [x] 2026-08-14 |
| #2 | Redirects | P3-05, P3-06 | [x] 2026-08-15, serving over HTTP since P3-13 |
| #3 | SEO metadata | P8-01…P8-03, P6-17 | [x] 2026-08-18 — one builder resolves every fallback and every absolute URL, so preview and delivery cannot emit different heads for one version; hand-authored JSON-LD **replaces** the generated set rather than joining it |
| #4 | `sitemap.xml` & `robots.txt` | P8-04, P8-05 | [x] 2026-08-18 — exclusions live in the query rather than being applied afterwards, and non-production serves `Disallow: /` from the environment name, which is the one fact a copied production database cannot carry with it |
| #5 | Scheduled publish/unpublish | P7-13…P7-16 | [x] 2026-08-18 — claimed with one atomic `UPDATE … OUTPUT`, so running the poller on every instance is correct rather than merely tolerated; a failure is terminal and notifies its owner rather than retrying every thirty seconds |
| #6 | Approval workflow | P7-08…P7-12 | [x] 2026-08-18 — three modes, the draft frozen while under review, and a rejection that keeps the refused version exactly as it was refused while handing the author an editable copy |
| #7 | Granular permissions | P7-01…P7-07 | [x] 2026-08-18 — role grants from the §21.1 matrix, narrowed by section ACLs resolved as an indexed prefix match, enforced in the service layer and swept for IDOR across nineteen entry points |
| #8 | Shareable preview links | P3-17…P3-19 | [x] 2026-08-15 |
| #9 | Version diff & rollback | P2-13, P2-14 | [ ] — the diff and the rollback have been done since Phase 2; what `P9-25` found is that the **retention sweep beside them had no caller**, so it now runs nightly. The gap stays open on `P2 #6`/`#7` |
| #10 | Soft delete & recycle bin | P2-08, P6-28 | [x] 2026-08-16 — the service since Phase 2, the screen since `P6-28`. The bin lists subtree roots rather than deleted rows, restores bring a page back as a draft, and the one irreversible operation is Administrator-only and asks for the name to be typed |
| #11 | HTML sanitization / XSS defense | P1-18…P1-20, P9-06 | [x] 2026-08-19 — the corpus was already a merge gate against the sanitizer; `P9-06` runs the same 52 payloads through the whole path, stored and published and read back over HTTP with the delivered document re-parsed, which is what would catch a renderer that un-escaped what the sanitizer had cleaned |
| #12 | Upload validation & safe serving | P5-05…P5-07, P5-17 | [x] 2026-08-16 — decided at the sniffer and the sanitizer, so single-request upload, replace, and chunked assembly share one set of refusals |
| #13 | Alt text enforced | P5-21 | [x] 2026-08-16 — at upload, at `PATCH`, and at publish on both the page and reusable paths; a placement override is one of the three ways out |
| #14 | Focal point / smart cropping | P5-12 | [x] 2026-08-16 |
| #15 | Renditions, `srcset`, WebP | P5-13…P5-16, P5-20 | [x] 2026-08-16 — every descriptor is the width the browser will actually receive, because the pipeline never upscales |
| #16 | Where-used / link integrity | P4-07, P4-08 | [x] 2026-08-16 — transitive, split by pinned, and the delete guard is built on it |
| #17 | Output caching + invalidation | P8-06…P8-13 | [x] 2026-08-18 — caching is opt-in per endpoint rather than a base policy with exclusions, and invalidation is enqueued inside the publish transaction and applied by every instance |
| #18 | Concurrency control | P2-03, P2-15, P6-19 | [x] 2026-08-16 — both layers, and the UI that makes the authoritative one usable: the `rowversion` decides, the advisory lock warns, and a lost race now hands the losing editor the draft that won so keep-mine, take-theirs, and open-diff are real choices rather than a banner |
| #19 | Backoffice search & content tree | P6-02…P6-04, P8-18, P8-19 | [x] 2026-08-18 — full-text over titles, extracted body text, and keywords where the engine exists and a scan where it does not, with the filters [§17.1] lists and a URL an editor can keep |
| #20 | Audit trail surfaced in the UI | P7-20 | [x] 2026-08-18 — read-only by construction, filtered by entity, id, user, and date, and gated on `Audit.View` rather than on user management |
| #21 | Template change / schema evolution safety | P1-25, P1-26, P1-32 | [ ] |
| #22 | Public site search | **v2** — index built by P8-18 | [-] *the index and its `IsPublished` column exist, so v2 is a filter and a results page rather than infrastructure* |
| #23 | Localization | **out of scope** — Q1 resolved, [§19] | [-] |
| #24 | Navigation/menu management | P8-14…P8-17 | [x] 2026-08-18 — generated from the tree and hand-managed, both filtered to published content, both cache-tagged |
| #25 | Forms / lead capture | **v2** | [-] |
| #26 | Headless read API + webhooks | **v2** | [-] |
| #27 | Import/export & environment promotion | P1-26, P1-28 (structure, v1); content bundles **v2** | [ ] |
| #28 | Rate limiting & brute-force protection | P9-03, P9-04 | [x] 2026-08-19 — the six limits of [§20.6] as named policies on endpoint groups, two of which decide per request whether they apply; plus lockout for new users, a twelve-character minimum, a breach screen, and mandatory 2FA for the three roles that can change what the public site says |
| #29 | Editorial metadata | P6-17, P8-20 | [x] 2026-08-18 — owner, review-by, internal notes, **and now tags**: `P8-20` gave the panel somewhere to write them, and the box completes against the vocabulary the site already uses rather than inviting a fourth spelling of one label |
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
| 9 | Multi-site support | [§29.3] | 25 ed — **assessed 2026-08-18, [ADR 0025](./docs/adr/0025-single-site-in-v1-no-siteid-discriminator.md): v1 stays single-site.** The estimate roughly doubles once v2 adds tables, which the ADR states outright |

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
| R6 | ~~Catch-all route shadows framework/admin paths~~ | — | 3 | **Closed 2026-08-15** — mapped last, reserved prefixes read from `Slugs.Reserved`, outcome asserted by `RouteOrderingTests` ([ADR-0020](./docs/adr/0020-catch-all-route-ordering-and-reserved-prefixes.md)) | Closed |
| R7 | ~~`DynamicComponent` under static SSR misbehaves~~ | — | 0/3 | **Closed** — S2 returned go at the Phase 0 gate; now running in shipped code through four levels of `DynamicComponent` (`P3-13`) | Closed |
| R8 | Invalidation fan-out slow for a reusable item on 10,000 pages | Med | 4/8 | Publish exceeds NFR-7 (2 s) | **Measured 2026-08-16, still open for P8** — the where-used walk is ~2.8 ms for 40 pages and its query count is bounded by nesting depth (5), not by page count ([baseline](./docs/phase-4-fanout-baseline.md), `ReferenceFanOutTests`). The residual is the eviction itself, which has no implementation until P8 |
| R9 | Testcontainers unreliable in CI | Med | 0 | Flake rate above 5% | Open |
| R10 | ~~Six Labors licensing stalls Phase 5~~ | — | 5 | **Closed** — SkiaSharp selected; residual is the silent-null AVIF encode, mitigated by P5-09 | Closed |
| R11 | Rendition generation saturates CPU | High | 5 | CPU above 70% sustained during load test | **Mitigated and measured 2026-08-16, still open for P9** — renditions are lazy rather than warmed (ADR 0007), a per-key semaphore collapses N cold requests for one rendition into one encode (`P5-30`), and generation is bounded by an allowlist of six widths. `RenditionBenchmarkTests` holds NFR-8 on cold encodes through the whole endpoint. None of that is the trigger: the contingency turns on **sustained CPU under a load test**, and no load test exists until P9 |
| R12 | SVG sanitization bypassed | **Critical** | 5 | Any bypass found → disable SVG | **Unreached in the shipped default, still open** — `SvgUploadPolicy` defaults to `Reject` (`P5-06`), so a deployment that never opts in cannot be bypassed because it never sanitizes. The sanitizer is reachable only by an explicit `Sanitize`, and **Q7 is unanswered**, so the risk cannot be closed — it is the opt-in branch that carries it. The contingency is already the default state, which is the point |
| R13 | Phase 6 scope expands | Med | 6 | 20% over budget at the midpoint → cut to acceptance criteria only | **Open at the end of the phase's build, and the trigger was never pulled 2026-08-16** — the fallback is verified rather than assumed: `IFieldEditorCatalog.FallbackEditor` is reached by any field type with no editor and `PlainZoneEditorTests` pins that it still round-trips a value. Every task in the phase is now done or explicitly deferred, and what came in wider than the tasks named — eighteen field editors rather than ten, four dashboard tiles over one service, a bulk operation set of five — is scope the criteria required rather than scope that expanded. **No budget figure has been taken at any point**, so the trigger has never been evaluated; the risk stays open on that alone, and closing it would be recording a measurement nobody made |
| R14 | JS interop leaks memory in long sessions | Med | 6/9 | Browser memory grows >50% over 2 hours | **Mitigated 2026-08-16, still open** — `JsEditorComponentBase` owns all three of the teardown steps S3 found (`P6-16`): the editor's own `destroy()`, Quill's sibling toolbar, and `DotNetObjectReference.Dispose()`. The JS registry counts created against disposed and reports surviving DOM nodes, which is the instrument. None of that is the trigger: it turns on **browser memory over two hours**. `P6-31a` has now run — ten mount/unmount cycles of each editor in Chromium, created equal to disposed, and no surviving editor node, toolbar included — which is the instrument reading zero on a short run. `P9-16`'s two-hour soak is what the trigger actually names, and it has not |
| R15 | ACL resolution slow on a deep tree | Med | 7 | Tree load exceeds 500 ms at depth 10 | **Mitigated and measured 2026-08-18** — the caller's rules are read once per request and every node after that is a string prefix comparison in memory (`P7-05`), so resolution cost is independent of how many nodes are being decided. `AclPerformanceTests` loads a depth-10 tree with rules at several depths inside the budget. Kept **open** because the trigger names a wall-clock figure on a loaded system, and the only load test in the plan is `P9-16` |
| R16 | Duplicate scheduled publishes under scale-out | Med | 7 | Any duplicate observed | **Closed 2026-08-18** — a job leaves `Pending` only through a single `UPDATE … OUTPUT`, which is atomic against every other writer: the row is claimed and its identity returned in one statement, so there is no read-then-write window to lose. `PublishSchedulerTests` runs two uncoordinated passes over the same rows and asserts exactly one claim and exactly one published version. A claim abandoned by a dying instance is reclaimed after ten minutes rather than stranding the page |
| R17 | Cache invalidation misses a dependent page | **High** | 8 | Any stale page reported after publish | **Mitigated 2026-08-18, still open** — the fan-out is not computed at eviction time: every renderer declares what it used as a cache tag *while* rendering, so evicting `ru:{id}` reaches every page showing that item with no query and nothing to forget (`P8-07`, `P8-10`). A response that published no tags is stored under `content` rather than untagged, so even a render that forgot its dependencies is reachable by a purge-all, and a one-hour backstop TTL bounds anything that still slips (`P8-12`). `CachingTests` asserts the negative half by rewriting a bystander's stored content behind the cache's back. Kept **open** because the trigger is a *stale page reported in production*, and nothing has run in production |
| R18 | Full-text index degrades write throughput | Med | 8 | Save latency exceeds NFR-6 | **Mitigated 2026-08-18, still open** — indexing was moved off the save entirely: a write enqueues an id in its own transaction and the outbox rebuilds the document afterwards, so extracting text from every zone is never on the path an editor waits on (`P8-18`). What that buys in latency it pays for in a window where a just-saved page is not yet findable, which the nightly reconcile repairs — and `SearchTests` asserts the reconcile fixes both a lost document and an orphaned one. Kept **open** because the trigger is a **measured** save latency against NFR-6, and the only load test in the plan is `P9-16` |
| R19 | Requirements shift mid-build (multi-site, multilingual) | **High** | any | Either raised → stop and re-plan | **Assessed for multi-site 2026-08-18, still open** — [ADR 0025](./docs/adr/0025-single-site-in-v1-no-siteid-discriminator.md) records what the reversal costs and what keeps it affordable, so if the requirement is raised the re-plan starts from a written estimate rather than from a survey. Neither requirement has been raised, which is why the risk stays open rather than closing |
| R20 | Key-person dependency on Blazor/EF expertise | Med | 1–3 | Either engineer unavailable >1 week | Open |
