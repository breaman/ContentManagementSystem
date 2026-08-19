# 0023 — One allow rule turns a permission into an allowlist for that principal

- **Identifier:** D23
- **Status:** Accepted
- **Source:** tasks `P7-04`, `P7-06`, [`spec.md` §21.2](../../spec.md), acceptance criterion `P7 #5`

## Context

[§21.2](../../spec.md#212-section-level-acls) says section ACLs "narrow" global role grants to a
subtree, and it states four rules: a rule applies to a page and its descendants, deny beats allow at
the same depth, a deeper rule beats a shallower one, and `Administrator` bypasses everything.

Those four rules do not determine what happens on a page **no rule mentions**, and the two readings
give opposite answers:

1. **A rule only ever adds.** No matching rule means the caller keeps whatever their role gave them
   globally. An editor granted `Content.Edit` on `/products` can still edit `/about`, because their
   `Editor` role already said so and nothing has said otherwise.
2. **An allow implies a boundary.** Granting somebody `/products` is a statement about where they
   work, so it refuses everywhere it does not reach.

Reading 1 is the obvious implementation and makes the feature useless: an allow rule grants nothing
the principal did not already hold, so the only usable rule is a deny, and confining a team to a
section means writing a deny against every other branch of the tree and remembering to write another
one each time somebody adds a top-level page.

Acceptance criterion `P7 #5` settles it in the direction of reading 2 — "a user with an ACL on
`/products` can edit that subtree and receives `403` on `/about`" — but it settles it for one case
rather than as a rule, and the rule is what the resolver needs.

## Decision

**The presence of any `IsAllow` rule for a principal and a permission makes that permission an
allowlist for them: every page outside the reach of an allow rule is refused.** A principal with only
deny rules keeps their global grant everywhere the denies do not reach. A principal with no rules at
all is unaffected.

Concretely, in `AclFilter`:

- No rules → allowed. Most callers on most sites, and it costs one cheap query per request per
  permission.
- Rules, none of them an allow → the default is *allowed*, and a page is refused when the winning
  applicable rule is a deny.
- Rules including an allow → the default flips to *refused*, and a page is allowed when the winning
  applicable rule is an allow.

"Winning" is unchanged from the spec: deepest applicable rule, and deny over allow at equal depth.
The site root is outside every allow rule by construction, which is why creating a top-level page
asks `IsAllowedAtRootAsync` rather than being silently permitted.

## Consequences

- **An allow rule is a boundary, and administrators have to be told that.** Granting an `Editor`
  `Content.Edit` on `/products` takes away their edit rights everywhere else. That is the intended
  effect and it is also the surprising one; it is stated on `PageAcl`, in the resolver, and here.
- **A grant to a role narrows every holder of that role.** An allow on `/products` for `Editor`
  confines all editors to `/products` for that permission. This follows from the same rule and is
  what "narrow a global grant to a subtree" has to mean when the grant is held by a role.
- **The failure direction is closed rather than open.** A rule the resolver cannot interpret, a role
  id it cannot resolve, or a principal it does not recognise ends in refusal, never in access.
- **Read denials are indistinguishable from absence.** Because a hidden subtree must be hidden
  entirely (`P7 #6`), a refused `Content.Read` answers *not found*. A `403` where a `404` would
  otherwise have been returned is an existence oracle: an outsider can map the content tree by
  watching which guessed ids come back which way.
- The precedence arithmetic lives in one type, `AclFilter`, shared by the single-page check and the
  tree filter, so the two cannot disagree. `AclFilterTests` pins every clause, including
  deny-over-allow with the rows supplied in both orders — an answer that depended on the query plan
  would not be an answer.
