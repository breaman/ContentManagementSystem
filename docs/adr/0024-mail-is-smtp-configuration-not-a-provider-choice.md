# 0024 — Mail is SMTP configuration, not a provider chosen in code

- **Identifier:** D24
- **Status:** Accepted
- **Source:** task `P7-18`, open question **Q5**, [`spec.md` §14.8](../../spec.md)

## Context

`IdentityNoOpEmailSender` discarded every message the system produced — password resets and account
confirmations included — and Phase 7 adds workflow notifications on top of it. Open question **Q5**
asks Ops which email provider replaces it, and has never been answered.

Waiting is not neutral. Notifications are what makes an approval queue work: an author who submits a
page and is never told it was rejected will conclude the CMS lost it. Shipping the phase with a
sender that silently drops mail would make every one of those failures invisible.

Nor is guessing. Picking SendGrid or SES in code means a NuGet package, an account model, and a
credential shape chosen on Ops' behalf, in a decision that is theirs and that would have to be undone
if they chose otherwise.

## Decision

**Send over SMTP, and treat the provider as configuration.** `CmsEmailOptions` carries a host, port,
credential, and from-address; `SmtpCmsEmailSender` uses them. Every candidate — SendGrid, Mailgun,
Amazon SES, Microsoft 365, a corporate relay — exposes an SMTP submission endpoint, so answering Q5
becomes filling in a host and a secret rather than a code change.

**With no host configured, the deployment gets `LoggingCmsEmailSender`**, which writes what it would
have sent at warning level and reports itself unconfigured. Identity's three account emails go
through the same transport via `IdentityCmsEmailSender`, so a deployment configures mail once.

`System.Net.Mail.SmtpClient` is used rather than MailKit. That is a deliberate acceptance of the
framework's "obsolete for new development" guidance: this sends a handful of short plain-text
messages a day to a submission endpoint, which is squarely inside what it does correctly, and it
keeps a mail library out of the dependency graph for a feature whose provider is still an open
question.

## Consequences

- **Q5 stops blocking anything.** It is a configuration value with a safe, visible default, in the
  same shape as the answer `P5-06` gave open question Q7.
- **A delivery failure never fails the operation that caused it.** The in-app notification row is
  committed first and the send is attempted after; a sender returns `false` and logs rather than
  throwing. An approval that succeeded must not be reported as having failed because a mail server
  was down, and the inbox row is what makes the missed message recoverable.
- **Bodies are plain text.** Every value interpolated into one — a page title, an editor's note, a
  rejection reason — is written by a person, and a text body has nowhere for markup to be
  interpreted.
- **The unconfigured state is visible rather than silent.** `LoggingCmsEmailSender` warns on every
  send, and the registration-confirmation screen only offers its "confirm this account" shortcut when
  mail is unconfigured *and* the environment is Development — the scaffolded version offered it
  whenever the sender was the no-op one, which on a production deployment that forgot to configure
  SMTP would have handed any visitor a working confirmation link for any address they typed.
- Swapping in a provider SDK later, for deliverability reporting or templating, is a second
  `ICmsEmailSender` and a registration line. Nothing that raises a notification knows how mail is
  sent.
