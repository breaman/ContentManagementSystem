# Zone and block property keys — the contract nothing enforces

**Audience:** anyone defining zones or block-type properties in the backoffice, and anyone running
[`demo.md`](../demo.md).

**The short version:** a template or block type arrives in two halves that must agree on their keys,
and *nothing in the system checks that they do*. A property you add in the backoffice with a key the
deployed component never names is authored, saved, validated, published — and rendered as nothing,
silently.

---

## 1. The two halves

| Half | Where it lives | Who changes it | What it decides |
|---|---|---|---|
| **Markup** | A Razor component in `src/ContentManagementSystem.Rendering` | A developer, in code, with a deployment | Which keys are read out of the payload, and where they land on the page |
| **Definition** | `Zone` / `BlockTypeProperty` rows and the revision snapshots cut from them | A Developer, in **Structure**, with no deployment | What the editor screen draws, what the field type validates, what blocks publishing |

The definition half is data on purpose — it is the whole argument of Act 1 of the demo, and of
[ADR-0005](./adr/0005-templates-developer-authored-zone-keys-immutable.md): a marketing team gets a
new field next week rather than next quarter.

The markup half is code, and it names its keys **literally**:

```razor
@* src/ContentManagementSystem.Rendering/Blocks/RichTextSection.razor *@
<CmsBlockProperty Name="body" />
<CmsBlockProperty Name="alignment" />
<CmsBlockProperty Name="embed" />
<CmsBlockProperty Name="settings" />
```

`BlocksRenderer` hands the component the block's whole `properties` JSON object
(`BlockParameters.cs`); the component decides what to pull out of it. A key it does not name is
never asked for. `CmsZone` and template zone keys work exactly the same way.

**There is no reconciliation between the halves.** No startup check, no health check, no publish
warning, no log line. This is the single most surprising thing about the content model, and it is
the cause of both symptoms below.

---

## 2. The two symptoms

### 2.1 "The revision this block was written against declared no properties."

Shown by `BlockCard` when the block type's captured revision has zero slots. The block type row was
created by the startup reconciler from a `[CmsBlockType]` attribute, and **the attribute carries no
properties** — key, name, description, icon, and summary template only. So the row is created with
revision 1 and an empty snapshot:

```csharp
// TemplateReconciler.cs
fresh.Revisions.Add(new BlockTypeRevision
{
    RevisionNumber = 1,
    PropertySnapshotJson = ContentSchemaSnapshot.WriteSlots([]),   // ← empty, always
    Notes = $"Created from code by {declaration.ComponentTypeName}.",
});
```

Templates get the same treatment — a code-declared template arrives with revision 1 and zero zones,
which is why the demo's Act 1 exists.

The one exception is **`rawHtml`**, which is *seeded* rather than reconciled (`CmsSeedData`) and so
arrives with its `content` property already defined. It is the only block type that works out of the
box, and that is why every reusable-content fixture uses it.

**What would normally fill the gap** is a `src/ContentManagementSystem.Server/CmsSchema/*.json` file
applied at startup by `SchemaSyncService` (spec §27.1). **This repository has none** — see §5.

### 2.2 The editor takes the value and the page renders nothing

You added the property, the editor drew a control, you typed into it, the draft saved, the publish
succeeded — and the public page and the preview show nothing.

The key is not in the component's markup. Nothing is wrong; nothing was asked for.

This produces **no log line**, which is what makes it hard: `CmsBlockProperty` only logs about keys
it was asked to render (§4). A key nobody asked for cannot be reported by the thing that would have
reported it.

---

## 3. Reference — every key the deployed components name

Field types are what the reference markup *assumes*, not what it enforces: the renderer is chosen
from the stored value's own `type` discriminator, so a mismatched field type renders through the
wrong renderer rather than being refused.

### 3.1 Templates

| Template | Zone keys |
|---|---|
| `marketing-landing` | `hero`, `intro`, `body`, `accent`, `cta`, `footer` |
| `article` | `kicker`, `standfirst`, `publishedAt`, `reviewedOn`, `readingMinutes`, `isFeatured`, `layout`, `poster`, `body`, `embed`, `gallery`, `tags`, `related`, `analytics` |

The `article` `<h1>` is the page's own title, not a zone — a title is a property of the version
(spec §11.1), not content an editor places.

### 3.2 Block types

**`rich-text`** — `RichTextSection.razor`

| Key | Assumed field type | Notes |
|---|---|---|
| `body` | `richText` | The prose. This is the one people mean when they "add a rich text property". |
| `alignment` | `choice` | Rendered as the chosen option's text |
| `embed` | `html` | Hand-written HTML beneath the prose |
| `settings` | `json` | **Renders nothing by design** — developer-only data for the markup to read |

**`hero-banner`** — `HeroBanner.razor`

| Key | Assumed field type | Notes |
|---|---|---|
| `headline` | `plainText` | Read through `@Text("headline")`, so it needs a string-valued type |
| `background` | `color` | |
| `image` | `media` | |
| `standfirst` | `multilineText` | Emitted inside a `<p>` |
| `cta` | `link` | Emitted inside a `<p>` |
| `isFullBleed` | `boolean` | |

**`feature-grid`** — `FeatureGrid.razor`

| Key | Assumed field type | Notes |
|---|---|---|
| `heading` | `plainText` | Read through `@Text("heading")` |
| `columns` | `number` | |
| `publishedOn` | `date` | |
| `updatedAt` | `dateTime` | |
| `items` | `blocks` | Nested blocks; use `allowedBlockTypes` to bound them |
| `gallery` | `mediaList` | |
| `tags` | `tags` | |
| `related` | `pageReference` | |
| `promo` | `reusable` | |

**`rawHtml`** — `RawHtmlBlock.razor`

| Key | Field type | Notes |
|---|---|---|
| `content` | `html` | **Seeded, already defined.** Required. |

---

## 4. What logs, and what does not

| Situation | Logged? |
|---|---|
| Key defined in the backoffice, absent from the markup | **No.** Nothing asks for it |
| Key in the markup, absent from the payload | No — "never authored" and "deliberately cleared" are both ordinary (spec §15.3) |
| Stored value carries no `type` discriminator | Warning, from `CmsZone` / `CmsBlockProperty` |
| No renderer registered for the field type the value names | Warning, from `CmsZone` / `CmsBlockProperty` |
| Payload names a block type no component declares | Warning, from `BlocksRenderer` |
| A `json` property or zone | **No.** It renders nothing on purpose |

So: if you are debugging a property that will not appear and the log is silent, the key is the
problem. If the log has a warning, the field type or the deployment is the problem.

---

## 5. Adding a property that actually renders

### Option A — use a key the markup already names

Pick from the tables in §3. Nothing else is needed; the property renders where the component puts
it.

### Option B — add the key to the markup

One line in the block component, then rebuild and redeploy:

```razor
<CmsBlockProperty Name="yourKey" />
```

The renderer is resolved from the stored value, so no other change is required. Text-shaped
properties can also be read with `@Text("yourKey")` when the markup owns the element around them.

### Either way: existing blocks stay on their old revision

A block instance captures `blockTypeRevision` when it is inserted
(`StoredBlock.Create(type.Key, type.CurrentRevision)`) and is drawn from **that** revision forever —
which is the point of [ADR-0017](./adr/0017-revisions-cut-only-when-content-is-read-differently.md)
and spec §8.5, not a defect. Adding a property cuts a new revision and bumps `CurrentRevision`, so:

- **new** blocks pick the property up immediately;
- **existing** blocks keep showing "declared no properties" until they are deleted and re-added.

### The durable fix — a schema file

Rather than clicking properties in for every fresh database, commit them.
`src/ContentManagementSystem.Server/CmsSchema/rich-text.json`:

```json
{
  "kind": "BlockType",
  "key": "rich-text",
  "name": "Rich Text",
  "iconKey": "bi-text-paragraph",
  "summaryTemplate": "{body}",
  "slots": [
    { "key": "body", "name": "Body", "fieldTypeKey": "richText", "isRequired": true, "sortOrder": 10 },
    { "key": "alignment", "name": "Alignment", "fieldTypeKey": "choice", "sortOrder": 20,
      "configuration": { "options": ["left", "center", "right"] } },
    { "key": "embed", "name": "Embed", "fieldTypeKey": "html", "sortOrder": 30 },
    { "key": "settings", "name": "Settings", "fieldTypeKey": "json", "sortOrder": 40 }
  ]
}
```

`SchemaSyncService` applies these at startup, additively and non-destructively
([ADR-0019](./adr/0019-schema-sync-is-additive-and-non-destructive.md)), so a fresh database comes
up with the properties already there. `dotnet run -- cms schema export|diff|apply` supports the
loop, and `diff` gives CI a drift check.

> `options` is a real setting on the `choice` field type (alongside `multiple`, `min`, and `max`).
> When you write configuration for any other field type, check its declared settings first — one it
> does not declare is refused on save rather than ignored
> ([ADR-0015](./adr/0015-field-configuration-declared-in-code-json-schema-generated.md)), and the
> **Settings:** line under *Configuration (JSON)* on the property form lists them.

---

## 6. Checklist before authoring against a new block type

1. Does the block type have any properties at all? (**Structure → Block types →** the type.) A
   code-declared one starts with none.
2. Does every key match one the component names? (§3, or `grep -o 'CmsBlockProperty Name="[^"]*"'`
   over the component.)
3. Is the field type the one that key's markup context expects? (§3.)
4. If you defined the properties after adding blocks to a page, delete and re-add those blocks.

---

## 7. See also

- [ADR-0005](./adr/0005-templates-developer-authored-zone-keys-immutable.md) — why keys are
  developer-authored and immutable
- [ADR-0014](./adr/0014-field-type-components-resolved-by-the-hosting-layer.md) — how a field type
  becomes a renderer and an editor
- [ADR-0017](./adr/0017-revisions-cut-only-when-content-is-read-differently.md) — when a revision is
  cut
- [ADR-0019](./adr/0019-schema-sync-is-additive-and-non-destructive.md) — what `CmsSchema/*.json`
  will and will not do
- spec §8.5 (template evolution), §15.3 (degraded rendering), §27.1 (structure promotion)
