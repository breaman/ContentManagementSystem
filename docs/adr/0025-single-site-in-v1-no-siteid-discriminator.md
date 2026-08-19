# 0025 — v1 ships single-site: no `SiteId` discriminator, and the migration path if that changes

- **Identifier:** D25
- **Status:** Accepted
- **Source:** task `P8-26`, the scheduling constraint attached to Phase 8 — *"decide during this phase
  whether multi-site is plausible within 18 months; adding a `SiteId` discriminator is dramatically
  cheaper before v2 adds tables than after."*

## Context

Nothing in [`requirements.md`](../../requirements.md) or [`spec.md`](../../spec.md) asks for more than
one site. The schema has been built for one: `SiteSettings` is a singleton row keyed on
`SiteSettings.SingletonId`, routing resolves a request by URL path alone with the host never
consulted, and the site's public base address comes from `ISiteAddress` — configuration first, the
request second.

The constraint is real, though, and it is about *when* rather than *whether*. A discriminator added
before v2's tables exist is one migration over twenty-odd tables. Added afterwards it is that same
migration plus every table v2 introduced, plus a backfill of production data, plus an audit of every
query written in between.

What multi-site would actually cost is not the column. It is the **uniqueness rules**, of which this
schema has around twenty, and each one is a scope decision rather than a mechanical edit:

| Constraint today | What it becomes | The decision hidden in it |
|---|---|---|
| `PageRoute.UrlHash` unique where published | unique per site | is `/about` one page or one per site? |
| sibling slug unique under a parent | unchanged | the tree root is per site, so this follows from `Page.SiteId` |
| `Template.Key`, `BlockType.Key` unique | per site, or global | are templates shared infrastructure or site content? |
| `ReusableContent.Key` unique | per site, almost certainly | a shared footer across brands is a feature request, not a default |
| `NavigationMenu.Key` unique | per site | |
| `Tag.Slug` unique | per site, or global | a shared taxonomy is arguably the point of running two sites in one CMS |
| `MediaItem.Sha256` unique where not deleted | global is *better* | deduplication across sites is free storage, but a delete guard then spans sites |
| `SiteSettings` singleton | one row per site | `robots.txt`, the 404 page, and retention all become per site |

And the failure mode of getting one wrong is the reason this is not a cheap hedge: **a query that
forgets its site filter leaks another site's content**, and no test written against a single-site
fixture can fail because of it. A discriminator that nothing sets and nothing filters by is not
insurance; it is a column that has to be remembered in every query written for the next two years,
proving nothing in exchange.

## Decision

**v1 stays single-site. No `SiteId` column is added, and no query is written as though one existed.**

The assessment behind that: multi-site is *possible* within eighteen months, but nobody has asked for
it, and two of the open questions that would inform it — **Q2** (content scale) and **Q8** (an
existing site to migrate, and whether its URLs must be preserved) — are still unanswered. Choosing
the expensive-now option on a maybe, in a schema whose uniqueness rules each encode a product
question nobody has been asked, buys a column and defers all the decisions that make the column mean
something.

Three properties of what is already built are what make this reversible rather than a trap, and they
are recorded here so that they are maintained deliberately:

1. **`ISiteAddress` is the only place the site's own address is decided.** A host-to-site lookup has
   exactly one seam to be inserted at; nothing else in the codebase asks the request what host it
   arrived on.
2. **`SiteSettings` is a row, not configuration.** Per-site settings are a foreign key away from
   existing, rather than an appsettings section that would have to be reshaped into one.
3. **Route resolution is centralized.** `RouteResolver` and `UrlService` own every URL read and
   write, so the site filter lands in a handful of files rather than being sprinkled across every
   service that happens to know a URL.

## Consequences

- **The reversal is a known quantity, and it is stated here so nobody has to rediscover it.** One
  migration adding `SiteId` to the tables above and recutting their unique indexes as composites; a
  `Site` table with the host bindings; a host resolver behind `ISiteAddress`; per-site
  `SiteSettings`; and a query audit whose only reliable form is a review of every `IQueryable` that
  reads a URL, a key, or a slug. It is a phase of work, not a sprint, and the honest estimate is that
  it roughly doubles after v2 adds tables — which is precisely what the scheduling constraint warned
  about, now recorded with its number attached.
- **The cache tags and the outbox do not change shape.** Tags are already per entity id, and eviction
  is already fan-out from what a render declared; neither would need a site dimension. The search
  index would: `SearchDocument` gains the column with everything else, and the backoffice search is
  where a missed filter would show up first — which makes it the natural canary if this is ever
  reversed.
- **The decision is revisited when there is a second host to serve**, not on a calendar. The trigger
  is concrete: a second brand or domain being asked for, or a customer asking for tenant isolation.
  Anything short of that is a hypothetical, and this record exists so the next person can see it was
  weighed rather than missed.
