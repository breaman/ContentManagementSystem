# Administrator guide

**Task:** `P9-21`. For whoever runs this site day to day: people, permissions, settings, and the
things only an administrator can break.

Deployment and monitoring are [`operations.md`](../operations.md). This is the part you do from
inside the application.

---

## Roles

Seven, and they are additive — somebody holding two gets the union. **The role ids are part of the
database contract**, because a role-scoped access rule stores one; do not renumber them.

| Role | Can | Notably cannot |
|---|---|---|
| **Administrator** | Everything, including users and settings | — |
| **Developer** | Everything content, plus templates and block types | Manage users |
| **Editor** | Create, edit, publish, and delete pages and media | Approve a submission |
| **Author** | Create and edit; submit for review | Publish, delete |
| **Approver** | Review, approve, reject, publish, schedule | Change structure |
| **MediaManager** | Full media library, including permanent deletion | Edit pages |
| **Viewer** | Read the backoffice, including drafts in preview | Change anything |

**Editor and Approver are deliberately different.** An editor may press publish; somebody else has to
say the content is ready. Collapsing them makes two-step review one button press away from nothing.

### The three roles that require a second factor

`Administrator`, `Developer`, and `Approver` cannot be used without two-factor authentication. An
account holding one of them and no second factor can reach account management and nothing else, until
it enrols. This applies from the account's *next request* — including an account you grant a role to
while its session is open, which is how most accounts meet it.

If somebody reports "I have lost the backoffice", check this first. It is a redirect to
`/Account/Manage/EnableAuthenticator`, not an error message.

---

## Adding people

**Self-service registration is off by default**, and the registration pages answer *not found* rather
than *forbidden* — a refusal a 404 would not have produced tells a caller the door is there. Accounts
are created by an administrator.

1. Create the account with an email address.
2. Grant roles — the fewest that let the person do their job. Roles are additive and adding one later
   is easy; noticing that somebody has had `Administrator` for eight months is not.
3. The account confirms its own email address before it can sign in. That page stays open even with
   registration disabled, for exactly this reason.

Passwords are at least twelve characters, with no requirement for a digit, a capital, or a symbol —
those rules are what produce `Password1!`. Length does the work, plus a screen that refuses passwords
appearing on a common-password list or containing the person's own name or address.

---

## Section access

Roles say *what*; section ACLs say *where*. A rule hangs on a page and reaches everything beneath it.

Two clauses decide every question:

- **Deeper beats shallower.** A rule on `/products/pricing` wins over one on `/products`.
- **Deny beats allow** at the same depth.

And one that surprises people: **one allow anywhere turns a permission into an allowlist for that
principal** ([ADR 0023](../adr/0023-one-allow-rule-makes-a-permission-an-allowlist.md)). Granting an
editor `Content.Edit` on `/products` does not add `/products` to what they could already do — it
narrows them *to* `/products`. That is what makes an ACL capable of narrowing rather than only
widening, and it is the behaviour to explain before somebody reports it as a bug.

`Administrator` bypasses the rules. A bypass is written to the audit log **only when a rule would
otherwise have refused**, so the log records the exceptions rather than every administrator action.

---

## Review workflow

Set in **Settings → Workflow**, one of three:

| Mode | What it means |
|---|---|
| `None` | Anybody who may publish, publishes |
| `Simple` | A draft is submitted, and any approver may approve it |
| `TwoStep` | As `Simple`, and **the approver may not be the author** |

In `TwoStep`, publishing an unapproved version is refused as well — otherwise the rule would be one
button press away from nothing.

**A draft is frozen while it is under review.** Saves against it are refused, because an approval has
to be a statement about the content that then publishes. A rejection keeps the refused version exactly
as it was refused and hands the author an editable copy, comments intact.

---

## Site settings

| Setting | Effect |
|---|---|
| Site name | Used in the document head and in social metadata |
| Home page | The page served at `/` |
| Not-found page | The CMS page rendered for unresolved URLs |
| Culture | The `lang` attribute on every public page. A screen reader chooses its pronunciation from it |
| Default share image | Used by pages that specify none |
| Redirect to parent on unpublish | Whether unpublishing a page redirects visitors up rather than 404ing |
| Google site verification | Rendered into the head |
| **Version retention days** | See below |
| **Audit log retention days** | See below |

### Retention

Both default to **zero, meaning keep everything**, and that is a deliberate placeholder rather than a
recommendation: how long an organisation's records last is its decision.

- **Version retention.** Superseded versions older than the window are pruned nightly. Five things
  are never pruned regardless: the current draft, the published version, anything that was ever
  published, any version an editor **named**, and the most recent twenty per page. Pages in the
  recycle bin keep everything — a restore that came back with no history is not a restore.
- **Audit retention.** Audit rows older than the window are deleted nightly, in batches. There are no
  exceptions and that is on purpose: a log with holes in it is worse evidence than a shorter one.

Set both. `AuditLog` is written on every save an editor makes, so with a window of zero it grows with
editorial activity for as long as the site is used — and it is written on the same transaction as the
content, so eventually every save waits for it.

---

## Redirects and 404s

**Moving or renaming a page creates a redirect automatically.** Internal links do not need one — they
are stored as page ids and follow the page — but every external link, bookmark, and search result
does.

The dashboard's *needs attention* tile lists 404s that are still taking real traffic. In the first
48 hours after a launch, check it hourly and create redirects for anything with traffic (`L-10`);
after that, weekly is enough.

---

## The audit log

Every tracked change, with who, when, and what changed. It is read-only and there is no way to edit
it from the application — which is the property that makes it evidence.

High-churn derived tables are deliberately excluded: the search index, the outbox, renditions, edit
locks, 404 logs, and content references. Those are written by background services rather than by
people, and including them would grow the table without bound and slow every save.

---

## Things only you can break

- **Permanently deleting media.** Guarded by a where-used check, and refused while anything uses it.
  Once it goes, it is gone — the recycle bin is the reversible one.
- **Permanently deleting a page.** Asks you to type its name. Takes its whole subtree and all history.
- **Changing a template's zones** through the schema sync. It refuses to remove or retype a zone, but
  it will happily add a *required* one — which makes every existing page fail validation on its next
  publish. Add required zones with a plan for the content that has to fill them.
- **Turning off a background service.** Each switch in `Cms:*` is there so a deployment can make a
  deliberate choice; the failure mode of all of them is silence. `/health` is what tells you.
- **Rotating the media signing key without a grace period.** Every image on the site breaks at once.
  [`operations.md §5`](../operations.md#5-secrets) has the procedure.
