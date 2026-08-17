# Demo Script — the first showing

**Purpose:** the first time anyone outside the team watches this system work. It performs the
**Phase 3 exit gate** (a person watches the publishing loop close), the **Phase 1 exit gate**
(`P1 #1`, a browser journey that has never been done), and shows the Phase 4, 5, and 6 work that has
landed since.

**Audience:** management / stakeholders. No CMS knowledge assumed.
**Running time:** ~50 minutes of demo, ~15 of questions. Acts 9 and 10 are cuttable; see §2.
**Last updated:** 2026-08-16

The loop the Phase 3 gate names, in order:

> define a template → create a page → fill zones → save draft → preview → publish →
> view anonymously → edit draft → confirm the public page is unchanged → publish again

Acts 1, 5, 6, 7, and 8 are those ten steps. Acts 2, 3, and 4 are the media library, reusable
content, and the real authoring surface — the three things that did not exist when this demo was
first drafted. Acts 9 and 10 are the safety net and the close.

---

## 0. Verification status — read this before you present

Checked against the working tree on **2026-08-16** (branch `main`, last commit `f4e6def`, with the
Phase 6 work staged and not yet committed — 69 changed paths).

### Build and tests

| | Result |
|---|---|
| `dotnet build ContentManagementSystem.slnx` | Succeeds, exit 0, **0 warnings** (`TreatWarningsAsErrors` is on solution-wide) |
| `Core.Tests` | **1331 passed**, 0 failed |
| `Server.Tests` | **380 passed**, 0 failed |
| `Client.Tests` | **217 passed**, 0 failed |
| `Data.Tests` | **42 passed**, 0 failed |
| `E2E.Tests` | **34 passed**, 0 failed |
| **Total** | **2004 passed, 0 failed** |

**The suite is green.** `Data.Tests` and `Server.Tests` run against real SQL Server in
Testcontainers and `E2E.Tests` drives Chromium, so it is not a fast check — `Server.Tests` alone
takes about seven minutes. **Run it yourself the morning of the demo:**

```bash
cd /Users/stokes/repos/breaman/ContentManagementSystem
dotnet build-server shutdown          # the RZ1021 trap; see §3
dotnet build ContentManagementSystem.slnx
dotnet test ContentManagementSystem.slnx --no-build
```

If anything is red, **say so in Act 0 rather than hoping**. The recovery line is in §3. If somebody
asks "is it all green?", the answer today is simply yes — roughly two thousand tests, none failing,
none skipped.

### 0.1 Where the plan stands

| Phase | Tasks done | Acceptance criteria | Exit gate |
|---|---|---|---|
| 0 — Foundations & spikes | 19/19 | all met | met 2026-08-12 |
| 1 — Content structure | 33/33 | 4/5 — `P1 #1` open | **open — Act 1 closes it** |
| 2 — Pages, versioning, publishing | 29/29 | 11/11 | met 2026-08-14 |
| 3 — Delivery, routing, preview | 28/31 | 11/11 | **open — Acts 5–8 close it** |
| 4 — Reusable content | 19/19 | 7/7 | met 2026-08-16 |
| 5 — Media library & image pipeline | 32/33 | 13/13 | met 2026-08-16 |
| 6 — Authoring experience | 36/41 | 12/14 | open |
| 7 — Workflow, permissions, scheduling | 0/26 | — | not started |
| 8 — SEO, caching, navigation, search | 0/26 | — | not started |
| 9 — Hardening, accessibility, launch | 0/24 | — | not started |
| **v1 total** | **196/281** | | |

**Performing this demo closes two exit gates**, which is worth saying out loud at the end:

- **`P1 #1`** — *"A Developer creates a template with four zones of differing field types through the
  admin form."* Never done in a browser. **Act 1 performs it**, and Phase 1's gate closes with it.
- **The Phase 3 exit gate** — the ten-step loop, watched by a person. **Acts 5–8 perform it.**

### 0.2 What is open, and none of it is on the demo path

| Task | What it is | Why it is open |
|---|---|---|
| `P3-27` | Performance benchmark harness with CI regression thresholds | No benchmark project in the solution. Needs a committed baseline and CI thresholds. |
| `P3-29` | Visual regression baseline (Playwright screenshots) | `E2E.Tests` holds accessibility, zoom, and editor-teardown tests; no screenshot baselines. |
| `P3-30` | Confirm **Q8** (legacy URL preservation) and test the redirect import path | **Blocked on a decision management owns.** See Act 10. |
| `P5-33` | Confirm **Q9** (retention on versions and audit logs) | **Blocked on Legal.** No longer blocking code: version retention is a configured number; audit-log retention is recorded as `P9-25`. |
| `P6-17` | Properties panel — *tags* and the *share image* | Neither has anywhere to be stored. `Tag`/`PageTag` is `P8-20`; `OgImageMediaId` is `P8-02`. The panel says so rather than drawing dead controls. |
| `P6-32`–`P6-34` | Browser journeys: full editor flow, autosave over a flaky network, save conflict | The E2E project renders components statically; these need a hosted app, a real Kestrel address, and a database. Harness work that does not exist yet. Everything they gate is asserted a level down. |
| `P6-37` | Manual keyboard-only pass | Written up in [`docs/phase-6-keyboard-pass.md`](./docs/phase-6-keyboard-pass.md), not yet performed. |

### 0.3 What this demo does *not* show, and why

Be explicit about this in Act 0. It prevents the "why does it look like that?" question landing
mid-flow, and two of these are things a stakeholder would otherwise assume from the plan.

- **There is no content tree in the running application, and no three-pane shell.** Both are built
  (`P6-01` to `P6-04`: lazy loading, virtualization at 500 siblings, drag and keyboard moves, the
  context menu, the delete-impact confirmation, branch publish, bulk operations) and both are
  covered by tests — but **`AdminShell` and `ContentTree` are not mounted by any route yet.** The
  content screen an editor reaches today is still the plain Phase 2 table at `/admin/pages`.
  Composing the shell is a wiring job, not new behaviour, and the panel and canvas were built to be
  moved. **Say this once, plainly, in Act 0.** Do not discover it live.
- **Bulk operations are therefore also unreachable from a screen.** `BulkOperationService` is
  finished and tested against a real database — a 100-page publish runs as a background job with
  per-item results — but the only UI that calls it is the tree's menu. The API is reachable.
- **The published page is still almost unstyled.** `site.scss` now carries a `.cms-content`
  typography layer and a large backoffice layer, but no `.cms-page` / `.cms-article` /
  `.cms-landing` rules, and the delivery renderers do not wrap prose in `.cms-content`. So the
  editor's preview pane has typography and the public page does not. That is correct for this phase
  — the CMS emits structure, a site's designer supplies the stylesheet — but it does mean the
  preview and the public page look different *typographically* while being identical *structurally*.
  Say: *"this is the markup a designer styles; what preview guarantees is the HTML, not the CSS."*
- **No workflow, approvals, scheduling, search, sitemap, navigation menus, or output caching.**
  Phases 7 and 8.
- **No user-management screen.** Roles are assigned by SQL in the pre-flight below; that screen is
  Phase 7 (`P7-01`).
- **No admin screen for the redirect table.** The API exists and is tested; Act 9 reads it as JSON.

---

## 1. Pre-flight — do this the day before, and again 90 minutes before

Do not do this in front of an audience. Several steps are slow, one requires reading a confirmation
link off a page, and one is a file you have to prepare.

### 1.1 Prepare the demo photograph

Act 2 shows GPS coordinates being stripped from an upload. You need a JPEG that actually carries
them. Any photo taken on a phone with location services on will do; confirm before the demo:

```bash
# exiftool, or any EXIF reader
exiftool -GPSLatitude -GPSLongitude -Orientation ~/Desktop/autumn-hero.jpg
```

You want at least one GPS tag present. Keep it around 2000–4000 px wide so the responsive rendition
set in Act 8 has something to work with. Have a **second copy under a different filename** — that is
the dedupe demonstration.

### 1.2 Start the stack

```bash
cd /Users/stokes/repos/breaman/ContentManagementSystem
dotnet build-server shutdown   # cheap insurance against the RZ1021 Razor trap
aspire run
```

Wait for the Aspire dashboard, then wait for **three** things rather than one:

1. the **`sqlserver`** resource,
2. the **`storage` / Azurite** resource — media originals live in blob storage, not in `wwwroot`, so
   the media library does nothing without it,
3. the **`ef-migrations`** resource **finishing**. The server does not start until it does
   (`server.WaitForCompletion(migrations)`).

> **This matters.** There are now **six** migrations — `InitialDatabase`, `AddCmsStructure`,
> `AddCmsPages`, `AddCmsRouting`, `AddCmsReusableContent`, `AddCmsMedia`. A database left over from
> an older run will be missing the last two, and the media and reusable screens will fail in ways
> that look like bugs. If you are unsure, do the full reset in §5.

If `dotnet tool restore` has never been run, use the **"Restore Tools"** command on the
`ef-migrations` resource in the dashboard, then restart it.

Note the server's HTTPS URL from the dashboard. Everything below is relative to it; this script
writes `https://localhost:PORT`.

### 1.3 Create the demo account

1. Go to `https://localhost:PORT/Account/Register`.
2. Register `demo@example.com` with a password of at least 6 characters (the policy is relaxed in
   this configuration: no digit, case, or symbol requirement — `P9-04` hardens it).
3. The site requires a confirmed account and the email sender is a no-op, so the confirmation link
   is **printed on the page**. Click *"Click here to confirm your account"*.
4. Log in at `/Account/Login`.

### 1.4 Grant the account its roles

There is no user-management screen until Phase 7 and **nothing seeds the roles**, so this is SQL.
They are ordinary ASP.NET Identity rows.

```bash
docker exec contentmanagementsystem-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'P@ssw0rd!' -C -d contentmanagementsystemdb -Q "
INSERT INTO AspNetRoles (Name, NormalizedName, ConcurrencyStamp)
SELECT v.n, UPPER(v.n), NEWID()
FROM (VALUES ('Administrator'),('Developer'),('Editor'),('MediaManager')) AS v(n)
WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles r WHERE r.NormalizedName = UPPER(v.n));

INSERT INTO AspNetUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM AspNetUsers u CROSS JOIN AspNetRoles r
WHERE u.NormalizedEmail = 'DEMO@EXAMPLE.COM'
  AND r.NormalizedName IN ('ADMINISTRATOR','DEVELOPER','EDITOR','MEDIAMANAGER')
  AND NOT EXISTS (SELECT 1 FROM AspNetUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);

SELECT u.Email, r.Name FROM AspNetUsers u
JOIN AspNetUserRoles ur ON ur.UserId = u.Id
JOIN AspNetRoles r ON r.Id = ur.RoleId;"
```

`Developer` gets you the structure screens, `Editor` gets you drafts, publishing, and the recycle
bin, `Administrator` gets you permanent deletion, `MediaManager` the media library's own management.

**Then log out and log back in.** Role claims are baked into the auth cookie by
`CustomUserClaimsPrincipalFactory`; a cookie issued before the insert has no roles in it, and every
`/admin` screen will bounce you to access-denied.

### 1.5 Confirm the ground is where you expect it

Open `https://localhost:PORT/admin`. You should land on the **dashboard**, with a section bar across
the top: **Content · Media · Reusable · Structure · Recycle bin**.

> That bar matters and is worth knowing about before you present: `/admin` did not exist as a route
> until Phase 6. Everything in this demo is now reachable by clicking, which is half of acceptance
> criterion `P6 #1`. You should not need to type a URL after Act 0 — except `/preview/…` and the
> public site, which are meant to be typed.

Then open **Structure**. You should see **`article`** and **`marketing-landing`**, both badged
**Ready**, both with **0 zones**.

Those two rows were written by the startup reconciler from the `[CmsTemplate]` attributes on
deployed components; there are no `Server/CmsSchema/*.json` files in this repository, so nothing
pre-populates the zones. **Zero zones is correct and is the point of Act 1.**

Also open **Structure → Block types**. Four rows, all **Ready**: `hero-banner`, `feature-grid`,
`rich-text`, and `rawHtml`. These are Act 4's building blocks.

### 1.6 Dry run — and reset afterwards

Run Acts 1 through 8 once, end to end, against a **fresh** database (§5). Time it. The first run
always takes longer than you expect: adding zones is one form submission each, and the first media
upload pays for Azurite waking up.

**Then reset the database again**, so the demo itself starts from zero templates-with-zones. A
second `autumn-campaign` at the root collides with the first on the sibling-slug rule.

### 1.7 Browser setup

- **Browser A** — normal window, logged in as `demo@example.com`. This is the editor's desk.
- **Browser B** — a *private/incognito* window, never logged in. This is the public. Keeping the
  anonymous view in a genuinely separate session is what makes "the public cannot see the draft" a
  demonstration rather than an assertion.
- Zoom both to ~125%.
- Have a terminal open on the `sqlcmd` command from Act 9.4, ready to run.
- Have `~/Desktop/autumn-hero.jpg` and its duplicate somewhere you can find in a file dialog in two
  seconds. Practise this. Fumbling a file picker on a projector is the most avoidable stall there
  is.

---

## 2. The demo

**If you are short on time,** cut in this order: Act 9 (whole), then Act 3 + Act 8 together (the
reusable-content story is one arc and does not survive being halved), then Act 2's dedupe beat.
Never cut Acts 5, 6, and 7 — they are the exit gate.

### Act 0 — Framing (3 minutes, no clicking)

> "This is a content management system built from scratch. Today is the first time anybody outside
> the team has seen it, and it is being shown now because a specific thing became true: the whole
> loop closes. A developer defines the shape of a page. An editor fills it in — with real editors,
> a media library, and content shared across pages. They review it privately, publish it, and a
> member of the public sees it at a real URL.
>
> Four things to watch for, because they are the ones that are hard and that we got right.
> **One:** a draft is genuinely invisible — not hidden, not filtered out, but absent from the query
> the public site runs. **Two:** publishing takes a snapshot, so editing tomorrow's draft cannot
> disturb today's live page. **Three:** content authored once and used in forty places updates in
> one publish, without republishing the forty. **Four:** it is built to survive its own mistakes — a
> broken component takes out one block, not the page.
>
> Two things I want to be straight about before we start. The published page has almost no
> stylesheet — that is deliberate, the CMS's job is to emit correct structure and a designer's job
> is to style it. And the three-pane editor shell with the drag-and-drop content tree is built and
> tested but is not yet wired into a screen, so today you will see the editor through a plainer
> list. That is a wiring job, not missing work, and I will point at it when we get there."

### Act 1 — A Developer shapes the content model (7 minutes)

*This is acceptance criterion `P1 #1`. Doing it here closes the Phase 1 exit gate.*

1. From `/admin`, click **Structure**.

   > "Two templates. Neither was typed in here — a developer deployed a component carrying a
   > `[CmsTemplate]` attribute, and the system found it at startup and registered it. That badge says
   > *Ready*: there is a real component behind this key. If somebody deletes the component, this row
   > goes *Orphaned* and says so, rather than failing on a page request at three in the morning."

2. Click **`marketing-landing`**.

   > "Zero zones. That is correct, and it is the most important idea in the system. The component
   > decides *where* things go on the page — it has a hero slot, an intro, a body, a footer. It does
   > not decide *what* those hold. A developer decides that right here, in the browser, with no
   > deployment and no code change. That separation is why a marketing team can get a new field next
   > week instead of next quarter."

3. Add four zones with the form at the bottom. **Four zones of differing field types** — that is the
   acceptance criterion, word for word.

   | Key | Display name | Field type | Configuration (JSON) | Required | Group | Sort |
   |---|---|---|---|---|---|---|
   | `hero` | Hero image | `media` | leave empty | no | Hero | 10 |
   | `intro` | Introduction | `richText` | leave empty | **yes** | Hero | 20 |
   | `body` | Body | `blocks` | `{ "allowedBlockTypes": ["hero-banner", "rich-text", "rawHtml"] }` | no | Body | 30 |
   | `footer` | Shared footer | `reusable` | leave empty | no | Footer | 40 |

   The field-type dropdown lists all **eighteen** built-in types by name. Scroll it once, slowly —
   it is a better answer to "can it hold X?" than any sentence.

   While filling the third one, point at *Configuration (JSON)* and the **Settings:** line beneath it:

   > "That list is generated from the field type itself, not written in a document somewhere. A
   > setting the field type does not declare is refused on save, so this box cannot quietly accept a
   > typo and then ignore it."

   While filling any of them, read the key's help text out:

   > "'Written into every payload. Cannot be changed afterwards.' The key and the field type are
   > both write-once. Every piece of content ever authored against this template quotes that key, and
   > changing what a zone holds is a content migration, not an edit. The system refuses to let me
   > pretend otherwise."

   On `intro`, tick **Required**, and read the label out:

   > "'Required — an empty value blocks publishing, never a draft save.' An editor must always be
   > able to save unfinished work. Required means you cannot *publish* it half-done."

4. Point at the **Editor group** boxes you filled and the revision number, now bumped.

   > "The groups are how the editing screen lays itself out — you will see those exact words as
   > headings in a moment. And each change here cut a new revision. Content authored against
   > revision 1 still reads as revision 1 forever; nothing we just did can reach back and
   > reinterpret a page somebody published last month."

**Watch out:** define the zones *before* creating the page in Act 4. A page captures the template
revision it was created at, and a page created against a zero-zone revision shows an editor no boxes
to type in. The screen tells you this if it happens, but it is not a good look mid-demo.

### Act 2 — The media library (6 minutes)

1. Click **Media** in the section bar. Empty library, a folder tree on the left, an upload panel.

2. Type an **Alternative text** — `Autumn light through a stand of birch` — *before* choosing the
   file. Then upload `autumn-hero.jpg`.

   > "It asked for the description first, and that is not politeness. An image with no alternative
   > text and no 'this is decorative' flag **fails the publish check** — the page will not go live.
   > The point of asking at upload is that the person who knows what the picture is, is the person
   > holding it, and they are here now rather than three weeks later when somebody else is trying to
   > publish."

   Point at the checkbox beside it: *decorative*. > "The other legitimate answer, and it disables
   the box rather than letting somebody type 'image' into it."

3. Click into the item. Show the metadata: dimensions, size, SHA-256, format.

   > "That hash is the file's identity. Watch."

4. Upload **the same photograph under its second filename**.

   > "It came back as the item we already had rather than a second row — same bytes, same hash. A
   > media library that lets four people upload the same hero image four times is a media library
   > where nobody can find anything, and where a correction has to be made four times."

5. **The privacy one.** Back in the terminal, or with the file open in an EXIF reader beside the
   browser:

   > "That photograph had GPS coordinates in it. Almost every phone photo does. When it went in, we
   > stripped **all** metadata — GPS, camera serial, the lot — and we baked the orientation flag
   > into the actual pixels first, so nothing is rotated wrongly by the removal. A published photo
   > that says where it was taken is a privacy incident waiting for somebody to notice, and the only
   > reliable time to prevent it is on the way in. The original bytes we keep are the stripped ones;
   > there is no copy with the coordinates still in it."

   If asked how it is proven: `APhotographsGpsCoordinatesAreGoneFromTheStoredOriginal` fetches the
   stored original back through its own signed URL — what the site would hand a visitor — and
   asserts no GPS directory survives.

6. Show the numeric image editor (rotate / crop / focal point) briefly and move on.

   > "Every edit here is non-destructive — we store the *instruction*, never a modified file, so
   > 'revert to original' is always available and always exact. A drag-and-drop crop surface is
   > authoring polish; the model underneath it is finished."

**Two things to have ready if asked, not to volunteer:** SVG uploads are **refused** by default
(Q7's safe reading — see Act 10), and an HTML file renamed `.jpg` is refused on its magic bytes
rather than its extension.

### Act 3 — Content authored once, used everywhere (5 minutes)

*Lead with the problem. This is the part that lands with anyone who has run a website.*

> "Every site has a footer, a promo banner, a legal disclaimer that appears on two hundred pages.
> In most systems that means either two hundred copies, or a developer. Here is the third answer."

1. Click **Reusable** in the section bar. Create an item: name **Autumn footer**, key
   **`autumn-footer`**, type **Raw HTML**.

2. In the editor, fill the content:

   ```html
   <p><strong>Autumn campaign</strong> — offer ends 31 October.</p>
   ```

3. **Publish** it.

   > "That is now one row in one table. Nothing has copied it anywhere. In a few minutes we will put
   > it on a page, and later we will change it once and watch every page carrying it change — without
   > republishing any of them."

Leave the **Where used** panel on screen for a beat. It says nothing uses it yet. That is the number
Act 8 comes back to.

### Act 4 — Authoring, with the editors an editor actually gets (12 minutes)

*This is the heart of Phase 6 and the longest act. Do not rush the block list or the HTML warning.*

1. Click **Content**, then in *Create a page*: template **Marketing Landing Page (4 zone(s))**, title
   **Autumn Campaign**, slug blank, parent *At the root of the site*. Submit.

   > "I left the slug empty. It generated `autumn-campaign` from the title — accents folded,
   > punctuation dropped, and checked against a reserved list so nobody can create a page at
   > `/admin` or `/api` and shadow the application."

2. You land in the page editor. Take five seconds on the shape of the screen before touching it.

   > "Canvas on the left, properties on the right. The cards are grouped and ordered exactly as the
   > Developer set them up four minutes ago — 'Hero', 'Body', 'Footer'. Nobody wrote this layout;
   > it is the content model, drawn."

   Point at the header line: template, revision, draft v1, no published version.

   > "Two version counters, and they are about to start disagreeing on purpose."

3. **Hero image.** Click the picker. The media browser opens *inside* the dialog.

   > "That is the same component as the library screen, not a copy of it — which matters because
   > this is the one an editor uses forty times a day and a second copy would drift first."

   Choose the photograph. Point at the alt text carried through, and the per-placement override.

   > "The description follows the picture, and this page can override it. That is not a loophole —
   > a picture that means one thing in the library can mean something else in context, and forcing a
   > choice between an accurate library and a publishable page is how alt text gets filled with
   > junk."

4. **Introduction — the rich-text editor.** This is the `richText` zone. Switch through
   **Edit / Preview / Split**.

   Type into it:

   ```
   ## What changes in October

   Three things move at once, and they move together.

   - New pricing on the public site
   - A refreshed landing page for paid traffic
   - One consistent story across both
   ```

   Then, in **Split**, scroll the source pane and let them watch the preview follow.

   > "Two things worth knowing about that preview. It is not a second Markdown renderer in the
   > browser — the text goes to the server and comes back rendered by *exactly* the same code that
   > will render the published page, through the same sanitizer. A second implementation would agree
   > on day one and drift on the first upgrade of either side, and 'preview is accurate' is a promise
   > you only get to break once.
   >
   > And the scroll sync is by proportion, not by pixels. One line of Markdown becomes a picture, so
   > matching pixel offsets drifts further apart the further down you go — which is exactly where
   > the feature is worth having."

   Point at the word and character counters underneath.

5. **Body — the block list.** This is the `blocks` zone and the star of the phase.

   - **Add** a *Hero Banner*. Fill its headline. Point out that a block's properties are drawn by
     the same field editors the zones use.
   - **Add** a *Rich Text* block. Fill its body.
   - **Collapse** both. Point at the summary lines.

     > "The collapsed line is the block's own content, not its type name. 'Hero Banner' twelve times
     > is a list nobody dares collapse."

   - **Move** one up and down with the arrow buttons. Then **duplicate** one. Then **delete** one —
     and point at the inline undo bar.

     > "Deleting keeps the block *and its position* and offers it back until the next change. An
     > inline bar rather than a toast, because a toast times out on the one action on this screen
     > worth taking back after you have finished reading what happened."

   - **Now say the keyboard thing**, because it is acceptance criterion `P6 #4` and it is the
     accessibility argument in one sentence:

     > "Every one of those is a button. The drag handle can do nothing the buttons cannot, and it is
     > deliberately not focusable — a handle a keyboard user can reach and cannot use is worse than
     > no handle at all. The tests drive only the buttons, so a build where somebody removed them in
     > favour of drag would fail."

6. **The HTML block — the one that stops the commonest support ticket.** Add a *Raw HTML* block and
   paste this into it:

   ```html
   <p>Reviewed by the marketing team.</p>
   <script>alert('hello')</script>
   <iframe src="https://example.com"></iframe>
   ```

   Wait a beat. The warning appears *while you type*, before any save.

   > "Read that. 'These will be removed when this is saved' — and it names them. Silent stripping is
   > the number-one 'the CMS ate my content' ticket in every system I have worked on: somebody
   > pastes an embed, saves, and finds out three days later that half of it vanished. The fix is not
   > to stop stripping — the stripping is the security boundary. The fix is to stop it being silent.
   >
   > And that check is the server's real sanitizer, not a guess in the browser. The banner above it
   > lists what the profile *keeps*, fetched from the server for the same reason: a second copy of an
   > allowlist is a banner that eventually lies."

7. **Footer.** Place **Autumn footer** in the `footer` zone through the picker. It asks whether to
   pin the version.

   > "Leave it unpinned. Unpinned means this page follows the item — which is the whole feature, and
   > what Act 8 is about. Pinning is the escape hatch for a page that must not move, and the choice
   > is asked here because this is the only moment anybody is thinking about it."

8. **Now stop typing and wait.** Do not touch anything for twenty seconds. Point at the save
   indicator when it changes.

   > "Nobody pressed save. Twenty seconds idle, and it wrote the draft. It also saves when you
   > navigate away, it retries a transient failure with a backoff, and if the connection drops
   > entirely it holds the *intent* and saves the text as it stands when the connection comes back —
   > not the stale text that failed. If you close the tab with unsaved work the browser asks you
   > first."

9. **The properties panel**, on the right. Open **Search and social** and type a meta description.
   Point at the search-result preview updating live and at the character counters.

   > "That widget is deliberately not a pixel-perfect forgery of Google's result page. It exists to
   > show two rules a number cannot state — a blank meta title falls back to the page title, and both
   > fields get truncated rather than refused. A convincing imitation would invite trust in details
   > no search engine actually guarantees."

   Open **Editorial** and set **Review by** to a date in the past.

   > "That is the only practical defence against a page quietly going stale. It puts the page on the
   > dashboard's 'needs attention' list the day it passes — we will see it there in Act 9."

10. Press **Ctrl/⌘+S**, then **`?`**.

    > "Every shortcut is an accelerator for a button that is also on the screen — that is a rule, not
    > a coincidence, and it is stated at the top of this list. The list and the thing that listens
    > for the keys are one table read twice, so a shortcut cannot work undocumented or be documented
    > and do nothing."

11. **Browser B (incognito):** go to `https://localhost:PORT/autumn-campaign`.

    > **404.** "The page exists, it has content, it has a URL — and the public gets nothing. Not a
    > filtered result, not a redirect to a login. The URL simply does not resolve, because the route
    > that makes it resolve is only written when the page is published."

### Act 4b — A second page (2 minutes, and do not skip it)

Act 8 needs two live pages carrying the footer, and Act 9 needs a page whose URL you can change.

1. **Content → Create a page**: template **Marketing Landing Page**, title **Pricing**, slug
   **`pricing-old`**, parent *At the root of the site*.
2. Fill **Introduction** — it is the zone you marked required, and publishing is refused without it.
   One sentence is enough.
3. Place **Autumn footer** in its `footer` zone, unpinned.
4. **Publish** it. Confirm `https://localhost:PORT/pricing-old` serves in Browser B.

Narrate it as housekeeping — *"a second page so we have a site rather than a page"* — and move on.

### Act 5 — Preview (4 minutes)

1. Back on the campaign page in Browser A, press **Preview draft**. A new tab opens on
   `/preview/{id}`.

   > "Same page, same moment, and I can read it — because I am signed in and hold the permission
   > that means 'may see unpublished content'."

2. Point at the floating toolbar: the **Preview** badge, the title, **v1**, the status, *Exit
   preview*.

   > "The toolbar lives in an outer document and the page itself is rendered into a frame by
   > *exactly* the same code that will serve the public — same renderer, same document, same
   > components. There is no 'but this is a preview' branch anywhere underneath. That is what makes
   > preview trustworthy: it is not a good imitation of the live page, it is the live page's code."

3. Click **Tablet**, then **Mobile**, then **Desktop**.

   > "834 and 390 pixels. And the constraint is on the frame, not on a box inside the page — so the
   > page's own responsive breakpoints see the width they would see on a real device. A `div` with a
   > max-width would look narrow and lie about every breakpoint."

   Aside, if anyone asks why the layout does not change: there is no site stylesheet with
   breakpoints in it yet. The mechanism is what is being shown.

4. Note the URL bar: no version number, no editing controls, plain links between the widths.

   > "No JavaScript on this page at all. The whole preview shell is server-rendered."

### Act 6 — Sharing a draft with somebody who has no account (5 minutes)

*Lead with the problem again.*

> "Every organisation has the same broken habit: an editor wants a review from a lawyer, an agency,
> or an executive who will never have a CMS login. So they screenshot it, or they publish it and
> hope nobody notices for ten minutes. Here is the alternative."

1. From the page editor, click **Preview links**.
2. Version **Current draft**, days **7**, max views **blank**, note **"Managing director"**. Press
   **Issue link**.
3. The banner appears. Read the first line out:

   > "'Copy this link now. It is not shown again.' We store only a SHA-256 hash of the token.
   > Anybody with full read access to that database table holds a hash and no way to turn it back
   > into a working link. That is a deliberate choice — it means we cannot recover a lost link, and
   > we would rather have that problem than the other one."

4. Copy the link. Paste it into **Browser B (incognito)**.

   > "No account. No cookie. No identity of any kind — the link is the entire authority. And look at
   > the toolbar: it says preview, it says which version, and it shows when the link expires, so the
   > reviewer knows what they are looking at."

5. Back in Browser A, on the preview-links table, press **Revoke**. Reload Browser B.

   > **"This preview link is not valid."** "Revoked. And notice the row is still in the table,
   > stamped with when and by whom — because 'why did the link I sent stop working?' is a question
   > somebody will ask on Thursday, and it is also the only record of who could once read an
   > unpublished page."

   Note for you, not for the room: **a revoked link answers 404**, with the wording *"It may have
   been revoked, or copied incompletely."* That is deliberate — a revoked token and a string that
   was never a token get **one** answer, because confirming that a string was once real narrows the
   search for anyone probing, and the person holding a revoked link has to go back to the sender
   either way. **410 Gone** is reserved for a link that demonstrably worked and has stopped:
   *expired* or *used up*. A deleted page gets its own 404 with different wording, and does not
   spend one of a single-use link's views. If someone asks about status codes, that is the answer.

### Act 7 — Publish, and view it as the public (4 minutes)

1. Back on the campaign page, press **Check before publishing**.

   > "A dry run. It tells me what would block a publish and what is merely worth a second look,
   > *before* I commit to anything."

   If you want to show a refusal — and it is worth 30 seconds — clear **Introduction**, save, and
   re-check. The required zone blocks it, **by name**, and the dialog's heading for it is a link
   that closes the dialog and puts your cursor **in** that zone. Put the text back afterwards.

   > "That is the difference between a validation message and a usable one. It does not scroll the
   > field into view behind a modal — it closes the dialog and moves focus into the box."

2. Press **Publish**. The dialog groups problems by zone, in the order the canvas draws them; any
   warnings must be ticked before the button will go.

   > "The acknowledgement box is unticked every time this opens. Consent to a list nobody is looking
   > at is not consent."

3. **Browser B (incognito):** reload `https://localhost:PORT/autumn-campaign`.

   > "There it is. No login, no cookie, no session. That is the milestone."

4. Right-click → **View Source**. Scroll it.

   > "Worth a minute. Look at what is *not* here: no `blazor.web.js`, no application bootstrapper,
   > no JavaScript framework at all. The public site is server-rendered HTML and the editing tools
   > are a completely separate front door. Two consequences: a visitor downloads a page instead of an
   > application, and because the response is plain HTML with no per-user content, the whole thing
   > can go in a cache later." (That caching is Phase 8.)

5. Find the `<picture>` element for the hero image.

   > "That is the image pipeline's whole argument in six lines. A WebP source and the original
   > format as a fallback. A `srcset` of six widths so a phone downloads a phone-sized file. Explicit
   > width and height so the layout does not jump while it loads — and those numbers are the widths
   > the browser will *actually receive*, because we never upscale, so a 900-pixel original asked for
   > at 1280 comes back at 900 and the markup says 900. Markup that claimed 1280 would reserve a box
   > the picture never fills, which is precisely the layout shift the attributes exist to prevent.
   >
   > And every one of those URLs is signed. Change a pixel of the query string and it is refused
   > *before* anything is encoded — otherwise a signature check would just move a denial-of-service
   > rather than prevent one."

   Also visible: a `<title>`, a `<meta name="description">`, a canonical link, and the
   `data-template="marketing-landing"` marker.

### Act 8 — The two things that make it a CMS (6 minutes)

**8.1 — Edit the draft; the public page does not move.**

*This is acceptance criterion `P3 #3`, and it is the one that matters most to anyone who has been
burned by a CMS.*

1. In Browser A, change the hero banner's headline to `Autumn Campaign — REVISED`, and add a
   paragraph to the introduction.
2. **Save draft.** Point at the amber banner:

   > "'This draft has moved on from what is published. The public site still shows v1 until you
   > publish again.'"

3. **Browser B:** hard-reload `/autumn-campaign`, to be beyond argument.

   > "Unchanged. And this is not a caching artefact and it is not a filter somebody remembered to
   > write — the query that serves the public projects through the page's *published* version and
   > never mentions the draft at all. The draft row is not in the result set to be picked by
   > mistake. That is the difference between a rule and a property."

4. Reload the preview tab — the revised text *is* there.

   > "Same moment. Two audiences. Two answers."

5. **Publish** again. **Browser B:** reload. The revision appears.

   > "That is the ten-step loop, closed. Define, create, fill, save, preview, publish, view, edit,
   > confirm nothing moved, publish again."

**Pause here.** This is the natural applause point and the natural question point.

**8.2 — One publish, every page.**

1. Go to **Reusable → Autumn footer**. Show the **Where used** panel: **2 pages** now, both
   late-bound, none pinned.
2. Change the text to `<p><strong>Autumn campaign</strong> — extended to 15 November.</p>` and press
   **Publish**.
3. The impact dialog appears and refuses to be skipped.

   > "It will not publish this without me acknowledging what it changes. And that is not the
   > dialog being careful — the *server* refuses an unacknowledged publish whose blast radius is not
   > zero. A script with no dialog at all still cannot change forty pages silently. It also re-reads
   > the number immediately before the irreversible click rather than trusting the one it loaded
   > when I opened the tab."

4. Confirm. **Browser B:** reload **both** `/autumn-campaign` and `/pricing-old`.

   > "Both changed. Neither was republished — go and look at their version numbers, they have not
   > moved. Nothing in that publish touched a page at all: it repointed one row, and every page
   > carrying the item reads that pointer at the moment it is served.
   >
   > Two pages is a demo. The reason this is on the slide is what it means at two hundred: a legal
   > line changes, somebody edits it once, and the site is correct. The alternative — which is what
   > most organisations actually live with — is a spreadsheet and a Friday afternoon."

   If someone asks about the pages that must *not* move: that is the pin, offered at placement, shown
   as a badge on the page with an "update to latest" action.

### Act 9 — The safety net (8 minutes, cut freely)

Pick three or four. They are ordered by how much they tend to land.

**9.1 — A URL change leaves a 301 behind.** On the **Pricing** page, open the properties panel and
change **URL segment** from `pricing-old` to `pricing`. Read the help text out as you do:

> "'Changing it moves this page and everything beneath it, and leaves redirects behind.' It said
> that before I touched it."

Save, publish, then in **Browser B** go to `https://localhost:PORT/pricing-old`.

> "The old address still works — permanently redirected, which is the status code that tells Google
> to move its index across rather than to check back tomorrow. Nobody typed that rule. Renaming the
> page wrote it. And if that page had had fifty children, there would be fifty-one rules — the whole
> subtree moves in one transaction and every old address follows."

In **Browser A**, open `/api/cms/v1/redirects` to show the row and its hit count. (There is no admin
screen for the redirect table yet; this is raw JSON and worth flagging as such.)

> "If we are migrating an existing site, there is a CSV import behind this that takes thousands of
> rows and reports each bad line by number rather than refusing the file. **That is exactly the open
> question in Act 10.**"

**9.2 — Version history, diff, and restore.** From the page editor, **Version history**.

> "Every version this page has had. The draft is the only mutable row; everything else is frozen.
> Pick any two and compare them —" (open a diff) "— word by word, which is what an approver actually
> needs to see. And restoring an old version copies it into the *draft* rather than slamming it onto
> the live site, so a restore is still a decision somebody publishes."

**9.3 — The dashboard.** Go to `/admin`.

> "Four tiles, and every one of them is a thing nobody would think to go looking for. What I have in
> progress. What is scheduled — including anything whose moment came and went, which is invisible
> everywhere else because a failed scheduled publish looks exactly like an ordinary draft. What
> needs attention: content past its review date" — *point at the Pricing page you dated in Act 4* —
> "images with no description, broken references on **live** pages, and the URLs visitors are hitting
> that do not exist. And recent activity.
>
> Every tile links to the same query at a larger limit, not to a second screen that resembles it —
> two definitions of 'needs attention' would drift, and the tile would end up advertising a list
> that did not contain what it promised."

**9.4 — Dead URLs are recorded, not just refused.**

- **Browser B:** visit `https://localhost:PORT/no-such-page` two or three times. Built-in 404.
- **Terminal:**

```bash
docker exec contentmanagementsystem-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'P@ssw0rd!' -C -d contentmanagementsystemdb -Q \
  "SELECT Url, HitCount, Referrer, LastSeenOn FROM NotFoundLogs ORDER BY HitCount DESC"
```

> "One row per URL, not one per request — a crawler cannot make this the biggest table on the site.
> This is the report that tells you which dead link is costing you traffic, and the referrer column
> tells you who is still pointing at it. Turning that into a fix is one redirect."

**9.5 — The recycle bin.** Click **Recycle bin** in the section bar.

> "Deleting a page takes it off the public site immediately and keeps everything — every version,
> every reference. Restoring brings it back as a **draft**, never straight back to live, because
> somebody should decide that on purpose.
>
> And this lists what was *deleted*, not every row that went with it. Delete a section of forty
> pages and this shows one entry with '40 pages' beside it — because a bin listing forty rows would
> ask an editor to restore one delete forty times, in an order that matters. Restore a child before
> its parent and it comes back at the root of the site."

Permanent deletion asks you to type the page's name, is Administrator-only, and is **refused** while
anything still points at the page.

**9.6 — A broken page is a broken block.** No live demo; describe it.

> "Templates run authored content. Content is authored by people, and people make things that break.
> So there is an error boundary around every zone *and* around every individual block: if a component
> throws, that one region drops out, the rest of the page renders, and the log records the page id,
> the zone, the version, and the block — not just a stack trace naming a component that four hundred
> pages share. If the template itself is missing entirely, there is a fallback that still puts the
> page's text in front of the reader. Eight tests cover the failure shapes, including the nasty one
> where a component emits half its markup and *then* fails."

**9.7 — Nothing shadows the application.**

> "The public site is a catch-all route: anything not otherwise claimed is treated as a content URL.
> The obvious risk is that somebody publishes a page at `/api` and takes down the integrations. Ten
> tests assert the outcome — not the registration order, the outcome — for `/api`, `/admin`,
> `/Account/Login`, `/health`, and the framework paths. And the reserved list the router protects is
> the same single list the page editor validates slugs against, so a page cannot be created at an
> address the site would then decline to serve."

**9.8 — Two editors, one page.** Describe it; demonstrating it needs a second browser profile and is
not worth the stage time.

> "If two people edit the same draft, the second save is refused — and the refusal *carries the
> winning draft with it*, so the dialog can offer three things: keep mine, take theirs, or show me
> the difference. Keeping mine is one click, because nothing it overwrites is lost — the history has
> it. Taking theirs asks twice, because what it replaces exists nowhere else. And closing the dialog
> decides nothing. No path silently discards work; that is the requirement, in those words."

### Act 10 — Close (4 minutes)

**Where we are.**

> "Phases 0 through 5 are complete, and Phase 6 — the authoring experience — is built out. That is
> the content model, the page and versioning engine, the publishing pipeline, routing, rendering,
> preview, reusable content, the media library and image pipeline, and the editing surface you have
> been watching. **196 of 281 planned tasks**, about two thousand automated tests with none failing,
> and every acceptance criterion in Phases 0 through 5 is met.
>
> What is left in Phase 6 is honest and small: the three-pane shell needs mounting, three
> end-to-end browser journeys need a test harness that does not exist yet, and a keyboard-only pass
> has to be performed by a person rather than asserted. And the accessibility gates are already
> green — zero critical or serious violations on every backoffice screen, and the whole flow works
> at 200% zoom, which found four screens that did not and fixed them."

**What comes next.**

> "Phase 7 is workflow, permissions, and scheduling — approvals, per-section access, and 'publish
> this on Tuesday'. Phase 8 is SEO, caching, navigation, and search. Phase 9 is hardening,
> accessibility verification, load testing, and launch. Seven and Eight can run in parallel."

**What I need from you.** Do not skip this — these are live blockers recorded in `task.md`, and this
room is where most of them get answered.

| | Question | Who owns it | What it blocks |
|---|---|---|---|
| **Q8** | Is there an existing site to migrate, and must its URL structure be preserved? | Product | `P3-30`, **now**. Bulk redirect import is built and needs a real legacy URL list to be tested against. |
| **Q9** | Retention obligations on content versions and audit logs? | Legal | Version retention is a configured number today. **Audit logs have no retention at all**, which is not a state to launch in — it is recorded as `P9-25`. |
| **Q2** | Hundreds of pages, or tens of thousands? | Product | Search backend and caching topology. The tree is already built and tested at 5,000 pages with 500 siblings under one parent. |
| **Q6** | Is there a CDN in front of the site? | Ops | Cache headers and a purge integration in Phase 8. |
| **Q4** | One instance at launch, or scaled out? | Ops | Whether a Redis output cache is required — and whether background bulk jobs need to survive an instance restart. |
| **Q5** | Which email provider replaces the no-op sender? | Ops | Password resets and the Phase 7 notifications. |
| **Q7** | Is SVG upload permitted at all? | Security | Not blocking — it ships **refused** by default, which is the safe reading. A "yes" changes one line of configuration. |
| **Q10** | Does self-service registration stay on, and with what default role? | Security | Enforced in Phase 9; relevant to launch. |

Close on **Q8** specifically — it is the one blocking work *today*.

---

## 3. If something goes wrong

| Symptom | Cause | Fix |
|---|---|---|
| `/admin/...` bounces to access-denied | Auth cookie predates the role insert | Log out, log back in |
| Templates screen is empty | Migrations did not run, or the server started before `ef-migrations` finished | Check the `ef-migrations` resource in the Aspire dashboard; restart `server` |
| Media screen loads but uploads fail | Azurite is not running | Check the `storage` resource in the dashboard |
| Media or Reusable screens error | Database predates migrations 5 and 6 | Full reset (§5). Do not try to patch it live |
| Page editor says "captured no zones" | The page was created before the zones were added | Create a new page. Do **not** try to fix it live |
| A zone shows a raw JSON box | A field type with no editor — should not happen; all 18 have one | Note it and move on; `CmsEditorStartupService` would normally catch this at startup |
| Rich-text editor is unstyled | The CSP style nonce did not reach it | Reload the page once. It fails *silently* by design of the browser, not of the app |
| Published URL 404s | Publish silently refused, or you are testing the wrong URL | Re-check on `/admin/pages` — State should read **Live**. Confirm the slug |
| Preview shows an empty frame | Zones defined but no values saved | Save the draft again |
| Shared preview link 404s rather than rendering | Token mistyped or truncated on copy | Issue a fresh one; the secret is shown once and is not recoverable |
| Build fails with a wall of `RZ1021` | Poisoned Razor build server on SDK 10.0.301 — a known trap, **not** the markup | `dotnet build-server shutdown`, rebuild. Do not edit the `.razor` files |
| Anonymous browser shows a draft | You are not actually anonymous | Confirm Browser B is a private window and has never logged in |

**The universal recovery:** the whole loop is covered by automated tests. If the live app misbehaves
in a way you cannot fix in fifteen seconds, say so, move on, and offer to show
`Server.Tests/Delivery/DeliveryTests`, `PreviewTests`, and `ReusableContentTests` afterwards. **Do
not debug in front of the room.**

---

## 4. Questions you should expect

**"How do we know the public really cannot see a draft?"**
Two independent mechanisms. The query that serves the public projects through the page's *published*
version and never mentions the draft, so there is no draft row in the result set to leak. And a page
only has a published route while it is published, so an unpublished page's URL does not resolve at
all. Asserted byte-for-byte across three intervening draft saves.

**"When can editors actually use this?"**
The engine and the editing surface both work today. What is between here and editors-in-production is
Phase 7 — permissions and approvals — because until a person's access can be scoped to their section
and a publish can require a second pair of eyes, giving a room full of editors logins is not a
decision anybody should make. That is 26 tasks and about 16 engineer-days.

**"Why is the content tree not there if it is built?"**
Because it lives in a three-pane shell that no route mounts yet, and mounting it is the last wiring
step of Phase 6 rather than the first. The tree itself is finished and tested — including at 5,000
pages with 500 siblings under one parent, where what is asserted is the *mechanism* (one fetch per
expansion, a bounded number of rows in the document) rather than a stopwatch reading that would only
be true on the machine it was measured on.

**"What happens if a developer breaks a template?"**
One zone or one block drops out and the rest of the page renders, with the page id, zone, version,
and block in the log. If the whole template is missing, a fallback still puts the page's text in
front of the reader. Covered for all three ways a component can fail, at both levels.

**"Can we roll back a bad publish?"**
Yes — version history restores any prior version into the draft, and you then publish it. The restore
is deliberately not instant-live: a restore is still a decision somebody makes on purpose.
Unpublishing is also one button, it is confirmed, it tells you what visitors will see, and it has an
undo.

**"How fast is it?"**
Honest answer: the public page is not measured end to end yet — that is `P3-27`, one of the three
open Phase 3 tasks. What *is* measured: image renditions meet NFR-8 (a cold 4000 px source to a
1280 px WebP under 800 ms at p95, measured through the endpoint rather than the encoder, which is
where the cost actually is), and the reusable-content fan-out costs about 2.8 ms for an item on 40
pages, bounded by nesting depth rather than page count. The architecture is built for caching: the
public page is static HTML with no per-user content, and every render already accumulates its own
cache-invalidation tags even though the cache itself is Phase 8.

**"Is it accessible?"**
The automated half is green and was not free: axe-core over every backoffice screen at WCAG 2.1 AA
plus best-practice, zero critical or serious violations — it found three real defects on the way in.
Every status is a word as well as an icon, never colour alone. Reduced-motion preferences are
respected. The whole flow reflows at 200% zoom, which found four screens that could not and fixed
them. What is **not** done is the part a machine cannot do: a keyboard-only pass performed by a
person, and screen-reader passes, which are `P6-37` and `P9-08`.

**"Is it secure?"**
Content is sanitized when written and again when rendered, under a configured allowlist. Link fields
re-apply a scheme allowlist at render time so a `javascript:` URL that arrived through an import
cannot execute. Uploads are checked against their magic bytes rather than their extension, decode
bombs are refused from the header before anything is decoded, SVG is refused by default, and every
rendition URL is signed. Every management endpoint carries a named permission policy and every write
requires an antiforgery token. Preview links store only a hash. Two things are explicitly *not* done:
the `Content-Security-Policy` header is wired but not switched on (turning it on today breaks working
screens; it is a Phase 9 sweep), and the full security review and penetration pass are Phase 9.

**"Can we migrate our existing site's URLs?"**
The mechanism is built — CSV import and export, with per-row warnings rather than an all-or-nothing
file. What it has not had is a real legacy URL list to be tested against, because **Q8 is
unanswered**. This is the ask.

**"Why does the published page look so plain?"**
Because the CMS's job is to emit correct, semantic structure and a designer's job is to style it.
There is no site stylesheet for the public templates in the repository yet. The markup carries the
class names and `data-template` hooks a designer needs, and the image markup is already doing the
performance work — WebP, `srcset`, explicit dimensions, lazy loading below the fold.

---

## 5. Resetting between runs

Doing this demo twice needs a clean database: a second `autumn-campaign` at the root collides with the
first on the sibling-slug rule, and the media library will deduplicate your hero photograph rather
than accept it again.

**Cheapest reset (keeps your account and the template zones):** delete the demo pages and the
reusable item through the screens, and use fresh titles the second time (`Autumn Campaign 2`,
`Winter Campaign`). Note that the dedupe beat in Act 2 will not work twice against the same photo —
have a third file ready, or skip it.

**Full reset**, which is what the dry run should end with: stop `aspire run`, drop the database,
restart. Migrations and seed rows come back automatically; the startup reconciler re-registers both
templates and all four block types. **You will have to redo §1.3 to §1.5** — account, confirmation,
roles.

```bash
docker exec contentmanagementsystem-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'P@ssw0rd!' -C -Q \
  "ALTER DATABASE contentmanagementsystemdb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
   DROP DATABASE contentmanagementsystemdb;"
```

Media blobs survive in Azurite and are orphaned by the database drop, which is harmless for a demo.
To clear them too, delete the `contentmanagementsystem-azurite` container's volume.

---

## 6. After the demo — update `task.md`

Performing this demo closes two exit gates. When it is done:

1. **`P1 #1`** — Act 1 is the browser journey it was waiting for. Change `[~]` to `[x]` and note that
   it was performed during this demo. The **Phase 1 exit gate** then closes too, and its row in the
   progress table gets a date.
2. **Phase 3 exit gate** — Acts 5 through 8 are the ten-step loop, watched. Change `[ ]` to `[x]` and
   record the date and who watched it.
3. Record any answers you got to **Q8** and **Q9** in the Blocking decisions list, and unblock
   `P3-30` and `P5-33`.
4. Update the **Last updated** date at the top of `task.md`.

Remember the four bottom-of-file tables — the progress summary, the traceability table, the
existing-code table, and the risk register — which the document's own "Updating" section does not
mention.
