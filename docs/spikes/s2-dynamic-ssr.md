# S2 — Dynamic component rendering under static SSR

**Task:** `P0-04` · **Timebox:** 2 ed · **Date:** 2026-08-12
**Code:** [`spikes/S2.DynamicSsr`](../../spikes/S2.DynamicSsr) — throwaway, not in the solution
**Spec:** [§5.3](../../spec.md#53-the-two-front-doors), [§15.2](../../spec.md#152-rendering-pipeline),
[§15.3](../../spec.md#153-fallback-behavior), [§16.2](../../spec.md#162-cache-tags)

## Recommendation: **GO**

`DynamicComponent` composes template → zone → field renderer → block component correctly with no
interactive render mode, and an `ErrorBoundary` isolates a failing block in all three failure shapes
that matter. The source-generated static render switch fallback is not needed.

`P3-08` through `P3-13` can be built as planned. Five implementation constraints came out of the
spike and are listed under [Consequences](#consequences-for-phase-3).

---

## The question

> Does `DynamicComponent` compose template → zone → field renderer correctly with no interactive
> render mode, and does an error boundary isolate a failing block?
>
> Fallback if no: source-generate a static render switch per template.

Risk **R7** — `DynamicComponent` under static SSR misbehaves.

## What was built

A minimal ASP.NET Core app registering `AddRazorComponents()` and **nothing else** — no
`AddInteractiveServerComponents`, no `AddInteractiveWebAssemblyComponents`, no `@rendermode` anywhere:

- `CmsRenderContext`, `CmsTemplateBase`, `CmsZone`, `CmsErrorBoundary`, `[CmsTemplate]` /
  `[CmsBlockType]` discovery — the [§15.2](../../spec.md#152-rendering-pipeline) shapes.
- Two templates, four block types (two of which are deliberately broken), five field renderers.
- Ten sample pages, one per row of the [§15.3](../../spec.md#153-fallback-behavior) fallback matrix.
- Two delivery strategies compared: `HtmlRenderer` → string → headers → write, and
  `RazorComponentResult`.

Run it with `dotnet run --project spikes/S2.DynamicSsr -c Release`. **40 checks, 40 passing.**

## Findings

### 1. The composition works, four levels deep — GO

Template → `CmsZone` → field renderer → block component → *nested* field renderer all resolve through
`DynamicComponent` with types looked up at render time. Nothing is switched on statically, and the
template component names only the zone key:

```razor
<article class="landing">
    <header><CmsZone Name="hero" /></header>
    <div class="container"><CmsZone Name="body" /></div>
    <footer><CmsZone Name="footer" /></footer>
</article>
```

Also confirmed:

- **A zone declared by the template but absent from the payload renders empty.** Adding a zone to a
  template cannot break already-published pages, which is what makes the
  [§8.5](../../spec.md#85-template-evolution-and-schema-safety) "add is free" rule true at render
  time as well as at validation time.
- **`await` works.** A field renderer that awaits a repository in `OnParametersSetAsync` (the
  reusable-content resolver, `P4-06`) and a block that awaits before rendering both complete before
  the response is written. Static SSR waits for quiescence.
- **The output is genuinely static.** No `<!--Blazor:` markers, no `blazor.web.js`, no `_framework`
  references. There is nothing to hydrate, which is the precondition for output caching in `P8`.

### 2. Error boundaries isolate a failing block in all three failure shapes — GO, and this was the risk

A page with a healthy block, a **failing** block, and another healthy block, in one zone:

| Failure shape | Siblings | Other zones | Status | Logged |
|---|---|---|---|---|
| throws from `OnParametersSet` | ✅ render | ✅ render | 200 | ✅ |
| throws from `BuildRenderTree` (mid-markup) | ✅ render | ✅ render | 200 | ✅ |
| throws from `OnParametersSetAsync`, **after** an await | ✅ render | ✅ render | 200 | ✅ |

The rendered zone, verbatim:

```html
<div class="blocks" data-zone="hero">
  <figure class="quote"><blockquote>Sibling before the failure.</blockquote>…</figure>
  <span data-cms-block-failed="22222222-2222-4222-8222-222222222222"></span>
  <figure class="quote"><blockquote>Sibling after the failure.</blockquote>…</figure>
</div>
```

Two details worth having tested rather than assumed:

- **Half-written markup does not leak.** The `BuildRenderTree` case emits an element and *then*
  throws. That element does not appear in the response — the boundary discards the failing subtree
  rather than flushing what it had. A renderer that fails halfway cannot corrupt the page.
- **The post-await failure is caught too.** This is the case a naive `try`/`catch` around the render
  call misses, because the exception surfaces on a continuation rather than on the calling stack.

**Derive the boundary from `ErrorBoundaryBase`, not from `ErrorBoundary`.** Overriding
`OnErrorAsync` is what gets the page id, zone key, version id, and block id into the log line, which
is acceptance criterion `P3 #8`:

```
render.failure kind=block zone=hero block=22222222-… page=4 version=1004 exception=InvalidOperationException
```

The stock `ErrorBoundary` renders "An error has occurred." — never acceptable on a public page.

### 3. The full §15.3 fallback matrix behaves — GO

| Condition | Observed |
|---|---|
| Unknown `templateKey` | Fallback layout with the page's text content, HTTP 200, recorded for the `cms-templates` health check |
| Unknown field type key | Zone renders nothing, other zones unaffected, warning logged |
| Unknown block type key | Block skipped, siblings render, warning logged |
| Referenced media missing | Placeholder carrying the alt text, warning logged |
| Referenced reusable content unpublished | Renders nothing, warning logged |
| Renderer throws | Boundary isolates it, per finding 2 |

Every one is a logged non-event. Nothing reached the client as a stack trace or a blank page.

### 4. Cache tags accumulate during render, and both delivery strategies can carry them — GO

Tags added by field renderers *while rendering* (`media:812` from the media renderer, `ru:3` from the
reusable renderer) reached the response header under both strategies. `RazorComponentResult` works
because the response is buffered — `Response.OnStarting` still fires after the render completes.

> **The caveat that matters:** that holds only while nothing streams. The moment a component is
> marked `[StreamRendering]`, or `RazorComponentResult.PreventStreamingRendering` is left off for
> something that streams, headers go out before the render finishes and the tag set is silently
> incomplete — producing a page that never invalidates. **Render to a buffer, then set headers, then
> write.** `P3-12`/`P3-13` should take the `HtmlRenderer`-to-string path, which cannot regress this
> way, and public delivery components must never opt into streaming.

### 5. Render cost is not a concern — GO

Server render only, Release, Apple Silicon, p50/p95 over 200 iterations after warmup:

| Page | Blocks | p50 | p95 |
|---|---:|---:|---:|
| Typical marketing page | 2 | 0.05 ms | 0.06 ms |
| Large page | 50 | 0.37 ms | 0.40 ms |

Roughly 7 µs per block, including a fresh DI scope and a new `HtmlRenderer` per render. Against
NFR-1's page-render budget this is negligible, and output caching (`P8`) has not been applied yet.

These numbers exclude database access, which is where the real cost will be. Treat them as the
**rendering floor** for the `P3-27` benchmark harness, not as a projected page latency.

### 6. Two traps found by accident, both worth knowing before Phase 3

**The Razor compiler strips HTML comments from `.razor` markup.** `<!-- cms:fallback-template -->`
never reaches the output. This first showed up as a *passing* test that should have failed — an
assertion looking for a marker that could never be present was checking a condition that was
vacuously true. Markers, and any comment intended to survive to the client, must be elements or
attributes. Programmatic `builder.AddMarkupContent("<!-- … -->")` is unaffected.

**The spec's `RenderMode` name collides with `Microsoft.AspNetCore.Components.Web.RenderMode`.**
Every `.razor` file imports that namespace, so a CMS type called `RenderMode` is ambiguous in exactly
the files that need it most. Renamed to `CmsRenderMode` in the spike; `P3-08` should do the same.

## Consequences for Phase 3

1. **`P3-11` boundaries derive from `ErrorBoundaryBase`**, overriding `OnErrorAsync` to log page id,
   zone key, version id, and block id. Never the stock `ErrorBoundary`.
2. **Boundaries go at both levels** — per zone *and* per block. Zone-level alone would let one bad
   block blank an entire zone.
3. **`P3-12`/`P3-13` render to a buffer, then set headers, then write.** Public components never opt
   into streaming rendering; cache-tag correctness depends on it.
4. **Rename `RenderContext.RenderMode` to `CmsRenderMode`** in `P3-08`.
5. **Markers and structural hints are attributes, not HTML comments.**
6. **`@key` on each block** in the block-list renderer, keyed by the block GUID. Free correctness now,
   and required later when the same components are reused in the interactive editing canvas (`P6-06`).

## What this spike did not cover

- **Output caching and tag eviction** (`P8`). The spike proves tags can be *collected* and applied to
  the response, not that eviction works.
- **`RenderContext` under concurrency.** The tag set is per-render here. If a future
  `PublishedContentService` shares a context across requests, the `ISet<string>` needs to be
  per-request — worth a note in `P3-12`.
- **Real templates and styling.** Two reference templates and three reference block types are `P3-10`.
- **Interactive islands** ([§15.4](../../spec.md#154-response-shape)). No component in the spike opts
  into `InteractiveWebAssembly`; the mixed case is Phase 6's problem.
