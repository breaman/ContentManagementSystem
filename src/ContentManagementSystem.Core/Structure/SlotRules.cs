using ContentManagementSystem.Core.Fields.Configuration;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// The rules a zone and a block-type property both obey, in one place.
/// </summary>
/// <remarks>
/// A zone and a block-type property are the same thing at validation time — <c>P1-15</c> already
/// reads them into one <c>ContentPropertySchema</c> — so they must be the same thing at save time
/// too. Two copies of "a key is immutable" would drift, and the copy that drifted would be the one
/// nobody wrote a test for.
/// <para>
/// The request members are named by literal string rather than by <c>nameof</c> over one request
/// type: the diagnostic path has to be true for every request shape that carries these fields, and
/// naming one of them here would quietly mislead callers of the others. The names are identical
/// across all of them, which is itself part of the API contract.
/// </para>
/// </remarks>
internal static class SlotRules
{
    /// <summary>Request member holding the stable key.</summary>
    public const string KeyPath = "Key";

    /// <summary>Request member holding the editor-facing label.</summary>
    public const string NamePath = "Name";

    /// <summary>Request member holding the help text.</summary>
    public const string DescriptionPath = "Description";

    /// <summary>Request member holding the editor grouping.</summary>
    public const string GroupPath = "Group";

    /// <summary>Request member holding the field type key.</summary>
    public const string FieldTypePath = "FieldTypeKey";

    /// <summary>
    /// Normalises an optional text value on its way into a column.
    /// </summary>
    /// <param name="value">The value as supplied.</param>
    /// <returns>The trimmed value, or null when there was nothing but whitespace.</returns>
    /// <remarks>
    /// Blank and absent collapse to one stored value. Otherwise a form that posts an empty string
    /// where it was given null would make two rows differ on a field nobody filled in, and every
    /// comparison downstream — a structure diff, an idempotency check in the schema sync — would
    /// report a change that is not one.
    /// </remarks>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Checks the labels a slot carries, against the columns that store them.
    /// </summary>
    /// <param name="name">The editor-facing label.</param>
    /// <param name="description">Optional help text.</param>
    /// <param name="group">Optional editor grouping.</param>
    /// <returns>Diagnostics found, one per broken rule rather than only the first.</returns>
    public static List<ValidationDiagnostic> ValidateMetadata(string? name, string? description, string? group)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        if (string.IsNullOrWhiteSpace(name))
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.NameRequired,
                "A display name is required.",
                ValidationSeverity.Error,
                NamePath));
        }
        else if (name.Trim().Length > FieldLengths.EntityName)
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.TooLong,
                $"A display name may be at most {FieldLengths.EntityName} characters.",
                ValidationSeverity.Error,
                NamePath));
        }

        if (description?.Trim().Length > FieldLengths.ShortDescription)
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.TooLong,
                $"A description may be at most {FieldLengths.ShortDescription} characters.",
                ValidationSeverity.Error,
                DescriptionPath));
        }

        if (group?.Trim().Length > FieldLengths.GroupName)
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.TooLong,
                $"A group name may be at most {FieldLengths.GroupName} characters.",
                ValidationSeverity.Error,
                GroupPath));
        }

        return diagnostics;
    }

    /// <summary>
    /// Checks the field type binding and its configuration.
    /// </summary>
    /// <param name="configurations">The configuration validator built in <c>P1-12</c>.</param>
    /// <param name="fieldTypeKey">Key of the field type the slot binds to.</param>
    /// <param name="configurationJson">Normalised configuration, or null.</param>
    /// <returns>Diagnostics found. Warnings are included and do not block.</returns>
    /// <remarks>
    /// This is the call site spec section 7.2 asks for: a configuration the editor component cannot
    /// honour is refused where it would be stored, rather than found later by an editor whose value
    /// will not publish. An unregistered field type key is reported by the validator as an error —
    /// unlike delivery, which must be forgiving about a payload it already stored, a structure save
    /// is the moment before anything is stored.
    /// </remarks>
    public static IReadOnlyList<ValidationDiagnostic> ValidateFieldType(
        IFieldConfigurationValidator configurations,
        string? fieldTypeKey,
        string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(fieldTypeKey))
        {
            return
            [
                new ValidationDiagnostic(
                    StructureCodes.FieldTypeRequired,
                    "A field type is required: nothing can be edited or validated without one.",
                    ValidationSeverity.Error,
                    FieldTypePath),
            ];
        }

        return configurations.Validate(fieldTypeKey.Trim(), configurationJson).Diagnostics;
    }

    /// <summary>
    /// Refuses the two changes a slot may never undergo (spec section 8.5).
    /// </summary>
    /// <param name="requestKey">Key as supplied. Null or blank means "unchanged".</param>
    /// <param name="requestFieldTypeKey">Field type as supplied. Null or blank means "unchanged".</param>
    /// <param name="storedKey">Key as stored.</param>
    /// <param name="storedFieldTypeKey">Field type as stored.</param>
    /// <param name="noun">What the slot is called in a message — <c>zone</c> or <c>property</c>.</param>
    /// <param name="owner">What owns it, phrased for a message, such as <c>template 'landing'</c>.</param>
    /// <returns>Diagnostics found.</returns>
    /// <remarks>
    /// Comparison is ordinal, so a case-only edit is refused too: content addresses the slot by this
    /// exact string, and changing its case orphans exactly as much as a full rename does.
    /// </remarks>
    public static List<ValidationDiagnostic> ValidateImmutable(
        string? requestKey,
        string? requestFieldTypeKey,
        string storedKey,
        string storedFieldTypeKey,
        string noun,
        string owner)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        if (!string.IsNullOrWhiteSpace(requestKey) &&
            !string.Equals(requestKey.Trim(), storedKey, StringComparison.Ordinal))
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.KeyImmutable,
                $"A {noun} key cannot be changed. This {noun} is '{storedKey}' on {owner}, and every " +
                $"payload authored against it names that key. Remove the {noun} and add a new one if " +
                "the content model has to change.",
                ValidationSeverity.Error,
                KeyPath));
        }

        if (!string.IsNullOrWhiteSpace(requestFieldTypeKey) &&
            !string.Equals(requestFieldTypeKey.Trim(), storedFieldTypeKey, StringComparison.Ordinal))
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.FieldTypeImmutable,
                $"The {noun} '{storedKey}' holds '{storedFieldTypeKey}' values and cannot be rebound " +
                $"to '{requestFieldTypeKey.Trim()}' by an edit. Changing what a slot holds is a " +
                "content migration: it needs a converter chosen for the values already stored under " +
                $"the key, and the drafts that converter rewrites do not exist yet. Remove the {noun} " +
                "and add it again to start the key empty.",
                ValidationSeverity.Error,
                FieldTypePath));
        }

        return diagnostics;
    }
}
