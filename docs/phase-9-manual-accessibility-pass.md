# Phase 9 — manual keyboard and screen-reader pass

**Task:** `P9-08` · **Criterion:** `P9 #2` — WCAG 2.2 AA verified on backoffice and public output
([spec §28](../spec.md#28-accessibility)) · **Last run:** _not yet run_

The automated gates answer "is this markup conformant". This answers the question they cannot: **is
it usable**. A screen reader announcing "button, button, button" violates nothing; a focus ring that
lands somewhere sensible and a focus ring that lands somewhere technically correct are the same to
axe and are not the same to a person.

It builds on [`phase-6-keyboard-pass.md`](phase-6-keyboard-pass.md), which covers the authoring flow
by keyboard. What is new here is the **screen-reader** half, and the **public site** — which Phase 6
never looked at, because the public site is where the audience is.

## What the automated suites already prove

Stated so the manual pass can spend its time on what they cannot.

| Behaviour | Where it is pinned |
|---|---|
| No ARIA, label, name, contrast, or focus-order violation on any backoffice screen | `PageScreenAccessibilityTests`, `StructureScreenAccessibilityTests` (`P6-36`) |
| The same, on the **public document** — the real `CmsDeliveryDocument`, full and near-empty | `PublicPageAccessibilityTests` (`P9-07`) |
| `lang` comes from `SiteSettings.Culture` rather than a hard-coded literal | `PublicPageAccessibilityTests` (`P9-10`) |
| Exactly one `h1` per public page, and it is the page title | `PublicPageAccessibilityTests` (`P9-07`) |
| Navigation is a named landmark | `PublicPageAccessibilityTests` (`P8-17`, `P9-07`) |
| Backoffice and public page both reflow at 200% zoom without horizontal scrolling | `ZoomTests` (`P6-38`, `P9-09`) |
| Nothing animates when the browser asks for reduced motion, on either surface | `ReducedMotionTests` (`P6-39`, `P9-09`) |
| Authored content is warned about skipped headings, uninformative link text, and headerless tables at publish | `AuthoredAccessibilityValidatorTests` (`P9-10`) |
| Every keyboard affordance in the authoring flow — block reordering, the tree, the panes, the dialogs | see the Phase 6 pass |

## What a person still has to do

Two passes, on two machines, because the two screen readers disagree about enough to matter.

### 1. NVDA on Windows, in Firefox and in Chrome

**The public site first.** It is the larger audience and the shorter list.

1. **Read a page from the top.** The title should be announced as a heading level 1 and should be the
   page's, not the site's. The navigation should be announced as a named landmark before the main
   content, and it must be possible to skip past it in one action.
2. **Navigate by heading** (<kbd>H</kbd>). The sequence should describe the page. This is where a
   skipped level stops being a warning in the publish dialog and becomes a section that is not there.
3. **Navigate by landmark** (<kbd>D</kbd>). Banner, navigation, main, contentinfo — each announced
   once and named where there are two of a kind.
4. **List the links** (<kbd>Insert</kbd>+<kbd>F7</kbd>). Every entry should say where it goes without
   its surrounding sentence. `P9-10` warns an editor about this at publish; this is the check that
   the warning is calibrated — too many "read more" entries getting through means the list is too
   short, and editors dismissing the warning means it is too long.
5. **Read a table.** Row and column headers should be announced with each cell. A table authored
   without `scope` is the case to try deliberately.
6. **An image.** Its alt text should be announced, and a decorative image should not be announced at
   all.

**Then the backoffice.**

7. **Autosave and validation announcements.** Save, wait, and confirm the live region announces the
   result without stealing focus. Then trigger a validation failure and confirm the same. A live
   region that is polite when it should be assertive says nothing anybody hears; one that is
   assertive when it should be polite interrupts typing.
8. **The tree.** Each row should announce its label, its level, its position among siblings, and its
   status — the status is the one likely to be missing, because it is a coloured dot to a sighted
   user and `P6-39` gave it a text alternative that this pass is verifying is actually announced.
9. **The two editors.** CodeMirror and Quill both take over a region and both have their own
   accessibility stories. Confirm entering and leaving each is announced and that neither is a
   keyboard trap.
10. **Every dialog.** Announced by name on open, focus inside, and — the one usually missed — the
    page behind it inert, so browsing by heading does not wander out of the dialog.

### 2. VoiceOver on macOS, in Safari

The same list. VoiceOver differs from NVDA in enough places that "it works in NVDA" is not evidence:

- It reads table headers differently and is stricter about `scope`.
- It handles `aria-live` regions differently, particularly a region that is added to the DOM rather
  than updated in place.
- Rotor navigation (<kbd>VO</kbd>+<kbd>U</kbd>) is the equivalent of the element lists above and is
  the fastest way to see the page as a structure.
- Safari applies `prefers-reduced-motion` from the system setting; confirm it is on before judging.

### 3. Keyboard-only, pointer physically unplugged

Per the Phase 6 pass, plus the public site: every link and control reachable, a visible focus
indicator on each, and no focus trap. **Unplug the pointer rather than merely not using it** — that
is how a pass accidentally uses a mouse for one step and does not notice.

## Recording the result

Findings go in a table below with a severity and an owner. `P9-11` closes when there are no critical
or serious ones left; anything moderate that is not fixed gets an owner and a date, per `P9 #1`'s
shape for the security pass.

| # | Surface | Screen reader | Finding | Severity | Owner | Resolved |
|---|---|---|---|---|---|---|
| — | — | — | _no findings recorded; the pass has not been run_ | — | — | — |
