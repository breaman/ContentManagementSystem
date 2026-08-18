# 0022 — One request's `DbContext` is used once at a time: the pre-render shims serialize, the delivery readers open their own

- **Identifier:** D22
- **Status:** Accepted
- **Source:** the `/admin/pages/{id}` pre-render failure and the reusable-footer delivery failure;
  governs the shims of tasks `P1-29`, `P2-23`, `P4-11`, `P5-22`, `P6-24` and the delivery readers of
  `P3-13`, `P4-06`

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

### The same bug on the public site

The first version of this ADR fixed the backoffice and stopped there, on the reading that the
collision belonged to pre-rendering. It does not. It belongs to *rendering*, and the public delivery
path renders the same way.

`CmsPageRenderer` builds its `HtmlRenderer` over the request's service provider, so every field
renderer in a page resolves the one scoped `ApplicationDbContext` — and five of them read the
database: `ReusableRenderer` through `ReusableContentResolver`, `MediaRenderer` and
`MediaListRenderer` through `MediaResolver`, `LinkRenderer` and `PageReferenceRenderer` through
`LinkResolver`. A template with a media zone and a reusable footer — `marketing-landing`, which is
the one the demo uses — has two of those in flight at once, and the footer is the one that loses:

```
System.InvalidOperationException: A second operation was started on this context instance before a
previous operation completed.
   at ContentManagementSystem.Core.Delivery.ReusableContentResolver.ResolveAsync(...)
   at ContentManagementSystem.Rendering.Fields.ReusableRenderer.OnParametersSetAsync()

CMS render failure in Zone 'footer' on page 1, version 1. The rest of the page still renders.
```

Delivery's degradation rule (spec section 15.3) is what made it survivable and what made it easy to
miss: the zone rendered nothing, the page around it was fine, and the only symptom was a footer
missing from every page on the site.

`DatabaseContentSchemaCatalog` is the sharper edge of the same problem. Its interface is synchronous
by design, so on a cache miss it issues a *blocking* query — and a synchronous query is the one thing
that can land in the middle of an in-flight asynchronous one, because `BlocksRenderer.OnParametersSet`
runs while a sibling renderer is still awaiting. A gate cannot cover that without being reentrant:
`ReusableContentResolver` asks the catalog from inside its own resolve, so one non-reentrant
semaphore around both would deadlock instead of colliding.

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

**5. The delivery readers take `IDbContextFactory<ApplicationDbContext>` and open a context per
call.** `ReusableContentResolver`, `MediaResolver`, `LinkResolver`, and
`DatabaseContentSchemaCatalog` — the four types a renderer can reach the database through, and the
only four.

```csharp
public sealed class MediaResolver(IDbContextFactory<ApplicationDbContext> contexts) : IMediaResolver
{
    await using var context = await contexts.CreateDbContextAsync(cancellationToken);
```

A gate is the wrong instrument here for the reason the context section gives — the synchronous
catalog would have to be inside the same critical section as the asynchronous resolver that calls
it, and the semaphore is not reentrant. A context per call also fits what these four actually are:
every query is `AsNoTracking`, none of them writes, and so there is no unit of work that sharing one
context preserves. This is the narrow version of the alternative rejected below, and it is narrow on
purpose: it applies to reads on the render path, not to the service layer.

**6. `Program.cs` registers `AddDbContextFactory<ApplicationDbContext>(…, ServiceLifetime.Scoped)`
in place of `AddDbContext`.** Since EF Core 6 the factory registration also registers the context
itself as a scoped service, so every service that takes an `ApplicationDbContext` is unaffected.
`ServiceLifetime.Scoped` rather than the default singleton is load-bearing twice: it keeps
`DbContextOptions` scoped, which is the lifetime `EnrichSqlServerDbContext` preserves when it patches
that descriptor, and it gives the factory a scoped provider to build from — a singleton factory
cannot resolve the scoped `IUserService` the context's constructor takes.

That constructor now carries `[ActivatorUtilitiesConstructor]`. `DbContextFactory` builds contexts
through `ActivatorUtilities`, which — unlike the service provider's greedy rule — refuses to choose
between constructors that all accept the arguments it was given, and `ApplicationDbContext` has
three. Without the attribute the host fails at startup with "multiple constructors accepting all
given argument types", which is worth stating because `WebApplicationFactory` reports it as an
unrelated `ObjectDisposedException` on a disposed `IServiceProvider`.

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

**The two halves are fixed differently, and the difference is not arbitrary.** The shims serialize
because they front the whole service layer, writes included, where one change tracker per request is
the contract. The delivery readers open their own contexts because they are four read-only queries
with no contract to keep. Stated the other way round: the gate is for code that must keep sharing a
context, the factory is for code that never needed to.

**Delivery renders its reads in parallel now, rather than merely without colliding.** A page with a
hero image and a reusable footer issues both queries at once on separate connections. That is a small
win and not the reason for the change — the reason is that they stop being refused — but it is the
one respect in which delivery came out ahead of where it was before the bug existed.

**A new delivery-path read that takes an `ApplicationDbContext` is a latent version of this bug.**
The reviewable shape is narrow: a type a field renderer can reach that names `ApplicationDbContext`
in its constructor rather than `IDbContextFactory<ApplicationDbContext>`.

**The regressions are pinned at both levels.** `PrerenderGateTests` drives the shim with a
substituted service that counts its own overlaps, which states the property under test — two callers
were inside at once — far more precisely than waiting for EF Core to throw, and runs in
milliseconds. `ConcurrentRenderReadTests` publishes a page with a media zone and a reusable footer
and fetches it anonymously, which is the delivery failure exactly as it was reported; before the fix
it fails with the footer missing and the render-failure log line above.

## Alternatives considered

**Take an `IDbContextFactory<ApplicationDbContext>` through the Core service layer.** The canonical
answer to this exception, and much the larger change: every Core service takes an
`ApplicationDbContext` by construction and several deliberately share one within a request, so a write
that spans services depends on a single change tracker. Rejected for the service layer as the wrong
size of change for the problem. Decision 5 takes it for the four delivery readers, where the
objection does not apply: those are read-only, `AsNoTracking`, and share a context with nothing.

**Extend `PrerenderGate` to the delivery path.** The consistent-looking answer, and wrong here. The
semaphore is not reentrant and `ReusableContentResolver` calls `DatabaseContentSchemaCatalog` from
inside a resolve, so gating both deadlocks; gating only the asynchronous resolvers leaves the
catalog's synchronous query free to land in the middle of an in-flight one, which is the same bug
with a smaller window. Making the gate reentrant with an `AsyncLocal` depth counter would work and is
more subtle than the factory, for a path that wanted no serialization in the first place. Rejected.

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
