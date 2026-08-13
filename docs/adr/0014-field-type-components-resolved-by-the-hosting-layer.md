# 0014 — A field type's components are resolved by the hosting layer, not declared by the field type

- **Identifier:** D14
- **Status:** Accepted
- **Source:** task `P1-10`, [`spec.md` §7, §5.2](../../spec.md)

## Context

[§7](../../spec.md#7-field-type-catalog) defines a field type as a triple — a storage contract, an
editor component, and a renderer component — and puts all three on one interface:

```csharp
Type EditorComponent { get; }             // rendered in backoffice
Type RendererComponent { get; }           // rendered on public site
```

The project layout in [§5.2](../../spec.md#52-project-structure) puts the three parts in three
different assemblies, and the reference graph between them runs one way:

```
Shared  ←  Core  ←  Rendering        Shared  ←  Client
```

`Core` holds the field type implementations (`Core/Fields/Types/`, and §7.3 names that folder as the
extension point). `Rendering` holds the renderer components and references `Core`. `Client` holds
the editor components. Neither is visible from `Core`, so `typeof(RichTextRenderer)` cannot be
written inside `RichTextFieldType` — not as an inconvenience, but because the reference would be a
cycle.

The alternatives that keep the property populated are all worse than the problem:

| Option | Why not |
|---|---|
| Move the field types up into `Rendering` | Validation, sanitization, and reference extraction run on the server during publish, with no Blazor involved. Putting them in a Razor Class Library to satisfy a `typeof` puts the content model behind the UI. |
| Resolve the component by type name at first access | Reflection by string: renames compile fine and fail at render, on the public site, on a page that used to work. |
| Have `Rendering` subclass each field type to add its renderer | Two assemblies would each need to add one — `Rendering` the renderer, `Client` the editor — and no single class can derive from both sides. |

## Decision

**`EditorComponent` and `RendererComponent` become `Type?`, and the layer that owns the components
maps them to field type keys.**

- A built-in field type answers null for both. `Rendering` and `Client` each register their
  components against `IFieldType.Key` in a field component catalog, built in `P3-09` (renderers) and
  `P6` (editors).
- A field type shipped in an assembly that *does* reference the component projects — the ordinary
  case for a site's own extension — can answer directly with a `typeof`. That is the simpler path
  and stays open.
- Resolution consults the catalog first and the field type second. A deployment can therefore
  replace the renderer of a field type it did not write without reimplementing the field type.

## Consequences

- A field type with no component from either source renders nothing and logs a warning, which is
  already the required behaviour for an unknown field type key
  ([§15.3](../../spec.md#153-fallback-behavior)). A missing component is never an exception on the
  public surface, so this adds no new failure mode to delivery — but it does mean a forgotten
  registration is invisible until someone looks at the page. `P3-09` carries a startup check that
  every registered field type resolves to a renderer.
- The `Type?` on the interface is a real widening of the contract: consumers must null-check. That
  is the honest shape. The previous signature could only have been satisfied by every built-in field
  type lying about a component it cannot name.
- Two places now answer "what draws this field type" — the catalog and the interface property. The
  precedence rule above is what keeps that from being ambiguous, and it has to stay documented on
  both.
- The catalog is one more thing a field type author must wire up. `services.AddCmsFieldType<T>()`
  gains an overload taking the component types, so the registration stays a single call.
