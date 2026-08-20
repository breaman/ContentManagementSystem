# Lighthouse

Core Web Vitals and the mobile performance score, against the public templates (task `P9-15`,
[§25](../spec.md#25-non-functional-requirements) — NFR-3 and NFR-4).

| Requirement | Assertion in `lighthouserc.json` |
|---|---|
| **NFR-3** — Lighthouse performance ≥ 90 mobile | `categories:performance` ≥ 0.9 |
| **NFR-4** — LCP < 2.5 s | `largest-contentful-paint` < 2500 ms |
| **NFR-4** — CLS < 0.1 | `cumulative-layout-shift` < 0.1 |
| **NFR-4** — INP < 200 ms | `total-blocking-time` < 200 ms — see below |
| **NFR-12**, carried along | `categories:accessibility` = 1 |
| SEO basics | `document-title`, `meta-description`, `crawlable-anchors`, `http-status-code` |

## Running it

```bash
# 1. a site with content in it (see docs/load-testing.md)
cd src/ContentManagementSystem.Server && dotnet run -- cms seed load --pages 2000

# 2. the templates, three runs each, median reported
cd ../../lighthouse
BASE_URL=http://localhost:5080 ./run.sh

# or name the pages yourself
BASE_URL=https://staging.example.com ./run.sh /about /news/2026/launch
```

Lighthouse is not a dependency of this repository — `run.sh` fetches `@lhci/cli` with `npx` and
finds a Chrome, preferring an installed one and falling back to the browser Playwright downloaded
for the E2E suite. Reports land in `lighthouse/.lighthouseci/` and a breached assertion exits
non-zero, so this works as a build step rather than only as something to read.

## Two honest caveats

- **INP cannot be measured in a lab run.** It is an interaction metric and there is no interaction
  in a Lighthouse run, so the assertion here is on **total blocking time**, which is the lab proxy
  Google publishes for it. A TBT under 200 ms is evidence for NFR-4's INP figure, not a measurement
  of it. The measurement is field data from real visitors, which is why NFR-4's verification column
  says "field + lab".
- **The default URLs are the seeded dataset's.** They are three shapes rather than three pages: the
  root and a section are `marketing-landing`, the topic is an `article`, and between them they
  exercise every field renderer and both block-driven bodies. A real site's own pages are better
  input; pass them as arguments.

## What is switched off, and why

- `uses-responsive-images` — the rendition pipeline emits a `srcset` the audit does not always
  credit on emulated mobile, and the responsive-image behaviour has its own tests (`P5-27`).
- `uses-long-cache-ttl` — cache lifetimes here are a deployment and CDN decision (**Q6**), not a
  property of the application.
- **The SEO category as a whole**, in favour of the four audits above. **Outside Production the site
  serves `Disallow: /` on purpose** so a staging deployment is never indexed (`RobotsEndpoint`), and
  Lighthouse reads that as "page is blocked from indexing" — which holds the category at 0.69 on
  every run of every non-production environment. A warning that is always there is a warning nobody
  reads. What the category would have caught is either asserted audit by audit here or covered by
  `SeoTests`.

Neither is a performance excuse: both are asserted elsewhere or belong to configuration this
repository does not own.
