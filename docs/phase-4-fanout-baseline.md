# Cache-invalidation fan-out baseline

Recorded for task **P4-13**, against risk **R8** (invalidation fan-out). This is the cost of
answering the question a reusable-content publish has to answer before it can evict anything:
*which pages show this item?*

**Recorded:** 2026-08-16 · **Host:** macOS (Darwin 25.5.0), arm64 · **SDK:** .NET 10.0.301 ·
**Database:** SQL Server in Testcontainers, same host, cold cache warmed once

## What was measured

`IReferenceQueryService.WhereUsedAsync(ReusableContent, id)` — the walk that produces both the
publish-impact list of [§9.4](../spec.md#94-impact-analysis-and-where-used) and, in Phase 8, the set
of `ru:{id}` cache tags to evict.

The fixture is one published reusable item placed late-bound in a zone of **40 published pages**,
all arranged through the real services. Asserted continuously by
[`ReferenceFanOutTests`](../tests/ContentManagementSystem.Server.Tests/Content/ReferenceFanOutTests.cs).

| Referencing pages | Nesting levels | Elapsed (warm) |
|---|---|---|
| 40 | 1 | **≈ 2.8 ms** |

## The shape, which matters more than the number

The walk is **three round trips per level of reusable nesting**, not one per referencing page:

1. `ContentReference` rows whose `(TargetType, TargetId)` is anything in the current frontier — one
   indexed seek, one `IN` clause however wide the frontier is;
2. the page versions those rows came from, narrowed to each page's *live* versions;
3. the reusable versions those rows came from, narrowed the same way, which becomes the next
   frontier.

`ReferenceQueryService.MaxDepth` bounds the levels at 5, so the query count is bounded at 15
regardless of how many pages come back. That is the property the test guards: its threshold is two
seconds against an observed 2.8 ms, which is not a tolerance — it is an order-of-magnitude tripwire
for the one regression that would hurt, the walk becoming per-page.

## What this means for Phase 8

- **Eviction can be driven straight from this call.** At 40 pages it is noise beside the publish
  transaction itself; the cost is in the eviction the cache then performs, not in computing the list.
- **The list is capped and the counts are not.** `MaxListedPages` is 100, so a site-wide footer
  returns exact counts and a bounded list. Anything in Phase 8 that needs *every* page id — a purge
  loop rather than a dialog — must read `ContentReference` directly rather than paging this,
  because `IsTruncated` is a UI affordance and not a cursor.
- **The number to re-measure is at depth, not at breadth.** Breadth is one `IN` clause. What has not
  been measured is a five-level nest where each level fans out, because no such content exists yet;
  when the media library (P5) starts adding its own reference rows to the same table, this is the
  measurement worth repeating.

## Not measured here

- The eviction itself. There is no output cache until P8, so "how long does evicting 40 tagged
  entries take" has no implementation to time.
- Concurrent publishes. One editor publishing a footer while another publishes a page is a lock
  question, not a fan-out question, and it belongs with the caching work that introduces the
  contention.
