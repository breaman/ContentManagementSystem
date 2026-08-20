# Load tests

k6 scripts for the requirements that can only be verified under load (task `P9-13`,
[§25](../spec.md#25-non-functional-requirements)). The dataset they run against is built by
`cms seed load` — see [`docs/load-testing.md`](../docs/load-testing.md).

| Script | Requirement | Target |
|---|---|---|
| `cached-delivery.js` | **NFR-1** | Cached public page TTFB < 200 ms p95 |
| `uncached-delivery.js` | **NFR-2** | Uncached public page TTFB < 800 ms p95 |
| `scale.js` | **NFR-9** | 5,000 rps of cached public traffic |

`NFR-7` (publish under two seconds, invalidation included) is **not** here. It needs an
authenticated editor session and a page to publish, which is an API test rather than a traffic
generator — it lives in
[`PublishBenchmarkTests`](../tests/ContentManagementSystem.Server.Tests/LoadTesting/PublishBenchmarkTests.cs),
measured against a five-thousand-page seeded site, alongside the reusable-content publish whose
fan-out is what risk `R8` is about.

## Before the first run: the rate limiter

Public pages are limited to **600 requests a minute per address**
([§20.6](../spec.md#206-rate-limiting)) — ten a second — and a load generator is one address. Against
the defaults, a run at 200 rps is refused nine requests in ten. Configure the environment under test:

```
Cms__RateLimits__PublicPagesPerMinute=2000000
Cms__RateLimits__MediaResponsesPerMinute=500000
```

Every script carries a `rate_limited: ['count==0']` threshold, so a run that forgot says so on one
line instead of reporting excellent latencies for the requests that got through.

## Running them

k6 is not installed as a dependency; the runner uses the official image, so every run uses the same
version and there is nothing to set up but Docker.

```bash
# 1. seed the dataset against the environment under test
cd src/ContentManagementSystem.Server
dotnet run -- cms seed load

# 2. point the scripts at that environment
cd ../../loadtests
BASE_URL=https://loadtest.example.com ./run.sh cached-delivery.js
BASE_URL=https://loadtest.example.com ./run.sh uncached-delivery.js
BASE_URL=https://loadtest.example.com ./run.sh scale.js
```

The thresholds are the requirements, so **a run that breaches one exits non-zero**. That is what
makes these usable as a gate rather than as a report somebody reads.

| Variable | Default | Applies to |
|---|---|---|
| `BASE_URL` | `http://host.docker.internal:5000` | all |
| `MANIFEST_DIR` | `../src/ContentManagementSystem.Server/App_Data/load-test` | all |
| `RATE` | 200 (`cached`), 5000 (`scale`) | arrival rate per second |
| `DURATION` | `2m` (`cached`), `10m` (`uncached`) | length of the measured phase |
| `HOLD` / `RAMP` | `5m` / `1m` | `scale.js` stages |
| `VUS` / `MAX_VUS` | varies | virtual users |
| `HOT_URLS` | 50 (`cached`), 500 (`scale`) | size of the warmed set |

## What to watch besides the exit code

Two contingencies are stated against numbers k6 cannot see, so read them off the application's own
dashboards during the run ([`docs/operations.md`](../docs/operations.md)):

- **CPU above 70% sustained** — risk `R11`, rendition generation saturating the box. `scale.js`
  requests pages rather than images, so pair a run with real traffic to the media endpoint if that
  is the question.
- **Save latency against NFR-6** — risk `R18`, the full-text index slowing writes. These scripts
  generate read traffic only; the editor side of `NFR-9` (200 concurrent editors) is a separate
  profile that does not exist yet.

## Why the scripts are shaped the way they are

- **Cached and uncached are separate scripts, not one run with two tags.** The only way to be sure a
  request was a cache hit is to have asked for that URL already, and the only way to be sure it was
  a miss is to have never asked. Mixing them makes both numbers ambiguous.
- **`http_req_waiting`, not `http_req_duration`.** The requirements are stated as time to first
  byte. Duration includes downloading the page, which turns the measurement into one about page
  weight.
- **`dropped_iterations` is a threshold in `scale.js`.** An arrival-rate run that cannot start the
  iterations it planned reports excellent latencies for the requests it did make. Without this
  threshold, a load generator that ran out of VUs looks like a site that met its target.
