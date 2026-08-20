# Content Management System

A CMS built on ASP.NET Core 10: a **statically rendered public site** for readers and an
**interactive WebAssembly backoffice** for editors, from one codebase and one set of rendering
components ([ADR 0002](docs/adr/0002-static-ssr-public-interactive-wasm-backoffice.md),
[ADR 0010](docs/adr/0010-shared-rendering-rcl.md)).

| Document | What it is for |
|---|---|
| [`spec.md`](spec.md) | What the system does, and why |
| [`plan.md`](plan.md) | Phases, sequencing, estimates |
| [`task.md`](task.md) | The working checklist, edited as work is performed |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Conventions, and the failure each one prevents |
| [`docs/adr/`](docs/adr/) | Decisions, including what they cost |
| [`docs/operations.md`](docs/operations.md) | Deploying it, watching it, being woken by it |
| [`docs/guides/`](docs/guides/) | Editor, template-authoring, and administrator guides |
| [`docs/load-testing.md`](docs/load-testing.md) | The dataset the NFR-9 load tests run against, and what it does not represent |
| [`loadtests/`](loadtests/) | k6 scripts for NFR-1, NFR-2, and NFR-9 |
| [`lighthouse/`](lighthouse/) | Core Web Vitals and the mobile performance score, NFR-3 and NFR-4 |

---

## Running it

```bash
dotnet tool restore                                      # dotnet-ef is a local tool
cd src/ContentManagementSystem.Server && npm install      # front-end toolchain
npm run build                                             # stylesheet and editor bundles
cd ../.. && aspire run                                    # SQL Server + Azurite + the app
```

`aspire run` starts SQL Server, Azurite (standing in for blob storage), and the application, and
applies migrations on the way up. Integration tests need Docker; see
[`docs/phase-0-baseline.md`](docs/phase-0-baseline.md) for what the stack starts and for the RZ1021
build-server issue you will eventually hit.

### The front-end build is not optional

`site.css`, the CodeMirror and Quill bundles, Bootstrap's JavaScript, and the icon font all come out
of `node_modules`, and none of them is checked in. `dotnet build` runs the bundle step and copies the
rest, so a missing bundle fails the build rather than the page — which is the point
([ADR 0013](docs/adr/0013-backoffice-editor-bundle-and-style-nonce.md)). During development,
`npm run sass-dev` watches the stylesheet.

---

## How content is modelled

Five things, and the distinction between the first two is the one worth learning:

- **Templates** are developer-authored components with named **zones**. A zone declares a field type;
  what an editor puts in it is content. Zone keys are immutable, which is what stops a rename from
  silently discarding what was written there
  ([ADR 0005](docs/adr/0005-templates-developer-authored-zone-keys-immutable.md)).
- **Block types** are the same idea one level down: a component with named properties, placed many
  times inside a `blocks` zone.
- **Pages** are a position in a tree, a template, and a stack of **versions**. Publishing snapshots
  the draft; the draft survives and keeps being editable
  ([ADR 0003](docs/adr/0003-publish-snapshots-the-draft.md)).
- **Reusable content** is a block authored once and placed anywhere, late-bound by default so editing
  it updates every page showing it
  ([ADR 0004](docs/adr/0004-reusable-content-late-bound-by-default.md)).
- **Media** lives in a library, is edited non-destructively, and is served through signed URLs
  ([ADR 0007](docs/adr/0007-non-destructive-media-editing-signed-renditions.md)).

Content is stored as a **JSON payload** with a relational projection of its references beside it — one
read for a page, and still a queryable answer to "what links to this"
([ADR 0001](docs/adr/0001-hybrid-json-payload-with-relational-projection.md)).

---

## Authoring a template

A template is a Razor component in `ContentManagementSystem.Rendering` carrying `[CmsTemplate]` and
inheriting `CmsTemplateBase`. Zones are `<CmsZone Name="…" />`; the component never reads a payload
itself.

```razor
@attribute [CmsTemplate("article", "Article", Description = "A long-form editorial page.")]
@inherits CmsTemplateBase

<article class="cms-page" data-template="article">
    <h1>@Page.Title</h1>
    <CmsZone Name="standfirst" />
    <CmsZone Name="body" />
</article>
```

Three rules that are easy to get wrong:

1. **The `h1` is the page's title, not a zone.** A title is a property of the version, and the
   rich-text profile has no `h1` in it at all — which is what makes an authored `h2` the right level
   rather than a convention somebody has to remember.
2. **Zone keys are matched byte for byte** against stored payloads. Renaming one orphans its content.
3. **The site's navigation belongs to the shell**, not to a template. A template that had to render it
   is a template that can forget to.

A block type is the same shape with `[CmsBlockType]`, and its properties are declared rather than
placed. See [`docs/guides/template-authoring.md`](docs/guides/template-authoring.md) for the field
types, their configuration, and how a revision is cut.

---

## The schema sync CLI

Templates and block types are declared in code, so a deployment has to get them into the database.
Startup reconciliation does that automatically for what the assemblies declare; the CLI is for moving
the *editorial* structure — zones, properties, and their configuration — between environments as
files.

```bash
cd src/ContentManagementSystem.Server

dotnet run -- cms schema export ./structure   # write the database's structure out as files
dotnet run -- cms schema diff   ./structure   # what applying them would change
dotnet run -- cms schema apply  ./structure   # apply them
```

`diff` exits non-zero when anything would change, which is what makes it usable as a deployment gate:
run it against production, read what it says, then `apply`.

**The sync is additive and non-destructive.** It creates and widens; it refuses rather than dropping
anything that content might be stored under, and says what it refused and why
([ADR 0019](docs/adr/0019-schema-sync-is-additive-and-non-destructive.md)). Removing a zone is a
deliberate act performed with a migration, not something a deployment does on your behalf.

---

## Migrations

```bash
cd src/ContentManagementSystem.Server
dotnet ef migrations add <Name> -p ../ContentManagementSystem.Data
```

They are numbered in [`task.md`](task.md) and must be added in that order. **Review the generated
migration before committing it** — `CONTRIBUTING.md` lists what to look for, and every item on that
list is something that has destroyed data in some CMS somewhere. `Up` and `Down` are both tested in
CI against a real SQL Server container, from empty, on every build.

---

## Security

The rules that are not negotiable are in [`CONTRIBUTING.md`](CONTRIBUTING.md#security-rules-that-are-not-negotiable).
The three worth knowing before writing anything:

- **HTML is sanitized on write and on render**
  ([ADR 0008](docs/adr/0008-sanitize-on-write-and-on-render.md)). Widening a profile is a security
  change and fails the XSS corpus gate, which restates its forbidden list deliberately so a widening
  cannot make the suite agree with itself.
- **Authorization is enforced in the service layer**, on the id the caller supplied — never only at
  the endpoint and never in the client.
- **A strict Content Security Policy is on**, in Development too, with three profiles and the public
  one carrying no nonce
  ([ADR 0026](docs/adr/0026-three-content-security-policies-public-carries-no-nonce.md)). If something
  you added does not load, that is the policy telling you so, and the fix is usually to stop doing
  the thing rather than to widen it.
