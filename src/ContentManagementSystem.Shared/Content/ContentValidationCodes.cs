namespace ContentManagementSystem.Shared.Content;

/// <summary>
/// Stable diagnostic codes raised by the payload walk itself, as opposed to by a field type.
/// </summary>
/// <remarks>
/// The division is by who can see the problem. A field type sees one value and raises
/// <c>field.*</c> codes (<see cref="Fields.FieldValidationCodes"/>); the walk sees the envelope, the
/// template revision, and which keys the schema does and does not account for, and raises these.
/// <para>
/// As with the field codes: clients switch on the code, messages may be reworded freely, and a code
/// does not change once shipped (spec section 22.2).
/// </para>
/// </remarks>
public static class ContentValidationCodes
{
    /// <summary>The payload is not a JSON object.</summary>
    public const string PayloadShape = "payload.shape";

    /// <summary>The envelope declares no <c>schemaVersion</c>, or one that is not a number.</summary>
    public const string SchemaVersionMissing = "payload.schemaVersion.missing";

    /// <summary>The envelope declares a <c>schemaVersion</c> this build cannot read.</summary>
    public const string SchemaVersionUnsupported = "payload.schemaVersion.unsupported";

    /// <summary>The envelope names no template, so there is no schema to check it against.</summary>
    public const string TemplateMissing = "payload.template.missing";

    /// <summary>The envelope carries no <c>zones</c> object.</summary>
    public const string ZonesMissing = "payload.zones.missing";

    /// <summary>
    /// The template revision the payload captured is not one this deployment knows about.
    /// </summary>
    /// <remarks>
    /// An error rather than a warning: with no schema, nothing below this can be checked at all, and
    /// publishing content nobody can validate is how a page reaches the public site broken. Delivery
    /// treats the same condition as a degraded render, not a failure (spec section 15.3).
    /// </remarks>
    public const string TemplateUnknown = "template.revision.unknown";

    /// <summary>
    /// The payload carries a zone the template revision does not declare.
    /// </summary>
    /// <remarks>
    /// A warning, and deliberately so: removing a zone must not destroy content. The value is
    /// retained and surfaced in the editor's obsolete-content panel, and is lost only when an editor
    /// discards it explicitly (spec section 8.5).
    /// </remarks>
    public const string ZoneOrphaned = "zone.orphaned";

    /// <summary>
    /// A block names a block type, or a revision of one, that this deployment does not know about.
    /// </summary>
    /// <remarks>
    /// A warning. Its properties cannot be checked, but refusing the save would strand every page
    /// holding a block whose type was removed, leaving an editor no way to delete it — the same
    /// reasoning that keeps an unknown block type key out of the <c>blocks</c> field type's own
    /// errors.
    /// </remarks>
    public const string BlockTypeUnknown = "blockType.unknown";

    /// <summary>A block carries a property its block type revision does not declare.</summary>
    /// <remarks>A warning, for the same reason as <see cref="ZoneOrphaned"/>.</remarks>
    public const string PropertyOrphaned = "property.orphaned";

    /// <summary>
    /// A zone or property is defined against a field type no longer registered in this deployment.
    /// </summary>
    /// <remarks>
    /// A warning: the value cannot be checked and will not render, which is exactly what spec
    /// section 15.3 prescribes for an unknown field type key. Erroring would make removing a field
    /// type from a build unsaveable rather than merely degraded.
    /// </remarks>
    public const string FieldTypeUnknown = "fieldType.unknown";

    /// <summary>
    /// Blocks are nested deeper than the walk will follow.
    /// </summary>
    /// <remarks>
    /// Only reachable from a hand-edited or migrated payload — the <c>blocks</c> field type refuses
    /// nesting past one level long before this, and the editor cannot produce it. The guard exists so
    /// that such a payload is reported rather than overflowing the stack.
    /// </remarks>
    public const string BlockDepth = "block.depth";
}
