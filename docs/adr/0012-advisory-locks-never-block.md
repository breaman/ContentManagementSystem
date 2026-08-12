# 0012 — Advisory edit locks never block; `rowversion` is authoritative

- **Identifier:** D12
- **Status:** Accepted
- **Source:** [`spec.md` §11.8](../../spec.md)

## Context

Two editors opening the same page is routine. The tempting fix is a lock that prevents the second
editor from editing. In practice those locks get stuck — a browser is closed, a laptop sleeps, a
process dies — and the result is a support request to unlock a page.

## Decision

`EditLock` is **advisory only**. Opening the editor acquires a lock, a 30-second heartbeat renews it,
and a reaper expires it after two minutes of silence. A second editor sees who holds the lock and may
override it. **A lock never prevents editing.**

Correctness comes from the `rowversion` concurrency token on `Page` and `PageVersion`. A conflicting
save returns `409 Conflict` carrying both payloads.

## Consequences

- No stuck-lock support load, and no path where a person is blocked from doing their job by stale
  state.
- Conflicts are possible by design, so the conflict experience has to be good: the UI offers
  keep-mine, take-theirs, and open-diff, and **no path silently discards work** (task P6-19).
- The lock is a social signal ("Sam is editing this"), not a safety mechanism. It should never be
  described in the UI as protection.
- Lock rows are high-churn derived data, so `EditLock` is excluded from the `AuditLog` interceptor
  (task P1-05).
