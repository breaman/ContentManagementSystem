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

    /// <summary>A supplied value is longer than the column that stores it.</summary>
    public const string TooLong = "structure.too-long";

    /// <summary>The template, zone, block type, or revision addressed does not exist.</summary>
    public const string NotFound = "structure.not-found";

    /// <summary>The caller is authenticated but holds no role permitting structural changes.</summary>
    public const string Forbidden = "structure.forbidden";
}
