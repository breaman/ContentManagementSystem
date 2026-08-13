namespace ContentManagementSystem.Shared.Content;

/// <summary>
/// Member names of the content payload envelope (spec section 6.2).
/// </summary>
/// <remarks>
/// These strings are the on-disk contract for every page version ever written, so they are as
/// immutable as a field type key: renaming one makes every stored payload unreadable. They live in
/// <c>Shared</c> because the payload is written by the backoffice, read by the delivery renderer, and
/// walked by the validator, and none of those three reference each other.
/// </remarks>
public static class ContentPayloadMembers
{
    /// <summary>Envelope version, so the deserializer can evolve without rewriting stored rows.</summary>
    public const string SchemaVersion = "schemaVersion";

    /// <summary>Key of the template this content was authored against.</summary>
    public const string TemplateKey = "templateKey";

    /// <summary>The template revision whose schema was captured at write time (spec section 8.5).</summary>
    public const string TemplateRevision = "templateRevision";

    /// <summary>The authored content, keyed by zone key.</summary>
    public const string Zones = "zones";

    /// <summary>
    /// The field type key a stored value was written by, present on every zone value and every block
    /// property value.
    /// </summary>
    /// <remarks>
    /// A value has to be read by whatever wrote it. When the schema and this discriminator disagree
    /// — because a property's field type was changed without a converter — the discriminator is what
    /// says how the stored bytes are shaped, and the disagreement is itself a diagnostic
    /// (<see cref="Fields.FieldValidationCodes.TypeMismatch"/>).
    /// </remarks>
    public const string Type = "type";
}
