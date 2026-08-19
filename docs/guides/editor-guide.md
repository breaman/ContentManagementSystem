# Editor guide

**Task:** `P9-21` · **Criterion:** `P9 #7` — an editor unfamiliar with the system completes
create → publish using only this guide.

This is the guide that criterion is measured against. If you get stuck, the place you got stuck is a
defect in this document, not in you — say where.

---

## What you are looking at

`/admin` is three panes.

- **Left — the content tree.** Every page on the site, in the shape visitors navigate it. A page's
  position here is its address: a page under *Products* is at `/products/…`.
- **Middle — the page.** Its content, one card per zone.
- **Right — properties.** Everything about the page that is not its content: its name, its address,
  its search and social settings, its tags, and who owns it.

`/admin` on its own is the dashboard: what you have in progress, what publishes or expires this week,
what needs attention, and what has happened lately. Every tile links into the same list at full
length.

---

## Create a page and publish it

1. **Pick where it goes.** In the tree, find the page it belongs under. A page at the top level of
   the site goes under the root.
2. **Right-click it** (or press <kbd>Shift</kbd>+<kbd>F10</kbd>) and choose **New child page**.
3. **Choose a template.** The template decides what content the page can hold. You cannot change it
   later, so if the list does not have what you need, ask a developer rather than picking the closest
   one — the closest one becomes permanent.
4. **Give it a name.** The address is generated from it — *Autumn campaign 2026* becomes
   `autumn-campaign-2026` — and you can override the address in the properties pane if you need to.
5. **Fill the zones.** Each card is one zone. Required ones are marked; the rest are optional and an
   empty one renders nothing.
6. **It saves itself.** Twenty seconds after you stop typing, and again when you leave. The indicator
   above the content says where it is up to. You can save now with <kbd>Ctrl</kbd>+<kbd>S</kbd>.
7. **Preview.** <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>P</kbd>, or the Preview button. This is the
   page as a visitor will see it, in the real template — not an approximation. You can switch device
   widths, and you can share a preview link with somebody who has no account.
8. **Publish.** The publish dialog tells you what is wrong before it lets you, grouped by zone with a
   link into each card. **Errors stop a publish; warnings do not** — see below.
9. **Check it.** The published page is at the address shown in the properties pane.

### Saving is not publishing

Editing never touches the live page. Your draft and the published version are separate; the site
keeps serving what was published until you publish again. This is why you can leave something
half-written for a week without anybody seeing it.

---

## What the publish dialog tells you

**Errors** stop the publish. Each one is something that would be broken or missing on the live page:

- a required zone left empty
- an image with no alt text — a picture nobody can describe is invisible to a reader using a screen
  reader, and it is the one accessibility rule that blocks rather than warns
- a link to a page that has been deleted
- another page already published at this address

**Warnings** do not stop it. They are things that will work and will be worse than they need to be —
publish anyway if you have a reason, and if you find yourself dismissing the same warning every time,
say so, because that means it is calibrated wrong:

- a link to a page that is not published yet (publishing a section top-down is ordinary work)
- **a skipped heading level** — an *Heading 2* followed by an *Heading 4*. People using a screen
  reader move through a page by heading, so a skipped level reads as a section that is not there
- **link text that does not say where it goes** — "click here", "read more", or a pasted URL. A
  screen reader can list every link on a page, and a list of eleven entries reading "read more" is a
  list of nothing. Write *the 2026 pricing table*, not *click here*
- **a table with no header cells** — every cell then gets read out without the column it belongs to

---

## Writing in a zone

**Rich text** is the ordinary one. The toolbar is short on purpose: everything on it survives being
saved. If you paste from Word or a web page, formatting that is not on the toolbar is removed — that
is the system keeping the site consistent, not losing your work. The words always survive.

Headings start at **Heading 2**. The page title is the Heading 1 and there is only ever one.

**Links** and **images** open a picker rather than a box you type a URL into. That is what lets the
system move a page later without breaking every link to it: internal links follow the page, not the
address.

**Markdown** and **HTML** modes are available on some zones for people who prefer them. The HTML mode
shows you what will be removed as you type; the removal happens whichever mode you use.

---

## Media

Upload from the picker, or from the media library at **Media**. Once uploaded:

- **Alt text is required.** Describe what the picture shows, in a sentence. If it is purely decorative
  and carries no information, say so with the decorative toggle rather than leaving it blank.
- **Cropping and adjustments are non-destructive.** The original is kept, always; *Revert* takes you
  back to it at any point, however long afterwards.
- **Replace** puts new bytes behind the same picture, and every page using it updates. Use this for a
  corrected version of the same image — not for a different image, which should be a new upload.
- **Deleting** puts an item in the bin. Deleting it *permanently* asks what uses it first and refuses
  while anything does.

---

## Versions, and getting something back

Every publish keeps the version it published. **History** in the properties pane lists them, with who
and when, and lets you compare any two side by side — changed words are highlighted rather than whole
paragraphs marked as different.

- **Restore** brings an old version back as your current draft. It does not publish it, so you can
  look at it first.
- **Name a version** to mark it as a checkpoint. Named versions are never pruned by the retention
  sweep; unnamed ones can be, after the site's retention window.

A deleted page goes to the **recycle bin** with everything under it, and restores with its history.
Only permanent deletion is irreversible, and it asks you to type the page's name.

---

## Working with other people

- **Somebody else is editing.** You will see who. The system does not lock them out — it warns you
  both, and if you save over each other it offers *keep mine*, *take theirs*, or *show me the
  difference* rather than silently picking one.
- **Review.** Depending on how the site is configured, publishing may require an approval. Submit for
  review, and an approver gets a notification. **The draft is frozen while it is under review** — an
  approval has to be a statement about the content that then publishes. A rejection hands you back an
  editable copy with the comments attached.
- **Comments** live on the page, not on a version, so they survive a rejection and a restore.

---

## Scheduling

Set a publish date, an expiry date, or both, in the properties pane. The dialog states the exact
instant your wall-clock time means, offset and all, before anything is saved — so a page scheduled
across a daylight-saving boundary goes live when you meant.

A scheduled publish runs as **you**, and is refused if you would be refused when it fires. If it fails
validation it stops and tells you, rather than retrying every thirty seconds forever.

---

## Keyboard

Press <kbd>?</kbd> for the full list. The ones worth learning:

| | |
|---|---|
| <kbd>Ctrl</kbd>+<kbd>S</kbd> | Save the draft |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>P</kbd> | Preview |
| <kbd>Ctrl</kbd>+<kbd>Enter</kbd> | Publish |
| <kbd>/</kbd> | Search |
| <kbd>Shift</kbd>+<kbd>F10</kbd> | The tree's menu, on the selected page |
| <kbd>Alt</kbd>+arrows | Move the selected page in the tree |

Everything on that list is also a button on the screen. Nothing in this system needs a mouse.
