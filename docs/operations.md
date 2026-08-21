# Operations

**Tasks:** `P9-19`, `P9-20` · **Spec:** [§24](../spec.md#24-observability) ·
[§20.8](../spec.md#208-secrets-and-configuration)

Everything needed to deploy this system, watch it, and be woken by it. It is written for whoever is
on call rather than for whoever wrote the code: each section says what breaks, what the symptom looks
like from outside, and what to do about it.

---

## 1. Deployment

### What is deployed

| Component | What it is | Notes |
|---|---|---|
| `ContentManagementSystem.Server` | The whole application — delivery, backoffice, API, media, background services | One process. There is no separate worker tier. |
| SQL Server | Content, structure, identity, audit, search index | Full-text catalog required for the search latency budget (`NFR`, `P8 #10`). |
| Blob storage | Media originals and renditions | A local directory works and is the fallback when no connection string is configured. |
| Redis *(optional)* | Shared output cache | Needed only when more than one instance serves public traffic. |

### Order of operations

1. **Migrate.** Migrations are additive and are applied by the Aspire `ef-migrations` resource, or by
   `dotnet ef database update` against the target. They are backward compatible through launch, so
   the previous build keeps working against the new schema — which is what makes an application
   rollback safe without a database rollback (`L-09`).
2. **Promote structure.** `dotnet run -- cms schema diff` on the target, reviewed, then
   `dotnet run -- cms schema apply`. The sync is additive and refuses rather than destroys
   ([ADR 0019](adr/0019-schema-sync-is-additive-and-non-destructive.md)).
3. **Start the new instances.** They refuse to start on a development secret — see §5 — and their
   `cms-database` check reports *degraded* until the migration has reached them, which is the state a
   rolling deployment passes through rather than an error.
4. **Cut over.** Blue/green or slot-based, previous version kept warm (`L-08`).

### Background services

All of them run on every instance and are safe to. Each has a switch, and the switch exists so that a
deployment which turns one off has done so deliberately rather than silently.

| Service | Interval | Setting | Safe on several instances because |
|---|---|---|---|
| `PublishSchedulerService` | 30 s | `Cms:Scheduler:Enabled` | A job leaves `Pending` only through one atomic `UPDATE … OUTPUT`, so every instance may poll and exactly one may claim (`R16`). |
| `OutboxProcessorService` | 5 s | `Cms:Outbox:Enabled` | Cache eviction is per-node memory and claims nothing; the index handler claims its row. |
| `SearchReconcileService` | 24 h | `Cms:Search:ReconcileEnabled` | The pass is a comparison that rewrites only what is wrong. |
| `RetentionService` | 24 h | `Cms:Retention:Enabled` | Both sweeps are idempotent; the audit sweep deletes in batches by primary key, so two racing sweeps delete disjoint rows. |

---

## 2. Configuration reference

Only settings that change behaviour in production are listed. Anything absent takes the default in
the options class, and every default is the safe reading.

### Security

| Key | Default | What it does |
|---|---|---|
| `Cms:SecurityHeaders:ContentSecurityPolicyEnabled` | `true` | Emits the policy. Off is for a deployment that has found a genuine break and needs to ship the fix rather than a rollback. Never a launch setting. |
| `Cms:SecurityHeaders:ReportOnly` | `false` | Reports violations without blocking. For measuring a policy change against real traffic. A report-only policy stops no attack. |
| `Cms:SecurityHeaders:ReportUri` | *(none)* | Where violation reports are posted. |
| `Cms:Identity:MinimumPasswordLength` | `12` | Per [§20.3](../spec.md#203-authentication). |
| `Cms:Identity:SelfRegistration` | `Disabled` | `Disabled` answers the registration routes with `404`. `NoRole` opens them; nothing grants a role on registration either way. **Q10.** |
| `Cms:Identity:UseHaveIBeenPwned` | `false` | Adds the breach-corpus screen. Puts a third party on the path of every password change. |
| `Cms:Identity:RefuseWhenBreachServiceUnavailable` | `false` | Accepts on an unreachable service, and logs. Failing closed stops every password reset during the incident that prompted them. |
| `Cms:MediaSigning:Key` | *(none)* | **Required outside Development.** Base64, at least 32 bytes. The instance refuses to start without it. |
| `Cms:MediaSigning:PreviousKey` / `:PreviousKeyExpiresOn` | *(none)* | Rotation. Setting the first without the second refuses to start: a rotation that never completes has not removed the old key from anything. |
| `Cms:RateLimits:PublicPagesPerMinute` | `600` | Public pages one address may fetch per minute ([§20.6](../spec.md#206-rate-limiting)). **Raise it only on a load-test environment** — a load generator is one address, and the default is ten requests a second (`P9-13`, [load testing](load-testing.md)). Zero or negative refuses to start; there is no way to spell "no limit". |
| `Cms:RateLimits:MediaResponsesPerMinute` | `300` | The same, for renditions and originals. |

### Content and delivery

| Key | Default | What it does |
|---|---|---|
| `Cms:Cache:*` | see `DeliveryCacheOptions` | Output cache lifetimes and the Redis connection when scaled out. |
| `Cms:Outbox:PollSeconds` | `5` | How often invalidation is dispatched. Also the basis of the `cms-outbox` silence threshold. |
| `Cms:Search:UseFullText` | *(probe)* | Null asks the server. Set only to force the fallback during a catalog rebuild, or to assert the full-text path in a test. |
| `Cms:Retention:Enabled` | `true` | The nightly version and audit sweeps. |
| `Cms:Email:*` | *(none)* | SMTP host and credentials. With nothing configured the deployment logs what it would have sent and reports itself unconfigured ([ADR 0024](adr/0024-mail-is-smtp-configuration-not-a-provider-choice.md)). |
| `Cms:SiteStylesheet:MaxBytes` | `262144` | Largest stylesheet an administrator may publish ([§30.5](../spec.md#305-what-is-refused-and-why)). Raising it is a decision about what the public page carries; it does not widen what the validator refuses. |
| `Cms:SiteStylesheet:SharedMaxAgeSeconds` | `300` | How long a CDN may serve `/css/site-custom.css` without revalidating. A publish evicts this instance immediately; this bounds what sits in front of it (**Q6**). |

### Retention windows

Not configuration — they are `SiteSettings` columns an administrator edits, because how long an
organisation's records last is its decision rather than its deployment's.

| Setting | Default | Effect |
|---|---|---|
| `SiteSettings.VersionRetentionDays` | `0` | Zero keeps every version. The five clauses of [§11.7](../spec.md#117-version-retention) protect pointers, published versions, checkpoints, the window, and the last twenty regardless. |
| `SiteSettings.AuditLogRetentionDays` | `0` | Zero keeps every audit row. **Q9** decides the number. |

---

## 3. Health checks, monitors, and alert thresholds

`P9-20` asks that every check has a monitor and a threshold. All five of
[§24.2](../spec.md#242-health-checks) are below, and `HealthCheckContractTests` asserts that the set
registered by the application is exactly this set — so a check added later without a row here fails
the build rather than going unmonitored.

`/health` requires every check to pass; `/alive` requires only those tagged `live`. Both are exposed
in Development only by default; a production deployment maps them on an internal port or behind its
ingress.

| Check | Degraded when | Unhealthy when | Alert | Why it matters |
|---|---|---|---|---|
| `cms-database` | A migration this build expects has not been applied | The database refuses a connection | Page on unhealthy; ticket on degraded lasting > 10 min | Degraded is the normal state mid-rollout. Degraded that *persists* is a half-finished cutover serving 500s from whichever request touches the new column first. Aspire's own connectivity check is switched off in favour of this one: two checks reporting the same fact under two names is worse than one, and `ApplicationDbContext` is not a name any alert rule uses. |
| `cms-media-store` | — | A write-then-read round trip fails | Page | No upload succeeds and no cold rendition can be generated. Unhealthy rather than degraded because there is no partial version of this. |
| `cms-templates` | A deployed template or block type does not reconcile | — | Ticket | Never unhealthy: a bad deployment must be visible without taking down a site whose pages still render. |
| `cms-scheduler` | The poll loop is off on this instance | Publishing is > 5 min behind, or the loop has stopped | Page on unhealthy | A stopped scheduler has no symptom until somebody notices a page that never went live. |
| `cms-outbox` | Invalidation is switched off on this instance | A message has waited > 5 min, or no pass in 6 poll intervals | Page on unhealthy; ticket on degraded | The failure with no other symptom: every request succeeds and every page renders, with content that was replaced hours ago. |

### Dashboards

Built from the instruments of [§24.1](../spec.md#241-telemetry). One page, five rows:

1. **Traffic** — request rate and status mix, split public / `/admin` / `/api` / `/media`.
2. **Delivery** — `cms.page.render.duration` p50/p95 by template, and output-cache hit ratio.
   NFR-1 is < 200 ms p95 cached, NFR-2 < 800 ms uncached.
3. **Publishing** — `cms.publish.duration` p95 (NFR-7: < 2 s) and the publish result counter split by
   outcome. A rise in `refused` is editorial; a rise in `failed` is not.
4. **Queues** — outbox pending count and oldest-pending age; scheduler lag.
5. **Errors** — unhandled exception rate, and the `429` rate per limiter policy. A `429` spike on
   `cms-credentials` is a credential-stuffing attempt and is the one to look at.

---

## 4. Rate limits

Set in `CmsRateLimits`, per [§20.6](../spec.md#206-rate-limiting). A refused request answers `429`
with `Retry-After` and a body — the body matters, because the site's status-code pages re-execute any
body-less error response and would otherwise rewrite the refusal as a `404`.

> **Behind a proxy, set `KnownProxies`.** Every per-address partition reads
> `Connection.RemoteIpAddress`. Behind an ingress controller or a CDN that is the proxy's address, and
> every visitor shares one bucket — which turns the public limit into a site-wide one. The fix is
> `UseForwardedHeaders` with `KnownProxies` or `KnownNetworks` set to that infrastructure. It is
> deliberately not switched on in code: forwarded headers trusted from anywhere are a header any
> client can write, which would let one attacker occupy an unbounded number of buckets.

---

## 5. Secrets

`CmsSecretsGuard` runs at startup and **throws** outside Development when it finds a development
secret. It is a refusal rather than a warning because everything it checks appears to work when it is
wrong:

- **No media signing key.** Every instance generates one of its own, so an image served by one is
  refused by the next. On a single instance it looks perfect.
- **A rotation with no end date.** The old key validates forever, which is a rotation that has not
  removed anything.
- **The Aspire development password in the connection string.** The database connects.

Keys come from a key vault or the environment, never from `appsettings.json`. The Aspire
`sql-password` parameter is a run-mode default marked `secret: true`; it is not written to the
deployment manifest, and the guard is what catches it having been copied by hand.

### Rotating the media signing key

1. Move the current `Cms:MediaSigning:Key` to `:PreviousKey`, and set `:PreviousKeyExpiresOn` to a
   date past the longest cache lifetime in front of the site — a CDN's, if there is one.
2. Set `:Key` to a new 32-byte value.
3. Deploy. Pages re-rendered from now on emit URLs signed with the new key; every URL already in a
   cached page, a CDN copy, or an email keeps working until the expiry.
4. After the expiry, remove `:PreviousKey` and `:PreviousKeyExpiresOn`.

Swapping the key without a grace period invalidates every rendition URL on the site at once — every
image breaking simultaneously, for as long as the caches take to turn over.

---

## 6. Incident runbooks

### "Published changes are not appearing"

1. Check `cms-outbox`. Unhealthy means invalidation has stopped; degraded means it is switched off on
   this instance.
2. If the backlog is growing, look for an exception in the outbox log. The runner counts a failure
   and moves past it, so a poison message is a log line rather than a stall.
3. Republishing the page enqueues a fresh invalidation. It does not clear the backlog ahead of it.
4. Last resort: restart the instance. The outbox is durable and picks up where it stopped.

### "The public site looks wrong, and nothing is failing"

Check **Appearance → Stylesheet** first. An administrator can publish CSS from inside the CMS
([§30](../spec.md#30-site-stylesheet)), it reaches every visitor on their next request, and it cannot
fail loudly — a stylesheet that makes the site unreadable still returns `200 OK`, so no health check,
alert, or test goes red (**R21**).

1. `GET /css/site-custom.css` — a `404` means nothing is published and the cause is elsewhere.
2. The revision list on that screen says who published what and when. Compare it against when the
   reports started.
3. **Revert.** Publishing an earlier revision, or publishing nothing at all, is one button and takes
   effect on the next request. It leaves the draft alone, so nothing is lost by doing it early.
4. The backoffice never loads this stylesheet, so the screen you are reverting from cannot be
   affected by the thing you are reverting.

### "Every image is broken"

Almost always the signing key. Check the startup log for the generated-key warning and
`Cms:MediaSigning:Key` on the affected instance. If a rotation is in progress, check that
`PreviousKeyExpiresOn` has not passed while old URLs are still in cached pages.

### "Nobody can sign in"

1. `cms-database` — if it is unhealthy, that is the answer.
2. Check the `429` rate on `cms-credentials`. Five attempts per fifteen minutes per address is
   generous for a person and tight for an office behind one NAT address; see the proxy note in §4.
3. Check whether a privileged account is being redirected to `/Account/Manage/EnableAuthenticator`.
   `Administrator`, `Developer`, and `Approver` may not be used without a second factor, and an
   account granted one of those roles mid-session meets the gate on its next request.

### "A page returns 500 after a deployment"

Check `cms-database` for pending migrations. A build serving against a schema missing its migration
fails on whichever request first touches the new column, and looks healthy everywhere else.

### "Search returns nothing for content that exists"

The index is asynchronous by construction. The nightly reconcile repairs it; to force one, restart an
instance with `Cms:Search:ReconcileStartupDelayMinutes` set to `0`. If full text is unavailable the
service falls back to a scan and only the latency budget is lost, not the results.

---

## 7. Backup and restore

See [`runbooks/backup-restore.md`](runbooks/backup-restore.md). The drill is `P9-18` and is timed
against the RTO; the part worth stating here is that **a database restore alone does not produce a
working site** — the media store is a second system, and a page whose pictures 404 is not restored.
