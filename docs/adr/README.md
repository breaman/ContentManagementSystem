# Architecture decision records

Each file records one decision: the context that forced it, the option taken, and what that option
costs. They are written so that someone arriving in six months can tell whether a decision still
holds, without reading `spec.md` end to end.

## Conventions

- One decision per file, named `NNNN-kebab-title.md`.
- `D1`–`D12` carry the identifiers used in [`spec.md` §29.1](../../spec.md) and the task list, so a
  reference like "D6" resolves to exactly one file here. `D13` onwards are decisions taken after the
  spec was written; each names its source.
- Status is one of **Accepted**, **Superseded by `NNNN`**, or **Proposed**. Records are never
  deleted or edited into something else — a reversal is a new record that supersedes the old one.
- A spike that returns no-go must produce a record capturing the agreed fallback (task P0-06).

## Index

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-hybrid-json-payload-with-relational-projection.md) | D1 — Hybrid JSON payload plus relational reference projection | Accepted |
| [0002](0002-static-ssr-public-interactive-wasm-backoffice.md) | D2 — Static SSR public site, interactive WASM backoffice | Accepted |
| [0003](0003-publish-snapshots-the-draft.md) | D3 — Publish snapshots the draft; the draft survives | Accepted |
| [0004](0004-reusable-content-late-bound-by-default.md) | D4 — Reusable content is late-bound by default, pinnable by exception | Accepted |
| [0005](0005-templates-developer-authored-zone-keys-immutable.md) | D5 — Developer-authored revisioned templates; zone keys immutable | Accepted |
| [0006](0006-internal-links-stored-as-page-id.md) | D6 — Internal links stored as `pageId`, never as URL text | Accepted |
| [0007](0007-non-destructive-media-editing-signed-renditions.md) | D7 — Non-destructive media editing with signed, lazy renditions | Accepted |
| [0008](0008-sanitize-on-write-and-on-render.md) | D8 — Sanitize on write **and** on render | Accepted |
| [0009](0009-no-locale-dimension.md) | D9 — No locale dimension anywhere; `en-US` only | Accepted |
| [0010](0010-shared-rendering-rcl.md) | D10 — Shared rendering RCL between delivery and preview | Accepted |
| [0011](0011-skiasharp-image-processing-no-avif.md) | D11 — SkiaSharp behind `IImageProcessor`; AVIF dropped from v1 | Accepted |
| [0012](0012-advisory-locks-never-block.md) | D12 — Advisory locks never block; `rowversion` is authoritative | Accepted |
| [0013](0013-backoffice-editor-bundle-and-style-nonce.md) | D13 — Locally bundled editors; per-request style nonce for the backoffice | Accepted |
| [0014](0014-field-type-components-resolved-by-the-hosting-layer.md) | D14 — Field type components resolved by the hosting layer, not declared by the field type | Accepted |
| [0015](0015-field-configuration-declared-in-code-json-schema-generated.md) | D15 — Field configuration declared in code; the JSON Schema generated from it | Accepted |
| [0016](0016-markdown-extensions-bounded-by-the-sanitization-allowlist.md) | D16 — Markdown extensions are bounded by the sanitization allowlist | Accepted |
| [0017](0017-revisions-cut-only-when-content-is-read-differently.md) | D17 — A revision is cut only when content would be read differently | Accepted |
| [0018](0018-compositions-flattened-into-block-type-revisions.md) | D18 — Compositions flattened into block type revisions; editing one recuts every host | Accepted |
| [0019](0019-schema-sync-is-additive-and-non-destructive.md) | D19 — The schema sync is additive and non-destructive; it refuses rather than applies | Accepted |
| [0020](0020-catch-all-route-ordering-and-reserved-prefixes.md) | D20 — The content catch-all is mapped last; reserved prefixes are refused at both ends | Accepted |
| [0021](0021-reusable-content-stored-as-a-payload-envelope.md) | D21 — A reusable item's content is an ordinary payload envelope over its block type's properties | Accepted |
| [0022](0022-pre-render-shims-serialize-their-service-calls.md) | D22 — One request's `DbContext` is used once at a time: the pre-render shims serialize, the delivery readers open their own | Accepted |
| [0023](0023-one-allow-rule-makes-a-permission-an-allowlist.md) | D23 — One allow rule turns a permission into an allowlist for that principal | Accepted |
| [0024](0024-mail-is-smtp-configuration-not-a-provider-choice.md) | D24 — Mail is SMTP configuration, not a provider chosen in code | Accepted |
