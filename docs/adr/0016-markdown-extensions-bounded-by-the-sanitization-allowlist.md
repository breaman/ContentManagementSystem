# 0016 — Markdown extensions are bounded by the sanitization allowlist

- **Identifier:** D16
- **Status:** Accepted
- **Source:** tasks `P1-18`, `P1-19`, [`spec.md` §14.4, §20.2](../../spec.md)

## Context

Markdig ships more than twenty extensions and `UseAdvancedExtensions()` turns most of them on in one
call. It is the obvious thing to write, and it is what almost every Markdig integration does.

The sanitization profiles in [§20.2](../../spec.md#202-html-sanitization) are small closed
allowlists. `Basic` is seventeen tags. Most of what those extensions add emits markup that is on none
of the three lists:

| Extension | Emits | Nearest profile that allows it |
|---|---|---|
| `EmphasisExtras` | `del`, `ins`, `mark`, `sub`, `sup` | none |
| `Footnotes` | `section`, `sup`, back-reference anchors | none |
| `Abbreviations` | `abbr` | none |
| `GenericAttributes` | arbitrary `id`, `class`, and attributes on any element | none |
| `DefinitionLists` | `dl`, `dt`, `dd` | none |
| `Figures` | `figure`, `figcaption` | `Extended` |
| `TaskLists` | `input type=checkbox` | none — `input` is refused under every profile |

An enabled extension whose output no profile carries is worse than a disabled one. The syntax
*works*: the author types `~~gone~~`, Markdig renders `<del>gone</del>`, and the sanitizer unwraps it
to `gone`. Nothing errors. The feature appears to exist, is documented by every markdown cheat sheet
the author will find, and silently does nothing — which is the exact "the CMS ate my content"
failure [ADR 0008](0008-sanitize-on-write-and-on-render.md) is trying to avoid, arriving through the
front door instead of through a paste.

## Decision

**An extension is enabled only if some profile allows every element it emits. Enabling one and
widening a profile to carry it is a single decision, made together.**

`MarkdownRenderer` therefore builds a near-CommonMark pipeline with two extensions:

- **`PipeTables`** — a table is the one construct authors reach for that CommonMark cannot express,
  and `Extended` already carries the table tags.
- **`AutoLinks`** — emits `<a href>`, which every profile allows, and a bare URL rendering as plain
  text reads as a bug to everyone who writes one.

Everything else stays off until a profile is widened to carry it.

The rule cuts the other way too. `Basic` has no `h1` — the top-level heading belongs to the page
title, not to body content — so `# Title` in a markdown zone is unwrapped to its text. That is
deliberate and is pinned by a test, because the alternative reading of the allowlist deletes the
first line of every document written by someone used to typing `#`.

## Consequences

- Markdown authors get a smaller feature set than "Markdig with advanced extensions", and it is a set
  that actually survives to the page. `MarkdownRendererTests` pins what renders.
- Adding markdown syntax is a security review, because it means adding a tag to a profile in
  `SanitizationPolicy`. That is the correct amount of friction for widening an XSS allowlist, and
  `SanitizationPolicyTests` fails if the widening reaches something executable.
- Raw HTML parsing stays **enabled** in the pipeline. Disabling it would escape an author's pasted
  markup into visible angle brackets rather than cleaning it, and the design in
  [§20.2](../../spec.md#202-html-sanitization) is markdown → HTML → sanitize, with the sanitizer as
  the single gate. `richText` in markdown format is stored exactly as authored and is never
  sanitized on write, so this conversion is the *only* thing between a stored payload and a browser.
- The editor's preview and public delivery call one method on one registered `IMarkdownRenderer`,
  which is what makes acceptance criterion `P1 #7` structural rather than a promise to keep
  re-checking.
