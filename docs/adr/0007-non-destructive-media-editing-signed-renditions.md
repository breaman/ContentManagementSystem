# 0007 — Non-destructive media editing with signed, lazily generated renditions

- **Identifier:** D7
- **Status:** Accepted
- **Source:** [`spec.md` §13.4, §13.5](../../spec.md)

## Context

Editors crop and rotate images and change their minds. Separately, an image endpoint that accepts
arbitrary dimensions is a denial-of-service amplifier: one attacker can force unbounded encodes.

## Decision

**Editing is non-destructive.** Original bytes are never modified. Edits are stored as data on
`MediaItem.EditsJson` with an `EditsVersion` counter, at library scope (affects every usage) or
usage scope (affects one placement). Revert-to-original is always available.

**Renditions are signed and lazy.** `GET /media/{id}/{w}x{h}/{mode}/{name}.{ext}` validates an
HMAC-SHA256 signature over the normalised parameter set, restricted to an allowlist of widths
(320, 640, 960, 1280, 1920, 2560) and modes (`crop`, `contain`, `cover`, `pad`). Renditions are
generated on first request behind a per-key semaphore and persisted.

## Consequences

- An unsigned or tampered URL is refused, so the encode surface is exactly what the application
  itself linked to.
- `EditsVersion` is folded into the signature, so a library-level edit changes every rendition URL
  and thereby busts client and CDN caches without an explicit purge.
- Renditions are derived data: they are not backed up, and they regenerate on demand.
- The per-key semaphore is what keeps twenty concurrent cold requests from producing twenty encodes.
- The signing key must be rotatable with a grace period during which the previous key still
  validates, or rotation breaks every cached page's image URLs at once.
