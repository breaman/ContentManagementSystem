# Contributing

Conventions for working in this repository. They exist because each one has a specific failure it
prevents; the reason is given so you can tell when a rule genuinely does not apply.

Coding style beyond this document lives in [`.editorconfig`](.editorconfig) and the rules under
[`.claude/rules/`](.claude/rules/). The functional specification is [`spec.md`](spec.md); the work
plan is [`task.md`](task.md); decisions are in [`docs/adr/`](docs/adr/).

## Getting set up

```bash
dotnet tool restore                                     # dotnet-ef is a local tool
cd src/ContentManagementSystem.Server && npm install     # Bootstrap SCSS toolchain
npm run sass-dev                                         # or sass-prod; site.css is not checked in
cd ../.. && aspire run                                   # SQL Server + Azurite + server
```

Integration tests need Docker running. See [`docs/phase-0-baseline.md`](docs/phase-0-baseline.md)
for what the stack starts and for the RZ1021 build-server issue you will eventually hit.

## Projects

| Project | Holds |
|---|---|
| `ContentManagementSystem.Data` | EF entities, `IEntityTypeConfiguration<>` classes, migrations, seeding |
| `ContentManagementSystem.Shared` | DTOs, field-type contracts, validation, `FieldLengths` |
| `ContentManagementSystem.Core` | Domain services — content, publishing, routing, media, security |
| `ContentManagementSystem.Rendering` | Razor Class Library: templates, block components, field renderers |
| `ContentManagementSystem.Server` | Delivery pipeline, management API, media endpoints, hosted services |
| `ContentManagementSystem.Client` | Backoffice WebAssembly UI |
| `tests/*.Tests` | Unit, data integration, API integration, and E2E suites |
| `tests/ContentManagementSystem.TestSupport` | Shared test infrastructure; not a test suite itself |

`Rendering` is a Razor Class Library specifically so that public delivery and backoffice preview
render the same components — see [ADR 0010](docs/adr/0010-shared-rendering-rcl.md).

## Entities

- Derive from **`FingerPrintEntityBase`** for anything an editor mutates. `FingerPrintInterceptor`
  stamps `CreatedOn/By` and `ModifiedOn/By` automatically, so attribution never needs to be written
  by hand.
  Use plain `EntityBase` only for derived or machine-written rows where authorship is meaningless.
- Every entity gets an explicit `IEntityTypeConfiguration<>` in `Data/Configurations/`. Do not
  configure entities inline in `OnModelCreating` — one file per entity keeps keys, indexes, and
  lengths findable.
- **Column types come from `ColumnTypes`.** Never write a provider type as a string literal. This is
  what stops one money column being `decimal(18,2)` while another silently truncates cents.
- **String lengths come from `FieldLengths`.** The same constant backs the EF configuration and the
  API contract's validation attribute, so a column and its validator cannot drift apart.
- Instants are `DateTimeOffset` and are stored UTC. `ConfigureConventions` already maps every
  `DateTimeOffset` to `ColumnTypes.Timestamp`; do not override it per property.
- Soft-deletable entities carry `IsDeleted` with a global query filter. Recycle-bin queries call
  `IgnoreQueryFilters()` explicitly. Never rely on `Remove()` — `SoftDeleteInterceptor` is a safety
  net, not the intended path.

### Identity schema version

`IdentitySchema.Version` is part of the database contract, not a runtime preference: it changes the
shape of the generated EF model. Anything that constructs an `ApplicationDbContext` — the web host,
design-time tooling, test fixtures — must configure the same value, or EF reports the model as
having pending changes even when the migrations are current.

## Migrations

Migrations are numbered in [`task.md`](task.md); add them in that order.

```bash
cd src/ContentManagementSystem.Server
dotnet ef migrations add <Name> -p ../ContentManagementSystem.Data
```

**Review the generated migration before committing it.** Specifically:

- Read every generated statement. A rename that EF models as drop-plus-add destroys data.
- `Up` **and** `Down` must both apply cleanly. `MigrationsApplyFromEmptyTests` asserts this in CI
  against a real SQL Server container, from empty, on every build.
- Filtered unique indexes (`WHERE IsDeleted = 0`, `WHERE IsPublished = 1`) are usually what you want
  in this schema — they let soft-deleted and draft rows coexist with live ones. Plain unique indexes
  on those columns are the standard CMS schema trap.
- URL columns exceed SQL Server's 900-byte index key limit. Index the `binary(32)` hash column, not
  the URL.
- Never edit a migration that has been applied anywhere but your own machine. Add a new one.

After launch the policy switches to roll-forward-only and `Down` methods become documentation
(task P9-23). Until then they are tested.

## The save interceptors

Three `SaveChangesInterceptor`s in `Data/Interceptors/` carry everything that happens to an entity on
its way to the database. They run in this order, and the order is the behaviour:

1. **`SoftDeleteInterceptor`** rewrites a `Remove()` of an `ISoftDeletable` into a flag update.
2. **`FingerPrintInterceptor`** stamps `CreatedOn/By` and `ModifiedOn/By`.
3. **`AuditLogInterceptor`** writes an `AuditLog` row for every tracked change — so a soft delete is
   audited as the update it became, carrying the fingerprints stamped a step earlier.

`CmsSaveInterceptors` owns that order and the registration. **Anything that builds
`DbContextOptions` by hand must add them**: unlike a `SaveChanges` override they are not part of the
context type, so a context built without them saves happily and records nothing. The places that do
are the host, `SqlServerFixture`, and the two suites that re-register the context to inject a failing
interceptor — `CmsSaveInterceptors.Resolve(provider)` from a scope, `Create(users, clock)` without
one.

High-churn derived tables must be **excluded** from audit capture — `SearchDocument`,
`OutboxMessage`, `MediaRendition`, `EditLock`, `NotFoundLog`, `ContentReference`. Including them
grows the audit table without bound and slows every `SaveChanges`. If you add a table that is written
by a background service rather than by a person, it almost certainly belongs on that list in
`AuditLogInterceptor`.

The interceptors mutate the change tracker only, with no SQL of their own, so
`SaveInterceptorTests` drives them directly against a context that never opens a connection. Test
new behaviour there rather than through a container.

## Tests

- **TUnit is the test framework — use it for every new test.** FluentAssertions and NSubstitute
  round it out. Tests are written without `// Arrange` / `// Act` / `// Assert` comments; match the
  naming and capitalisation of nearby test methods.
- `[Test]` for every test, `[Arguments(...)]` for inline cases, `[MethodDataSource(nameof(Member))]`
  for computed ones. Per-test setup and teardown are `[Before(HookType.Test)]` and
  `[After(HookType.Test)]` methods rather than a lifecycle interface.
- Pass `TestContext.Current!.Execution.CancellationToken` to anything that accepts one. The build
  runs warnings-as-errors, and `Current` is nullable, hence the `!`.
- TUnit runs on Microsoft.Testing.Platform, so a test project *is* its own runner: `dotnet run` in
  the project directory executes the suite, and `dotnet test` still works with runner flags placed
  after `--`. Never add `Microsoft.NET.Test.Sdk` or `coverlet.collector` — either one takes over the
  entry point and discovery then finds nothing.
- Data and API tests run against a real SQL Server container, never the in-memory provider. The
  behaviour that matters here — filtered unique indexes, `rowversion` conflicts, query filters — has
  no faithful in-memory equivalent. Take the container with
  `[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]` and accept it as a
  primary-constructor parameter.
- TUnit parallelises tests *inside* a class as well as across classes, which xUnit did not. One
  container cannot serve every suite at once, so each of those classes also carries
  `[NotInParallel(SqlServerConstraint.Key)]` — the shared constraint key that queues them
  behind one another.

### Package licensing

**FluentAssertions is pinned to the 7.x line on purpose.** Version 8.0.0 moved to a commercial
licence that would create a per-developer obligation for this repository; 7.2.2 is the last
Apache-2.0 release. Do not let a routine dependency bump cross that boundary. The same reasoning
chose SkiaSharp over ImageSharp — see
[ADR 0011](docs/adr/0011-skiasharp-image-processing-no-avif.md).

All package versions are declared centrally in
[`Directory.Packages.props`](Directory.Packages.props). Never put a `Version` attribute on a
`PackageReference`.

## Build hygiene

`TreatWarningsAsErrors` is on across the solution and the build is expected to be **warning-free**.
If a warning is genuinely not actionable, suppress it narrowly — in the one project, with a comment
saying why — rather than widening `NoWarn` at the root.

## Security rules that are not negotiable

These are the ones where a reasonable-looking shortcut causes a real incident:

- HTML is sanitized on write **and** on render ([ADR 0008](docs/adr/0008-sanitize-on-write-and-on-render.md)).
- **Widening a sanitization profile is a security change.** The allowlists live in one file,
  `Core/Security/SanitizationPolicy.cs`. `SanitizationPolicyTests` refuses anything executable, and
  the `XSS corpus` CI job is a required check — it asserts against a set of elements and attributes
  no profile may ever permit, deliberately restated there rather than derived from the policy, so a
  widening cannot make the suite agree with itself.
- Adding markdown syntax means widening a profile to carry what it emits, so Markdig extensions are
  enabled one at a time and only alongside that change ([ADR 0016](docs/adr/0016-markdown-extensions-bounded-by-the-sanitization-allowlist.md)).
- Internal links are stored as `pageId`, never as URL text ([ADR 0006](docs/adr/0006-internal-links-stored-as-page-id.md)).
- Delivery filters on `PublishedVersionId` **at the data layer**, so an unpublished draft cannot leak
  through a missing check higher up.
- Write endpoints take explicit DTOs. A client must not be able to mass-assign `Status: "Published"`;
  status transitions happen only through dedicated endpoints.
- Authorization is enforced in the **service layer**, not only at the endpoint and never in the
  client.
- Uploaded files are validated by magic-number sniffing, not by extension, and stored outside
  `wwwroot` under server-generated keys.
- Media metadata is stripped after orientation is baked in. GPS coordinates in a published photo are
  a privacy incident.

## Decisions

Anything architecturally load-bearing gets an ADR in [`docs/adr/`](docs/adr/) — one decision per
file, including what it costs. Records are superseded, never rewritten. A de-risking spike that
returns no-go must record its agreed fallback as an ADR.
