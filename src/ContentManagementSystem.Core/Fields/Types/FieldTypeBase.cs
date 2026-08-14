using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Base class for a field type whose payload shape is the envelope's
/// <c>{ "type": "&lt;key&gt;", "value": … }</c> object (spec section 6.2).
/// </summary>
/// <remarks>
/// Every built-in value field type stores its data under a single <c>value</c> member, with any
/// extra members alongside it — <c>richText</c>'s <c>format</c>, for instance. Holding that
/// convention in one place is what lets the four rules every field type shares live here once:
/// the discriminator has to agree with the schema, the container has to be an object, an empty
/// value is only an error when publishing a required property, and a value that is present is the
/// subclass's business.
/// <para>
/// The structured field types store their data under a different member — <c>media</c> under
/// <c>mediaId</c>, <c>blocks</c> under <c>items</c> — and say so by overriding
/// <see cref="PayloadMember"/>. All four rules then apply to them unchanged.
/// </para>
/// <para>
/// Subclasses override <see cref="ValidateValue"/> and are never called for an empty value, so they
/// do not each have to re-derive what "empty" means.
/// </para>
/// </remarks>
public abstract class FieldTypeBase : IFieldType
{
    /// <summary>The member every value field type stores its data under.</summary>
    protected const string ValueMember = "value";

    /// <summary>The member carrying the field type key a stored value was written by.</summary>
    protected const string TypeMember = "type";

    /// <summary>
    /// The member this field type stores its payload under.
    /// </summary>
    /// <remarks>
    /// <see cref="ValueMember"/> for the scalar field types. A structured field type overrides it
    /// with the member that decides whether anything was authored at all — <c>mediaId</c> for
    /// <c>media</c>, <c>kind</c> for <c>link</c>, <c>items</c> for <c>blocks</c> — so that "unfilled"
    /// means the same thing for them as for a plain text property, and the required rule needs no
    /// special case per shape.
    /// </remarks>
    protected virtual string PayloadMember => ValueMember;

    /// <inheritdoc />
    public abstract string Key { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Null for every built-in field type: this assembly sits below <c>Client</c> in the reference
    /// graph and cannot name a component in it. The backoffice registers the editor against
    /// <see cref="Key"/> instead (ADR 0014).
    /// </remarks>
    public virtual Type? EditorComponent => null;

    /// <inheritdoc />
    /// <remarks>
    /// Null for every built-in field type, for the same reason as <see cref="EditorComponent"/>:
    /// <c>Rendering</c> references this assembly, not the other way round.
    /// </remarks>
    public virtual Type? RendererComponent => null;

    /// <inheritdoc />
    public abstract FieldTypeCapabilities Capabilities { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Empty is the honest default: a field type that takes no configuration should accept none,
    /// and the closed schema turns anything written into its <c>ConfigurationJson</c> into a save
    /// error rather than a line that does nothing. A subclass reading a setting must declare it
    /// here, which the configuration contract test checks rather than trusting.
    /// </remarks>
    public virtual FieldConfigurationSchema ConfigurationSchema => FieldConfigurationSchema.Empty;

    /// <inheritdoc />
    public ValueTask<ValidationResult> ValidateAsync(
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Validate(value, configuration, mode));
    }

    /// <inheritdoc />
    public virtual ValueTask<JsonElement> SanitizeAsync(
        JsonElement value,
        FieldConfiguration configuration,
        CancellationToken cancellationToken) => ValueTask.FromResult(value);

    /// <inheritdoc />
    /// <remarks>
    /// A value field type points at nothing, so the base answer is empty. A field type that can
    /// carry a reference must override this — an omission here is the failure the whole reference
    /// projection exists to prevent (spec section 7.3).
    /// </remarks>
    public virtual IEnumerable<ContentReference> ExtractReferences(JsonElement value) => [];

    /// <inheritdoc />
    public virtual string ExtractSearchText(JsonElement value) => string.Empty;

    /// <summary>
    /// Checks a value that is present and non-empty.
    /// </summary>
    /// <param name="property">
    /// The whole stored property object, for field types whose value is qualified by a sibling
    /// member such as <c>format</c>.
    /// </param>
    /// <param name="value">The <c>value</c> member, guaranteed present and not empty.</param>
    /// <param name="configuration">Parsed configuration for the property being validated.</param>
    /// <param name="mode">Whether a draft is being saved or content is being published.</param>
    /// <returns>Diagnostics found.</returns>
    protected abstract ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode);

    /// <summary>
    /// Whether a stored value counts as unfilled for the purposes of the <c>required</c> rule.
    /// </summary>
    /// <param name="value">The <c>value</c> member, or an undefined element when it is absent.</param>
    /// <returns><see langword="true"/> when there is nothing authored here.</returns>
    /// <remarks>
    /// The default covers absent and explicitly cleared. Field types whose stored shape has a
    /// further empty form — the empty string, the empty array — widen it, so that publishing a
    /// required property that merely looks filled is still refused.
    /// </remarks>
    protected virtual bool IsEmpty(JsonElement value) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;

    /// <summary>
    /// Reads the <see cref="PayloadMember"/> of a stored property.
    /// </summary>
    /// <param name="property">The stored property object.</param>
    /// <returns>The member, or an undefined element when the property or the member is absent.</returns>
    protected JsonElement GetValue(JsonElement property) => GetMember(property, PayloadMember);

    /// <summary>
    /// Reads a named member of a stored property.
    /// </summary>
    /// <param name="property">The stored property object.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The member, or an undefined element when the property or the member is absent.</returns>
    /// <remarks>
    /// The structured field types are qualified by several members at once — a link's <c>text</c>
    /// and <c>target</c> sit beside its <c>kind</c> — so they read siblings through this rather than
    /// through <see cref="GetValue"/>.
    /// </remarks>
    protected static JsonElement GetMember(JsonElement property, string member) =>
        property.ValueKind is JsonValueKind.Object && property.TryGetProperty(member, out var value)
            ? value
            : default;

    /// <summary>
    /// Appends a diagnostic, allocating the list only once something is wrong.
    /// </summary>
    /// <param name="diagnostics">The list, created on first use.</param>
    /// <param name="diagnostic">The diagnostic to add.</param>
    /// <remarks>
    /// Validation runs per property on every save and publish, and almost every run finds nothing.
    /// The list is deferred so the common case allocates nothing at all.
    /// </remarks>
    protected static void Add(ref List<ValidationDiagnostic>? diagnostics, ValidationDiagnostic diagnostic) =>
        Diagnostics.Add(ref diagnostics, diagnostic);

    /// <summary>
    /// Turns collected diagnostics into a result.
    /// </summary>
    /// <param name="diagnostics">Diagnostics found, or null when there were none.</param>
    /// <returns>The result to return from <see cref="ValidateValue"/>.</returns>
    protected static ValidationResult Result(List<ValidationDiagnostic>? diagnostics) =>
        Diagnostics.Result(diagnostics);

    /// <summary>
    /// Reads the <see cref="PayloadMember"/> of a stored property as text.
    /// </summary>
    /// <param name="property">The stored property object.</param>
    /// <returns>The text, or null when the value is absent, cleared, or not a string.</returns>
    protected string? GetStringValue(JsonElement property) =>
        GetValue(property) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>
    /// Checks a property with nothing authored in it.
    /// </summary>
    /// <param name="configuration">Parsed configuration for the property being validated.</param>
    /// <param name="mode">Whether a draft is being saved or content is being published.</param>
    /// <returns>Diagnostics found.</returns>
    /// <remarks>
    /// An editor must always be able to save half-finished work, so a required value is only
    /// enforced at the point of publishing (spec section 8.3). List-shaped field types widen this:
    /// a configured minimum count says the same thing as <c>required</c>, and enforcing counts only
    /// on non-empty lists would leave <c>"min": 1</c> as the one count rule an empty zone slips
    /// past.
    /// <para>
    /// Read from <see cref="FieldConfiguration.IsRequired"/>, which carries the <c>IsRequired</c>
    /// column, rather than from a setting inside the configuration blob — the two would be free to
    /// disagree, and nothing would say which won.
    /// </para>
    /// </remarks>
    protected virtual ValidationResult ValidateEmpty(FieldConfiguration configuration, ValidationMode mode)
    {
        if (mode is ValidationMode.Publish && configuration.IsRequired)
        {
            return ValidationResult.Error(FieldValidationCodes.Required, "This field is required.");
        }

        return ValidationResult.Success;
    }

    private ValidationResult Validate(
        JsonElement property,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        // Absent and null are both legitimate stored states, and the payload engine keeps them
        // distinct (spec section 6.2). Neither is a shape error; both are unfilled.
        if (property.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return ValidateEmpty(configuration, mode);
        }

        if (property.ValueKind is not JsonValueKind.Object)
        {
            return ValidationResult.Error(
                FieldValidationCodes.Shape,
                $"Expected an object storing a {DisplayName} value.");
        }

        if (property.TryGetProperty(TypeMember, out var declaredType) &&
            declaredType.ValueKind is JsonValueKind.String &&
            !declaredType.ValueEquals(Key))
        {
            // The schema says this property is a Key; the stored value says it was written by
            // something else. Validating it as if it were ours would report a shape error against
            // the wrong field type, which sends whoever reads the message to the wrong place.
            return ValidationResult.Error(
                FieldValidationCodes.TypeMismatch,
                $"This value was stored as '{declaredType.GetString()}' but the schema defines the " +
                $"property as '{Key}'.");
        }

        var value = GetValue(property);

        return IsEmpty(value)
            ? ValidateEmpty(configuration, mode)
            : ValidateValue(property, value, configuration, mode);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Nothing to rewrite by default, which is correct for every field type that holds no
    /// references. A reference-bearing one that inherits this silently produces duplicated content
    /// still pointing at the originals, so <c>ReferenceExtractionContractTests</c> fails any field
    /// type that claims <see cref="FieldTypeCapabilities.ReferenceBearing"/> and does not override.
    /// </remarks>
    public virtual JsonNode? RemapReferences(JsonElement value, ReferenceRemapper remap) => null;
}
