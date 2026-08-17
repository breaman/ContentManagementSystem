# Phase 6 — keyboard-only pass

**Task:** `P6-37` · **Criterion:** the whole authoring flow is operable without a pointer
([spec §28](../spec.md#28-accessibility)) · **Last run:** _not yet run against a browser_

This is the one Phase 6 gate that a test suite cannot close on its own, and the reason is worth
stating rather than working around: a keyboard pass is not a check that every control is reachable —
it is a check that reaching them is *bearable*. Focus that jumps somewhere unexpected, a dialog that
returns focus to the top of the document, forty Tab stops between an editor and the Publish button:
none of those fail an assertion, and all of them are why somebody stops using the keyboard.

## What the automated suites already prove

These do not need re-checking by hand. What they cover is stated so the manual pass can spend its
time on what they cannot.

| Behaviour | Where it is pinned |
|---|---|
| Blocks add, reorder, duplicate, delete entirely by button; the drag grip takes no Tab stop | `BlockListEditorTests` (`P6-30`, criterion `P6 #4`) |
| The tree's context menu opens on <kbd>Shift</kbd>+<kbd>F10</kbd> **and** the Context Menu key, walks with the arrows, and closes on <kbd>Esc</kbd> | `ContentTreeMenuTests` (`P6-04`) |
| Tree navigation by arrows with a roving `tabindex`, so <kbd>Tab</kbd> steps past the whole tree | `ContentTreeTests` (`P6-02`) |
| Moving a page by keyboard — <kbd>Alt</kbd> plus the arrows | `ContentTreeMoveTests` (`P6-03`) |
| Pane resizing by keyboard — arrows, <kbd>Shift</kbd> for a coarse step, <kbd>Home</kbd>/<kbd>End</kbd> | `AdminShell` (`P6-01`) |
| Shortcuts match only their own chords, and every one is documented in the reference dialog | `ShortcutTests` (`P6-23`) |
| No ARIA, label, or focus-order violation on any backoffice screen | `PageScreenAccessibilityTests`, `StructureScreenAccessibilityTests` (`P6-36`) |
| Every status is a word as well as an icon | `ReducedMotionTests` (`P6-39`) |

## What a person still has to do

Run against the real application in a browser, with the pointer physically unplugged rather than
merely unused — the second is how a pass accidentally uses a mouse for one step and does not notice.

1. **Create a page.** From `/admin` to a saved draft without touching the pointer: dashboard → tree →
   new child → template → title → first zone.
2. **Fill every field type.** Tab into each control on a page whose template declares one of each,
   including the block list, the rich-text surface in all three modes, and each picker. CodeMirror and
   Quill are the two to watch: <kbd>Tab</kbd> must move focus *out* of the editor rather than
   indenting, or the editor is a keyboard trap ([§28](../spec.md#28-accessibility)).
3. **Every dialog.** Publish, conflict, unpublish, delete, branch publish, permanent delete, shortcut
   reference. For each: focus lands inside on open, <kbd>Tab</kbd> cannot leave, <kbd>Esc</kbd> backs
   out, and focus returns to the control that opened it.
4. **Publish it**, acknowledge a warning, and follow a deep link from the publish dialog into the
   zone it names — focus must land *in* the zone, not merely scroll it into view.
5. **Recover from a conflict.** Two browsers, same draft: the losing save must present all three
   options and each must be reachable and operable.
6. **The tree at depth.** Expand, collapse, move a page, and paste one, on a site deep enough that the
   target is off screen.

Record the date and the browser at the top of this file when it is run. A pass that is not written
down is one nobody can tell from a pass that never happened.

## Known gaps to check first

- The three-pane shell (`P6-01`) is not yet mounted by any route, so the panes are currently reached
  through the individual screens. When the shell is composed, the pass has to be run again: pane order
  and the Tab path between panes are exactly what changes.
- `P6-32` to `P6-34` — the browser journeys for the full editor flow, autosave over a flaky network,
  and the conflict dialog — are still open. Until they land, steps 4 and 5 above are the only coverage
  those paths have in a real browser.
