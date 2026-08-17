# 0022 — Pre-render shims serialize their service calls; one request's `DbContext` is used once at a time

- **Identifier:** D22
- **Status:** Accepted
- **Source:** the `/admin/pages/{id}` pre-render failure; governs the shims of tasks `P1-29`, `P2-23`,
  `P4-11`, `P5-22`, `P6-24`

## Context

[ADR-0002](./0002-static-ssr-public-interactive-wasm-backoffice.md) puts the backoffice on
interactive WebAssembly, which means every admin screen renders twice: once on the server while the
runtime downloads, and again in the browser once it has. The screens are written against one client
interface per area — `IStructureClient`, `IPageClient`, `IReusableClient`, `IMediaClient`,
`IDashboardClient` — with two implementations behind each. In the browser the implementation calls
the HTTP API. On the server the `Server*Client` shims call the Core services directly, because a
request the server makes to itself would need a cookie it does not have and an antiforgery token that
has not been issued yet.

That arrangement carried an assumption nobody had written down: that a screen's data access happens
one call after another. It does not.

**Blazor starts sibling components' asynchronous lifecycle methods concurrently.** A render batch
calls `OnInitializedAsync` on every component the batch introduced; each runs to its first `await`,
and from there the rest of them overlap. In the browser that costs nothing — each call is its own
HTTP request, reaching its own server-side scope with its own `DbContext`. While pre-rendering it is
not free: every service in the request shares one scoped `ApplicationDbContext`, and EF Core refuses
a second operation started while the first is still reading.

```
System.InvalidOperationException: A second operation was started on this context instance before a
previous operation completed.
   at ContentManagementSystem.Core.Structure.BlockTypeService.ListAsync(...)
   at ContentManagementSystem.Server.Services.ServerStructureClient.GetBlockTypesAsync(...)
   at ContentManagementSystem.Client.Components.Admin.Fields.BlockList.BlockListEditor.OnInitializedAsync()
```

The page editor is the worst case, and it is the one that failed: it draws one field editor per zone,
and several of those editors fetch something of their own — block types, a media item, a page title,
a reusable item. Two zones are enough. Nothing about the failure is specific to block lists; whichever
editor loses the race is the one that appears in the stack trace, which is what made it read as a
block-list defect. It is also load-independent — one editor, one request, one browser tab reproduces
it — so it is not something a quiet deployment escapes.

## Decision

**1. Every pre-render shim call that reaches a service runs through `PrerenderGate`.** The gate is a
`SemaphoreSlim(1, 1)`; a call waits for whatever is inside to leave, and releases in a `finally` so a
refusal or an exception cannot strand it.

```csharp
public async Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(
    CancellationToken cancellationToken = default) =>
    (await gate.RunAsync(token => blockTypes.ListAsync(token), cancellationToken)).Value ?? [];
```

**2. The gate is scoped, so it has exactly the lifetime of the context it protects.** One gate per
request, guarding that request's one `ApplicationDbContext`. The HTTP API is untouched by
construction: each API request is its own scope with its own gate and its own context, and nothing
ever queues behind anything.

**3. A shim never calls another shim.** The semaphore is not reentrant, so a shim that did would wait
on itself forever. This holds today because the dependency direction forbids it — shims call
services, and no Core service depends on a client interface — and it is stated in the gate's
documentation so that a future shim-to-shim convenience method is recognised as the deadlock it is.

**4. Shims that touch no database are not gated.** `ServerMarkupPreviewClient` renders through two
singletons and `ServerCurrentUserClient` reads the request principal. Gating them would add a queue
around work that was never in the collision.

## Consequences

**Pre-render is serial, and its wall-clock cost is the sum of its queries rather than the longest of
them.** Nothing real is lost: those queries share one context on one connection, so they were never
going to overlap usefully — they were only ever going to collide. Screens still paint in one batch.

**A new shim method that forgets the gate is a latent version of this bug**, invisible until a screen
happens to draw two components that both use it. That is a reviewable rule with a visible shape: a
call in a `Server*Client` that names a service without `gate.RunAsync` around it is the defect.

**Components did not change, and must not be changed to work around this.** The concurrency is legal
and desirable in the browser, where it is what makes a screen with six editors load in one round of
requests rather than six. The constraint belongs to the server half alone, which is why the fix lives
there.

**Writes go through the gate too**, though pre-rendering only ever issues reads. The uniform rule —
everything reaching a service is gated — is easier to hold than an exception list, and the write
methods on these shims cost nothing to include because nothing calls them during a pre-render.

**The regression is pinned without a database.** `PrerenderGateTests` drives the shim with a
substituted service that counts its own overlaps, which states the property under test — two callers
were inside at once — far more precisely than waiting for EF Core to throw, and runs in
milliseconds.

## Alternatives considered

**Take an `IDbContextFactory<ApplicationDbContext>` through the Core service layer.** The canonical
answer to this exception, and much the larger change: every Core service takes an
`ApplicationDbContext` by construction and several deliberately share one within a request, so a write
that spans services depends on a single change tracker. Rejected as the wrong size of change for the
problem, and recorded here as the move to make if the services ever need genuine parallelism rather
than merely non-collision.

**Register `ApplicationDbContext` as transient.** Does not fix it. Two components calling one scoped
service still reach one context, which is exactly the reported crash, and it quietly breaks the
unit-of-work every write path assumes. Rejected.

**Create a fresh DI scope per shim call.** Fixes it, at a service graph per call, and hands each call
its own change tracker — so the shims would stop behaving like the endpoints they mirror, on the very
axis the pre-render pattern exists to keep identical. It is also the same per-method edit as the gate,
for more moving parts. Rejected.

**Cache the block type list for the request.** It would have made this particular crash go away, which
is what makes it the dangerous option: the next pair of editors that fetch different things
reproduces it, and by then the cache looks like the fix that already handled this. Rejected.

**Have the page editor load everything and pass it down as parameters.** Pushes a server-only
constraint into components that also run in the browser, and grows the editor's parameter surface for
every new field type. Rejected — it also cannot cover screens outside the page editor.
