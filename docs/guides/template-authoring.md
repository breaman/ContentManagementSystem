# Template authoring guide

**Task:** `P9-21`. For developers adding templates, block types, and the components that render them.

A template is a Razor component. There is no separate template language, no configuration file, and
no admin screen that creates one — a template exists because a component with `[CmsTemplate]` was
deployed, and it stops existing when that component is removed.

---

## A template

Lives in `ContentManagementSystem.Rendering/Templates/`.

```razor
@attribute [CmsTemplate("article", "Article",
    Description = "A long-form editorial page.",
    SortOrder = 20)]
@inherits CmsTemplateBase

<article class="cms-page cms-article" data-template="article" data-page="@Page.PublicId">
    <header>
        <h1>@Page.Title</h1>
        <CmsZone Name="standfirst" />
    </header>

    <div class="cms-article-body">
        <CmsZone Name="body" />
    </div>
</article>
```

- **`key`** (`"article"`) is the contract. It is stored in every payload authored against this
  template and is matched byte for byte. Changing it orphans every page using it.
- **`name`** is what an editor picks from a list, and may be reworded freely.
- **`CmsZone Name="…"`** places a zone. The component never reads a payload: which renderer fills the
  zone is decided by the field type of the value stored there, not by the template
  ([ADR 0014](../adr/0014-field-type-components-resolved-by-the-hosting-layer.md)).
- **`Page`** comes from `CmsTemplateBase` and carries the version's own metadata — title, public id,
  URL, dates.

### Three rules that are easy to get wrong

1. **The `h1` is `@Page.Title`, never a zone.** A title is a property of the version
   ([§11.1](../../spec.md#111-page)), and the rich-text profiles have no `h1` at all — which is what
   makes an authored `h2` the correct level rather than a convention somebody has to remember.
2. **Never render the site's navigation.** It belongs to the shell, so that unpublishing a page
   removes it from the menu on every *other* page. A template that had to render it is a template
   that can forget to.
3. **Nothing may stream.** Cache tags accumulate while the body renders, and a response whose headers
   went out first carries an incomplete set — producing a page that never invalidates. Delivery
   renders the whole document to a string for exactly this reason; a `[StreamRendering]` attribute
   anywhere beneath a template would break it silently.

---

## Zones

A zone is a row in the database, not an attribute. The startup reconciliation creates a template it
finds declared; **the zones on it are editorial structure** and are managed either through the
backoffice or as files through the schema sync CLI:

```bash
dotnet run -- cms schema export ./structure
# edit, review
dotnet run -- cms schema diff ./structure     # non-zero if anything would change
dotnet run -- cms schema apply ./structure
```

Each zone declares a **field type key**, an optional **configuration** document, whether it is
required, and where it sits in the editor's ordering and grouping.

**Zone keys are immutable.** The sync refuses to remove or retype one rather than doing it
([ADR 0019](../adr/0019-schema-sync-is-additive-and-non-destructive.md)), because the alternative is
a deployment that silently discards what editors wrote there. Removing a zone for real is a
deliberate act with a migration behind it.

---

## The field types

Eighteen built in. The **key** is what a payload stores; the **configuration** column lists the
settings a zone may set, and configuration is closed — an undeclared setting is refused on save
([ADR 0015](../adr/0015-field-configuration-declared-in-code-json-schema-generated.md)).

| Key | Holds | Notable configuration |
|---|---|---|
| `plainText` | One line of text | `maxLength`, `softLimit`, `pattern` |
| `multilineText` | Several lines, no markup | `maxLength`, `softLimit` |
| `richText` | Formatted prose, markdown or HTML | `profile` (`basic`/`extended`), `maxLength` |
| `html` | Raw HTML, widest profile | `Developer` role only |
| `number` | A number | `minimum`, `maximum`, `decimals` |
| `boolean` | A toggle | — |
| `date` / `dateTime` | An instant | `minimum`, `maximum` |
| `choice` | One or more of a fixed list | `options`, `multiple` |
| `color` | `#RRGGBB` | `palette` — the design system's tokens |
| `json` | Configuration for the markup around it | renders nothing, by design |
| `media` / `mediaList` | Pictures from the library | `allowedTypes`, `minWidth`, aspect ratio |
| `link` | An internal or external link | `allowExternal` |
| `pageReference` | Another page | `allowedTemplates` |
| `reusable` | A placement of reusable content | `allowedTypes` |
| `blocks` | An ordered list of blocks | `allowedBlockTypes`, `min`, `max` |
| `tags` | Page metadata, not payload | — |

Two of these are worth a sentence each. **`color` takes a palette** and refuses anything outside it —
that is what stops a brand refresh from having to hunt down one-off colours typed into pages over two
years, and it is where the known-good contrast of [§28](../../spec.md#28-accessibility) comes from.
**`tags` are page metadata** rather than payload; a tag removed in the properties panel stays removed.

### Adding a field type

Implement `IFieldType` (usually via `FieldTypeBase`), register it, and give it an editor component in
the catalog. Three things it must get right:

- **Its key**, which is stored in every value it writes and can never change.
- **Its capabilities.** `Searchable` puts its text in the index; `Sanitizable` means the value is
  markup an author wrote — which also enrols it in the publish-time accessibility checks, without
  anybody adding it to a list.
- **Its storage shape.** The value is a whole JSON envelope and the field type owns it. Nothing else
  should know what is inside.

---

## Block types

The same idea one level down: a component placed many times inside a `blocks` zone.

```razor
@attribute [CmsBlockType("hero-banner", "Hero banner",
    Description = "A full-width image with a heading and one call to action.",
    IconKey = "image",
    SummaryTemplate = "{heading}")]
@inherits CmsBlockBase

<section class="cms-hero">
    <CmsBlockProperty Name="heading" />
    <CmsBlockProperty Name="image" />
</section>
```

`SummaryTemplate` is what an editor sees on the collapsed card in the block list, with `{property}`
substituted. Getting it right is the difference between a list of nine cards reading "Hero banner"
and a list they can navigate.

**A block type's properties are revisioned.** When a change means existing content would be *read*
differently, a new revision is cut and the payloads that were authored against the old one keep being
read by it ([ADR 0017](../adr/0017-revisions-cut-only-when-content-is-read-differently.md)). A change
that only affects presentation cuts nothing.

---

## Field renderers

A renderer decides how one field type's stored value becomes markup, and lives in
`Rendering/Fields/`. The catalog maps a field type key to a renderer; the same catalog serves public
delivery and backoffice preview, which is what makes preview faithful by construction rather than by
diligence ([ADR 0010](../adr/0010-shared-rendering-rcl.md)).

The one thing a renderer must never do is put an authored string into the document without going
through the sanitization pipeline. Content is sanitized on write *and* on render
([ADR 0008](../adr/0008-sanitize-on-write-and-on-render.md)), and a renderer reaching for
`MarkupString` on a raw stored value defeats both. `LiveXssTests` runs the whole XSS corpus through
delivery for exactly this reason.

---

## Markdown extensions

Markdig extensions are enabled **one at a time, and only alongside the sanitization profile change
that carries what they emit** ([ADR 0016](../adr/0016-markdown-extensions-bounded-by-the-sanitization-allowlist.md)).
Enabling an extension whose output the profile strips produces markdown that silently renders as
nothing — which looks like an editor's mistake and is not.

---

## Checklist for a new template

- [ ] `[CmsTemplate]` with a key you are prepared to keep forever
- [ ] `h1` is `@Page.Title`
- [ ] No navigation, no `@rendermode`, no `[StreamRendering]`
- [ ] Zones declared through the schema sync, with configuration
- [ ] Rendered output passes the accessibility gate — landmarks, one `h1`, headings in order
- [ ] `cms schema diff` clean on the target before you deploy
