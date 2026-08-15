# 0020 — The content catch-all is mapped last, and reserved prefixes are refused at both ends

- **Identifier:** D20
- **Status:** Accepted
- **Source:** tasks `P3-13`, `P3-14`, `P3-15`, `P3-31`, [`spec.md` §10.3, §15.1](../../spec.md)

## Context

Public delivery is one terminal endpoint:

```csharp
app.MapGet("/{**slug}", DeliveryEndpoint.HandleAsync).AllowAnonymous();
```

It has to match everything, because a CMS page can be published at any URL an editor chooses. That
makes it the single highest-consequence route in the system: if it wins where it should not, it
swallows the management API, the backoffice, the sign-in pages, and the health endpoints — and every
one of those failures presents identically, as "the CMS returns its 404 page", which is a sentence
nobody reads as a routing bug. It is risk **R6** on the plan.

Two independent things can go wrong, and they need different answers.

**A path an endpoint owns is matched by the catch-all instead.** ASP.NET Core routing does not pick
the first registered match; it picks the most specific one, so a literal `/health` beats
`/{**slug}` regardless of order. That is a real guarantee and it covers most of the surface. It does
*not* cover everything: terminal middleware and static asset handling run before endpoint routing at
all, and future work — output caching policies, rate limiting, a media endpoint in Phase 5 — is
sensitive to where in the pipeline the catch-all sits.

**A path an endpoint owns is matched by nothing, and the catch-all answers anyway.** This is the case
precedence does not help with, and it is the one that actually bit. `GET /api/cms/v1/no-such-thing`
matches no API endpoint, so it falls through to the catch-all, which cheerfully serves the site's
HTML 404 page. A JSON client then reports a parse failure somewhere else entirely — the same class of
misdirection that `UseStatusCodePagesWithReExecute` produced in `P1-21`, where a 403 from the
authorization middleware came back to a client as a 400 about a content type.

## Decision

**1. The catch-all is mapped last, after every other endpoint**, and `MapCmsDelivery` says so in its
own documentation. Precedence makes this belt-and-braces for endpoint routes and load-bearing for
everything ordered rather than scored.

**2. The delivery endpoint refuses to serve content under a reserved first segment.** A request
whose first path segment is one of

```
admin  api  media  _blazor  _framework  account  health  alive  sitemap.xml  robots.txt  preview
```

gets a bare `404` with no body, rather than the site's 404 page.

**3. That list has exactly one home.** It is `Slugs.Reserved`, already used by `Slugs.Validate` to
refuse a page at one of those addresses at the root of the site. Delivery reads it rather than
restating it, so the two ends cannot disagree: no page can be created at a reserved prefix, and
nothing can be served from one.

**4. Interactivity is scoped to `/admin`.** Public pages are rendered by
`CmsDeliveryDocument`, which carries no `@rendermode` and no `blazor.web.js`; the interactive router
in `Routes.razor` owns the backoffice. `InteractiveRoutingTests` asserts by reflection that no
routable component carrying a `RenderModeAttribute` has a route outside `/admin`, which is what turns
[ADR-0002](./0002-static-ssr-public-interactive-wasm-backoffice.md) from a convention into something
a test enforces.

## Consequences

**Reserved prefixes are permanently unavailable to content.** Eleven first segments can never hold a
page. That is the cost of a single-application deployment and it is the right trade: the alternative
is a content slug that can shadow the sign-in page.

**A path under a reserved prefix that nothing matches is a bare 404.** Deliberately not the site's
404 page. An operator debugging a mistyped API route sees a 404, not a marketing page, and a page can
never *appear* to be served from a prefix it could not have been published at.

**`P3-15` asserts outcomes, not registration order.** The tests check that `/api`, `/admin`,
`/Account/Login`, `/health`, `/alive`, and `/_framework/blazor.web.js` still reach the endpoints that
own them, and that an ordinary content URL does reach the catch-all. Order is one way to get that
right and precedence is another; what must not change is the outcome, so that is what is pinned. The
last of those tests matters more than it looks: without it, deleting the catch-all entirely would
pass every other assertion in the file.

**`/client-hello` moved to `/admin/client-hello`.** It was scaffolding, it was interactive, and it
sat in the public route space — the one violation of rule 4 when the rule was written.

**The scaffolding home page still owns `/`.** `Components/Pages/Home.razor` declares `@page "/"`, so
the site root reaches it rather than the CMS. [§10.3](../../spec.md#103-url-rules) gives `/` to a CMS
page, so a real deployment removes that page; it is left in place here because it is template
scaffolding rather than CMS code, and deleting it is a decision for whoever adopts the template.
Recorded because it is otherwise discovered as "why does my home page not publish".

**`/_blazor` is reserved but not mapped.** It is the SignalR endpoint for interactive *server*
rendering, which this solution never uses ([ADR-0002](./0002-static-ssr-public-interactive-wasm-backoffice.md)).
Keeping it reserved costs nothing and means turning that render mode on later cannot collide with a
published page.

## Alternatives considered

**Map the catch-all first and let it fall through.** An endpoint handler cannot decline after it has
been selected without re-running routing, and a middleware that tried to would duplicate the routing
table. Rejected.

**Serve the site's 404 page everywhere, including under `/api`.** Simpler by one branch, and it makes
every API misroute look like a content problem. Rejected.

**Keep a separate reserved-prefix list in the delivery endpoint.** Two copies drift, and the copy that
drifts is the one nobody wrote a test for. Rejected — this is the same reasoning that put the zone
and block-property rules into one `SlotRules` in `P1-23`.
