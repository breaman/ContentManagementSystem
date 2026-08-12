# Content Management System — Functional & Technical Specification

**Status:** Draft for review
**Version:** 1.0
**Last updated:** 2026-08-12
**Source requirements:** [`requirements.md`](./requirements.md)
**Implementation plan:** [`plan.md`](./plan.md)

---

## Table of contents

1. [Purpose, goals, and non-goals](#1-purpose-goals-and-non-goals)
2. [Glossary](#2-glossary)
3. [Personas and roles](#3-personas-and-roles)
4. [Gap analysis against `requirements.md`](#4-gap-analysis-against-requirementsmd)
5. [Architecture](#5-architecture)
6. [Content model](#6-content-model)
7. [Field type catalog](#7-field-type-catalog)
8. [Templates and zones](#8-templates-and-zones)
9. [Reusable content](#9-reusable-content)
10. [Pages, URLs, and routing](#10-pages-urls-and-routing)
11. [Versioning, workflow, and publishing](#11-versioning-workflow-and-publishing)
12. [Preview](#12-preview)
13. [Media library and image pipeline](#13-media-library-and-image-pipeline)
14. [Authoring experience](#14-authoring-experience)
15. [Public delivery and rendering](#15-public-delivery-and-rendering)
16. [Caching and invalidation](#16-caching-and-invalidation)
17. [Search](#17-search)
18. [SEO](#18-seo)
19. [Localization](#19-localization)
20. [Security](#20-security)
21. [Permissions matrix](#21-permissions-matrix)
22. [Management API](#22-management-api)
23. [Database schema](#23-database-schema)
24. [Observability and operations](#24-observability-and-operations)
25. [Non-functional requirements](#25-non-functional-requirements)
26. [Testing strategy](#26-testing-strategy)
27. [Environment promotion and content migration](#27-environment-promotion-and-content-migration)
28. [Accessibility](#28-accessibility)
29. [Decisions, open questions, and deferred scope](#29-decisions-open-questions-and-deferred-scope)

---

## 1. Purpose, goals, and non-goals

### 1.1 Purpose

Build a self-hosted, database-backed content management system that lets non-technical editors compose,
review, version, and publish web pages from developer-authored templates — without a deployment for
every content change.

The system is delivered as an extension of the existing .NET 10 / Blazor / Aspire solution in this
repository rather than as a greenfield application. Section 5 documents what already exists and what
is added.

### 1.2 Product goals

| # | Goal | How success is measured |
|---|---|---|
| G1 | An editor can publish a new page from an existing template with no developer involvement | Time from "create page" to "live URL" under 10 minutes for a trained editor |
| G2 | Published content is never disturbed by work in progress | A draft edit is invisible to anonymous traffic until an explicit publish action |
| G3 | Any published state can be restored | Every publish is restorable to the exact byte-for-byte payload that was live |
| G4 | Shared content is authored once | A footer change propagates to every page referencing it in one publish |
| G5 | Public pages are fast and indexable | TTFB < 200 ms cached / < 800 ms uncached at p95; server-rendered HTML with no JS required to read content |
| G6 | Editor mistakes are cheap | Soft delete + recycle bin, version rollback, and link-integrity warnings before destructive publishes |
| G7 | The CMS cannot become an XSS vector | All editor-authored HTML is sanitized on write and on render; CSP enforced on public pages |

### 1.3 Non-goals for v1

These are deliberately excluded. Each is revisited in [§29.3](#293-deferred-scope).

- Multi-site / multi-tenant hosting from one installation.
- Personalization, segmentation, or A/B testing.
- E-commerce, product catalog, or cart.
- A visual drag-and-drop *layout* builder that lets editors invent new page structures. Editors compose
  within developer-defined zones; they do not author CSS or arbitrary layout.
- Marketing automation, email campaigns, or CRM integration.
- A public GraphQL API.
- Real-time collaborative co-editing (Google-Docs style). v1 uses optimistic concurrency plus advisory
  locks ([§11.8](#118-concurrency-control)).

### 1.4 Guiding principles

1. **Developers define structure; editors supply content.** The set of templates, zones, and field
   types is a code-and-configuration concern. This is what keeps the output design-consistent and is
   the central lesson from modern component-driven content modeling — content models should describe
   *meaning*, not presentation.
2. **The published payload is immutable.** Publishing snapshots content. Rendering a published page
   never re-reads mutable draft state.
3. **Non-destructive media editing.** The uploaded original is never overwritten. Crops, rotations, and
   resizes are stored as instructions and materialized as derived renditions.
4. **Everything an editor can break is recoverable.** Soft delete, versioning, and audit logging are
   built in from the first migration, not retrofitted.
5. **Fail closed on trust boundaries.** Unknown field type → render nothing. Unsanitized HTML → reject.
   Unauthorized → 404, not 403, on the public surface.

---

## 2. Glossary

| Term | Meaning |
|---|---|
| **Template** | A developer-defined page structure: a named Razor layout plus an ordered set of zones. Pages are created *from* templates. |
| **Zone** | A named, typed placeholder within a template that an editor fills. Constrained by which field types and how many entries it accepts. |
| **Field type** | The kind of data a zone or block property holds — plain text, rich text, image, link, reference, etc. See [§7](#7-field-type-catalog). |
| **Block type** | A reusable, developer-defined group of fields (a "hero", "quote", "card") that can be placed into a zone that accepts blocks. Analogous to an Umbraco *element type*. |
| **Block** | An instance of a block type living inside a page's zone. Not independently addressable. |
| **Reusable content** | A named, independently versioned and published content instance referenced by many pages — a footer, a promo banner, a carousel. |
| **Page** | An addressable content node with a URL, a template, and a version history. |
| **Page version** | An immutable snapshot of a page's content payload plus metadata, at one point in the workflow. |
| **Draft** | The single mutable working version of a page. |
| **Published version** | The version currently served to anonymous visitors. |
| **Content payload** | The serialized JSON document holding all zone/field values for one version. |
| **Rendition** | A derived image file (resized/cropped/reformatted) generated from a media original. |
| **Media item** | An uploaded asset (image, document) plus its metadata and renditions. |
| **Content tree** | The hierarchical arrangement of pages that also drives default URL construction. |

---

## 3. Personas and roles

### 3.1 Personas

**Dana — Developer.** Authors templates, block types, and field renderers in Razor. Ships them with a
deployment. Owns the design system. Needs template changes to not corrupt existing page content.

**Elena — Content Editor.** Creates and edits pages daily. Non-technical. Wants an editing surface that
looks like the real page. Needs to save work in progress without exposing it.

**Marcus — Reviewer / Approver.** Reads drafts, leaves comments, approves or rejects. Rarely edits.
Needs a shareable preview link for stakeholders who have no CMS account.

**Priya — Site Administrator.** Manages users, roles, redirects, and site settings. Investigates
"who changed this and when." Restores content after mistakes.

**Anonymous visitor.** Reads published pages. Never sees drafts. Never sees an editor UI.

### 3.2 Roles

Roles map to ASP.NET Identity roles (the solution already has `Role : IdentityRole<int>` and a
`CustomUserClaimsPrincipalFactory`).

| Role | Intent |
|---|---|
| `Administrator` | Everything, including user/role management and system settings. |
| `Developer` | Manage templates, block types, field type registration, and reusable-content *types*. Full content access. |
| `Editor` | Full CRUD on pages and media within permitted sections; may publish. |
| `Author` | Create and edit pages; **cannot** publish — must submit for approval. |
| `Approver` | Review, approve, reject, publish, and schedule. Limited editing. |
| `MediaManager` | Full media library management including permanent deletion. |
| `Viewer` | Read-only backoffice access, including preview of drafts. |

Roles are additive. Section-level ACLs ([§21.2](#212-section-level-acls)) further narrow *where* a role
applies within the content tree.

---

## 4. Gap analysis against `requirements.md`

`requirements.md` explicitly asks for research into "elements that are clearly missing that would prevent
this from being a usable system." This section is that answer. Items marked **v1** are specified in this
document and planned for the initial release; **v2** items are specified at a lower fidelity and deferred.

### 4.1 Covered by the original requirements

Templates with typed zones; plain-text/HTML/Markdown zones with an edit/preview experience; reusable
content; pages created from templates with editor-supplied URLs; draft vs. published with drafts
invisible to anonymous users; page versioning; image upload with resize and rotate, referenced from pages.

### 4.2 Gaps identified — and why each one blocks usability

| # | Gap | Why the system is not usable without it | Release |
|---|---|---|---|
| 1 | **URL management beyond "specify a URL"** — uniqueness, hierarchy, slug generation, reserved paths, trailing-slash policy, case policy | Two pages can otherwise claim `/about`, and the router has undefined behavior. Editors will collide within a week. | v1 |
| 2 | **Redirects** — automatic 301 when a URL changes, plus a manual redirect manager | Changing a page's URL silently 404s every inbound link and destroys accumulated search ranking. This is the single most common CMS regression. | v1 |
| 3 | **SEO metadata** — title, meta description, canonical URL, robots directives, Open Graph / Twitter card fields, JSON-LD | A page with no `<title>` control is unusable for any public marketing site. | v1 |
| 4 | **`sitemap.xml` and `robots.txt`** | Search engines cannot discover pages reliably; editors have no way to exclude a page from indexing. | v1 |
| 5 | **Scheduled publish / unpublish (embargo & expiry)** | Editors otherwise have to be awake at 06:00 to launch a campaign, and expired content stays live forever. | v1 |
| 6 | **Approval workflow** — submit → review → approve/reject with comments | The requirements imply "content editors" plural. Without gating, any editor can publish anything. Regulated or brand-sensitive orgs cannot adopt it. | v1 |
| 7 | **Granular permissions** — role + section-level ACLs | "Admin/content editor" as one undifferentiated role does not survive contact with a real team. | v1 |
| 8 | **Shareable preview links for non-CMS users** | Approvers are frequently executives or clients with no account. Without this, review happens over screenshots. | v1 |
| 9 | **Version diff and one-click rollback** | Versioning without comparison or restore is just storage cost. "Show me what changed" is the primary reason versions exist. | v1 |
| 10 | **Soft delete + recycle bin** | Hard-deleting a page destroys history and breaks inbound links irreversibly. | v1 |
| 11 | **HTML sanitization / XSS defense** | A CMS that accepts editor HTML and renders it is a stored-XSS engine by default. A compromised or malicious editor account escalates to full site compromise. This is a **security blocker**, not a nicety. | v1 |
| 12 | **Upload validation and safe media serving** | Unrestricted upload → content-type confusion, SVG-borne script, path traversal, storage exhaustion. | v1 |
| 13 | **Alt text as a first-class, enforced media field** | Without it the CMS actively produces WCAG failures at scale. Legal exposure in many jurisdictions. | v1 |
| 14 | **Focal point / smart cropping** | Responsive layouts crop images differently per breakpoint. Without a focal point, faces get cut off. | v1 |
| 15 | **Image renditions, responsive `srcset`, and a modern format (WebP)** | Serving a 4 MB camera original into a 400 px slot destroys Core Web Vitals and thus rankings. | v1 |
| 16 | **Where-used / link integrity** | Deleting a reusable footer or unpublishing a linked page silently breaks pages. Editors need impact analysis *before* the destructive action. | v1 |
| 17 | **Output caching with publish-triggered invalidation** | Rendering a page means N database round-trips per request. Without caching the system will not hold up; without correct invalidation, publishes appear not to work. | v1 |
| 18 | **Concurrency control** | Two editors on one page silently overwrite each other. Guaranteed data loss in any team larger than one. | v1 |
| 19 | **Backoffice search & content tree navigation** | At 500 pages, a flat list is unusable. | v1 |
| 20 | **Audit trail surfaced in the UI** | The solution already writes `AuditLog` rows; nobody can read them. "Who unpublished the homepage?" must be answerable. | v1 |
| 21 | **Template change / schema evolution safety** | Renaming or removing a zone must not silently destroy content in 300 existing pages. Requires an explicit migration story. | v1 |
| 22 | **Public site search** | Visitors expect it on any site above ~30 pages. | v2 |
| 23 | **Localization / multi-locale content + hreflang** | Would block any non-monolingual deployment. **Confirmed not required** — this system is `en-US` only, so the capability is removed rather than deferred ([§19](#19-localization)). | out of scope |
| 24 | **Navigation/menu management** | Site chrome must reflect published pages. Hardcoding menus defeats the purpose of a CMS. | v1 |
| 25 | **Forms / lead capture** | Common expectation; large surface area (spam, PII, storage, notification). | v2 |
| 26 | **Headless read API + webhooks** | Needed for mobile apps, static-site builds, or downstream caches. | v2 |
| 27 | **Content import/export and environment promotion** | Templates authored in dev must reach production deterministically. | v1 (structure), v2 (content) |
| 28 | **Rate limiting and brute-force protection on the backoffice** | The admin surface is the highest-value target on the site. | v1 |
| 29 | **Editorial metadata** — owner, review-by date, internal notes, tags | Content rot is the default state of every CMS without scheduled review. | v1 |
| 30 | **Broken-link and orphaned-media reporting** | Housekeeping that prevents slow decay. | v2 |

### 4.3 Ambiguities in the source requirements, and the decisions taken

| Ambiguity | Decision | Rationale |
|---|---|---|
| "Reusable content would just be html elements" — raw HTML blobs, or structured? | **Both.** A reusable content item is an instance of a *block type*; one built-in block type is `RawHtml`. | Raw HTML alone forces editors to write markup and defeats sanitization and design consistency. Supporting structured reusable content costs almost nothing extra once block types exist. |
| When reusable content is republished, do pages referencing it change? | **Yes — late binding by default**, with an opt-in *pinned version* reference for compliance-sensitive uses. | "Change the footer once" is the stated purpose (G4). Pinning is the escape hatch when a page must not change under audit. Publishing shared content shows an impact list first ([§9.4](#94-impact-analysis-and-where-used)). |
| "Pages should be versioned" — version on every save, or on publish? | **Version on publish and on explicit snapshot; a single mutable draft between.** | Versioning every keystroke-level save creates unusable history. Autosave writes to the draft; the draft is snapshotted when published or when an editor names a checkpoint. |
| "an edit/preview editor experience" — a WYSIWYG, or a source/preview toggle? | **Source/preview toggle for Markdown and HTML; a constrained WYSIWYG for rich text.** True in-context editing on the rendered page is v2. | A toggle is unambiguous, testable, and avoids a WYSIWYG's tendency to emit unsanitizable markup. |
| Can editors create templates? | **No — `Developer` role only**, and templates require a Razor component that ships with a deployment. | A zone can only render if a component exists to render it. Letting editors define zones with no renderer produces broken pages. |
| Is the site multilingual? | **No.** `en-US` only; locale is absent from the model entirely. | Confirmed by the project owner. See [§19](#19-localization) for the cost of reversing this. |

---

## 5. Architecture

### 5.1 Existing solution baseline

This specification builds on what is already in the repository. Confirmed from source:

| Concern | Current state |
|---|---|
| Runtime | .NET 10 (`net10.0`), C# 14, nullable + implicit usings on, `TreatWarningsAsErrors` |
| Orchestration | .NET Aspire — `AppHost` provisions SQL Server (persistent container; `azure-sql-edge` on Windows ARM64) and runs EF migrations before the server starts |
| Web host | `ContentManagementSystem.Server` — Blazor Web App, `AddInteractiveWebAssemblyComponents`, `AddAuthenticationStateSerialization` |
| Client | `ContentManagementSystem.Client` — Blazor WebAssembly, `AddAuthenticationStateDeserialization` |
| Data | `ContentManagementSystem.Data` — EF Core 10 / SQL Server. `ApplicationDbContext : AuthDbContext : IdentityDbContext<User, Role, int>` |
| Auditing | `AuthDbContext.SaveChanges*` already writes `AuditLog` rows for every add/update/delete, and stamps `FingerPrintEntityBase` (`CreatedOn/By`, `ModifiedOn/By`) via `IUserService` |
| Identity | Identity Core with roles, 2FA, passkeys, external logins, email confirmation. `IdentitySchemaVersions.Version3` |
| Logging | Serilog → console / MSSqlServer / OpenTelemetry; OTel traces + metrics via `ServiceDefaults` |
| Styling | Bootstrap 5.3 compiled from SCSS via the `sass` npm script |
| Routing | `RouteOptions` already forces lowercase URLs and no trailing slash |

Two conventions from the existing codebase are adopted wholesale by the CMS model:

- **`FingerPrintEntityBase`** for anything an editor mutates, so created/modified attribution is automatic.
- **`ColumnTypes.Timestamp`** (`datetimeoffset(7)`) for every instant, applied model-wide by
  `ConfigureConventions`.

### 5.2 Projects added

```
src/
  ContentManagementSystem.Data/          (existing) + CMS entities, configurations, migrations
  ContentManagementSystem.Shared/        (existing) + DTOs, field-type contracts, validation
  ContentManagementSystem.Client/        (existing) + backoffice WASM UI
  ContentManagementSystem.Server/        (existing) + delivery pipeline, API, media endpoints
  ContentManagementSystem.Core/          NEW — domain services, no EF/ASP.NET dependency where avoidable
  ContentManagementSystem.Rendering/     NEW — Razor Class Library: templates, block components, field renderers
tests/
  ContentManagementSystem.Core.Tests/            NEW — unit
  ContentManagementSystem.Data.Tests/            NEW — EF integration (Testcontainers SQL Server)
  ContentManagementSystem.Server.Tests/          NEW — API + delivery integration (WebApplicationFactory)
  ContentManagementSystem.E2E.Tests/             NEW — Playwright
```

`ContentManagementSystem.Rendering` is a Razor Class Library so that public delivery components and the
backoffice preview render **the exact same components**. Preview fidelity is a correctness property, not
a best effort.

### 5.3 The two front doors

The single most important architectural decision: **the public site and the backoffice have different
rendering models.**

| | Public delivery | Backoffice |
|---|---|---|
| Route space | `/{**slug}` catch-all, plus `/media/*`, `/sitemap.xml`, `/robots.txt` | `/admin/**` |
| Render mode | **Static SSR** (`@rendermode` unset) | **Interactive WebAssembly** |
| Why | Full HTML in the first response for crawlers and Core Web Vitals; no SignalR circuit; cacheable by `OutputCache` and any CDN | Rich, stateful editing UI; offloads work from the server; already configured in this solution |
| Auth | Anonymous | Cookie auth, `Administrator`/`Developer`/`Editor`/… policies |
| Data access | Direct, in-process, read-only, cached | HTTP via the Management API ([§22](#22-management-api)) |

Static SSR for the public site is what makes output caching viable — an interactive circuit cannot be
cached. Interactive components (e.g. a search box) can still be opted in per-component within an
otherwise static page.

### 5.4 Request flow — public page

```
GET /products/widgets
        │
        ▼
[OutputCache middleware] ──hit──► 200 (cached HTML, ETag revalidation)
        │ miss
        ▼
[UrlResolver]  normalize (lowercase, strip trailing slash)
        │
        ├─► Redirect table hit? ──► 301/302 to target
        │
        ├─► No page and no redirect ──► 404 page
        │
        ▼
[PublishedContentService]
   PageRoute(slug) → PageId → Page.PublishedVersionId
        │
        ▼
[ContentPayloadReader]  deserialize ContentJson against the version's captured schema
        │
        ▼
[ReusableContentResolver] resolve referenced shared content to *their* published versions
        │
        ▼
[TemplateRenderer]  DynamicComponent(template) → per-zone DynamicComponent(field renderer)
        │
        ▼
[HtmlSanitizer] applied to any raw-HTML field on render (defense in depth)
        │
        ▼
200 text/html  +  cache tags: page:{id}, tpl:{id}, ru:{id}…, media:{id}…
```

### 5.5 Request flow — publish

```
POST /api/cms/pages/{id}/publish
        │
        ▼
[Authorize: Content.Publish + section ACL]
        │
        ▼
[Validation] required zones filled, field validators pass, URL unique,
             referenced media/pages/reusable content exist and are publishable
        │
        ▼
[Impact analysis] pages affected, links that will break  ──► returned for confirmation if warnings
        │
        ▼ (transaction)
   snapshot Draft → new PageVersion (Status=Published, VersionNumber=n+1)
   previous published version → Status=Archived
   Page.PublishedVersionId = new version
   upsert PageRoute rows; create 301 Redirect if the URL changed
   write ContentReference projection rows
   enqueue CacheInvalidationEvent (outbox)
        │
        ▼
[Cache invalidation] EvictByTagAsync for the page, its ancestors' navigation, and dependents
```

### 5.6 Component and dependency diagram

```
┌─────────────────────────────────────────────────────────────────┐
│ ContentManagementSystem.Server                                  │
│  ┌───────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │ Delivery      │  │ Management   │  │ Media endpoints      │  │
│  │ (Static SSR)  │  │ API (minimal)│  │ /media/{id}/{spec}   │  │
│  └───────┬───────┘  └──────┬───────┘  └──────────┬───────────┘  │
└──────────┼─────────────────┼─────────────────────┼──────────────┘
           │                 │                     │
           ▼                 ▼                     ▼
┌─────────────────────────────────────────────────────────────────┐
│ ContentManagementSystem.Core                                    │
│  PublishedContentService · DraftService · PublishingService     │
│  UrlService · RedirectService · MediaService · ImageProcessor   │
│  ContentSchemaValidator · SanitizationService · CacheInvalidator│
│  ReferenceIndexer · SearchIndexer · SchedulerJobs               │
└──────────┬──────────────────────────────┬───────────────────────┘
           │                              │
           ▼                              ▼
┌────────────────────────┐   ┌────────────────────────────────────┐
│ …Data (EF Core)        │   │ …Rendering (RCL)                   │
│ ApplicationDbContext   │   │ Templates/ · Blocks/ · Fields/      │
└────────────────────────┘   └────────────────────────────────────┘
           │
           ▼
   SQL Server  ·  Blob/File media store  ·  Redis (output cache, optional)
```

### 5.7 Aspire resource topology

`AppHost.cs` gains:

```
sqlserver ──► contentmanagementsystemdb  (existing, persistent container)
azurite / blob emulator                  (NEW — media store in dev)
redis                                    (NEW — output cache; optional, feature-flagged)
server ──references──► db, blob, redis
ef-migrations ──runs before──► server    (existing)
```

Redis is optional and gated behind configuration. With a single server instance the in-memory
`IOutputCacheStore` is sufficient; Redis becomes required the moment the app scales out, because
tag-based eviction must be visible to every node ([§16.3](#163-scale-out-considerations)).

---

## 6. Content model

### 6.1 Conceptual model

```
                        ┌──────────────┐
                        │   Template   │  developer-authored
                        └──────┬───────┘
                               │ 1..*
                        ┌──────▼───────┐
                        │     Zone     │  name, key, allowed field types, cardinality
                        └──────┬───────┘
                               │ constrains
     ┌─────────────┐    ┌──────▼───────┐    ┌──────────────────┐
     │    Page     │───►│ PageVersion  │───►│ Content payload  │ (JSON)
     │ url, tree   │ 1..*│ immutable    │    │  zoneKey → value │
     └─────────────┘    └──────────────┘    └────────┬─────────┘
                                                     │ references
              ┌──────────────────────┬───────────────┼──────────────┐
              ▼                      ▼               ▼              ▼
     ┌─────────────────┐   ┌──────────────┐  ┌────────────┐  ┌───────────┐
     │ ReusableContent │   │  MediaItem   │  │ Page link  │  │  Block    │
     │ (own versions)  │   │ + renditions │  │            │  │ instances │
     └────────┬────────┘   └──────────────┘  └────────────┘  └─────┬─────┘
              │                                                     │
              └────────────► BlockType ◄────────────────────────────┘
                             (field definitions)
```

### 6.2 The central storage decision: JSON payload + relational projection

**Decision:** a page version's content is stored as a **single JSON document**, with a **relational
projection table** for references.

Three options were considered:

| Option | Verdict |
|---|---|
| **A. Fully relational (EAV)** — `PageVersionFieldValue(versionId, zoneKey, index, type, textValue, intValue, mediaId…)` | Rejected. Rendering one page becomes a wide join over dozens of sparse rows; nested blocks need recursive CTEs; adding a field type means a schema change. This is the classic EAV trap. |
| **B. Fully JSON** — one `nvarchar(max)` column, no projection | Rejected. No referential integrity, and "which pages use this image?" becomes a full table scan with `JSON_VALUE`. Where-used and link integrity (gaps #16, #30) are unimplementable. |
| **C. Hybrid — JSON payload + derived reference rows** | **Selected.** |

Rationale for the hybrid:

- **Reads are whole-document.** Rendering always needs the entire payload for a version. One row, one
  `nvarchar(max)` read, one deserialization. No joins.
- **The schema is user-defined at runtime.** Zones and block-type properties are data, created by a
  `Developer` in the backoffice. They cannot be mapped to CLR types, so EF Core's `ToJson()` /
  owned-entity JSON mapping does not apply — that requires a compile-time POCO. The payload is
  therefore stored as a `string` and deserialized against the *runtime* schema by
  `ContentSchemaValidator`, which walks the `Zone`/`BlockTypeProperty` definitions. This is the same
  approach Umbraco's Block List takes (a `layout` array plus a `contentData` array keyed by GUID).
- **Immutability makes JSON safe.** Published versions are never partially updated, so the usual
  objection to document-in-a-column (concurrent partial writes) does not arise.
- **References get real rows.** On every save and publish, `ReferenceIndexer` walks the payload and
  rewrites `ContentReference` rows. This gives indexed, joinable answers to where-used, link
  integrity, cache-tag computation, and orphan detection — the things pure JSON cannot do.

**Payload format.** Versioned by an envelope so the deserializer can evolve:

```jsonc
{
  "schemaVersion": 1,
  "templateKey": "marketing-landing",
  "templateRevision": 7,          // schema captured at write time — see §8.5
  "zones": {
    "hero": {
      "type": "blocks",
      "items": [
        {
          "id": "0f6c…",           // stable GUID; survives reordering
          "blockTypeKey": "hero-banner",
          "blockTypeRevision": 3,
          "properties": {
            "headline":  { "type": "plainText", "value": "Ship faster" },
            "body":      { "type": "richText",  "format": "markdown",
                           "value": "We **help** teams…" },
            "image":     { "type": "media", "mediaId": 812,
                           "altOverride": null, "focalPoint": { "x": 0.5, "y": 0.33 },
                           "crop": { "x": 0, "y": 0.1, "w": 1, "h": 0.8 } },
            "cta":       { "type": "link", "kind": "page", "pageId": 44,
                           "text": "Get started", "target": "_self", "rel": null }
          }
        }
      ]
    },
    "body":   { "type": "richText", "format": "html", "value": "<p>…</p>" },
    "footer": { "type": "reusable", "reusableContentId": 3, "pinnedVersionId": null }
  }
}
```

Invariants:

- Every zone key present in the payload must exist in the template revision named by `templateRevision`.
- Every block `id` is a GUID generated at creation and preserved across edits, so version diffs can
  match blocks across reorders instead of reporting "everything changed."
- `null` and absent are distinct: absent means "never authored," `null` means "explicitly cleared."

### 6.3 Content type hierarchy

Three things share one underlying idea — a named bag of typed properties:

| Construct | Addressable? | Independently published? | Used where |
|---|---|---|---|
| **Template** | yes, via Page | via its Page | Page structure |
| **Block type** | no | no — published with its host | Inside zones that accept blocks |
| **Reusable content type** | yes, by key | **yes, on its own lifecycle** | Referenced from zones that accept `reusable` |

A **composition** mechanism lets a block type inherit property groups from a shared definition (e.g.
every block type composes `SeoFragment` or `SpacingOptions`), mirroring Umbraco compositions. This
avoids re-declaring the same six properties on twelve block types.

### 6.4 Entity overview

Full DDL in [§23](#23-database-schema). Conceptually:

**Structure (developer-owned, promoted between environments):**
`Template`, `TemplateRevision`, `Zone`, `BlockType`, `BlockTypeRevision`, `BlockTypeProperty`,
`Composition`, `FieldTypeRegistration`, `SiteSettings`

**Content (editor-owned, environment-specific):**
`Page`, `PageVersion`, `PageRoute`, `Redirect`, `ReusableContent`, `ReusableContentVersion`,
`ContentReference`, `WorkflowTask`, `Comment`, `Tag`, `PageTag`, `NavigationMenu`, `NavigationItem`

**Media:**
`MediaItem`, `MediaFolder`, `MediaRendition`, `MediaUsage` (a view over `ContentReference`)

**Operational:**
`AuditLog` (exists), `ScheduledJob`, `PreviewToken`, `EditLock`, `SearchDocument`, `OutboxMessage`

---

## 7. Field type catalog

A field type is the unit of extensibility. Each one is a triple:

1. a **storage contract** (the JSON shape it writes into the payload),
2. an **editor component** (Blazor, WASM, backoffice),
3. a **renderer component** (Blazor, static SSR, public).

Registered at startup into `IFieldTypeRegistry`, keyed by a stable string. Unknown keys render nothing
and log a warning — never an exception on the public surface.

```csharp
public interface IFieldType
{
    string Key { get; }                       // "richText"
    string DisplayName { get; }
    Type EditorComponent { get; }             // rendered in backoffice
    Type RendererComponent { get; }           // rendered on public site
    FieldTypeCapabilities Capabilities { get; }
    ValueTask<ValidationResult> ValidateAsync(JsonElement value, FieldConfiguration config, CancellationToken ct);
    ValueTask<JsonElement> SanitizeAsync(JsonElement value, FieldConfiguration config, CancellationToken ct);
    IEnumerable<ContentReference> ExtractReferences(JsonElement value);   // powers §16 and §9.4
    string ExtractSearchText(JsonElement value);                          // powers §17
}
```

`ExtractReferences` and `ExtractSearchText` on the interface are what make where-used, cache
invalidation, and search work uniformly for field types that do not exist yet.

### 7.1 v1 field types

| Key | Stores | Editor | Notes |
|---|---|---|---|
| `plainText` | `string` | single-line input | `maxLength`, `pattern`, `required` config. **No HTML permitted** — encoded on render. |
| `multilineText` | `string` | textarea | Same, newlines preserved. |
| `richText` | `{ format: "markdown"\|"html", value: string }` | **Edit/Preview toggle** ([§14.4](#144-the-editpreview-experience)) | Markdown via Markdig → HTML → sanitize. HTML sanitized directly. Configurable allowlist profile. |
| `html` | `{ value: string }` | code editor + preview | `Developer`-restricted by default. Strictest sanitization tier still applies. |
| `number` | `decimal` | numeric input | min/max/step. |
| `boolean` | `bool` | switch | |
| `date` / `dateTime` | ISO-8601 | picker | Stored UTC, displayed in site timezone. |
| `choice` | `string` or `string[]` | select / checkboxes | Options from static config or a lookup provider. |
| `media` | `{ mediaId, altOverride?, focalPoint?, crop? }` | media picker + inline crop | Restrict by `allowedTypes`, `minWidth`, `aspectRatio`. |
| `mediaList` | array of the above | reorderable gallery | min/max count. |
| `link` | `{ kind: "page"\|"external"\|"media"\|"anchor"\|"email", … }` | link picker | Internal links stored as `pageId`, **never as a URL string** — this is what makes URL changes safe. |
| `pageReference` | `{ pageId }` or array | content-tree picker | Restrict by template. |
| `reusable` | `{ reusableContentId, pinnedVersionId? }` | shared-content picker | See [§9](#9-reusable-content). |
| `blocks` | ordered array of block instances | block list editor | `allowedBlockTypes`, min/max. Supports one level of nesting in v1. |
| `tags` | `string[]` | tag input with autocomplete | |
| `color` | `#RRGGBB` | swatch picker | Constrained to a design-system palette by config. |
| `json` | arbitrary JSON | code editor | `Developer` only. Escape hatch. |

### 7.2 Field configuration

Every zone or block property carries a `ConfigurationJson` blob interpreted by its field type. Example
for a `blocks` zone:

```jsonc
{
  "allowedBlockTypes": ["hero-banner", "text-columns", "quote"],
  "min": 1, "max": 8,
  "allowNesting": false
}
```

Configuration is validated against a per-field-type JSON Schema when the zone is saved, so a
`Developer` cannot persist a configuration the editor component cannot honor.

### 7.3 Extensibility — adding a new field type

The field type is the intended extension point. Adding one requires no changes to the payload engine,
the publishing pipeline, the cache layer, or the search indexer, because all four dispatch through
`IFieldType`. The complete procedure:

1. Implement `IFieldType` in `Core/Fields/Types/`, supplying `ValidateAsync`, `SanitizeAsync`,
   `ExtractReferences`, and `ExtractSearchText`.
2. Write the editor component (`Client/Components/Admin/Fields/`) and the renderer component
   (`Rendering/Fields/`).
3. Define the configuration JSON Schema so zone configuration is validated on save.
4. Register in DI: `services.AddCmsFieldType<MyFieldType>()`.

What comes for free once registered: editor UI placement, publish validation, where-used and link
integrity, cache-tag derivation, search indexing, and version diffing.

The one rule that matters: **`ExtractReferences` must report every entity the value points at.**
A field type that omits a reference produces a page that silently fails to invalidate when its
dependency changes — the failure mode is stale content, and it is hard to diagnose after the fact.
A contract test enforces that every registered field type returns references for a representative
populated value.

Adding a new *template* or *block type* is lighter still: a Razor component with `[CmsTemplate]` or
`[CmsBlockType]`, picked up by `TemplateReconciler` at startup ([§8.4](#84-template-registration-and-reconciliation)).

---

## 8. Templates and zones

### 8.1 What a template is

A template binds three things:

1. A **key** (`marketing-landing`) — stable, referenced from payloads.
2. A **Razor component** in `ContentManagementSystem.Rendering` that produces the page's markup and
   declares where zones appear.
3. A **zone definition set** stored in the database, which drives the editor UI and validation.

The Razor component and the database definition must agree. They are kept in sync by a **startup
reconciliation** step ([§8.4](#84-template-registration-and-reconciliation)).

### 8.2 Declaring a template in code

```razor
@* Rendering/Templates/MarketingLanding.razor *@
@attribute [CmsTemplate("marketing-landing", "Marketing Landing Page",
    Description = "Hero, flexible body, and a shared footer.")]
@inherits CmsTemplateBase

<article class="landing">
    <CmsZone Name="hero" />
    <div class="container">
        <CmsZone Name="body" />
    </div>
    <CmsZone Name="footer" />
</article>
```

Zone *definitions* — the allowed field types, cardinality, help text — live in the database and are
editable by a `Developer` in the backoffice, because they are content-modeling decisions that change
more often than markup. `<CmsZone Name="…" />` only declares placement.

### 8.3 Zone properties

| Property | Purpose |
|---|---|
| `Key` | Stable identifier used in the payload. Immutable after creation. |
| `Name`, `Description` | Editor-facing labels. |
| `FieldTypeKey` | Which field type fills this zone. |
| `ConfigurationJson` | Field-type-specific config ([§7.2](#72-field-configuration)). |
| `IsRequired` | Blocks publish if empty. Does not block draft save. |
| `SortOrder` | Order in the editor UI. |
| `Group` | Optional tab/accordion grouping in the editor. |
| `IsInlineEditable` | Whether this zone participates in in-context editing ([§14.5](#145-in-context-editing-v2)). |

### 8.4 Template registration and reconciliation

At startup, `TemplateReconciler`:

1. Scans loaded assemblies for `[CmsTemplate]` and `[CmsBlockType]` attributes.
2. Inserts any template/block type that exists in code but not in the database (new deployment).
3. Marks any database record with no corresponding code component as `IsOrphaned = true`. Orphaned
   templates cannot be assigned to new pages, and pages already using them render a clearly logged
   fallback rather than throwing.
4. **Never deletes** database records or zone definitions automatically.
5. In `Development`, logs a diff. In `Production`, fails health check `cms-templates` on orphan
   detection so a bad deployment is visible without taking the site down.

### 8.5 Template evolution and schema safety

This is gap #21 and deserves an explicit contract.

Templates are **revisioned**. Every structural change to a template's zone set creates a new
`TemplateRevision`. A `PageVersion` records the `templateRevision` it was authored against.

| Change | Effect on existing content | Handling |
|---|---|---|
| **Add** a zone | None. Zone reads as absent → renders empty. | Allowed freely. If `IsRequired`, existing pages fail validation only on their *next* publish, with a clear message. |
| **Remove** a zone | Content orphaned in the payload. | Allowed, but the payload retains the data. UI shows an "Obsolete content" panel with the orphaned value and a copy/discard action. Data is only lost when an editor explicitly discards. |
| **Rename** a zone key | Would orphan all content. | **Forbidden.** Keys are immutable. Renaming the *display name* is always free. |
| **Change** a zone's field type | Payload shape mismatch. | Requires an explicit migration: the `Developer` picks a converter (or "clear values"), and a background job rewrites drafts. Published versions are **never rewritten** — they render against their captured revision. |
| **Delete** a template | Orphans pages. | Blocked while any non-deleted page references it. UI reports the count and links to the list. |

The rule that makes this safe: **published versions render against the revision they captured.**
A template change cannot retroactively alter what is live. Editors adopt the new revision when they
next open and publish the page.

---

## 9. Reusable content

### 9.1 Model

A `ReusableContent` item is a named, independently versioned content instance:

- `Key` (slug, stable), `Name`, `Description`
- `BlockTypeId` — the shape of its properties (including the built-in `RawHtml` block type)
- `FolderId` — organizational grouping
- Its own `DraftVersionId` / `PublishedVersionId` and full version history — the same lifecycle as a page

It has no URL and is never directly addressable on the public site.

### 9.2 Referencing

A zone configured for `reusable` stores `{ reusableContentId, pinnedVersionId? }`.

- `pinnedVersionId == null` → **late binding.** Renders whatever version is currently published.
  This is the default and delivers goal G4.
- `pinnedVersionId` set → **pinned.** Renders that exact version forever. Used when a page must be
  reproducible for audit. The editor UI shows a badge and an "update to latest" action.

### 9.3 Publishing behavior

Publishing reusable content changes every late-bound page that references it, **without republishing
those pages.** Consequences handled explicitly:

- Cache invalidation must evict all dependent pages — driven by `ContentReference`
  ([§16.2](#162-cache-tags)).
- The audit log records the reusable-content publish, and the impact list is stored with it, so
  "why did 40 pages change at 14:02?" is answerable.

### 9.4 Impact analysis and where-used

Before publishing, unpublishing, or deleting reusable content, the API returns:

```jsonc
{
  "affectedPages": [ { "id": 44, "title": "Home", "url": "/", "isPublished": true, "isPinned": false } ],
  "affectedPageCount": 40,
  "pinnedPageCount": 2,
  "warnings": [
    { "code": "large-blast-radius", "message": "40 published pages will change immediately." }
  ]
}
```

The UI requires an explicit confirmation when `affectedPageCount > 0`. **Deleting** reusable content
that is still referenced is blocked outright; the editor must first replace or remove the references.

The same `where-used` endpoint exists for media items and pages.

---

## 10. Pages, URLs, and routing

### 10.1 Page identity and the content tree

Pages form a tree. `Page.ParentId` gives hierarchy; the root node is a synthetic "site root."

The tree serves three purposes: default URL construction, permission inheritance
([§21.2](#212-section-level-acls)), and navigation generation ([§10.7](#107-navigation)).

`Page` carries a `Path` materialized column (e.g. `/1/8/44/`) so descendant queries — used constantly
by ACLs and the tree UI — are a single indexed `LIKE '/1/8/%'` rather than a recursive CTE.

### 10.2 Slugs and URL construction

- Each page has a `Slug` (a single path segment) and a computed **full URL** = ancestors' slugs joined.
- A page may opt out of hierarchy with `UseExplicitUrl = true` plus an `ExplicitUrl`, satisfying the
  requirement's "content editors specify a URL" while keeping the tree as the sane default.
- Slugs are auto-generated from the title on creation (lowercase, Unicode-normalized to ASCII where
  unambiguous, non-alphanumerics → `-`, collapsed, trimmed, truncated to 100 chars) and then freely
  editable.

### 10.3 URL rules

| Rule | Policy |
|---|---|
| Case | Lowercase only. `RouteOptions.LowercaseUrls` is already enabled; the resolver also lowercases on lookup. |
| Trailing slash | Not appended (already configured). A request with one 301s to the canonical form. |
| Uniqueness | `PageRoute.Url` is unique across published routes. Enforced by a unique index, not just application logic. |
| Reserved prefixes | `/admin`, `/api`, `/media`, `/_blazor`, `/_framework`, `/account`, `/health`, `/alive`, `/sitemap.xml`, `/robots.txt`, `/preview`. Validation rejects a slug that would collide. |
| Max length | 2000 chars total; 100 per segment. |
| Unicode | IDN/Unicode slugs permitted but stored percent-decoded and NFC-normalized; a homograph warning is shown. |
| Home page | Exactly one page has `Url = "/"`. |

### 10.4 `PageRoute` and route resolution

Routes are **materialized**, not computed per request:

```
PageRoute(Id, PageId, Url, IsPrimary, IsPublished)
   UNIQUE INDEX (Url) WHERE IsPublished = 1
```

Resolution is one indexed lookup. When a page moves or its slug changes, `UrlService` recomputes routes
for that page **and all descendants** in a single transaction, and emits redirects for each old URL.

Draft-only pages have `IsPublished = 0` routes so preview can resolve them without leaking into the
public unique index.

### 10.5 Redirects

Gap #2. A first-class `Redirect` table:

| Column | Notes |
|---|---|
| `FromUrl` | Unique. Normalized. |
| `ToUrl` **or** `ToPageId` | Target by page reference where possible, so the redirect follows future URL changes. |
| `StatusCode` | 301 or 302. Default 301. |
| `IsAutomatic` | Created by the system on a URL change vs. hand-entered. |
| `IsEnabled`, `Notes`, `HitCount`, `LastHitOn` | Housekeeping; `HitCount` identifies dead redirects to prune. |

Behavior:

- **Automatic creation** whenever a published page's URL changes, for the page and every descendant.
- **Loop detection** at write time (walk the chain, max depth 10) and again at resolve time.
- **Chain flattening**: if `A → B` exists and `B → C` is created, `A` is rewritten to `→ C`.
- Manual redirects override automatic ones on conflict.
- A live page always wins over a redirect with the same `FromUrl` — otherwise reusing an old URL for
  new content becomes impossible.
- CSV import/export for bulk migration from a legacy site.

### 10.6 404 handling

A configurable 404 page, itself a CMS page. Unresolved requests are logged to `NotFoundLog`
(URL, referrer, count, last seen) so an administrator can see which missing URLs actually receive
traffic and create redirects for them — the single highest-value report in a site migration.

### 10.7 Navigation

Gap #24. Two mechanisms, because sites need both:

- **Structural navigation** — generated from the content tree, filtered by
  `Page.ShowInNavigation` and publish state. Zero maintenance.
- **Managed menus** — a `NavigationMenu` with ordered `NavigationItem`s, each an internal page
  reference or an external link. Used for footers and utility navigation that do not mirror the tree.

Both are cached with the tag `nav:{menuKey}` and invalidated on any publish/unpublish/move.

---

## 11. Versioning, workflow, and publishing

### 11.1 Version model

Each `Page` has:

- `DraftVersionId` — the single mutable working version. Always present.
- `PublishedVersionId` — the immutable version served publicly. `null` until first publish.

`PageVersion` states:

| Status | Meaning | Mutable? |
|---|---|---|
| `Draft` | Working copy | **Yes** |
| `InReview` | Submitted for approval | No (locked while under review) |
| `Approved` | Approved, awaiting publish or scheduled window | No |
| `Published` | Currently live | No |
| `Archived` | Previously published, superseded | No |
| `Rejected` | Sent back with comments; content copied into a fresh draft | No |

This satisfies the core requirement directly: **the `Published` version is untouched while editors work
on the `Draft`.** Anonymous rendering reads `Page.PublishedVersionId` and never consults the draft.

### 11.2 Version lifecycle

```
  create page
      │
      ▼
  ┌────────┐  autosave / save  ┌────────┐
  │ Draft  │◄──────────────────│ Draft  │   (mutated in place; no new version rows)
  └───┬────┘                   └────────┘
      │ submit
      ▼
  ┌──────────┐  reject (+comments)   ┌──────────┐
  │ InReview │──────────────────────►│ Rejected │──► content copied to new Draft
  └───┬──────┘                       └──────────┘
      │ approve
      ▼
  ┌──────────┐  publish (or scheduled window opens)
  │ Approved │──────────────────────────────┐
  └──────────┘                              ▼
                                      ┌───────────┐
       previous Published ──────────► │ Published │
              │                       └───────────┘
              ▼
        ┌──────────┐
        │ Archived │  (retained per retention policy, §11.7)
        └──────────┘
```

Publishing **snapshots** the draft into a new immutable version rather than promoting the draft row
itself. The draft survives the publish and continues to be editable — which is exactly the requirement
that editors keep working while the published page stays live.

### 11.3 Save semantics

- **Autosave** every 20 seconds of inactivity and on navigation away, writing to the draft. No version
  row created. Shows "Saved 14:32."
- **Explicit save** — same, plus a toast.
- **Named checkpoint** — creates an `Archived`-status version row with a `Label`, so an editor can
  bookmark "before the big rewrite" without publishing.
- **Discard draft** — resets the draft to a copy of the currently published version.

### 11.4 Version comparison (diff)

Gap #9. `GET /api/cms/pages/{id}/versions/{a}/diff/{b}` returns a structural diff computed over the
payload, not over raw JSON text:

- Zone added / removed / changed.
- Within `blocks` zones, blocks matched by their stable GUID → reports *moved*, *added*, *removed*,
  *changed*, instead of "the whole array differs."
- Text fields diffed word-by-word for inline highlighting.
- Media/link/reference fields diffed by target identity, with a human label ("Image: hero-old.jpg →
  hero-new.jpg").
- Metadata (URL, SEO fields, template revision) diffed as a flat property list.

The UI presents side-by-side and unified views, plus a "restore this version" action.

### 11.5 Rollback

Restoring version *N* **copies** its payload into the current draft (it does not resurrect the row as
published). The editor then reviews and publishes normally. This keeps the timeline strictly forward-
moving and preserves the audit chain — the history never gains a cycle.

### 11.6 Scheduled publishing

Gap #5. `PageVersion` carries `PublishOn` and `UnpublishOn` (both `datetimeoffset`, nullable).

- A hosted background service (`PublishSchedulerService`) polls `ScheduledJob` every 30 seconds.
- Jobs are claimed with an atomic `UPDATE … OUTPUT` so multiple instances cannot double-publish.
- Publishing a scheduled version runs the identical validation and cache-invalidation path as a manual
  publish; a validation failure marks the job `Failed`, notifies the owner, and does **not** retry
  blindly.
- `UnpublishOn` sets `PublishedVersionId = null`, retires the public routes, and — importantly —
  auto-creates a redirect to the parent page rather than leaving a 404, if configured.
- Timezone: stored UTC; the UI presents and accepts the configured site timezone with the offset shown
  explicitly, because "publish at 9am" during a DST transition is a real support ticket.

### 11.7 Retention

Unbounded version history will grow without limit. Policy, configurable in `SiteSettings`:

- Keep **all** versions for 90 days.
- Beyond that, keep the last 20 versions per page, plus every version that was ever `Published`, plus
  all named checkpoints.
- A nightly job prunes and logs what it removed.
- Versions are never pruned for a page in the recycle bin.

### 11.8 Concurrency control

Gap #18. Two layers:

1. **Optimistic concurrency (authoritative).** `rowversion` on `Page`, `PageVersion`,
   `ReusableContentVersion`. EF Core `IsRowVersion()`. A conflicting save returns `409 Conflict` with
   both payloads so the UI can offer "keep mine / take theirs / open diff."
2. **Advisory edit locks (cooperative UX).** `EditLock(PageId, UserId, AcquiredOn, HeartbeatOn)`.
   Acquired on opening the editor, refreshed every 30 s, expires after 2 minutes of silence. Another
   editor opening the page sees "Elena is editing this (last active 12 s ago)" and must click
   "Edit anyway." A lock never *prevents* editing — locks that block are locks that get stuck.

### 11.9 Workflow

Gap #6. Configurable per site in v1 (not per template — that is v2):

| Mode | Behavior |
|---|---|
| `None` | Anyone with `Content.Publish` publishes directly. |
| `Simple` | Users without `Content.Publish` must submit; any `Approver` may approve and publish. |
| `TwoStep` | Submit → approve → publish are three distinct actions, and the approver may not be the author. |

`WorkflowTask` records assignee, state, due date, and decision. `Comment` supports threaded review
comments anchored optionally to a specific zone key, so feedback is "the hero headline is wrong," not
a paragraph of prose. Notifications are emailed via the existing `IEmailSender<User>` abstraction
(currently a no-op sender — replaced in Phase 1).

---

## 12. Preview

Gap #8. Preview is how the requirement "changes only they can see internally" is actually verified.

### 12.1 Authenticated preview

`GET /preview/{pageId}?version={versionId}` renders **any** version through the identical delivery
pipeline as the public site, with three differences: output caching disabled, `X-Robots-Tag: noindex`
set, and a floating preview toolbar injected (version label, status, exit).

Because delivery and preview share the same components from `ContentManagementSystem.Rendering`,
preview fidelity is structural rather than aspirational.

### 12.2 Shareable preview links

For stakeholders with no account:

- `PreviewToken(Token, PageId, VersionId, ExpiresOn, CreatedBy, MaxUses, UseCount, RevokedOn)`.
- Token is 32 bytes of CSPRNG, base64url-encoded; only a SHA-256 **hash** is stored.
- URL: `https://site/preview/s/{token}`.
- Default expiry 7 days, max 30. Revocable individually or in bulk.
- Serves exactly one page version. It is **not** a session — following a link to another page leaves
  the preview and hits the public site.
- Always `noindex, nofollow`; excluded from `sitemap.xml`; rate-limited.

### 12.3 Preview affordances

- **Device widths** — desktop/tablet/mobile via an iframe with constrained width.
- **Draft-link resolution** — an internal link to an unpublished page resolves to *that page's* draft
  inside preview, so a reviewer can walk an entire unreleased section. Clearly badged.
- **Compare mode** — published and draft side by side.

---

## 13. Media library and image pipeline

### 13.1 Model

```
MediaFolder (tree)
   └── MediaItem
         ├── original file (immutable, content-addressed)
         ├── metadata: alt text, title, caption, credit, tags, focal point
         ├── technical: width, height, bytes, mime, sha256, EXIF (read for orientation, then stripped)
         └── MediaRendition[]  (derived; regenerable; cache-like)
```

`MediaItem.StorageKey` is derived from the SHA-256 of the file bytes, which gives free deduplication:
re-uploading an identical file returns the existing item with a note rather than a second copy.

### 13.2 Storage abstraction

```csharp
public interface IMediaStore
{
    Task<MediaStoreResult> PutAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream?> GetAsync(string key, CancellationToken ct);
    Task<bool> ExistsAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
    Uri? GetPublicUrl(string key, TimeSpan? validFor = null);   // null when not directly addressable
}
```

Implementations: `FileSystemMediaStore` (dev — path-traversal-guarded, stores **outside** `wwwroot`),
`AzureBlobMediaStore` (prod). Files are never served from `wwwroot`; all delivery goes through the media
endpoint so authorization, content-type pinning, and rendition generation apply.

### 13.3 Upload pipeline

```
1. Auth + size limit (default 25 MB images, 50 MB documents; configurable, enforced by
   RequestSizeLimit and by FormOptions.MultipartBodyLengthLimit)
2. Extension allowlist          → .jpg .jpeg .png .gif .webp .svg .pdf .docx .xlsx .mp4
                                  (AVIF uploads rejected in v1 — decode support is inconsistent in the
                                   selected library and it cannot re-encode, so renditions would fail)
3. Magic-number sniff           → declared MIME must match actual bytes (a .jpg that is really HTML is rejected)
4. Decode-bomb guard            → reject if width*height > 100 MP or declared dimensions are implausible
5. SVG handling                 → sanitized with a dedicated strict profile (no <script>, no <foreignObject>,
                                  no external refs, no event handlers) or rejected entirely per config
6. Optional AV scan             → pluggable IMalwareScanner; quarantine on hit
7. SHA-256 → dedupe check
8. EXIF: orientation read via MetadataExtractor (falling back to `SKCodec.EncodedOrigin`) and applied
   to the pixels, then all metadata stripped from the served original — GPS coordinates in a published
   photo are a real privacy incident [§13.9.1]
9. Persist original to IMediaStore; write MediaItem
10. Queue rendition generation for the standard rendition set
```

### 13.4 Image editing — non-destructive

The requirement asks for resize and rotate. The design generalizes to a small, safe operation set:

| Operation | Stored as | Notes |
|---|---|---|
| Rotate | `rotate: 0\|90\|180\|270` | Free/arbitrary angles deferred — they require background fill decisions. |
| Flip | `flip: "h"\|"v"` | |
| Crop | `crop: {x,y,w,h}` normalized 0–1 | Resolution-independent; survives re-uploading a higher-res original. |
| Resize | requested per rendition | Never destructive to the original. |
| Focal point | `focalPoint: {x,y}` normalized | Drives automatic cropping at arbitrary aspect ratios. |

Two scopes, and the distinction matters:

- **Library-level edits** (`MediaItem.EditsJson`) — the editor fixes a sideways photo once; every usage
  benefits. Changing these invalidates all renditions and every page using the image.
- **Usage-level edits** (stored in the *page payload* on the `media` field) — this page needs a square
  crop. Affects only this usage.

The original bytes are never modified. A "revert to original" action is therefore always available, and
`MediaItem.EditsJson` doubles as a full edit history when versioned.

### 13.5 Rendition generation and the delivery endpoint

```
GET /media/{id}/{width}x{height}/{mode}/{name}.{ext}?f={focal}&c={crop}&q={quality}&s={sig}
    e.g. /media/812/1200x630/crop/hero-banner.webp?s=8f3a…
```

- **Signed.** `s` is an HMAC-SHA256 over the normalized parameter set with a server-side key.
  **Unsigned arbitrary dimensions are rejected.** Without this, `?width=1..10000` is a trivial
  CPU-and-storage denial-of-service. Signatures are generated server-side when rendering `srcset`, so
  editors never see them.
- **Allowlisted sizes.** In addition to signing, a configured set of widths
  (`320, 640, 960, 1280, 1920, 2560`) covers normal use; anything else must be explicitly registered.
- **Modes:** `crop` (focal-point aware), `contain`, `cover`, `pad`.
- **Format negotiation.** Serve WebP → original (JPEG/PNG) based on `Accept`, with `Vary: Accept`.
  AVIF is not emitted in v1 — the selected image library cannot encode it ([§13.9.1](#1391-consequences-of-choosing-skiasharp)).
- **Generation is lazy and cached.** First request generates and persists a `MediaRendition`; later
  requests stream it. Generation is guarded by a per-key semaphore so a burst of concurrent requests
  for a cold rendition does not spawn N encodes.
- **Cache headers:** `Cache-Control: public, max-age=31536000, immutable` — safe because the URL
  contains the item id and the parameters, and library-level edits bump `MediaItem.EditsVersion`,
  which is folded into the signature and thus the URL.

### 13.6 Rendering images in content

The `media` field renderer emits responsive markup automatically:

```html
<picture>
  <source type="image/webp" srcset="/media/812/640x360/crop/hero.webp?s=… 640w,
                                    /media/812/1280x720/crop/hero.webp?s=… 1280w"
          sizes="(max-width: 768px) 100vw, 800px">
  <img src="/media/812/1280x720/crop/hero.jpg?s=…" width="1280" height="720"
       alt="Team assembling a prototype" loading="lazy" decoding="async">
</picture>
```

Explicit `width`/`height` are always emitted to reserve layout space and protect Cumulative Layout
Shift. `loading="eager"` and `fetchpriority="high"` are set for the first image in the first zone
(the likely LCP element).

### 13.7 Alt text policy

Gap #13. Alt text is enforced, not suggested:

- `MediaItem.AltText` is required for images at upload, **or** the item must be explicitly flagged
  `IsDecorative = true` (which renders `alt=""`).
- A usage may override alt text for context.
- Publishing a page containing an image with neither alt text nor a decorative flag produces a
  **validation error** (configurable to warning for migration, but error by default).

### 13.8 Media deletion

- Soft delete first (recycle bin).
- Permanent deletion is blocked while `ContentReference` rows exist, with a where-used list.
- A nightly job reports **orphaned** media (no references, older than 30 days) for review. It never
  deletes automatically.

### 13.9 Image library selection

| Library | License | Assessment |
|---|---|---|
| **SkiaSharp** | MIT | **Selected.** Fast, no licensing friction, well-maintained. Native dependencies per platform. Weaker metadata handling and no AVIF encoding — see below. |
| **SixLabors.ImageSharp** | Six Labors Split License — free for open-source, non-profits, and companies under **USD 1M** annual gross revenue; a commercial license is required above that for closed-source use as a direct dependency. v4 additionally enforces a **build-time license key**. | Best API and format coverage, including AVIF. Rejected on licensing grounds. |
| **Magick.NET** | Apache 2.0 | Widest format support and best fidelity, largest footprint, slower. Held in reserve. |

**Decision (resolves Q3):** program against an `IImageProcessor` abstraction and ship
**`SkiaSharpImageProcessor` as the only v1 implementation.**

The deployment is expected to be closed-source and the operating entity's annual gross revenue is not
established. Under the Six Labors Split License that combination is precisely the case that requires a
paid commercial license, and v4's build-time license key means the ambiguity would surface as a broken
build rather than a quiet compliance question. MIT-licensed SkiaSharp removes the question entirely.

The `IImageProcessor` abstraction is retained even though only one implementation ships. It costs
almost nothing, and it is what makes the AVIF limitation below recoverable later.

#### 13.9.1 Consequences of choosing SkiaSharp

Two concrete capability differences follow, and both are accounted for in this specification rather
than discovered during Phase 5:

**1. No AVIF encoding — AVIF is dropped from v1.** AVIF appears in `SKEncodedImageFormat`, but
`Encode(SKEncodedImageFormat.Avif, …)` returns `null` rather than throwing; the encoder is not
present in SkiaSharp's native builds and remains unsupported
([mono/SkiaSharp#2718](https://github.com/mono/SkiaSharp/issues/2718),
[#3816](https://github.com/mono/skiasharp/issues/3816)). A silently-null encode is a nasty failure
mode, so the processor's format capability set is declared explicitly and
`IImageProcessor.SupportedOutputFormats` is asserted at startup.

v1 therefore serves **WebP → original (JPEG/PNG)** rather than AVIF → WebP → original
([§13.5](#135-rendition-generation-and-the-delivery-endpoint)). WebP has universal support in every
browser this project targets and captures the large majority of the available saving; AVIF would add
roughly a further 15–20% at the same visual quality. If that margin later matters, three paths remain
open behind the abstraction: a `MagickNetImageProcessor`, a libavif binding used for AVIF encode only,
or reinstating ImageSharp under a purchased license. None requires touching the rendition, delivery,
or caching layers.

**2. EXIF orientation needs a secondary reader.** `SKCodec.EncodedOrigin` exposes orientation, but has
known reliability defects across formats and platforms
([#1145](https://github.com/mono/SkiaSharp/issues/1145),
[#2850](https://github.com/mono/SkiaSharp/issues/2850)). The upload pipeline therefore reads
orientation with **MetadataExtractor** (Apache 2.0) and falls back to `EncodedOrigin`, rather than
trusting either alone. Orientation is baked into the pixels at upload, so every downstream consumer
sees an upright image.

The corresponding upside: Skia does not carry metadata through an encode, so EXIF — including GPS
coordinates — is stripped from every rendition as a property of the pipeline rather than as a step
that could be forgotten. The stored original is still explicitly scrubbed
([§13.3](#133-upload-pipeline) step 8), because the original is what a "download original" action
would serve.

---

## 14. Authoring experience

### 14.1 Backoffice shell

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ CMS   Content  Media  Reusable  Structure  Settings        🔍  Elena ▾        │
├───────────────┬──────────────────────────────────────────┬───────────────────┤
│ CONTENT TREE  │  EDITING CANVAS                          │  PROPERTIES       │
│               │                                          │                   │
│ ▾ Site        │  ┌────────────────────────────────────┐  │ ▸ Page            │
│   ▾ Products  │  │ ZONE: hero          [Edit│Preview] │  │   Title           │
│     • Widgets●│  │ ┌────────────────────────────────┐ │  │   Slug  /widgets  │
│     • Gadgets │  │ │ ⣿ Hero Banner            ⋮ ✕  │ │  │   Template ▾      │
│   ▾ About     │  │ │   Headline  [Ship faster     ] │ │  │                   │
│     • Team    │  │ │   Body      [rich text editor] │ │  │ ▸ SEO             │
│   • Contact   │  │ │   Image     [🖼 hero.jpg  ✎  ] │ │  │   Meta title      │
│               │  │ └────────────────────────────────┘ │  │   Description     │
│ ● draft       │  │            [+ Add block ▾]         │  │   Canonical       │
│ ○ published   │  └────────────────────────────────────┘  │   Robots ▾        │
│ ⚠ scheduled   │  ┌────────────────────────────────────┐  │                   │
│               │  │ ZONE: footer  (reusable)           │  │ ▸ Publishing      │
│ [+ New page]  │  │ 🔗 Global Footer v12  [Change][↗]  │  │   Status  Draft   │
│               │  └────────────────────────────────────┘  │   Publish on      │
│               │                                          │   Owner, Review by│
├───────────────┴──────────────────────────────────────────┴───────────────────┤
│ Saved 14:32 · Elena editing · v11 published    [Preview] [Submit] [Publish ▾] │
└──────────────────────────────────────────────────────────────────────────────┘
```

Three panes, all resizable and collapsible. The canvas is ordered by the template's zone `SortOrder`
and grouped by `Zone.Group` where set.

### 14.2 Content tree

- Lazy-loads children; virtualized for large sibling sets.
- Status indicators: published / draft-changes-pending / scheduled / unpublished / in-review / locked.
- Drag to reorder and reparent, with an explicit confirmation showing the URL changes and redirects
  that will be created.
- Right-click: new child, duplicate (deep or shallow), copy, move, delete, publish branch, unpublish.
- Filter box searches title, slug, and id within the tree ([§17.1](#171-backoffice-search)).

### 14.3 Zone editors

Each zone renders with a header (name, help text, required marker, validation state) and a body
supplied by the field type's editor component.

The **block list editor** for `blocks` zones supports: add (constrained to `allowedBlockTypes`),
drag-reorder with keyboard alternatives, collapse/expand, duplicate, delete with undo, and per-block
validation badges. Each block shows a configurable summary line when collapsed so a 12-block page is
still navigable.

### 14.4 The edit/preview experience

This is the requirement's explicit ask for plain-text and HTML/Markdown zones.

**Rich text (`richText`, `format: markdown`)**

```
┌─────────────────────────────────────────────────────┐
│ Body                        [ Edit │ Preview │ Split]│
├─────────────────────────────────────────────────────┤
│ B I  H▾  “ ⁝≡ ≡⁝  🔗 🖼  </>          ⌨ shortcuts  │
├─────────────────────────────────────────────────────┤
│ ## Why teams choose us                              │
│                                                     │
│ We help teams **ship faster** with…                 │
└─────────────────────────────────────────────────────┘
```

- **Edit** — a Markdown source editor with a formatting toolbar, syntax highlighting, and shortcuts.
- **Preview** — the Markdown rendered by the *same* pipeline the public site uses
  (Markdig → sanitize → the site's typography CSS), so preview is accurate rather than approximate.
- **Split** — synchronized-scroll side-by-side.
- Link and image insertion open the CMS pickers and insert internal references, never hand-typed URLs.
- Word/character count, and a configurable soft limit.

**Rich text (`format: html`)** — same three modes with a CodeMirror-style HTML editor, plus a
persistent banner showing which tags the active sanitization profile permits, and a live warning when
the editor's content contains something that *will be stripped on save*. Silent stripping is the
number-one source of "the CMS ate my content" support tickets, so the stripping is surfaced before it
happens.

**Plain text** — inline single/multi-line editing with a live character counter. "Preview" for a plain
text zone means rendering it in the template's actual typography, which is where the value is.

Implementation: Blazor components in the WASM client wrapping a JS editor via interop. The evaluated
options are Quill (mature, permissive, several existing Blazor wrappers), a CodeMirror 6 integration
for source modes, and TipTap/ProseMirror (best model, heaviest integration). **Decision:** CodeMirror 6
for Markdown/HTML source modes and Quill for the constrained WYSIWYG surface, both loaded as local
static assets — no CDN, so the CSP in [§20.5](#205-content-security-policy) can stay strict.

### 14.5 In-context editing (v2)

The v2 evolution of the editing canvas: the page renders in an iframe at its real URL in preview mode;
zones marked `IsInlineEditable` gain a hover outline; clicking one opens the field editor as an overlay
positioned over the actual element. Communication over `postMessage` with a strict origin check.

Deferred from v1 because it multiplies the editor surface area and every template must cooperate with
it. The v1 canvas already delivers "edit/preview" as required.

### 14.6 Validation and publish gating

Validation runs at three points with different severities:

| Point | Behavior |
|---|---|
| **Field blur** | Immediate, field-local. Never blocks. |
| **Save draft** | Structural validity only (well-formed payload, references resolve). Content validation failures are **recorded, not blocking** — an editor must always be able to save incomplete work. |
| **Publish** | Full validation. Errors block; warnings require acknowledgement. |

Publish-time checks: required zones filled; field validators pass; URL unique and not reserved;
every referenced page/media/reusable item exists and is not deleted; images have alt text or a
decorative flag; SEO title and description present (warning); no link points at an unpublished page
(warning, with the list).

The publish dialog presents errors and warnings grouped by zone, each deep-linking to the offending
field.

### 14.7 Editorial metadata

Gap #29. On every page: `OwnerUserId`, `ReviewByDate`, `InternalNotes`, `Tags`. A "Needs review"
dashboard lists content past its review date, which is the only practical defense against content rot.

### 14.8 Notifications

Email (via `IEmailSender`) plus an in-app inbox for: submitted for review, approved, rejected,
scheduled publish succeeded/failed, edit-lock override, and comment mentions.

### 14.9 Dashboard

The backoffice landing screen, scoped to the signed-in user's permissions:

- **My work** — drafts with unpublished changes, items assigned for review, rejected items needing
  attention.
- **Scheduled** — what publishes or expires in the next 7 days, with failures highlighted.
- **Needs attention** — content past its `ReviewByDate`, pages with broken references, images missing
  alt text, and the top `NotFoundLog` URLs by hit count.
- **Recent activity** — a filtered view of `AuditLog` for content the user can see.

Each tile deep-links into a filtered list. The "needs attention" tile is the mechanism that turns the
housekeeping reports from [§24.4](#244-background-services) into something anyone actually acts on —
a nightly job that writes a report nobody opens is wasted work.

### 14.10 Recycle bin

Gap #10. Soft delete is the only delete available to most roles.

- Deleting a page marks `IsDeleted`, retires its public routes, and creates a redirect to its parent
  if configured. Version history, references, and audit trail are all retained.
- The bin lists deleted pages, media, and reusable content with who deleted them and when, filterable
  and searchable.
- **Restore** returns the item to its former parent — or to the site root, with a warning, if that
  parent is itself deleted. A restored page returns as a *draft*; it does not silently reappear on the
  public site.
- Deleting a page with children deletes the subtree, shown with an explicit count in the confirmation.
  Restore likewise restores the subtree.
- Permanent deletion is `Administrator`-only, is refused while `ContentReference` rows point at the
  item, and is irreversible — the confirmation requires typing the item's name.
- Items older than a configurable retention period (default 90 days) are reported for purge, never
  purged automatically.

### 14.11 Bulk operations

At real content volumes, per-item actions are not enough. From any filtered list, with a selection:

| Operation | Notes |
|---|---|
| Publish / unpublish | Runs full validation per item; reports a per-item result rather than failing the batch |
| Move | Shows the aggregate URL-change and redirect impact before proceeding |
| Add / remove tags | |
| Set owner or review-by date | |
| Delete (soft) | Subtree-aware, with a combined count |
| Media: move to folder, tag, set credit | |

Bulk operations run as a background job with progress reporting when the selection exceeds 25 items,
so a large batch does not tie up a request. Every item's outcome is individually audit-logged; a
partial failure leaves the successful items applied and reports the rest.

### 14.12 Duplication

- **Duplicate page (shallow)** — copies the draft payload, metadata, and SEO fields; appends
  "(copy)" to the title; generates a non-colliding slug; creates it unpublished. Version history is
  *not* copied — the copy starts at version 1.
- **Duplicate subtree (deep)** — the same for a page and all descendants, preserving relative
  structure. Internal links **between pages inside the copied subtree** are rewritten to point at the
  new copies; links out of the subtree are left pointing at the originals. This rewriting is what
  makes "duplicate a section for next year's campaign" actually useful rather than a source of
  cross-linked confusion.
- Media is referenced, never duplicated.

---

## 15. Public delivery and rendering

### 15.1 The catch-all endpoint

A single terminal endpoint handles all content URLs, registered **after** every other route so
`/admin`, `/api`, `/media`, and Blazor framework paths keep priority:

```csharp
app.MapGet("/{**slug}", DeliveryEndpoint.HandleAsync)
   .CacheOutput(p => p.Tag("content"))
   .AllowAnonymous();
```

### 15.2 Rendering pipeline

`TemplateRenderer` resolves the template component for the version's `templateKey`, then renders it via
`DynamicComponent`. `<CmsZone Name="hero" />` reads the payload from a cascading
`RenderContext`, looks up the field type, and renders that field type's renderer component with the
zone's value and configuration.

```csharp
public sealed record RenderContext(
    PublishedPage Page,
    ContentPayload Payload,
    RenderMode Mode,            // Live | Preview | ScheduledPreview
    ISet<string> CacheTags);    // accumulated during render → applied to the response
```

Cache tags accumulate *during* rendering. A zone that resolves reusable content adds `ru:{id}`; a media
field adds `media:{id}`. This means invalidation is derived from what was actually rendered rather than
from a hand-maintained list — the class of bug where a developer forgets to add a tag disappears.

### 15.3 Fallback behavior

The public site must never show a stack trace or a blank page:

| Condition | Behavior |
|---|---|
| Unknown `templateKey` | Log error, health-check degraded, render a minimal fallback layout with the page's text content. |
| Unknown field type key | Log warning, render nothing for that zone. |
| Referenced media missing | Render a placeholder with the alt text; log. |
| Referenced reusable content unpublished | Render nothing; log warning; surface in the broken-references report. |
| Renderer component throws | Caught per zone by an error boundary — one broken block does not take down the page. Log with the page id, zone key, and version id. |

### 15.4 Response shape

Every public page emits: canonical `<link>`, SEO meta ([§18](#18-seo)), JSON-LD, `ETag`,
`Last-Modified` (the publish timestamp), `Cache-Control`, and the security headers from
[§20.5](#205-content-security-policy). Interactive islands (search box, forms in v2) opt in per
component with `@rendermode InteractiveWebAssembly`, keeping the rest of the page static and cacheable.

---

## 16. Caching and invalidation

Gap #17.

### 16.1 Layers

| Layer | Scope | TTL | Invalidation |
|---|---|---|---|
| **Output cache** (`AddOutputCache`) | Full rendered HTML | 1 hour default | Tag eviction on publish |
| **Published content cache** (`HybridCache`) | Deserialized `PublishedPage` objects | 15 min | Tag eviction |
| **Route cache** | url → pageId map | 15 min | On any route change |
| **Rendition cache** | Generated image files in `IMediaStore` | permanent | On `EditsVersion` bump (URL changes) |
| **Client/CDN** | `Cache-Control` headers | pages: `max-age=0, s-maxage=300, must-revalidate`; media: `immutable` | ETag revalidation; CDN purge webhook |

### 16.2 Cache tags

| Tag | Applied to | Evicted when |
|---|---|---|
| `page:{id}` | The page's own response | It is published, unpublished, moved, or deleted |
| `ru:{id}` | Every page rendering that reusable item | The reusable item is published or unpublished |
| `media:{id}` | Every page rendering that media | Library-level media edits or metadata change |
| `tpl:{id}` | Every page using that template | The template revision changes |
| `nav:{menuKey}` | Pages rendering that menu | Any menu edit, or any publish/move affecting structural nav |
| `content` | Everything | Manual "purge all" |

Eviction uses `IOutputCacheStore.EvictByTagAsync`.

### 16.3 Scale-out considerations

With more than one server instance, in-memory output cache produces stale content on the nodes that did
not process the publish. Two supported configurations:

- **Single instance (default):** in-memory `IOutputCacheStore`. Simple, no extra infrastructure.
- **Multi-instance:** `AddStackExchangeRedisOutputCache` with the Redis resource in Aspire.
  `IDistributedCache` is explicitly **not** used for output caching — it lacks the atomic operations
  tagging requires.

Invalidation is published through the transactional outbox (`OutboxMessage`) rather than fired
in-process, so a publish that commits always results in an eviction even if the process crashes
immediately afterward, and every node observes it.

### 16.4 Correctness rules

- `UseOutputCache()` is placed after `UseAuthentication`/`UseAuthorization` so authenticated responses
  are never cached for anonymous users.
- Preview and any authenticated request bypass the cache entirely (`.NoCache()` policy on those routes,
  plus a base policy predicate excluding requests carrying an identity cookie).
- Never vary the cache by cookie. The only `Vary` in use is `Accept` on the media endpoint for WebP
  negotiation; page responses vary by nothing.

---

## 17. Search

### 17.1 Backoffice search

Gap #19. Available in v1 across pages, media, and reusable content:

- Free text over title, slug, extracted body text, alt text, filename, tags.
- Filters: template, status, owner, tag, modified date range, "has unpublished changes,"
  "past review date."
- Backed by **SQL Server full-text search** over a `SearchDocument` table populated by
  `SearchIndexer` (which uses `IFieldType.ExtractSearchText`) on every save and publish. Full-text
  indexing avoids `LIKE '%…%'` scans and is available in every supported SQL Server edition,
  including Azure SQL.

### 17.2 Public site search (v2)

The same `SearchDocument` table, filtered to published content, with a
`/search` results page and result-count analytics. Deferred to v2 but the index is built in v1, so
enabling it is a UI task rather than an infrastructure one.

---

## 18. SEO

Gaps #3 and #4.

### 18.1 Per-page fields

`MetaTitle` (defaults to page title), `MetaDescription`, `CanonicalUrl` (defaults to the page's own
absolute URL), `RobotsDirectives` (`index/noindex`, `follow/nofollow`), `OgTitle`, `OgDescription`,
`OgImageMediaId`, `OgType`, `TwitterCard`, and `StructuredDataJson` (JSON-LD override).

Character-count guidance is shown against a search-result preview widget in the properties panel.

### 18.2 Generated output

- `<title>`, `<meta name="description">`, `<link rel="canonical">`, `<meta name="robots">`.
- Open Graph and Twitter Card tags, with the OG image rendered through a `1200x630` crop rendition.
- JSON-LD: `WebSite` and `Organization` on the home page, `BreadcrumbList` derived from the content
  tree, `WebPage`/`Article` per page, all overridable per page.

### 18.3 `sitemap.xml`

- Generated from published, indexable pages; excludes `noindex` pages, the 404 page, and preview URLs.
- `<lastmod>` from the publish timestamp; `<changefreq>`/`<priority>` configurable per page.
- Automatically splits into a sitemap index above 40,000 URLs.
- Cached with the `content` tag, so it refreshes on any publish.

### 18.4 `robots.txt`

Editable in site settings, with a sensible default that disallows `/admin`, `/api`, and `/preview`,
and points at the sitemap. Non-production environments serve `Disallow: /` unconditionally — an
unnoticed indexed staging site is a genuinely damaging and very common mistake.

---

## 19. Localization

Gap #23. **Out of scope. This system is single-language: `en-US`.**

An earlier draft of this specification carried `LocaleId` through the schema on the reasoning that
localization is the most expensive thing to retrofit. With multilingual support now confirmed as never
required, that hedge is removed: no `Locale` table, no `LocaleId` on `PageVersion`, `PageRoute`,
`Redirect`, or `SearchDocument`, no `Page.LocaleGroupId`, no `Translator` role, no `hreflang` output,
and no locale dimension on cache keys or unique indexes.

The simplification is worth taking. It removes a foreign key from four hot tables, drops a column from
every route-resolution unique index, and eliminates a permission dimension and an entire v2 workstream
(~15 engineer-days) — all in service of a capability that will not be used.

**One seam is retained**, and it is not a localization architecture: `SiteSettings.Culture`, defaulting
to `en-US`, is the single source for date/number formatting and the `<html lang>` attribute. Its only
purpose is to keep `"en-US"` from being hardcoded in a dozen renderers.

**If this decision is ever reversed**, the honest cost is a significant one — roughly 25–35
engineer-days rather than the 15 the earlier hedge would have made it. It requires a migration adding
locale to those four tables and rebuilding their unique indexes, changes to route resolution, cache
keying, the content tree, and the permissions model, plus the translation UI itself. That is the
trade being accepted here, recorded so the reversal is costed honestly rather than discovered.

---

## 20. Security

Gap #11 is the highest-severity item in this specification. A CMS is, by construction, a system that
stores attacker-influenceable markup and renders it to other users.

### 20.1 Threat model

| Threat | Vector | Mitigation |
|---|---|---|
| Stored XSS | Editor pastes `<script>` or an `onerror` attribute into an HTML/rich-text zone | Sanitize on write **and** on render ([§20.2](#202-html-sanitization)); CSP |
| Stored XSS via SVG | Upload of an SVG containing script | Strict SVG sanitization profile, or reject SVG entirely |
| Privilege escalation | Author publishes without approval | Policy-based authorization on every endpoint, re-checked server-side; never trust the client |
| Path traversal | `../` in a media key or filename | Keys are server-generated from content hashes; filenames are never used as paths |
| SSRF | "Import image from URL" feature | Not in v1. If added: allowlist schemes, block private/link-local ranges, cap redirects and size |
| Decompression bomb | Crafted image with huge dimensions | Dimension/pixel-count guard before decode |
| Denial of service | `?width=…` rendition flooding | HMAC-signed rendition URLs + allowlisted sizes |
| Credential attacks on backoffice | Brute force, credential stuffing | Identity lockout, rate limiting, 2FA/passkeys (already present) |
| CSRF | Cookie-authenticated API called cross-origin | Antiforgery tokens on all state-changing requests; `SameSite=Lax` cookies |
| Information disclosure | Draft content leaking | Public queries filter on `PublishedVersionId` at the data layer, not the UI layer; preview requires auth or a token |
| Insecure direct object reference | `/api/cms/pages/99` for a page outside the user's section | Section ACL checked in the service layer for every operation |
| Mass assignment | Client posts `Status: "Published"` on a draft save | Explicit DTOs; status transitions only via dedicated endpoints |

### 20.2 HTML sanitization

`SanitizationService` wraps **HtmlSanitizer** (`mganss/HtmlSanitizer`), which parses to a real DOM via
AngleSharp rather than pattern-matching — important because regex-based sanitizers are defeated by
malformed markup and tag poisoning.

Three profiles:

| Profile | Allowed | Used by |
|---|---|---|
| `Basic` | `p, br, strong, em, u, s, a, ul, ol, li, blockquote, h2–h6, code, pre` | `richText` default |
| `Extended` | Basic + `table, thead, tbody, tr, th, td, img, figure, figcaption, hr, div, span`, and a class allowlist | `richText` with extended config |
| `Developer` | Extended + `iframe` (src host allowlist), `video`, `audio`, `source`, data attributes | `html` field, `Developer` role only |

Rules across all profiles: no `<script>`, no `<style>`, no event handler attributes (`on*`), URL schemes
restricted to `http`, `https`, `mailto`, and `tel` (plus `data:` for images only, with a size cap),
`rel="noopener noreferrer"` forced on `target="_blank"` links, and CSS properties allowlisted.

**Sanitize twice, deliberately.** On write, so the database never holds hostile markup; on render, so
content that predates a profile change, or that arrived through an import or a direct database write,
is still neutralized. The render-time pass is cached with the rendition so the cost is paid once.

### 20.3 Authentication

Uses the existing Identity setup unchanged: cookie auth, roles, email confirmation, 2FA, passkeys,
external logins. Changes required:

- Replace `IdentityNoOpEmailSender` with a real sender — workflow notifications and password resets
  are non-functional without it.
- Tighten the password policy for CMS roles. The current configuration
  (`RequireDigit = false`, `RequireLowercase = false`, `RequireUppercase = false`,
  `RequireNonAlphanumeric = false`, `RequiredLength = 6`) is appropriate for a template but **not**
  for accounts that can publish to a public site. Recommended: minimum 12 characters, breached-password
  screening, and mandatory 2FA for `Administrator`, `Developer`, and `Approver`.
- Disable public self-registration, or gate it so registrants receive no role by default. An open
  `/account/register` on a CMS is a standing risk.

### 20.4 Authorization

Policy-based, evaluated server-side on every endpoint. Permission constants
(`Content.Read`, `Content.Edit`, `Content.Publish`, `Content.Delete`, `Media.Upload`,
`Media.Delete`, `Structure.Edit`, `Settings.Edit`, `Users.Manage`) map to roles via a policy provider,
with section ACLs applied in the service layer ([§21](#21-permissions-matrix)). The WASM client's
UI-level checks are convenience only and are never trusted.

### 20.5 Content Security Policy

Public pages:

```
Content-Security-Policy:
  default-src 'self';
  script-src 'self' 'nonce-{random}';
  style-src 'self' 'nonce-{random}';
  img-src 'self' data: https:;
  font-src 'self';
  frame-ancestors 'none';
  base-uri 'self';
  form-action 'self';
  object-src 'none'
```

Per-request nonces; no `unsafe-inline`, no `unsafe-eval`. Editor-supplied `<iframe>` embeds (YouTube,
Vimeo) are handled by a dedicated `embed` block type that emits a host-allowlisted iframe with
`frame-src` extended accordingly — not by permitting arbitrary iframe HTML.

The backoffice requires `wasm-unsafe-eval` for Blazor WebAssembly and therefore uses a separate,
appropriately scoped policy on `/admin`. `frame-ancestors 'self'` there, to support the v2 in-context
editor.

Also emitted: `Strict-Transport-Security` (HSTS is already enabled), `X-Content-Type-Options: nosniff`,
`Referrer-Policy: strict-origin-when-cross-origin`, and a minimal `Permissions-Policy`.

### 20.6 Rate limiting

Gap #28. ASP.NET Core rate limiting:

| Endpoint group | Limit |
|---|---|
| `/account/login`, `/account/register`, password reset | 5 per 15 min per IP, sliding |
| `/api/cms/**` (writes) | 100/min per user |
| `/media` uploads | 20/min per user |
| `/media/{id}/…` renditions | 300/min per IP |
| Preview tokens | 30/min per token |
| Public pages | 600/min per IP (generous; cached responses are cheap) |

### 20.7 Media serving safety

- Served from a dedicated endpoint with `Content-Disposition: inline` for images only and `attachment`
  for documents; `X-Content-Type-Options: nosniff`; the `Content-Type` pinned to the **sniffed** type,
  never the client-declared one.
- Consider a separate cookieless domain for media in production, so any content-type confusion cannot
  become same-origin script execution.

### 20.8 Secrets and configuration

Connection strings and the media-signing HMAC key come from configuration (user secrets in dev, key
vault / environment in production). The Aspire `sql-password` parameter's development default must not
reach production. The signing key must be rotatable, with a grace period during which the previous key
still validates.

---

## 21. Permissions matrix

### 21.1 Role → permission

| Permission | Admin | Developer | Editor | Author | Approver | MediaManager | Viewer |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| View backoffice | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Read content | ✔ | ✔ | ✔ | ✔ | ✔ | — | ✔ |
| Create page | ✔ | ✔ | ✔ | ✔ | — | — | — |
| Edit page | ✔ | ✔ | ✔ | ✔ | ✎ | — | — |
| Move / reorder | ✔ | ✔ | ✔ | — | — | — | — |
| Submit for review | ✔ | ✔ | ✔ | ✔ | — | — | — |
| Approve / reject | ✔ | ✔ | — | — | ✔ | — | — |
| Publish / unpublish | ✔ | ✔ | ✔ | — | ✔ | — | — |
| Schedule | ✔ | ✔ | ✔ | — | ✔ | — | — |
| Delete (soft) | ✔ | ✔ | ✔ | — | — | — | — |
| Empty recycle bin | ✔ | — | — | — | — | — | — |
| Rollback version | ✔ | ✔ | ✔ | — | ✔ | — | — |
| Preview drafts | ✔ | ✔ | ✔ | ✔ | ✔ | — | ✔ |
| Create preview links | ✔ | ✔ | ✔ | ✔ | ✔ | — | — |
| Upload media | ✔ | ✔ | ✔ | ✔ | — | ✔ | — |
| Edit media metadata | ✔ | ✔ | ✔ | ✔ | — | ✔ | — |
| Delete media permanently | ✔ | — | — | — | — | ✔ | — |
| Manage reusable content | ✔ | ✔ | ✔ | — | ✔ | — | — |
| Manage templates / block types / zones | ✔ | ✔ | — | — | — | — | — |
| Manage redirects | ✔ | ✔ | ✔ | — | — | — | — |
| Manage navigation | ✔ | ✔ | ✔ | — | — | — | — |
| Manage site settings | ✔ | ✔ | — | — | — | — | — |
| Manage users / roles / ACLs | ✔ | — | — | — | — | — | — |
| View audit log | ✔ | ✔ | — | — | — | — | — |
| Purge cache | ✔ | ✔ | — | — | — | — | — |

✔ full · ✎ edit only while the item is assigned to them for review

### 21.2 Section-level ACLs

Role grants are global; ACLs narrow them to a subtree.

```
PageAcl(PageId, PrincipalType /*User|Role*/, PrincipalId, Permission, IsAllow, IsInherited)
```

- Applied to a page **and all descendants** by default (`Page.Path` makes this an indexed prefix match).
- A user with no matching ACL sees the subtree but cannot edit it; with `Content.Read` denied, the
  subtree is hidden from the tree entirely.
- Deny beats allow at the same depth; a more specific (deeper) rule beats a shallower one.
- `Administrator` bypasses ACLs. Every bypass is audit-logged.

---

## 22. Management API

Consumed by the Blazor WebAssembly backoffice. Minimal APIs under `/api/cms`, cookie-authenticated,
antiforgery-protected on writes, versioned by URL segment (`/api/cms/v1/...`).

Conventions: `application/json` with `System.Text.Json` (camelCase); errors as RFC 9457
`application/problem+json`; `ETag`/`If-Match` for optimistic concurrency; cursor pagination
(`?cursor=&limit=`) on collections; `?fields=` projection on large resources.

### 22.1 Endpoints

**Pages**

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/pages` | List/search. Filters: `parentId, templateId, status, tag, q, modifiedAfter` |
| `GET` | `/pages/tree?parentId=&depth=` | Tree node fetch for lazy loading |
| `POST` | `/pages` | Create from a template |
| `GET` | `/pages/{id}` | Page metadata + draft payload |
| `PUT` | `/pages/{id}/draft` | Save draft payload (`If-Match` required) |
| `PATCH` | `/pages/{id}/metadata` | Title, slug, SEO, editorial metadata |
| `POST` | `/pages/{id}/move` | Reparent/reorder; returns URL + redirect impact |
| `POST` | `/pages/{id}/duplicate` | `?deep=true` for the subtree |
| `DELETE` | `/pages/{id}` | Soft delete → recycle bin |
| `POST` | `/pages/{id}/restore` | Restore from recycle bin |
| `POST` | `/pages/{id}/validate` | Dry-run publish validation |
| `POST` | `/pages/{id}/publish` | Publish now; body may carry `acknowledgedWarnings` |
| `POST` | `/pages/{id}/schedule` | Set `publishOn` / `unpublishOn` |
| `POST` | `/pages/{id}/unpublish` | Retire from the public site |
| `GET` | `/pages/{id}/versions` | Version history |
| `GET` | `/pages/{id}/versions/{vid}` | One version's payload |
| `GET` | `/pages/{id}/versions/{a}/diff/{b}` | Structural diff |
| `POST` | `/pages/{id}/versions/{vid}/restore` | Copy that version into the draft |
| `GET` | `/pages/{id}/references` | Where-used (inbound and outbound) |
| `POST` | `/pages/{id}/lock` · `DELETE` same | Acquire / release the advisory edit lock |
| `GET`/`POST` | `/pages/{id}/comments` | Review comments |

**Workflow**

`POST /pages/{id}/submit` · `POST /pages/{id}/approve` · `POST /pages/{id}/reject` ·
`GET /workflow/tasks?assignedTo=me`

**Media**

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/media` | Multipart upload; returns the item, or the existing item on a hash match |
| `GET` | `/media` | Browse/search; filters `folderId, type, q, unusedOnly` |
| `GET` | `/media/{id}` | Metadata + renditions |
| `PATCH` | `/media/{id}` | Alt text, title, caption, credit, tags, `isDecorative` |
| `PUT` | `/media/{id}/edits` | Library-level rotate/crop/flip/focal point; bumps `EditsVersion` |
| `POST` | `/media/{id}/revert` | Discard library-level edits |
| `POST` | `/media/{id}/replace` | Upload new bytes, keep the id and all references |
| `DELETE` | `/media/{id}` | Soft delete |
| `GET` | `/media/{id}/references` | Where-used |
| `GET`/`POST`/`PATCH`/`DELETE` | `/media/folders…` | Folder management |

**Reusable content** — `/reusable` mirroring the page endpoints (CRUD, versions, publish, references,
impact) minus URLs and the tree.

**Structure** (`Developer`/`Administrator`) — `/templates`, `/templates/{id}/zones`,
`/block-types`, `/block-types/{id}/properties`, `/compositions`, `/field-types` (read-only registry
introspection), `/templates/{id}/revisions`.

**Site** — `/redirects` (CRUD + `POST /redirects/import` CSV), `/navigation`, `/settings`,
`/not-found-log`, `/audit?entity=&entityId=&userId=&from=&to=`,
`POST /cache/purge`, `GET /reports/broken-references`, `GET /reports/orphaned-media`,
`GET /reports/review-due`.

**Preview** — `POST /preview-tokens`, `GET /preview-tokens?pageId=`, `DELETE /preview-tokens/{id}`.

### 22.2 Error contract

```jsonc
{
  "type": "https://cms.example/errors/validation",
  "title": "Publish validation failed",
  "status": 422,
  "detail": "3 errors prevent publishing.",
  "errors": [
    { "zoneKey": "hero", "blockId": "0f6c…", "property": "image",
      "code": "media.alt-text-required",
      "message": "Image \"hero.jpg\" has no alt text and is not marked decorative." }
  ],
  "warnings": [
    { "code": "link.target-unpublished",
      "message": "\"Get started\" links to \"Pricing\", which is not published." }
  ]
}
```

Warnings are returned with `422` on the first attempt; the client resubmits with
`acknowledgedWarnings: ["link.target-unpublished"]` to proceed.

---

## 23. Database schema

Conventions inherited from the existing solution: `int` identity keys; `EntityBase` / `FingerPrintEntityBase`;
`datetimeoffset(7)` for instants via `ConfigureConventions`; every editor-mutable table audited
automatically by `AuthDbContext.AddLogging()`.

### 23.1 Structure tables

```
Template
  Id, Key (unique, 100), Name (200), Description (500), ComponentTypeName (500),
  IsOrphaned, IsEnabled, CurrentRevision, SortOrder, [fingerprint]

TemplateRevision
  Id, TemplateId → Template, RevisionNumber, ZoneSnapshotJson (nvarchar(max)),
  CreatedOn, CreatedBy, Notes
  UNIQUE (TemplateId, RevisionNumber)

Zone
  Id, TemplateId → Template, Key (100), Name (200), Description (500),
  FieldTypeKey (100), ConfigurationJson (nvarchar(max)), IsRequired,
  IsInlineEditable, Group (100), SortOrder, [fingerprint]
  UNIQUE (TemplateId, Key)

BlockType
  Id, Key (unique, 100), Name, Description, ComponentTypeName (500),
  IconKey (50), SummaryTemplate (500), IsOrphaned, CurrentRevision, [fingerprint]

BlockTypeRevision      -- mirrors TemplateRevision
  Id, BlockTypeId, RevisionNumber, PropertySnapshotJson, CreatedOn, CreatedBy

BlockTypeProperty
  Id, BlockTypeId → BlockType, Key (100), Name, Description,
  FieldTypeKey (100), ConfigurationJson, IsRequired, Group, SortOrder, [fingerprint]
  UNIQUE (BlockTypeId, Key)

Composition                     -- shared property groups
  Id, Key (unique), Name, Description
CompositionProperty             -- same shape as BlockTypeProperty
BlockTypeComposition            -- BlockTypeId, CompositionId, SortOrder

SiteSettings                    -- single row
  Id, SiteName, Culture (16, default 'en-US'), TimeZoneId, RobotsTxt (nvarchar(max)),
  WorkflowMode, HomePageId, NotFoundPageId, VersionRetentionDays,
  DefaultOgImageMediaId, GoogleSiteVerification, [fingerprint]
```

### 23.2 Content tables

```
Page
  Id, ParentId → Page (null = root), PublicId (uniqueidentifier),
  Slug (100), UseExplicitUrl, ExplicitUrl (2000),
  Path (900, materialized '/1/8/44/'), Depth, SortOrder,
  TemplateId → Template,
  DraftVersionId → PageVersion (null-first, set after insert),
  PublishedVersionId → PageVersion (nullable),
  ShowInNavigation, OwnerUserId → User, ReviewByDate (date), InternalNotes (2000),
  IsDeleted, DeletedOn, DeletedBy, RowVersion (rowversion), [fingerprint]
  INDEX (ParentId, SortOrder) · INDEX (Path) · INDEX (PublishedVersionId)
  FILTERED INDEX on IsDeleted = 0

PageVersion
  Id, PageId → Page, VersionNumber, Status (tinyint),
  Label (200), Title (500),
  ContentJson (nvarchar(max)), TemplateId, TemplateRevision,
  MetaTitle (200), MetaDescription (500), CanonicalUrl (2000),
  RobotsIndex, RobotsFollow, OgTitle, OgDescription, OgImageMediaId → MediaItem,
  OgType (50), TwitterCard (50), StructuredDataJson (nvarchar(max)),
  ChangeFreq (20), Priority (decimal(2,1)),
  PublishOn, UnpublishOn, PublishedOn, PublishedBy,
  RowVersion, [fingerprint]
  UNIQUE (PageId, VersionNumber) · INDEX (PageId, Status) · INDEX (Status, PublishOn)

PageRoute
  Id, PageId → Page, Url (2000 → hashed/prefixed index), UrlHash (binary(32)),
  IsPrimary, IsPublished, CreatedOn
  UNIQUE (UrlHash) WHERE IsPublished = 1
  INDEX (PageId)

Redirect
  Id, FromUrl (2000), FromUrlHash (binary(32)), ToUrl (2000), ToPageId → Page,
  StatusCode (smallint), IsAutomatic, IsEnabled, Notes (500),
  HitCount (bigint), LastHitOn, [fingerprint]
  UNIQUE (FromUrlHash)

ReusableContent
  Id, Key (unique, 100), Name (200), Description (500), FolderId,
  BlockTypeId → BlockType, DraftVersionId, PublishedVersionId,
  IsDeleted, RowVersion, [fingerprint]

ReusableContentVersion
  Id, ReusableContentId, VersionNumber, Status, Label,
  ContentJson (nvarchar(max)), BlockTypeRevision,
  PublishOn, UnpublishOn, PublishedOn, PublishedBy, RowVersion, [fingerprint]
  UNIQUE (ReusableContentId, VersionNumber)

ContentReference                 -- derived projection; rebuilt on every save/publish
  Id, SourceType (tinyint: PageVersion|ReusableContentVersion),
  SourceVersionId, TargetType (tinyint: Page|MediaItem|ReusableContent),
  TargetId, ZoneKey (100), BlockId (uniqueidentifier), PropertyKey (100),
  IsPinned, PinnedVersionId
  INDEX (TargetType, TargetId)            -- "where used", the hot query
  INDEX (SourceType, SourceVersionId)     -- rebuild + cache-tag computation

WorkflowTask
  Id, PageVersionId, ReusableContentVersionId, State (tinyint),
  AssignedToUserId, AssignedByUserId, DueOn, DecidedOn, DecidedByUserId,
  Decision (tinyint), DecisionNotes (2000), [fingerprint]

Comment
  Id, PageId, PageVersionId, ZoneKey (100), BlockId, ParentCommentId,
  Body (4000), IsResolved, ResolvedOn, ResolvedBy, [fingerprint]

Tag / PageTag / NavigationMenu / NavigationItem / PageAcl        -- as described above
```

### 23.3 Media tables

```
MediaFolder
  Id, ParentId, Name (200), Path (900), SortOrder, IsDeleted, [fingerprint]

MediaItem
  Id, FolderId → MediaFolder, FileName (255), OriginalFileName (255),
  ContentType (100), SizeBytes (bigint), Sha256 (binary(32)),
  StorageKey (500), MediaKind (tinyint: Image|Document|Video|Audio),
  Width, Height, DurationSeconds,
  AltText (500), IsDecorative, Title (200), Caption (1000), Credit (200),
  FocalPointX (float), FocalPointY (float),
  EditsJson (nvarchar(max)), EditsVersion (int),
  IsDeleted, DeletedOn, DeletedBy, RowVersion, [fingerprint]
  UNIQUE (Sha256) WHERE IsDeleted = 0        -- deduplication
  INDEX (FolderId) · INDEX (MediaKind, IsDeleted)

MediaRendition
  Id, MediaItemId → MediaItem, SpecHash (binary(32)), Spec (500),
  Width, Height, Format (10), Quality, SizeBytes, StorageKey (500),
  EditsVersion, GeneratedOn, LastAccessedOn
  UNIQUE (MediaItemId, SpecHash)
```

### 23.4 Operational tables

```
ScheduledJob
  Id, JobType (tinyint), TargetType, TargetId, RunOn, ClaimedOn, ClaimedBy (200),
  CompletedOn, Status (tinyint), AttemptCount, LastError (2000)
  INDEX (Status, RunOn)

PreviewToken
  Id, TokenHash (binary(32), unique), PageId, PageVersionId,
  ExpiresOn, MaxUses, UseCount, RevokedOn, CreatedBy, [fingerprint]

EditLock
  PageId (PK), UserId, AcquiredOn, HeartbeatOn

SearchDocument
  Id, EntityType (tinyint), EntityId, Title (500),
  Body (nvarchar(max)), Keywords (nvarchar(max)), Url (2000),
  IsPublished, UpdatedOn
  UNIQUE (EntityType, EntityId)
  FULLTEXT INDEX (Title, Body, Keywords)

OutboxMessage
  Id (bigint), Type (200), PayloadJson, CreatedOn, ProcessedOn, AttemptCount, LastError
  INDEX (ProcessedOn) WHERE ProcessedOn IS NULL

NotFoundLog
  Id, Url (2000), UrlHash (binary(32) unique), Referrer (2000), HitCount, FirstSeenOn, LastSeenOn
```

### 23.5 Schema notes

- `Page.DraftVersionId` and `PageVersion.PageId` are mutually referential. Configure both FKs with
  `DeleteBehavior.Restrict` and set `DraftVersionId` in a second statement within the creating
  transaction, so EF Core does not attempt an impossible single-statement insert.
- `Url` columns exceed SQL Server's 900-byte index key limit, hence the `binary(32)` hash columns
  carrying the unique indexes. The full URL stays available for display and `LIKE` prefix queries.
- Filtered unique indexes (`WHERE IsPublished = 1`, `WHERE IsDeleted = 0`) let soft-deleted and
  draft rows coexist with live ones without violating uniqueness — the standard trap in CMS schemas.
- `ContentJson` is `nvarchar(max)`. It is written and read whole. Should payload-internal querying
  become necessary later, SQL Server's JSON functions over computed, persisted, indexed columns are
  the migration path — no table restructuring required.
- Global query filters exclude `IsDeleted = 1` on `Page`, `MediaItem`, `ReusableContent`, and
  `MediaFolder`. Recycle-bin queries call `IgnoreQueryFilters()` explicitly.
- The existing `AddLogging()` audit interceptor writes an `AuditLog` row for every tracked change. It
  must be configured to **skip** `SearchDocument`, `OutboxMessage`, `MediaRendition`, `EditLock`, and
  `NotFoundLog`, which are high-churn derived data — otherwise the audit table grows without bound and
  `SaveChanges` slows measurably. This is a required change to existing code, not an addition.

---

## 24. Observability and operations

### 24.1 Telemetry

The solution already wires Serilog and OpenTelemetry through `ServiceDefaults`. The CMS adds:

**Metrics** (`Meter` name `ContentManagementSystem.Cms`)

| Metric | Type | Purpose |
|---|---|---|
| `cms.page.render.duration` | histogram | Tagged `template`, `cache_hit` |
| `cms.cache.hit_ratio` | counter pair | Output and content cache |
| `cms.publish.count` / `.duration` | counter / histogram | Tagged `result` |
| `cms.media.rendition.generated` | counter | Cold-generation rate |
| `cms.media.rendition.duration` | histogram | Encoding cost by format |
| `cms.route.resolution.miss` | counter | 404 rate |
| `cms.scheduler.lag` | gauge | Seconds between `RunOn` and actual execution |
| `cms.draft.autosave.count` | counter | Editor activity |

**Traces** — spans for route resolution, payload deserialization, reusable-content resolution, template
render, rendition generation, and publish (with the invalidation fan-out as child spans).

**Structured logs** — publish, unpublish, schedule, rollback, permission denial, sanitization
stripping (with what was removed), and template orphan detection. Never log payload contents at
`Information`.

### 24.2 Health checks

Extending the existing `/health`:

| Check | Fails when |
|---|---|
| `cms-database` | Cannot query `SiteSettings` |
| `cms-media-store` | `IMediaStore` write/read/delete round trip fails |
| `cms-templates` | Any `IsOrphaned` template has non-deleted pages |
| `cms-scheduler` | Scheduler lag exceeds 5 minutes |
| `cms-outbox` | Unprocessed messages older than 5 minutes |

### 24.3 Backup and recovery

- Database: point-in-time restore. RPO 5 minutes, RTO 1 hour.
- Media originals: geo-redundant blob storage with soft delete and versioning enabled. Renditions are
  **not** backed up — they are derived and regenerate on demand.
- Restore drill quarterly, including a media-store restore, because a database restored without its
  media is not a working site.

### 24.4 Background services

| Service | Cadence | Work |
|---|---|---|
| `PublishSchedulerService` | 30 s | Claim and execute due publish/unpublish jobs |
| `OutboxProcessorService` | 5 s | Dispatch cache invalidation and webhooks |
| `SearchIndexService` | on change + nightly reconcile | Maintain `SearchDocument` |
| `VersionRetentionService` | nightly | Prune per [§11.7](#117-retention) |
| `MediaMaintenanceService` | nightly | Orphan report, stale-rendition eviction, storage reconciliation |
| `LinkIntegrityService` | nightly | Broken internal reference report |
| `EditLockReaperService` | 1 min | Expire stale locks |

All are `IHostedService` implementations guarded by a distributed lock (a claimed row in
`ScheduledJob`) so they behave correctly under scale-out.

---

## 25. Non-functional requirements

| ID | Requirement | Target | Verification |
|---|---|---|---|
| NFR-1 | Cached public page TTFB | < 200 ms p95 | Load test |
| NFR-2 | Uncached public page TTFB | < 800 ms p95 | Load test |
| NFR-3 | Public page Lighthouse performance | ≥ 90 mobile | CI Lighthouse run |
| NFR-4 | Core Web Vitals | LCP < 2.5 s, CLS < 0.1, INP < 200 ms | Field + lab |
| NFR-5 | Backoffice editor load | < 3 s to interactive on a warm WASM cache | Playwright timing |
| NFR-6 | Autosave round trip | < 500 ms p95 | API test |
| NFR-7 | Publish (typical page) | < 2 s including invalidation | API test |
| NFR-8 | Rendition generation (cold, 4000 px source → 1280 px WebP) | < 800 ms p95 | Benchmark |
| NFR-9 | Scale | 50,000 pages, 100,000 media items, 200 concurrent editors, 5,000 rps public (cached) | Load test |
| NFR-10 | Availability | 99.9% monthly for public delivery | Uptime monitoring |
| NFR-11 | Public delivery survives a backoffice outage | Cached content continues to serve | Chaos test |
| NFR-12 | Accessibility | WCAG 2.2 AA, backoffice and public output | axe-core in CI + manual audit |
| NFR-13 | Browser support | Last 2 versions of Chrome, Edge, Firefox, Safari | BrowserStack matrix |
| NFR-14 | No data loss on concurrent edit | 100% conflict detection | Integration test |
| NFR-15 | Zero stored-XSS escapes | 0 findings | OWASP payload corpus in CI |

---

## 26. Testing strategy

| Layer | Tooling | Coverage focus |
|---|---|---|
| **Unit** | xUnit, FluentAssertions, NSubstitute | Slug generation, URL construction, redirect chain flattening and loop detection, payload validation, diff algorithm, ACL resolution, cache-tag derivation, focal-point crop math |
| **Contract** | Snapshot tests over serialized payloads | Payload envelope stability across schema versions |
| **Data integration** | Testcontainers SQL Server | Migrations apply cleanly and are reversible; filtered unique indexes behave; concurrency conflicts surface; query filters exclude deleted rows |
| **API integration** | `WebApplicationFactory` | Every endpoint's authorization, validation, and concurrency behavior; publish transactionality |
| **Rendering** | bUnit | Field renderers, block components, template composition, fallback behavior for unknown types |
| **Security** | Custom + OWASP corpus | XSS payload corpus against every sanitization profile; upload-type confusion; path traversal; IDOR probes across ACL boundaries; unsigned rendition URL rejection |
| **E2E** | Playwright | The full editor journey: create → edit → preview → submit → approve → publish → verify anonymous visibility → edit again → verify published unchanged → rollback |
| **Accessibility** | axe-core (Playwright) | Backoffice screens and rendered public output |
| **Performance** | k6 or NBomber | NFR-1/2/7/9 |
| **Visual regression** | Playwright screenshots | Template rendering across breakpoints |

### 26.1 Non-negotiable test scenarios

These encode the requirements' core promises and must exist as automated tests:

1. Publish page → anonymous request returns the published content.
2. Edit the draft after publishing → anonymous request **still returns the old published content**;
   an authenticated editor's preview returns the new content.
3. Publish again → anonymous request returns the new content; the prior version is `Archived` and
   restorable.
4. Change a published page's URL → the old URL 301s to the new one, for the page and every descendant.
5. Publish reusable content → every late-bound referencing page reflects it without being republished;
   pinned pages do not change.
6. Two editors save the same draft concurrently → the second receives `409`, never a silent overwrite.
7. An `Author` attempting to publish receives `403` and the content stays unpublished.
8. Every payload in the XSS corpus is neutralized in stored content and in rendered output.
9. Soft-deleting a page removes it from the public site but keeps it restorable with full history.
10. Deleting media that is still referenced is refused, with an accurate where-used list.

---

## 27. Environment promotion and content migration

Gap #27. **Structure and content are promoted differently, and conflating them is a common failure.**

### 27.1 Structure (templates, block types, zones, compositions)

Authored by developers in a lower environment and promoted deterministically:

- Templates and block types originate in code (`[CmsTemplate]`, `[CmsBlockType]`) and arrive with the
  deployment.
- Zone and property *definitions* are data, so they are exported to versioned JSON files in
  `src/ContentManagementSystem.Server/CmsSchema/*.json`, committed to source control, and applied at
  startup by `SchemaSyncService` in an idempotent, additive-only pass (never destructive — see
  [§8.5](#85-template-evolution-and-schema-safety)).
- A CLI verb (`dotnet run -- cms schema export|diff|apply`) supports the authoring loop and gives CI a
  drift check.

### 27.2 Content

Content is **not** promoted between environments as a routine practice. Production content is the
source of truth. Supported operations instead:

- **Down-sync:** a scripted restore of a production database copy into staging, with a scrubbing step
  that anonymizes user emails and revokes preview tokens.
- **Selective import/export (v2):** a page-subtree export bundle (payload JSON + referenced media +
  a manifest) with GUID-based identity so it can be imported into an environment where integer ids
  differ. This is why `Page.PublicId` and block `id`s are GUIDs.

### 27.3 Legacy site migration

For onboarding an existing site: a documented import pipeline that maps source content into payload
JSON, plus bulk redirect import from CSV ([§10.5](#105-redirects)), plus a post-migration report of
unresolved links and unmapped fields. The `NotFoundLog` ([§10.6](#106-404-handling)) is the primary
tool for catching what the migration missed once traffic arrives.

---

## 28. Accessibility

Gap #13 generalizes: a CMS is an accessibility force multiplier in both directions.

**Authored output**

- Alt text enforced at publish ([§13.7](#137-alt-text-policy)).
- Heading structure validated — the rich-text editor offers only `h2`–`h6` (the template owns `h1`),
  and a publish-time warning flags skipped levels.
- Link text validated: "click here" / "read more" / bare URLs raise warnings.
- Contrast: the `color` field type is constrained to design-system tokens with known-good contrast.
- Tables authored in rich text get header cells; the editor's table tool always emits `<th scope>`.
- Video block types require a captions track or an explicit "no dialogue" declaration.
- `lang` attribute set from `SiteSettings.Culture`.

**The backoffice itself** must meet WCAG 2.2 AA: full keyboard operability including block reordering
(explicit "move up/down" controls alongside drag), visible focus indicators, ARIA live regions for
autosave and validation announcements, no color-only status encoding (the tree's status dots carry
shape and text alternatives), respect for `prefers-reduced-motion`, and 200% zoom without loss of
function.

---

## 29. Decisions, open questions, and deferred scope

### 29.1 Decisions recorded

| # | Decision | Rationale |
|---|---|---|
| D1 | Hybrid JSON payload + relational reference projection | [§6.2](#62-the-central-storage-decision-json-payload--relational-projection) |
| D2 | Public site is static SSR; backoffice is interactive WASM | Cacheability and SEO vs. editing richness ([§5.3](#53-the-two-front-doors)) |
| D3 | Publish snapshots the draft; the draft survives | Directly implements "published stays live while editors work" |
| D4 | Reusable content is late-bound by default, pinnable by exception | Goal G4 with an audit escape hatch |
| D5 | Templates are developer-authored and revisioned; zone keys are immutable | Prevents silent content loss ([§8.5](#85-template-evolution-and-schema-safety)) |
| D6 | Internal links stored as `pageId`, never as URL text | Makes URL changes safe by construction |
| D7 | Non-destructive media editing with signed, lazily generated renditions | Recoverability plus DoS resistance |
| D8 | Sanitize on write **and** on render | Defense in depth against imports, direct DB writes, and profile changes |
| D9 | No locale dimension anywhere; `en-US` only | Confirmed never required; removes a column from four hot tables and a whole v2 workstream ([§19](#19-localization)) |
| D10 | Shared rendering RCL between delivery and preview | Preview fidelity as a structural property |
| D11 | SkiaSharp (MIT) behind an `IImageProcessor` abstraction; AVIF dropped from v1 | Avoids Six Labors commercial-licensing exposure; abstraction keeps AVIF recoverable ([§13.9](#139-image-library-selection)) |
| D12 | Advisory locks never block; `rowversion` is authoritative | Blocking locks get stuck and generate support load |

### 29.2 Open questions

#### Resolved

| # | Question | Resolution |
|---|---|---|
| Q1 | Will this deployment ever be multilingual? | **No — `en-US` only.** Locale removed from the model entirely; see [§19](#19-localization) and D9. Saves ~15 ed of v2 scope; reversing it would cost ~25–35 ed. |
| Q3 | Is a Six Labors commercial license required (closed-source, ≥ USD 1M revenue)? | **Avoided — SkiaSharp (MIT) selected.** Closed-source is expected and revenue is not established, which is exactly the case the Split License charges for. Consequence: **no AVIF output in v1**; see [§13.9.1](#1391-consequences-of-choosing-skiasharp). |

#### Still open

| # | Question | Owner | Needed by |
|---|---|---|---|
| Q2 | Expected content scale — hundreds of pages or tens of thousands? Affects tree UI, search backend, and caching topology. | Product | Phase 1 |
| Q4 | Single instance or scaled out? Determines whether Redis output cache is required at launch. | Ops | Phase 2 |
| Q5 | Which email provider replaces `IdentityNoOpEmailSender`? | Ops | Phase 1 |
| Q6 | Is a CDN in front of the site? Changes cache headers and adds a purge integration. | Ops | Phase 6 |
| Q7 | Is SVG upload permitted at all? The safest answer is no; sanitization is a mitigation, not a guarantee. | Security | Phase 5 |
| Q8 | Is there an existing site to migrate, and does its URL structure need preserving? | Product | Phase 3 |
| Q9 | Retention/compliance obligations on content versions and audit logs? | Legal | Phase 5 |
| Q10 | Does self-service registration stay enabled, and with what default role? | Security | Phase 1 |

### 29.3 Deferred scope

| Item | Target | Notes |
|---|---|---|
| In-context (on-page) editing | v2 | [§14.5](#145-in-context-editing-v2) |
| Public site search UI | v2 | Index built in v1 |
| Forms and lead capture | v2 | Needs spam, PII, and notification design |
| Headless read API + webhooks | v2 | `ContentReference` already supports the invalidation fan-out |
| Content import/export bundles | v2 | GUID identity already in the schema |
| Multi-site from one installation | v3 | Would require a `SiteId` discriminator throughout — assess before v2 locks the schema |
| Personalization / A/B testing | v3 | Conflicts with aggressive output caching; needs an edge strategy |
| Real-time collaborative editing | v3 | Requires CRDT or OT; substantial |
| A/B image and content variant testing | v3 | |
| Nested blocks beyond one level | v2 | Editor UX cost, not a storage limitation |
| Workflow configurable per template | v2 | v1 is per-site |

---

## Appendix A — Source references

Research informing this specification:

- [Umbraco — Block List editor](https://docs.umbraco.com/umbraco-cms/model-your-content/property-editors/built-in-umbraco-property-editors/block-editor/block-list-editor) — element types, layout/contentData JSON separation, content vs. settings models
- [Umbraco — Default document types](https://docs.umbraco.com/umbraco-cms/model-your-content/content-types-and-structure/data/defining-content/default-document-types) — compositions and content-type structure
- [Webiny — Content modeling best practices](https://www.webiny.com/docs/headless-cms/basics/content-modeling-best-practices) — modular components over page-shaped models, dynamic zones
- [Strapi — Headless CMS best practices](https://strapi.io/blog/headless-cms-for-business-best-practices-and-expert-tips) — reusable component architecture
- [Payload — Versions](https://payloadcms.com/docs/versions/overview) and [Directus — Content versioning](https://docs.directus.io/guides/headless-cms/content-versioning) — draft/published version models
- [Microsoft Learn — Output caching middleware](https://learn.microsoft.com/aspnet/core/performance/caching/output?view=aspnetcore-10.0) — tag eviction, Redis store, middleware ordering relative to auth
- [Microsoft Learn — Blazor render modes](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0) — static SSR vs. interactive, render mode propagation
- [mganss/HtmlSanitizer](https://github.com/mganss/HtmlSanitizer) and [XSS prevention in ASP.NET Core using HTML sanitization](https://www.pitsolutions.com/blog/xss-prevention-in-aspnet-core-using-html-sanitization-pit-solutions) — DOM-based sanitization, allowlist strategy
- [Six Labors — Licensing](https://sixlabors.com/pricing/) and [license enforcement changes](https://sixlabors.com/posts/licence-enforcement-changes/) — the USD 1M threshold and v4 build-time key
- [HubSpot — Essential CMS features](https://blog.hubspot.com/website/cms-features) and [SEO-friendly CMS checklist](https://tomcrowedigital.com/seo-friendly-cms-checklist) — redirects, sitemaps, metadata, scheduling, workflow
