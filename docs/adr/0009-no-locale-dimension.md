# 0009 — No locale dimension anywhere; `en-US` only

- **Identifier:** D9
- **Status:** Accepted
- **Source:** [`spec.md` §19](../../spec.md), open question Q1 (resolved)

## Context

Localization is the kind of dimension that is cheap to add at design time and expensive to add
later, so it was asked about explicitly rather than assumed. The answer was that this deployment
will never be multilingual.

## Decision

No locale column anywhere in the model. `SiteSettings.Culture` holds a single culture (`en-US`),
used for formatting and for the `lang` attribute on rendered pages.

## Consequences

- A column is removed from four hot tables and a whole v2 workstream disappears (roughly 15
  engineer-days saved).
- Reversing this would cost an estimated 25–35 engineer-days: it touches routing, the payload
  envelope, publishing, and every editor surface. It is a decision to revisit deliberately, not
  incidentally.
- `SiteSettings.Culture` still exists, so the rendered `lang` attribute has a single source of truth
  (task P9-10) — this is about the *dimension*, not about hardcoding a culture string.
