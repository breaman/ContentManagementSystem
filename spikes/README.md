# Spikes

Throwaway code for the Phase 0 de-risking spikes (`P0-03`, `P0-04`, `P0-05` in
[`../task.md`](../task.md)).

**Nothing in this directory is part of the solution.** These projects are deliberately *not*
listed in `ContentManagementSystem.slnx`, so `dotnet build ContentManagementSystem.slnx` and CI
never touch them. They exist only as the evidence behind the findings in
[`../docs/spikes/`](../docs/spikes/), and can be deleted once Phase 1 starts.

| Project | Spike | Finding |
|---|---|---|
| `S1.RuntimeSchema` | Runtime-schema payload round trip | [`s1-runtime-schema.md`](../docs/spikes/s1-runtime-schema.md) |
| `S2.DynamicSsr` | `DynamicComponent` under static SSR | [`s2-dynamic-ssr.md`](../docs/spikes/s2-dynamic-ssr.md) |
| `S3.EditorInterop` | CodeMirror 6 / Quill interop in Blazor WASM | [`s3-editor-interop.md`](../docs/spikes/s3-editor-interop.md) |

Each project is a self-checking console/web app: run it and it prints a pass/fail report for the
questions the spike was asked.

```bash
dotnet run --project spikes/S1.RuntimeSchema -c Release
dotnet run --project spikes/S2.DynamicSsr -c Release

# S3 bundles CodeMirror and Quill from npm, and drives a real browser via Playwright.
# The bundle is generated, not committed, so build it first.
cd spikes/S3.EditorInterop && npm install && npm run build && cd -
dotnet run --project spikes/S3.EditorInterop/Server -c Release
```

S3 also serves the harness page on its own for poking at by hand:
`dotnet run --project spikes/S3.EditorInterop/Server -- --serve` → <http://127.0.0.1:5599>
(and <http://127.0.0.1:5599/no-nonce> for the CSP control case).
