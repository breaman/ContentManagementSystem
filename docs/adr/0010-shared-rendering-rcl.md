# 0010 — A shared rendering Razor Class Library for delivery and preview

- **Identifier:** D10
- **Status:** Accepted
- **Source:** [`spec.md` §5.2, §12](../../spec.md)

## Context

Preview exists so an editor can see what publishing will produce. A preview built from a second,
parallel rendering implementation drifts from the real one, and the drift is discovered by
publishing.

## Decision

Templates, block components, and field renderers live in `ContentManagementSystem.Rendering`, a Razor
Class Library. Public delivery and backoffice preview both render **the same components** from it.

## Consequences

- Preview fidelity is a structural property rather than a maintenance commitment. There is no second
  implementation that can drift.
- The rich-text preview pane goes through the same Markdig → sanitize → site typography pipeline the
  public site uses, so "preview matches published exactly" is testable as byte equality.
- Rendering components must work under static SSR, which constrains what they may assume — no
  interactive render mode on the delivery path.
- The RCL must stay browser-compatible, since preview surfaces are reachable from the WASM
  backoffice.
