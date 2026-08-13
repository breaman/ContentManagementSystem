# 0015 — Field configuration is declared in code; the JSON Schema is generated from it

- **Identifier:** D15
- **Status:** Accepted
- **Source:** task `P1-12`, [`spec.md` §7.2, §7.3](../../spec.md)

## Context

[§7.2](../../spec.md#72-field-configuration) says configuration "is validated against a
per-field-type JSON Schema when the zone is saved, so a `Developer` cannot persist a configuration
the editor component cannot honor", and [§7.3](../../spec.md#73-extensibility--adding-a-new-field-type)
makes "define the configuration JSON Schema" step 3 of adding a field type.

Read literally, that means authoring a JSON Schema document per field type and interpreting it here.
Three things make that the wrong shape for this repository.

**Interpreting JSON Schema means interpreting all of it.** A hand-written subset validator — object,
typed properties, `enum`, `minimum`, `additionalProperties: false` — covers every built-in field
type in about two hundred lines. It also silently ignores the `oneOf` an extension author reaches
for on their second day, and a schema keyword that is quietly skipped is a configuration rule that
does not exist. Taking a full implementation instead means a new third-party dependency on the
validation path of every structure write, for rules the platform itself writes.

**Two of the rules that matter cannot be said in JSON Schema at all.** That a `pattern` compiles
under .NET's regex engine, and that a lower bound is not above the upper bound it is paired with.
`{ "min": 5, "max": 2 }` is not an odd zone, it is a zone no value can satisfy, and the symptom is
an editor who cannot publish and cannot see why. Both would have to live outside the document
regardless, so the document was never going to be the whole answer.

**The backoffice needs more than a schema.** `P1-29` builds a zone configuration form and `P6`
builds the editors; both need to know a setting's kind, its bounds, its help text, and — for the
settings stubbed in `P1-11` — whether the phase that honours it has shipped. Deriving that by
walking an arbitrary JSON Schema is strictly harder than reading a declaration.

## Decision

**A field type declares its configuration as `FieldConfigurationSchema` in C#, beside the field
type. The JSON Schema document the spec calls for is generated from that declaration.**

- `IFieldType.ConfigurationSchema` is the new member — a closed list of
  `FieldConfigurationSetting`s (name, kind, description, bounds, allowed values, syntactic format)
  plus the `FieldSettingRange` pairs that bound each other.
- `FieldConfigurationValidator` is the authority. It runs on every structure write, and its
  diagnostics carry `config.*` codes and the offending setting's name as a path.
- `FieldConfigurationSchemaWriter` renders a draft 2020-12 document per field type for
  `/api/cms/v1/field-types` (`P1-24`) and the configuration form. Rules JSON Schema cannot express
  are carried as `x-cms` annotations, so the document says everything the server enforces even where
  a generic validator would skip it.
- **Configuration is closed.** A setting the schema does not declare is refused. A mistyped
  `maxlength` is a save error rather than a line that persists and does nothing.
- **`required` is not a setting.** It is carried by the `IsRequired` column on `Zone` and
  `BlockTypeProperty` and reaches a field type through `FieldConfiguration.IsRequired`. Declaring it
  as a setting throws; writing it into `ConfigurationJson` is refused.

## Consequences

- The spec's wording is met in substance and not in mechanism: there is a per-field-type JSON Schema,
  served and consumable, but nothing in the platform reads one. An extension author who expected to
  hand over a `.json` file writes a C# declaration instead. That is the ordinary path in a codebase
  where the field type it describes is already C#.
- A client that only honours the generated document accepts configurations the server refuses — the
  compiled `pattern` and the ordered range are annotations to it. The document is therefore a
  convenience for the editor, never the gate.
- Closed configuration is a breaking constraint on the phases that finish the stubbed field types.
  A setting they will read has to be declared before it can be stored, so `media`'s `allowedTypes`,
  `link`'s `allowedKinds`, `pageReference`'s `allowedTemplates`, and `reusable`'s `allowedTypes` are
  declared now and marked `NotEnforcedUntil`. Configuring one is accepted with a warning naming the
  phase. The alternative — refusing them until their phase ships — makes a developer set up half a
  content model and come back for the rest.
- Moving `required` out of the configuration blob removed a second source of truth that would have
  been free to disagree with the column the admin screens write. It also means
  `FieldConfiguration.Parse` now takes the flag, and every caller has to supply it: the zone-save
  path in `P1-22` and the schema walk in `P1-15` both read it from the row they are validating
  against.
- `FieldConfiguration` is no longer sealed and its `TryGetValue` is virtual, so the configuration
  contract test can record which settings a field type actually reads. The two halves of the
  contract fail in opposite directions and both silently — a setting read but not declared can never
  be stored, and a setting declared but not satisfiable is one a developer is invited to write and
  then refused — so neither half is trustworthy checked alone.
