# Phase 3 Demo Script — the vertical slice, end to end

**Purpose:** perform the Phase 3 exit gate, which is the one thing in
[`task.md`](./task.md) that a test cannot close. Every step below is covered by an automated test
against real SQL Server over real HTTP; what this gate asks for is that **a person watches it work**.

**Audience:** management / stakeholders. No CMS knowledge assumed.
**Running time:** ~30 minutes of demo, ~10 of questions.
**Last updated:** 2026-08-15

The ten steps the gate names, in order:

> define a template → create a page → fill zones → save draft → preview → publish →
> view anonymously → edit draft → confirm the public page is unchanged → publish again

Acts 1–6 below are those ten steps. Act 7 is the safety net around them, and Act 8 is the close.

---

## 0. Verification status — read this before you present

This was checked against the working tree on **2026-08-15** (branch `main`, clean, `e0fe97d`).

### Build and tests

| | Result |
|---|---|
| `dotnet build ContentManagementSystem.slnx` | Succeeds, exit 0, 0 warnings |
| `Core.Tests` | **1186 passed**, 0 failed |
| `Data.Tests` | **30 passed**, 0 failed |
| `E2E.Tests` | **10 passed**, 0 failed |
| `Server.Tests` | **268 passed**, 0 failed |
| **Total** | **1494 passed, 0 failed** |

The suite is green. It was not when this document was first drafted: one test,
`VersionAndDiffTests.RetentionKeepsWhatAnEditorWouldBeUpsetToLose`, was failing, and `task.md`
recorded it under `P3-09` as failing identically on a clean checkout of `main`. It has since been
diagnosed and fixed — the cause was two clocks, not a retention defect. Entity timestamps were
stamped from `DateTimeOffset.UtcNow` inside the `SaveChanges` interceptor while the retention sweep
computed its cutoff from the injected `TimeProvider`, so a suite that advanced its fake clock moved
the cutoff and left the rows behind. Whether the test passed depended on the real calendar date it
ran on, and the date it turned over was 2026-08-15. `AuthDbContext` now reads every stamp it writes
from the injected clock. **If asked "is it all green?", the answer is now simply yes.**

### Task completion through Phase 3

**Phases 0, 1, and 2: every task is done.** 19/19, 33/33, 29/29.

**Phase 3: 28 of 31 tasks are done, and all 11 acceptance criteria (`P3 #1`–`P3 #11`) are met.**
Three tasks remain open, and none of them is on the demo path:

| Task | What it is | Why it is open |
|---|---|---|
| `P3-27` | Performance benchmark harness with CI regression thresholds | The start of a cross-cutting workstream. No benchmark project exists in the solution yet; verified absent. Needs a committed baseline and CI thresholds. |
| `P3-29` | Visual regression baseline (Playwright screenshots) | Verified absent — `E2E.Tests` holds accessibility tests, no screenshot baselines. Now unblocked, but needs seeded content, a stylesheet worth photographing, and a cross-platform baseline policy. |
| `P3-30` | Confirm **Q8** (legacy URL preservation) and test the redirect import path | **Blocked on a decision management owns.** See Act 8. |

Two acceptance criteria in earlier phases are still marked in-progress, and both are about a person
rather than about code:

- **`P0 #3`** — CI green on the Testcontainers integration suite. The phase gate is recorded met
  (2026-08-12).
- **`P1 #1`** — *"A Developer creates a template with four zones of differing field types through the
  admin form."* This has never been done in a browser. **Act 1 of this demo performs it**, so
  running this demo closes the Phase 1 gate as well as the Phase 3 one. Worth saying out loud.

### What this demo does *not* show, and why

Be explicit about this early — it prevents the "why does it look like that?" question landing
mid-flow.

- **The published page is deliberately unstyled.** `site.css` carries no `cms-*` rules, so a
  published page renders as clean semantic HTML with a `data-template` attribute and nothing else.
  That is correct for this phase: the CMS emits structure, and a site's designer supplies the
  stylesheet. Say *"this is the markup a designer styles; the CMS's job is to get the right content
  into the right shape."*
- **The zone editor is a set of plain text boxes.** Rich editors, media pickers, drag-and-drop
  block lists, and the page tree UI are **Phase 6 — Authoring experience** (41 tasks, not started).
  Field types the current screen cannot edit are shown read-only as stored JSON, and the screen says
  so itself.
- **No media library.** Images are **Phase 5**. Media zones render the spec's placeholder-with-alt-
  text.
- **No reusable content, workflow, approvals, scheduling, search, sitemap, or output caching.**
  Phases 4, 7, and 8.
- **No user-management screen.** Roles are assigned by SQL in the pre-flight below; that screen is
  **Phase 7**.
- **No admin screen for redirects or for changing a page's slug.** The API exists and is tested; the
  screens are Phase 6. Act 7 works around this — see the pre-staging note there.

---

## 1. Pre-flight — do this the day before, and again 60 minutes before

Do not do this in front of an audience. Several steps are slow, and one of them requires reading a
confirmation link off a page.

### 1.1 Start the stack

```bash
cd /Users/stokes/repos/breaman/ContentManagementSystem
dotnet build-server shutdown   # cheap insurance against the RZ1021 Razor trap
aspire run
```

Wait for the Aspire dashboard, then wait for the **`ef-migrations` resource to finish**. The server
does not start until it does (`server.WaitForCompletion(migrations)`).

> **This matters.** The dev database was verified on 2026-08-15 as holding only migration 1 of 4 —
> the CMS tables did not exist. `RunDatabaseUpdateOnStart()` applies migrations 2, 3, and 4 at
> startup. If you skip straight to the SQL in 1.3 before this completes, the tables will not be
> there.

If `dotnet tool restore` has never been run, use the **"Restore Tools"** command on the
`ef-migrations` resource in the dashboard, then restart it.

Note the server's HTTPS URL from the dashboard. Everything below is relative to it; this script
writes `https://localhost:PORT`.

### 1.2 Create the demo account

1. Go to `https://localhost:PORT/Account/Register`.
2. Register `demo@example.com` with a password of at least 6 characters (the policy is relaxed in
   this configuration: no digit, case, or symbol requirement).
3. The site requires a confirmed account and the email sender is a no-op, so the confirmation link
   is **printed on the page**. Click *"Click here to confirm your account"*.
4. Log in at `/Account/Login`.

### 1.3 Grant the account its roles

There is no user-management screen until Phase 7, so this is SQL. The roles are ordinary ASP.NET
Identity rows.

```bash
docker exec contentmanagementsystem-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'P@ssw0rd!' -C -d contentmanagementsystemdb -Q "
INSERT INTO AspNetRoles (Name, NormalizedName, ConcurrencyStamp)
SELECT v.n, UPPER(v.n), NEWID()
FROM (VALUES ('Administrator'),('Developer'),('Editor')) AS v(n)
WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles r WHERE r.NormalizedName = UPPER(v.n));

INSERT INTO AspNetUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM AspNetUsers u CROSS JOIN AspNetRoles r
WHERE u.NormalizedEmail = 'DEMO@EXAMPLE.COM'
  AND r.NormalizedName IN ('ADMINISTRATOR','DEVELOPER','EDITOR')
  AND NOT EXISTS (SELECT 1 FROM AspNetUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);

SELECT u.Email, r.Name FROM AspNetUsers u
JOIN AspNetUserRoles ur ON ur.UserId = u.Id
JOIN AspNetRoles r ON r.Id = ur.RoleId;"
```

`Developer` gets you the structure screens, `Editor` gets you drafts and publishing.

**Then log out and log back in.** Role claims are baked into the auth cookie by
`CustomUserClaimsPrincipalFactory`; a cookie issued before the insert has no roles in it, and every
`/admin` screen will bounce you to access-denied.

### 1.4 Confirm the ground is where you expect it

Open `https://localhost:PORT/admin/structure/templates`. You should see **`article`** and
**`marketing-landing`**, both badged **Ready**, both with **0 zones**.

Those two rows were written by the startup reconciler from the `[CmsTemplate]` attributes on
deployed components. **Zero zones is correct and is the point of Act 1** — a template's markup
declares *placement*; which zones exist and what they hold is a database decision a `Developer`
makes in the backoffice, with no deployment.

There are **no admin nav links** to any of this. Bookmark these five URLs in the demo browser, in
this order, before you start:

1. `/admin/structure/templates`
2. `/admin/pages`
3. `/preview/1` (fix the id after Act 2)
4. `/api/cms/v1/redirects`
5. `/autumn-campaign` (the page you are about to build)

### 1.5 Pre-stage the redirect page (for Act 7)

Act 7 shows a URL change issuing a 301. There is no admin screen for changing a slug, so **build
this page during pre-flight** and show the result live. Do this *after* Act 1's zones exist.

1. Create a second page from `article`, titled **Pricing**, slug **`pricing-old`**.
2. Fill at least **Standfirst** — it is the zone you marked required in Act 1, and publishing is
   refused without it. Save the draft, then publish. Confirm `https://localhost:PORT/pricing-old`
   serves.
3. Note its page id from the URL of `/admin/pages/{id}`.
4. Rename its slug with the console snippet below — open DevTools on any `/admin` page so the
   request carries your session cookie:

```js
const id = 2;                       // the Pricing page's id
const t = await (await fetch('/api/cms/v1/antiforgery-token')).json();
const r = await fetch(`/api/cms/v1/pages/${id}/metadata`, {
  method: 'PATCH',
  headers: { 'Content-Type': 'application/json', [t.headerName]: t.requestToken },
  body: JSON.stringify({ slug: 'pricing' })
});
console.log(r.status, await r.text());
```

Expect `200`. Now `/pricing` serves the page and `/pricing-old` 301s to it — which is what you show
in Act 7. (You *can* run this snippet live if the audience is technical and you are comfortable with
a DevTools console on screen. Pre-staging is the lower-risk choice.)

### 1.6 Browser setup

- **Browser A** — normal window, logged in as `demo@example.com`. This is the editor's desk.
- **Browser B** — a *private/incognito* window, never logged in. This is the public. Keeping the
  anonymous view in a genuinely separate session is what makes "the public cannot see the draft"
  a demonstration rather than an assertion.
- Zoom both to ~125%.
- Have a terminal open on the `sqlcmd` command from Act 7.4, ready to run.

### 1.7 Dry run

Run Acts 1–6 once, end to end, against a **fresh** database (see §5, Reset). Time it. The first run
always takes longer than you expect, because adding four zones is four form submissions.

---

## 2. The demo

### Act 0 — Framing (2 minutes, no clicking)

> "This is a content management system built from scratch. Today it hits its first milestone that
> is worth showing anyone: the whole loop closes. A developer defines the shape of a page, an editor
> fills it in, reviews it privately, publishes it — and a member of the public sees it at a real
> URL. Nothing before this point was visible to anybody outside the team.
>
> Three things to watch for, because they are the ones that are hard and that we got right.
> **One:** a draft is genuinely invisible — not hidden, not filtered out, but absent from the query
> the public site runs. **Two:** publishing takes a snapshot, so editing tomorrow's draft cannot
> disturb today's live page. **Three:** it is all built to survive its own mistakes — a broken
> component takes out one block, not the page.
>
> What you will not see today is polish. The editing screens are deliberate scaffolding, and the
> published page has no stylesheet. Both are on the plan, and I will show you where."

### Act 1 — A Developer defines the template (5 minutes)

*This is acceptance criterion `P1 #1`. Doing it here closes the Phase 1 exit gate.*

1. Go to **`/admin/structure/templates`**.

   > "Two templates. Neither was typed in here — a developer deployed a component carrying a
   > `[CmsTemplate]` attribute, and the system found it at startup and registered it. That badge says
   > *Ready*: there is a real component behind this key. If somebody deletes the component, this row
   > goes *Orphaned* and says so, rather than failing on a page request at three in the morning."

2. Click **`article`**.

   > "Zero zones. That is correct. The component decides *where* things go on the page; a
   > developer decides *what* the page holds, right here, with no deployment and no code change.
   > That separation is why a marketing team can get a new field next week instead of next quarter."

3. Add four zones with the form at the bottom. **Four zones of differing field types** — that is
   the acceptance criterion, word for word.

   | Key | Display name | Field type | Required | Group |
   |---|---|---|---|---|
   | `kicker` | Kicker | `plainText` | no | Header |
   | `standfirst` | Standfirst | `multilineText` | **yes** | Header |
   | `body` | Body | `richText` | no | Body |
   | `embed` | Embed | `html` | no | Body |

   Leave *Configuration (JSON)* empty. Set *Sort order* 10/20/30/40 if you want them ordered.

   While filling the third one:

   > "Notice the key is write-once, and so is the field type. Every piece of content ever authored
   > against this template quotes that key, and changing what a zone holds is a content migration,
   > not an edit. The system refuses to let me pretend otherwise."

   On `standfirst`, tick **Required**, and read the label out:

   > "'Required — an empty value blocks publishing, never a draft save.' An editor must always be
   > able to save unfinished work. Required means you cannot *publish* it half-done."

4. Point at the revision number, now bumped.

   > "Each change cut a new revision. Content that was authored against revision 1 still reads as
   > revision 1 forever — nothing we just did can reach back and reinterpret a page somebody
   > published last month."

**Watch out:** define the zones *before* creating the page in Act 2. A page captures the template
revision it was created at, and a page created against a zero-zone revision shows an editor no
boxes to type in. The screen tells you this if it happens, but it is not a good look mid-demo.

### Act 2 — Create a page, fill the zones, save a draft (5 minutes)

1. Go to **`/admin/pages`**.

   > "The content tree. Empty."

2. In *Create a page*: template **Article**, title **Autumn Campaign**, slug blank, parent
   *At the root of the site*. Submit.

   > "I left the slug empty. It generated `autumn-campaign` from the title — accents folded,
   > punctuation dropped, and checked against a reserved list so nobody can create a page at
   > `/admin` or `/api` and shadow the application."

3. You land in the page editor. Point at the header line.

   > "Template `article`, revision 2, draft v1, no published version. Two version counters, and they
   > are about to start disagreeing on purpose."

4. Fill the four boxes:

   - **Kicker:** `Campaign`
   - **Standfirst:** `Everything we are putting behind the autumn push, in one place.`
   - **Body** (this is markdown — say so):

     ```
     ## What changes in October

     Three things move at once, and they move together.

     - New pricing on the public site
     - A refreshed landing page for paid traffic
     - One consistent story across both

     Nothing here is live until somebody presses publish.
     ```

   - **Embed:** `<p><strong>Reviewed by:</strong> the marketing team.</p>`

   > "Rich text is stored as markdown, and it is sanitized both when it is saved and again when it
   > is rendered. Belt and braces on purpose: a value can reach the database through an import or a
   > restored backup without ever passing the write-time check."

5. Press **Save draft**. Read the confirmation aloud:

   > "'Draft saved. The published version is untouched.' There is no published version yet, so that
   > is a promise about the future — and in four minutes you will watch it hold."

6. **Browser B (incognito):** go to `https://localhost:PORT/autumn-campaign`.

   > **404.** "The page exists, it has content, it has a URL — and the public gets nothing. Not a
   > filtered result, not a redirect to a login. The URL simply does not resolve, because the route
   > that makes it resolve is only written when the page is published."

### Act 3 — Preview (4 minutes)

1. Back in Browser A, press **Preview draft**. A new tab opens on `/preview/{id}`.

   > "Same page, same moment, and I can read it — because I am signed in and hold the permission
   > that means 'may see unpublished content'."

2. Point at the floating toolbar: the **Preview** badge, the title, **v1**, the status, *Exit
   preview*.

   > "The toolbar lives in an outer document and the page itself is rendered into a frame by
   > *exactly* the same code that will serve the public — same renderer, same document, same
   > components. There is no 'but this is a preview' branch anywhere underneath. That is what makes
   > preview trustworthy: it isn't a good imitation of the live page, it is the live page's code."

3. Click **Tablet**, then **Mobile**, then **Desktop**.

   > "834 and 390 pixels. And the constraint is on the frame, not on a box inside the page — so
   > the page's own responsive breakpoints see the width they would see on a real device. A `div`
   > with a max-width would look narrow and lie about every breakpoint."

   Aside, if anyone asks why the layout does not change: there is no stylesheet with breakpoints in
   it yet. The mechanism is what is being shown.

4. Note the URL bar: no version number, no editing controls, plain links between the widths.

   > "No JavaScript on this page at all. The whole preview shell is server-rendered."

### Act 4 — Sharing a draft with somebody who has no account (5 minutes)

*This is the part that usually gets the room's attention. Lead with the problem.*

> "Every organisation has the same broken habit: an editor wants a review from a lawyer, an agency,
> or an executive who will never have a CMS login. So they screenshot it, or they publish it and
> hope nobody notices for ten minutes. Here is the alternative."

1. From the page editor, click **Preview links**.
2. Version **Current draft**, days **7**, max views **blank**, note **"Managing director"**.
   Press **Issue link**.
3. The banner appears. Read the first line out:

   > "'Copy this link now. It is not shown again.' We store only a SHA-256 hash of the token.
   > Anybody with full read access to that database table holds a hash and no way to turn it back
   > into a working link. That is a deliberate choice — it means we cannot recover a lost link, and
   > we would rather have that problem than the other one."

4. Copy the link. Paste it into **Browser B (incognito)**.

   > "No account. No cookie. No identity of any kind — the link is the entire authority. And look
   > at the toolbar: it says preview, it says which version, and it shows when the link expires, so
   > the reviewer knows what they are looking at."

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

### Act 5 — Publish, and view it as the public (3 minutes)

1. Back on `/admin/pages/{id}`, press **Check before publishing**.

   > "A dry run. It tells me what would block a publish and what is merely worth a second look,
   > *before* I commit to anything." — Expect **Ready to publish**.

   If you want to show the refusal, clear **Standfirst**, save, and re-check: the required zone
   blocks it, by name. Put the text back afterwards.

2. Press **Publish**.
3. **Browser B (incognito):** reload `https://localhost:PORT/autumn-campaign`.

   > "There it is. No login, no cookie, no session. That is the milestone."

4. Right-click → View Source. Scroll it.

   > "Worth thirty seconds. Look at what is *not* here: no `blazor.web.js`, no application
   > bootstrapper, no JavaScript framework at all. The public site is server-rendered HTML and the
   > editing tools are a completely separate front door. Two consequences: a visitor downloads a
   > page instead of an application, and because the response is plain HTML with no per-user
   > content, the whole thing can go in a cache later." (That caching is Phase 8.)

   Also visible: a `<title>`, a `<meta name="description">` if set, a canonical link, and the
   `data-template="article"` marker.

### Act 6 — Edit the draft; the public page does not move (4 minutes)

*This is acceptance criterion `P3 #3`, and it is the one that matters most to anyone who has been
burned by a CMS.*

1. In Browser A, change **Kicker** to `Campaign — REVISED`, and add a line to the **Body**:

   ```
   This paragraph only exists in the draft.
   ```

2. **Save draft.** Point at the amber banner:

   > "'This draft has moved on from what is published. The public site still shows v1 until you
   > publish again.'"

3. **Browser B:** reload `/autumn-campaign` — hard-reload it, to be beyond argument.

   > "Unchanged. Byte for byte. And this is not a caching artefact and it is not a filter somebody
   > remembered to write — the query that serves the public projects through the page's *published*
   > version and never mentions the draft at all. The draft row is not in the result set to be
   > picked by mistake. That is the difference between a rule and a property."

4. Show the preview tab again (reload it) — the revised text *is* there.

   > "Same moment. Two audiences. Two answers."

5. **Publish** again.
6. **Browser B:** reload. The revision appears.

   > "That is the ten-step loop, closed. Define, create, fill, save, preview, publish, view, edit,
   > confirm nothing moved, publish again."

**Pause here.** This is the natural applause point and the natural question point. Everything after
this is bonus.

### Act 7 — The safety net (6 minutes, cut freely if time is short)

Pick two or three of these. They are ordered by how much they tend to land.

**7.1 — Version history, diff, and restore.** From the page editor, **Version history**.

> "Every version this page has had. The draft is the only mutable row; everything else is frozen.
> Pick any two and compare them —" (open a diff) "— word by word, which is what an approver
> actually needs to see. And restoring an old version copies it into the draft rather than
> slamming it onto the live site, so a restore is still a decision somebody publishes."

**7.2 — A URL change leaves a 301 behind.** This is the page you pre-staged in §1.5.

- **Browser B:** go to `https://localhost:PORT/pricing-old`.

  > "I renamed that page's URL earlier. The old address still works — permanently redirected, which
  > is the status code that tells Google to move its index across rather than to check back
  > tomorrow. Nobody typed that rule. Renaming the page wrote it."

- **Browser A:** open `/api/cms/v1/redirects`.

  > "There it is, with a hit count. And if that rename had had fifty child pages under it, there
  > would be fifty-one rules here — the whole subtree moves in one transaction and every old address
  > follows. A live page always outranks a redirect at the same address, so re-using a retired URL
  > later is possible instead of being blocked forever."

- If asked about legacy migration: `/api/cms/v1/redirects/export` returns CSV, and the matching
  import takes thousands of rows and reports each bad line by number rather than refusing the file.
  That is the bulk path for moving an existing site across. **Which is exactly the open question in
  Act 8.**

**7.3 — Dead URLs are recorded, not just refused.**

- **Browser B:** visit `https://localhost:PORT/no-such-page` two or three times. Built-in 404.
- **Terminal:**

```bash
docker exec contentmanagementsystem-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'P@ssw0rd!' -C -d contentmanagementsystemdb -Q \
  "SELECT Url, HitCount, Referrer, LastSeenOn FROM NotFoundLogs ORDER BY HitCount DESC"
```

> "One row per URL, not one per request — a crawler cannot make this the biggest table on the site.
> Hit count three. This is the report that tells you which dead link is costing you traffic, and the
> referrer column tells you who is still pointing at it. Turning that into a fix is one redirect."

**7.4 — A broken page is a broken block.** No live demo for this one; describe it.

> "Templates run authored content. Content is authored by people, and people make things that
> break. So there is an error boundary around every zone *and* around every individual block: if a
> component throws, that one region drops out, the rest of the page renders, and the log records the
> page id, the zone, the version, and the block — not just a stack trace naming a component that
> four hundred pages share. If the template itself is missing entirely, there is a fallback that
> still puts the page's text in front of the reader. Eight tests cover the failure shapes, including
> the nasty one where a component emits half its markup and *then* fails."

**7.5 — Nothing shadows the application.**

> "The public site is a catch-all route: anything not otherwise claimed is treated as a content URL.
> The obvious risk is that somebody publishes a page at `/api` and takes down the integrations. Ten
> tests assert the outcome — not the registration order, the outcome — for `/api`, `/admin`,
> `/Account/Login`, `/health`, and the framework paths. And the reserved list the router protects is
> the same single list the page editor validates slugs against, so a page cannot be created at an
> address the site would then decline to serve."

### Act 8 — Close (3 minutes)

**Where we are.**

> "Phases 0 through 3 are done. That is the content model, the page and versioning engine, the
> publishing pipeline, routing, rendering, and preview — 109 of 281 planned tasks, and about 90 of
> 203 engineer-days. Roughly 1,500 automated tests, and every acceptance criterion in Phase 3 is
> met. Three Phase 3 items are still open: a performance benchmark harness, a visual-regression
> baseline, and one item waiting on a decision I need from you."

**What comes next.**

> "Phase 4 is reusable content — a footer or a banner authored once, updated everywhere in one
> publish. Phase 5 is the media library and image pipeline. Those two run in parallel. Then Phase 6
> is the authoring experience, which is where the scaffolding you saw today becomes a product: real
> editors, a media picker, drag-and-drop blocks. Phase 7 is workflow, permissions, and scheduling —
> approvals and 'publish this on Tuesday'. Phase 8 is SEO, caching, navigation, and search. Phase 9
> is hardening, accessibility, and launch."

**What I need from you.** Do not skip this — these are live blockers recorded in `task.md`, and
this room is where most of them get answered.

| | Question | Who owns it | What it blocks |
|---|---|---|---|
| **Q8** | Is there an existing site to migrate, and must its URL structure be preserved? | Product | `P3-30`, **now**. Bulk redirect import is built and needs a real legacy URL list to be tested against. |
| **Q2** | Hundreds of pages, or tens of thousands? | Product | Tree UI, search backend, caching topology. |
| **Q7** | Is SVG upload permitted at all? | Security | Blocks a Phase 5 task outright. Safest answer is no. |
| **Q6** | Is there a CDN in front of the site? | Ops | Cache headers and a purge integration in Phase 6. |
| **Q4** | One instance at launch, or scaled out? | Ops | Whether a Redis output cache is required. |
| **Q5** | Which email provider replaces the no-op sender? | Ops | Password resets and notifications. |
| **Q9** | Retention obligations on content versions and audit logs? | Legal | Version retention policy. |
| **Q10** | Does self-service registration stay on, and with what default role? | Security | Enforced in Phase 9; relevant to launch. |

Close on Q8 specifically — it is the one that is blocking work *today*.

---

## 3. If something goes wrong

| Symptom | Cause | Fix |
|---|---|---|
| `/admin/...` bounces to access-denied | Auth cookie predates the role insert | Log out, log back in |
| Templates screen is empty | Migrations did not run, or the server started before `ef-migrations` finished | Check the `ef-migrations` resource in the Aspire dashboard; restart `server` |
| Page editor says "captured no zones" | The page was created before the zones were added | Create a new page. Do **not** try to fix it live |
| Published URL 404s | Publish silently refused, or you are testing the wrong URL | Re-check on `/admin/pages` — State should read **Live**. Confirm the slug |
| Preview shows an empty frame | Zones defined but no values saved | Save the draft again |
| Shared preview link 404s rather than rendering | Token mistyped or truncated on copy | Issue a fresh one; the secret is shown once and is not recoverable |
| Build fails with a wall of `RZ1021` | Poisoned Razor build server on SDK 10.0.301 — a known trap, **not** the markup | `dotnet build-server shutdown`, rebuild. Do not edit the `.razor` files |
| Anonymous browser shows a draft | You are not actually anonymous | Confirm Browser B is a private window and has never logged in |

**The universal recovery:** the whole loop is covered by automated tests. If the live app misbehaves
in a way you cannot fix in fifteen seconds, say so, move on, and offer to show
`Server.Tests/Delivery/DeliveryTests` and `PreviewTests` afterwards. Do not debug in front of the
room.

---

## 4. Questions you should expect

**"How do we know the public really cannot see a draft?"**
Two independent mechanisms. The query that serves the public projects through the page's *published*
version and never mentions the draft, so there is no draft row in the result set to leak. And a page
only has a published route while it is published, so an unpublished page's URL does not resolve at
all. Asserted byte-for-byte across three intervening draft saves.

**"What happens if a developer breaks a template?"**
One zone or one block drops out and the rest of the page renders, with the page id, zone, version,
and block in the log. If the whole template is missing, a fallback still puts the page's text in
front of the reader. Covered for all three ways a component can fail, at both levels.

**"Can we roll back a bad publish?"**
Yes — version history restores any prior version into the draft, and you then publish it. The
restore is deliberately not instant-live: a restore is still a decision somebody makes on purpose.
Unpublishing is also one button, and it takes the page off the public site while keeping everything.

**"When can editors actually use this?"**
The engine works today; the editing surface is Phase 6, which is the largest phase in the plan (41
tasks, 34.5 engineer-days). Everything before it is what Phase 6 builds *on* — and it is deliberately
sequenced last among the content phases, because building a nice editor on an engine that leaks
drafts would be building it twice.

**"How fast is it?"**
Honest answer: not measured end to end yet — that is `P3-27`, one of the three open Phase 3 tasks.
What we have is spike numbers excluding the database, roughly 1.2 microseconds to validate a block
and 7 microseconds to render one, and live instrumentation on the running site
(`cms.page.render.duration`, tagged by template rather than by page, because pages are not slow —
templates are). The architecture is built for caching: the public page is static HTML with no
per-user content, and every render already accumulates its own cache-invalidation tags even though
the cache itself is Phase 8.

**"Is it secure?"**
Content is sanitized when written and again when rendered, under a configured allowlist. Link fields
re-apply a scheme allowlist at render time so a `javascript:` URL that arrived through an import
cannot execute. Every management endpoint carries a named permission policy and every write requires
an antiforgery token. Preview links store only a hash. A full security review is Phase 9.

**"Can we migrate our existing site's URLs?"**
The mechanism is built — CSV import and export, with per-row warnings rather than an all-or-nothing
file. What it has not had is a real legacy URL list to be tested against, because **Q8 is
unanswered**. This is the ask.

**"Why does the published page look so plain?"**
Because the CMS's job is to emit correct, semantic structure and a designer's job is to style it.
There is no site stylesheet in the repository yet. The markup carries the class names and
`data-template` hooks a designer needs.

---

## 5. Resetting between runs

Doing this demo twice needs a clean tree — a second `autumn-campaign` at the root collides with the
first on the sibling-slug rule.

**Cheapest reset (keeps your account and the template zones):** delete the demo pages through
`DELETE /api/cms/v1/pages/{id}` — or just use fresh titles the second time
(`Autumn Campaign 2`, `Winter Campaign`).

**Full reset:** stop `aspire run`, drop the database, restart. Migrations and seed rows come back
automatically; the startup reconciler re-registers both templates. **You will have to redo §1.2 to
§1.5** — account, confirmation, roles, zones, and the pre-staged redirect page.

```bash
docker exec contentmanagementsystem-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'P@ssw0rd!' -C -Q \
  "ALTER DATABASE contentmanagementsystemdb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
   DROP DATABASE contentmanagementsystemdb;"
```

---

## 6. After the demo — update `task.md`

Performing this demo closes two exit gates. When it is done:

1. **Phase 3 exit gate** — change `[~]` to `[x]` and record the date and who watched it.
2. **`P1 #1`** — Act 1 is the browser journey it was waiting for. Change `[~]` to `[x]` and note
   that it was performed during the Phase 3 demo. Then the **Phase 1 exit gate** closes too, and its
   row in the progress table gets a date.
3. Record any answer you got to **Q8** in the Blocking decisions list, and unblock `P3-30`.
4. Update the **Last updated** date at the top of `task.md`.
