# Runbook — backup and restore

**Task:** `P9-18` · **Criterion:** `P9 #5` — a full restore produces a working site, timed against the
RTO · **Spec:** [§24.3](../../spec.md#243-backup-and-recovery) · **Last drill:** _not yet run_

**The thing this runbook exists to say: restoring the database does not restore the site.** Content
is in SQL Server and pictures are in a blob container, and they are two systems with two backup
schedules and two restore procedures. A drill that restores only the first produces a site whose every
page renders and whose every image is a broken icon — and it passes any check that only asks whether
pages load.

---

## What has to be backed up

| Store | Holds | Loss means |
|---|---|---|
| SQL Server | Pages, versions, structure, identity, ACLs, audit, redirects, the search index | Everything. |
| Blob container / media root | Media originals **and** generated renditions | Every picture on the site. Renditions regenerate from originals; originals do not regenerate from anything. |
| Key vault / secrets | `Cms:MediaSigning:Key`, connection strings, SMTP credentials | Every rendition URL in every cached page and every email stops validating. |

Renditions are derived and could in principle be left out of the backup. **Do not.** They are the
warm cache in front of an expensive encode, and a site restored without them re-encodes every image
on the first page view of each — at the moment the site is under the most scrutiny it will ever get.

## Cadence

| Store | Full | Incremental | Retained |
|---|---|---|---|
| SQL Server | Nightly | Log backup every 15 min | 35 days |
| Media | Nightly | Container versioning / soft delete on | 35 days |
| Secrets | On change | — | Vault's own history |

The log-backup interval is the RPO. Fifteen minutes means up to fifteen minutes of editorial work is
lost in the worst case; that is a product decision, and this is where the number lives.

---

## Restore

Timed. Record the clock at each step — the total is what `P9 #5` measures against the RTO, and a step
that always takes forty minutes is the one worth attacking.

1. **Stop writes.** Scale the application to zero, or put the ingress into maintenance. A restore that
   runs while editors are saving produces a database and a media store from different moments.
2. **Restore SQL Server** to the target point in time. Note the instant restored to — every later step
   is judged against it.
3. **Restore the media store** to the *same* instant. Container versioning restores per blob; a
   point-in-time restore of the whole container is preferable where the provider offers one.
4. **Confirm the secrets.** Particularly `Cms:MediaSigning:Key`. A restore that brought back the
   content and a *different* signing key produces a site where every image 403s — which looks like a
   media restore failure and is not.
5. **Start one instance.** It refuses to start on a development secret, so a failure here is
   informative rather than mysterious. Watch `/health`:
   - `cms-database` healthy — the schema matches this build. Degraded means the restored database
     predates a migration; apply migrations before continuing.
   - `cms-media-store` healthy — the round trip works, which is the first real proof the media
     restore landed.
6. **Verify content**, in this order, because each step rules out a different failure:
   1. The home page renders.
   2. A page with an image renders **and the image loads** — this is the step a database-only restore
      fails.
   3. A rendition at a size nothing has requested since the restore is generated on demand — proves
      originals are present, not just the rendition cache.
   4. An editor signs in and opens a draft.
   5. Search returns a result for a word in a page body. If it does not, the index is stale rather
      than the content lost: force a reconcile (see [operations §6](../operations.md#6-incident-runbooks)).
   6. `sitemap.xml` lists the expected number of URLs.
7. **Restore traffic.** Scale up, take the ingress out of maintenance.
8. **Record the times.** Each step, and the total, against the RTO.

---

## What a drill must actually prove

A drill that only confirms the backups exist has proved nothing. It has to be run against a *separate*
environment, from the artefacts alone, by somebody who is not the person who set the backups up:

- Restore into an empty subscription or resource group — not over the top of a working environment,
  which hides every "it was already there" dependency.
- Use only what the backup contains. If a step needs a value somebody has in a terminal, that value is
  a gap in the backup and is the finding.
- Time it end to end.
- Verify the media step explicitly. It is the one this runbook is written around.

Quarterly thereafter (`L-13`).
