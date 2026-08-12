# Architecture decision records

Each file records one decision: the context that forced it, the option taken, and what that option
costs. They are written so that someone arriving in six months can tell whether a decision still
holds, without reading `spec.md` end to end.

## Conventions

- One decision per file, named `NNNN-kebab-title.md`.
- `D1`–`D12` carry the identifiers used in [`spec.md` §29.1](../../spec.md) and the task list, so a
  reference like "D6" resolves to exactly one file here.
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
