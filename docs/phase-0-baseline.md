# Phase 0 baseline

Recorded for task **P0-02**. This is the state the CMS work builds on: what runs, what is verified,
and what a developer should expect on a first checkout.

**Recorded:** 2026-08-12 · **Host:** macOS (Darwin 25.5.0), arm64 · **SDK:** .NET 10.0.301 ·
**Docker:** Engine 29.7.2

## What `aspire run` starts

| Resource | Detail |
|---|---|
| `sqlserver` | `mcr.microsoft.com/mssql/server` container, persistent lifetime, named `contentmanagementsystem-sqlserver`. Falls back to `azure-sql-edge` on Windows arm64. |
| `contentmanagementsystemdb` | Database on the above. |
| `ef-migrations` | Applies EF migrations before the server starts; the server waits for it to complete. |
| `storage` / `media` | Azurite blob emulator, persistent lifetime, named `contentmanagementsystem-azurite`. Dev stand-in for the media store (task P0-13). |
| `server` | The Blazor Web App, with an HTTP health check on `/health`. |
| `outputcache` | Redis. **Not started by default** — set `Cms:UseRedisOutputCache` to `true` to provision it. Unused until Phase 8 (task P0-14). |

## Verified

- `dotnet build ContentManagementSystem.slnx` — **succeeds with zero warnings** across all eleven
  projects (acceptance criterion P0 #2).
- `dotnet test ContentManagementSystem.slnx` — 8 tests, all passing, across the four suites.
- `aspire run` starts SQL Server, Azurite, and the server; `GET /health` and `GET /alive` both
  return `Healthy` (acceptance criterion P0 #4).
- The `InitialDatabase` migration applies to an empty database and reverts cleanly (task P0-01),
  asserted continuously by `MigrationsApplyFromEmptyTests`.

## Notes for a first checkout

1. `dotnet tool restore` — the EF tooling is a local dotnet tool.
2. `cd src/ContentManagementSystem.Server && npm install && npm run sass-dev` — Bootstrap CSS is
   compiled from SCSS and is not checked in.
3. Playwright browsers install themselves on the first E2E run; no PowerShell needed.
4. Integration tests need Docker running. They pick their SQL Server image per
   [`SqlServerImage`](../tests/ContentManagementSystem.TestSupport/SqlServerImage.cs) — Azure SQL
   Edge on arm64, SQL Server 2022 elsewhere — overridable with `CMS_TEST_SQL_IMAGE`.

### Known environment issue: RZ1021 build errors

A build can fail with a wall of **RZ1021** errors ("Markup in a code block must start with a tag")
in Razor files nobody touched. This is a .NET SDK 10.0.301 build-server defect, not a code problem —
it reproduces on a brand-new `dotnet new blazor` project.

Fix: `dotnet build-server shutdown`, then build again. Do not edit the Razor files, clean
`obj/`/`bin/`, or change the SDK pin.
