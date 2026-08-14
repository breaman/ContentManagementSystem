namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// Stable diagnostic codes returned by the structure management API.
/// </summary>
/// <remarks>
/// Codes, not messages, are the contract. The backoffice switches on these to decide which field to
/// mark and what remedy to offer; the wording beside them may be rewritten freely, and will be, the
/// first time a developer misreads one.
/// </remarks>
public static class StructureCodes
{
    /// <summary>A stable key was not supplied.</summary>
    public const string KeyRequired = "structure.key-required";

    /// <summary>A key is not of the permitted shape.</summary>
    public const string KeyFormat = "structure.key-format";

    /// <summary>Something already exists under that key.</summary>
    public const string KeyDuplicate = "structure.key-duplicate";

    /// <summary>
    /// A write tried to change a key that content already addresses by name (spec section 8.5).
    /// </summary>
    public const string KeyImmutable = "structure.key-immutable";

    /// <summary>A display name was not supplied.</summary>
    public const string NameRequired = "structure.name-required";

    /// <summary>A zone or property was created without saying which field type fills it.</summary>
    public const string FieldTypeRequired = "structure.field-type-required";

    /// <summary>
    /// A write tried to change which field type fills a zone or property (spec section 8.5).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="KeyImmutable"/> because the remedy is different and a client should
    /// be able to offer it: a key is immutable forever, whereas a field type change is a content
    /// migration that needs a converter chosen for the values already stored under it.
    /// </remarks>
    public const string FieldTypeImmutable = "structure.field-type-immutable";

    /// <summary>
    /// Someone else changed the same template between this request reading it and writing it.
    /// </summary>
    /// <remarks>
    /// Retryable, unlike the other conflicts here: the client reloads the template and applies the
    /// change again. Reported rather than merged because a lost structural edit is invisible.
    /// </remarks>
    public const string ConcurrentChange = "structure.concurrent-change";

    /// <summary>A supplied value is longer than the column that stores it.</summary>
    public const string TooLong = "structure.too-long";

    /// <summary>The template, zone, block type, or revision addressed does not exist.</summary>
    public const string NotFound = "structure.not-found";

    /// <summary>
    /// A structural change was attempted on a built-in block type.
    /// </summary>
    /// <remarks>
    /// The code that renders a built-in expects exactly the property set it ships with, so adding,
    /// changing, or removing one would break a renderer no editor can fix. Editor-facing metadata on
    /// a built-in stays freely editable.
    /// </remarks>
    public const string BuiltInImmutable = "structure.built-in-immutable";

    /// <summary>
    /// A composed property key collides with one the block type already has.
    /// </summary>
    /// <remarks>
    /// Composed keys share one namespace with the host's own keys, because both land in the same
    /// block instance. Two definitions under one key have no defensible winner.
    /// </remarks>
    public const string CompositionCollision = "structure.composition-collision";

    /// <summary>The composition is already composed into this block type.</summary>
    public const string CompositionDuplicate = "structure.composition-duplicate";

    /// <summary>
    /// A delete was refused because something still depends on what would be deleted.
    /// </summary>
    /// <remarks>
    /// The message names what is in the way. Every structural delete in the system is guarded this
    /// way — a composition by the block types composing it, and from Phase 2 a template by the pages
    /// using it — so one code covers them and a client can offer one remedy.
    /// </remarks>
    public const string InUse = "structure.in-use";

    /// <summary>The caller is authenticated but holds no role permitting structural changes.</summary>
    public const string Forbidden = "structure.forbidden";
}
