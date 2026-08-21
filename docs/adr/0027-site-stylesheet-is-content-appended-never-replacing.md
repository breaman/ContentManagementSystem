# 0027 — The site stylesheet is content, appended and never replacing

- **Identifier:** D27
- **Status:** Accepted
- **Source:** task `P10-01`, [`spec.md` §30](../../spec.md#30-site-stylesheet), requirement `R-15`

## Context

The original requirements asked for editable content, editable structure, and editable media, and
said nothing about appearance. That omission is not neutral: every phase up to this one made content
changeable in minutes while leaving a margin, a colour, or a hidden element a developer task with a
build and a deployment behind it. Organisations do not wait for that. They route around it, and the
route is an inline `style` attribute typed into a rich-text zone — which the sanitizer permits under
the `Extended` profile, which no reviewer sees, and which has to be found and edited on every page
when the design changes.

So the question was not whether an administrator gets to change how the site looks. It was where the
change is allowed to land.

Three options were on the table.

1. **Editing `styles/site.scss` through the CMS.** The site's real source. It is also the
   *backoffice's* source — one compiled `site.css` serves both front doors — so a variable override
   restyles the screens the CMS is administered from, and a syntax error takes out the editor as well
   as the site. It needs a Sass compiler in the application, and it turns a file that is reviewed in a
   pull request into a file two systems write.
2. **A theme model** — a set of named tokens (brand colour, heading font, spacing scale) rendered into
   CSS custom properties. Safe, and it forecloses the long tail. Every request that is not a token is
   back to a developer, and the long tail is most of the requests.
3. **A plain CSS file, authored in the CMS, appended after the compiled stylesheet.**

## Decision

**Option 3, and the stylesheet is treated as content rather than as configuration.**

Two halves, and both matter.

**It is appended, never substituted.** The public document links `site.css` and then
`site-custom.css`. Later rules of equal specificity win, so the administrator's sheet overrides
anything the shipped one sets, without either file referring to the other and without the shipped one
becoming editable. The backoffice document links only the first, so the sheet cannot reach the screens
that administer it — including the button that reverts it.

**It gets content's lifecycle.** A draft, a published copy, a revision per publish, `rowversion`
concurrency with a `409` that carries the draft that won, and a revert. This is [ADR 0003](0003-publish-snapshots-the-draft.md)'s
promise — the public keeps seeing what was published while somebody works on the next thing — applied
to styling, which is the only way an administrator can develop a redesign against real pages without
the site wearing it half-finished.

**It is validated, and a refusal refuses the whole save.** `@import`, `url()` naming another host,
`expression()`, `behavior`, `-moz-binding`, `javascript:` values, and anything over 256 KB
([§30.5](../../spec.md#305-what-is-refused-and-why)). The validator parses rather than pattern-matches.
Unlike HTML sanitization ([ADR 0008](0008-sanitize-on-write-and-on-render.md)), nothing is silently
stripped: an author writing prose cannot be asked to fix a `<script>` they pasted out of Word, and an
administrator writing CSS can be asked to delete an `@import` they typed. Editing somebody's CSS
behind their back produces a file that does not match what they wrote and a bug they cannot reproduce.

**The URL is stable and revalidated, not fingerprinted.** `/css/site-custom.css` with an `ETag` of the
published hash, output-cached under the `sitecss` tag and evicted through the outbox on publish. A
content hash in the URL would serve the stylesheet more cheaply and would appear in the `<head>` of
every cached page, so every stylesheet publish would evict the entire site — a full re-render of every
page in exchange for one saved revalidation per visit.

One case does evict the site, and it is the **transition** rather than the content: the document omits
the `<link>` entirely while nothing is published, so the first publish and a revert-to-nothing change
every page's markup rather than only the bytes the link points at. A page cached before that moment
has no link in it and would go on having none until its hour was up. Those two operations enqueue
`content` alongside `sitecss`; every publish after the first enqueues `sitecss` alone.

## Consequences

- **A stylesheet cannot fail loudly.** CSS does not throw; a sheet that makes the site unreadable
  still returns `200 OK`. The mitigations are that revert is one action from a screen the sheet cannot
  affect, that "publish nothing" is always available, and that the public accessibility gate runs
  against the published stylesheet rather than only against the shipped one — contrast being the
  failure this feature makes easy to introduce and the one a machine can actually catch.
- **`Appearance.Edit` is a permission of its own**, held by `Administrator` and `Developer`. Folding
  it into `Settings.Edit` would have been one row less: the reason not to is that publishing CSS
  reaches every anonymous visitor immediately, with no draft state on the public side and no approval
  step, which is a different kind of act from setting a retention window — and keeping it separate is
  what lets a future `Designer` role exist without also handing over workflow mode and retention.
- **The public CSP is unchanged, and that is load-bearing.** `style-src 'self'` already admits a
  stylesheet served from this origin, and the off-origin `url()` refusal is what keeps
  `font-src 'self'` and `img-src` from needing to be widened for a web font somebody pasted
  ([ADR 0026](0026-three-content-security-policies-public-carries-no-nonce.md)). Widening the
  validator is therefore a CSP decision as well as a validator change.
- **Media in the stylesheet works, and only through the library.** A background image is referenced by
  its ordinary same-origin `/media/...` URL, so it is an asset somebody uploaded, described, and can
  find in where-used — rather than a hotlink to a host nobody controls.
- **Preview needed a second stylesheet route, and it is not simply gated.** `/preview/site-custom.css`
  serves the **draft** to a caller holding `Appearance.Edit` and the **published copy** to anyone
  else, `no-store` either way. The fallback is what keeps a *shared* preview link honest: those are
  opened by approvers and clients with no account, they are meant to see unpublished content, and a
  frame refused its stylesheet would show them a page that looks nothing like the one they are being
  asked to approve. The unpublished *design* is a different thing from the unpublished content, and
  only the first stays gated. The decision is inside the handler rather than on the route, because a
  route that refuses cannot fall back.
- **The Sass escape hatch is unchanged.** What plain CSS cannot express — a new component, a changed
  grid, a Bootstrap variable — is still a developer editing `styles/site.scss` under review. This
  decision reduces how often that is needed; it does not claim to remove it.
