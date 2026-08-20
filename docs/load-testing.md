# Load testing

**Tasks:** `P9-12` (the dataset), `P9-13` (the k6 scripts in [`loadtests/`](../loadtests/)),
`P9-14` (what profiling found) · **Spec:**
[§25](../spec.md#25-non-functional-requirements) — NFR-1, NFR-2, NFR-7, NFR-9 ·
**See also:** [`lighthouse/`](../lighthouse/) for NFR-3 and NFR-4

`NFR-9` names a size: **50,000 pages, 100,000 media items, 200 concurrent editors, 5,000 rps public
(cached)**. The first two of those are a dataset, and none of the latency requirements mean anything
measured against the two dozen pages a test fixture creates — a query plan over fifty thousand rows
is a different plan. This document is how that dataset is built, what it faithfully represents, and
what it does not.

---

## 1. Building it

```bash
cd src/ContentManagementSystem.Server

dotnet run -- cms seed load                 # the NFR-9 dataset
dotnet run -- cms seed load --pages 2000    # something smaller to try the tooling on
dotnet run -- cms seed load --reset         # rebuild it from scratch
dotnet run -- cms seed purge                # take it away again
```

The verbs run inside the fully built application against whatever connection string it is configured
with, so **the connection string is the thing to check twice**. In the `Production` environment the
command refuses to run at all without `--force`; a load-test environment configured as Production is
exactly the situation in which somebody types this against the wrong database.

| Flag | Default | What it changes |
|---|---|---|
| `--pages N` | 50000 | Total pages, including the branches above the leaves |
| `--media N` | 100000 | Media rows |
| `--images N` | 24 | How many distinct images are actually written to the store |
| `--tags N` | 200 | Tags spread across the pages |
| `--redirects N` | 500 | Redirects left pointing into the tree |
| `--random N` | 20260819 | Seed for the generator |
| `--batch N` | 10000 | Rows per bulk-copy batch |
| `--root SLUG` | `load-test` | Root page slug and media folder name |
| `--manifest P` | `App_Data/load-test/manifest.json` | Where the manifest is written |
| `--manifest-sample N` | 2000 | URLs of each kind the manifest carries |
| `--reset` | off | Delete an existing dataset first |
| `--force` | off | Allow the run in the Production environment |

**Measured:** 2026-08-19, macOS arm64, SQL Server in Docker on the same machine —
**27.5 s** for the full dataset: 50,000 pages, 94,592 page versions, 94,592 routes, 434,286 content
references, 100,000 media items, 59,385 page tags and 149,516 search documents. One command, under a
minute, which is what makes "reseed and run it again" a reasonable thing to ask of whoever is doing
the load test. (A first run on a machine that has never seeded before also draws the two dozen pool
images, which adds a few seconds.)

### Pointing it at a database

Under `aspire run` the connection string arrives from the AppHost and there is nothing to do. Against
any other database, the command takes it the way the application does:

```bash
ConnectionStrings__contentmanagementsystemdb="Server=…;Database=…;User Id=…;Password=…" \
  dotnet run -- cms seed load
```

Apply the migrations first (`dotnet ef database update -p ../ContentManagementSystem.Data`) — the
seeder writes rows, it does not create tables. Seeding a **separate** database rather than the
development one is the habit worth having: fifty thousand generated pages in the database somebody
demos from is not easily undone, and `cms seed purge` only removes what it wrote.

Running it twice does nothing the second time; it reports what is already there and exits. There is
no top-up, because a half-sized dataset that grew a second half would have two generations of
content in it and no way to tell them apart. `--reset` deletes and rebuilds.

---

## 2. What gets built

Everything hangs below a single root page (`/load-test`) and a single media folder of the same name.
That is what makes the purge safe: it deletes a subtree it can identify, never "rows that look
generated".

### The tree

Wide and shallow, with one deliberately deep branch:

| Level | Count at the default size | Template |
|---|---|---|
| Root | 1 | `marketing-landing` |
| Sections | 12 | `marketing-landing` |
| Topics | 408 | `marketing-landing` |
| Leaves | ~49,500 | `article` (80%) and `marketing-landing` (20%) |
| Deep branch | 8, below the first topic | `article` |

A fifth of the leaves are landing pages because landing pages are the ones carrying the shared
footer, which puts it on roughly **ten thousand pages** — the figure risk `R8`'s trigger is stated
against. `--pages` and the shares are the two knobs that move it.

Leaf pages are spread over the topics **unevenly** — a squared-uniform weight, so most branches sit
near the average and a few are several times larger. A site where every section holds the same
number of pages makes every listing query cost the same, and the queries that hurt in practice are
the ones over the one section that grew to ten times its neighbours.

The deep branch exists because depth is what ACL resolution and materialized-path prefix matching
cost (risk `R15`); its leaf sits at depth ten, which is the depth those were measured at in Phase 7.

### The state each page is in

| State | Share | What it exercises |
|---|---|---|
| Published, draft unchanged | ~76% | The ordinary cached delivery path |
| Published, draft moved on | ~13% | Public content and preview returning different bytes |
| Never published | ~10% | Draft routes that resolve for preview and 404 anonymously |
| Recycled | ~1% | Soft-deleted pages that must not serve |

A published page carries two version rows — the draft the editor left and the immutable published
copy of it — and two routes, a draft route and a live one, because that is what publishing produces.

### Content

Pages are authored against the two templates the `Rendering` project actually ships components for,
so a request renders through real field renderers rather than the unknown-template fallback. Each
article fills eleven zones: plain text, multiline text, a date, a number, a boolean, a picture, a
gallery, tags, a page reference, and a body of three to six HTML blocks. Landing pages carry a
picture, rich text, blocks, a link, and **the one shared footer**, referenced late-bound — which is
the fan-out a reusable-content publish has to invalidate, and the one cost a dataset of independent
pages could never measure (risk `R8`).

Everything is deterministic in `--random`. Two runs of the same options produce byte-identical
content, because a load test whose dataset changes between runs cannot tell a regression from a
different distribution of page sizes.

**The reference rows are written too.** Each version's pictures, its page reference, and its footer
get a `ContentReference` row, derived from what the seeder just wrote rather than extracted back out
of the JSON it wrote it into. Without them the table the where-used walk reads would be empty, and a
load test of publishing the footer would report the cost of invalidating nothing at all.

### Media

100,000 rows over **24 distinct images**, drawn rather than photographed, in sizes from 4000×3000
down to 800×600. Every row points at a real blob, so every row serves real bytes and generates real
renditions — while the store holds tens of megabytes rather than the hundreds of gigabytes a hundred
thousand distinct photographs would.

### The manifest

The seeder writes a JSON file the load-test scripts read their URLs out of: published URLs sampled
across the tree, the landing pages that carry the shared footer, the deep branch, URLs that answer
301, URLs that answer 404, tag slugs, and the media id range. A script that discovered URLs by
crawling would spend its first minutes crawling; a script with URLs hard-coded in it would go stale
the first time the dataset was reseeded.

Image URLs are deliberately **not** in the manifest. They are signed and may be given a lifetime, so
a URL written at seed time could expire before the run that reads it — a script gets them the way a
browser does, out of the HTML of the page it just fetched.

---

## 3. What this data does not represent

Read this before concluding anything from a number measured against it.

- **Deduplication.** The media rows carry synthetic hashes that do not match the bytes behind their
  storage keys, because the live-hash index is unique and a hundred thousand rows cannot honestly
  share two dozen blobs. Nothing about deduplication, virus scanning, or upload throughput can be
  measured here.
- **Index extraction.** Search rows are written directly, with generated prose for bodies rather
  than text pulled out of each zone. A search measurement over this data measures the query and the
  index, never the indexer.
- **Write history.** Every row is stamped with one instant and one user, and there are no audit rows
  for any of it — the seeder writes with bulk copy, which bypasses the save interceptors on purpose.
  Anything measured against `AuditLog` growth needs a different fixture.
- **Editor concurrency.** The 200-concurrent-editors half of `NFR-9` is a load profile, not a
  dataset. This gives those editors something to open; it does not simulate them.

## 4. How it is written, and why that matters

Rows go in with `SqlBulkCopy` rather than through the content services. Fifty thousand pages created
one `CreatePageRequest` at a time is upwards of a quarter of a million round trips, each in its own
transaction with its own URL rebuild — hours of work that would measure the writer rather than
produce a dataset.

Two consequences worth knowing:

- **Foreign keys are checked** (`SqlBulkCopyOptions.CheckConstraints`). Skipping them is faster and
  leaves the constraints marked untrusted, which changes the plans the optimizer produces — and a
  load test run against different plans from production's measures the wrong database. Pages
  therefore go in with their version pointers null and are repointed by a statement afterwards,
  because a page and its versions reference each other.
- **Identity counters are reseeded** after each table. Bulk copy with `KeepIdentity` inserts the ids
  it is given without advancing the counter, and the next row the application inserted by hand would
  collide. This is also why the seeder is for a database nothing else is writing to.

The seeder holds its own opinion of what a published page consists of, which is the risk in all of
the above.
[`LoadTestSeederTests`](../tests/ContentManagementSystem.Server.Tests/LoadTesting/LoadTestSeederTests.cs)
is what keeps that opinion and the services' agreeing: it seeds a small dataset — the same site in
miniature — and then asks the running application for the pages over HTTP, checks that the landing
page resolved its shared footer, that the 404 and 301 paths answer, that every page points at its
versions, that the tree really does reach depth ten, and that reseeding the same options produces
the same site.

---

## 5. Running the tests against it

The scripts live in [`loadtests/`](../loadtests/) and run from the official k6 image, so there is
nothing to install but Docker. Each script's thresholds **are** the requirement, so a run that
breaches one exits non-zero.

| Script | Requirement | Threshold |
|---|---|---|
| `cached-delivery.js` | NFR-1 | `http_req_waiting` p95 < 200 ms |
| `uncached-delivery.js` | NFR-2 | `http_req_waiting` p95 < 800 ms |
| `scale.js` | NFR-9 | 5,000 rps held, no dropped iterations, page and redirect failures zero |

[Lighthouse](../lighthouse/) covers NFR-3 and NFR-4 separately, against the same seeded pages:
performance **0.97 mobile**, LCP **2,254 ms**, CLS **0**, TBT **0 ms** on all three templates
(2026-08-19, same laptop).

`NFR-7` — publish under two seconds, invalidation included — is not a traffic-generator question; it
needs an authenticated editor and a page to publish. It is
[`PublishBenchmarkTests`](../tests/ContentManagementSystem.Server.Tests/LoadTesting/PublishBenchmarkTests.cs),
which seeds five thousand pages, publishes twenty of them through the real service with the outbox
dispatched inside the clock, and does the same for the shared footer whose fan-out reaches every
landing page — the measurement risk `R8` is stated against.

### The rate limiter will refuse the run

**This is the first thing to configure, and the first run without it fails in a way that looks like
the site is broken.** Public pages are limited to 600 requests a minute *per address*
([§20.6](../spec.md#206-rate-limiting)), which is ten a second, and a load generator is one address.
A first run at 200 rps against the defaults answered `429` to nine requests in ten.

```
Cms__RateLimits__PublicPagesPerMinute=2000000
Cms__RateLimits__MediaResponsesPerMinute=500000
```

Set on the environment under test, not in the repository. Every script carries a
`rate_limited: ['count==0']` threshold so that a run against the default budget says so in one line
rather than reporting excellent latencies for the tenth request.

### What has actually been run

**2026-08-19, macOS arm64, Debug build, SQL Server and k6 both in Docker on the same laptop, 5,000
seeded pages.** This is a harness check, not a verification of the requirements — the numbers below
are bounded by the machine generating the load, not by the site.

| Script | Result |
|---|---|
| `cached-delivery.js` | 300 rps for 20 s, 6,000 requests, **TTFB p95 0.76 ms**, no failures |
| `uncached-delivery.js` | 2,000 distinct pages, each requested once, **TTFB p95 44.4 ms**, 791 rps, no failures |
| `scale.js` | 3,000 rps requested, **2,434 rps achieved**, 121,996 requests, 0 dropped iterations, 0 page or redirect failures, page TTFB p95 445 µs |

The uncached figure is **after** the two fixes in section 6. Before them it was 516.8 ms — inside
NFR-2's 800 ms budget, but at 65% of it and growing with the number of pages.

`scale.js` did not reach its target rate, and the generator rather than the site is why: k6, SQL
Server, and the application were competing for one laptop's four cores. **NFR-9's 5,000 rps has not
been demonstrated**, and it needs an environment sized like the deployment with the load generated
from somewhere else. That is what `P9 #3` is still open on.

### Running the uncached script twice proves nothing

The second run of `uncached-delivery.js` against the same instance reported **1.25 ms** and 21,957
rps, because the first run cached every URL it asked for. The output cache is in memory, so a cold
run means **restarting the application** (or waiting out `Cms:Cache:OutputMinutes`). This is the
easiest number on this page to fake by accident.

---

## 6. What the profiling found (P9-14)

Profiled by logging every database command an uncached render issues, against the full
fifty-thousand-page dataset. The renders are of the two shipped templates, with query plans warm —
a first render of a shape pays for compilation, which is a per-process cost and not what NFR-2 is
about.

| | Article render | Landing render |
|---|---|---|
| Before | 60.8 ms in SQL, 10 queries | 74.1 ms in SQL, 10 queries |
| After both fixes | **7.1 ms in SQL**, 11 queries | **≈7 ms in SQL**, 11 queries |

End to end that is **uncached TTFB p95 516.8 ms → 44.4 ms**, and throughput on the same laptop from
90 rps to 791.

### 1. The breadcrumb read every page in the site — 42.8 ms

`SeoMetadataBuilder.TrailAsync` asked for "every page whose path is a prefix of this one", which
lands in SQL as a comparison against **every row in `Pages`** — an index cannot be seeked when the
column is on the wrong side of the prefix test. It was two thirds of the render's database time and
grew with the site.

Fixed by cutting the ancestor paths in memory — a materialized path *contains* its ancestors' paths —
and looking them up by equality. Two round trips, both seeks, ~1 ms together.

### 2. The structural menu read every page in the site — 7.8 ms

Same shape of problem, different query: the menu asks for live pages in the top levels of the tree
that want to appear in navigation, and no index covered `Depth`. A scan of fifty thousand rows to
return thirteen.

Fixed with a filtered covering index (migration 10, `IX_Pages_Navigation`), keyed on the ordering
columns so the sort comes for free and filtered to the rows a menu can contain. 7.8 ms → 0.7 ms.

### 3. Media is resolved one item at a time — and was left alone

Each media reference on a page is its own query: four on an article with a poster and a
three-picture gallery. It is a real N+1 and it is **not fixed**, because after the first two fixes
each of those queries is a sub-millisecond primary-key seek — batching them would save something
like 1.5 ms of a 7 ms render, and the change reaches into the render pipeline where each component
resolves its own value. Recorded here as the next candidate rather than done: the number to watch is
a page with a large gallery, where the count grows with what an editor put on it.

---

## 7. Cleaning up

`cms seed purge` removes the pages, versions, routes, tags, redirects, search documents, media rows,
and the shared footer. Two things it deliberately leaves:

- **The templates**, because somebody may have authored against them and a tool that removes its own
  scaffolding is a tool that deletes content.
- **The blobs** — the two dozen pool images and any renditions generated during a run. Deleting a
  hundred thousand generated files one call at a time would take longer than the seeding did, and
  the media store of a load-test environment is scratch space by definition. Empty the container or
  the directory instead.
