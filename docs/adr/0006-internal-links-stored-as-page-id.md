# 0006 — Internal links are stored as `pageId`, never as URL text

- **Identifier:** D6
- **Status:** Accepted
- **Source:** [`spec.md` §7.1, §10.4](../../spec.md)

## Context

Pages move and get renamed. A CMS that stores internal links as URL strings accumulates broken links
every time an editor reorganises the tree, and nothing detects it until a visitor hits a 404.

## Decision

`link` and `pageReference` fields store the target's `pageId`. The current URL is resolved at render
time. A URL is never written into a payload for an internal target.

External links are stored as URLs, because there is nothing else to store.

## Consequences

- Moving or renaming a page updates every link to it automatically, with no rewrite pass.
- Link integrity is a database question — a join against `ContentReference` — rather than a crawl.
- The editing UI must open the CMS page picker rather than accepting a typed URL, or the guarantee
  leaks (task P6-11).
- Redirects still get created on URL change, because external sites and bookmarks hold the old URL
  even though internal links do not.
- Rendering a link costs a route lookup. Route lookups are cached and cache-tagged.
