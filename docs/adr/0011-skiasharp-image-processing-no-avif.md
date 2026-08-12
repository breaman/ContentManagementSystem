# 0011 — SkiaSharp behind `IImageProcessor`; AVIF dropped from v1

- **Identifier:** D11
- **Status:** Accepted
- **Source:** [`spec.md` §13.9](../../spec.md), open question Q3 (resolved)

## Context

ImageSharp (Six Labors) is the obvious .NET choice, but its Split License charges closed-source
products above a revenue threshold. This product is expected to be closed-source, and revenue is not
established — which is precisely the case that license is written for. Committing to it would mean
either a licence purchase or a rewrite later.

## Decision

Use **SkiaSharp** (MIT) as the sole v1 implementation, behind an `IImageProcessor` abstraction.

SkiaSharp does not encode AVIF, so **AVIF is not produced in v1**. Renditions are WebP plus the
original format. AVIF *uploads* are rejected as well.

## Consequences

- No licensing exposure, and the abstraction keeps AVIF recoverable: a future processor that encodes
  AVIF slots in behind the same interface.
- `IImageProcessor` exposes a `SupportedOutputFormats` capability set that is **asserted at
  startup**, so an unsupported encode fails loudly at boot rather than returning null to a visitor
  mid-request (task P5-08/P5-09).
- A request for AVIF is rejected at the spec-parsing layer of the rendition endpoint. It must never
  fall through to an empty response.
- `Accept`-based negotiation offers WebP with `Vary: Accept`; browsers that prefer AVIF fall back to
  WebP normally.
- EXIF orientation is read with MetadataExtractor, with `SKCodec.EncodedOrigin` as a fallback, baked
  into pixels, and then **all** metadata is stripped — GPS coordinates in a published photo are a
  privacy incident, not a cosmetic issue.
