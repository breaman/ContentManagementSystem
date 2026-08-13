namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// What sort of thing a <see cref="SanitizationRemoval"/> describes.
/// </summary>
public enum SanitizationRemovalKind
{
    /// <summary>An element outside the profile's tag allowlist.</summary>
    Tag = 0,

    /// <summary>An attribute outside the profile's attribute allowlist — including every <c>on*</c>.</summary>
    Attribute = 1,

    /// <summary>
    /// An attribute dropped because its URL was refused: an off-allowlist scheme, an oversized or
    /// non-image <c>data:</c> URI, or an <c>iframe</c> pointing somewhere unlisted.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="Attribute"/> because the two mean different things to an author. A
    /// removed attribute is markup they should not have written; a refused URL is usually a link
    /// they meant, spelled in a way the allowlist cannot accept.
    /// </remarks>
    Url = 2,

    /// <summary>A CSS declaration outside the property allowlist.</summary>
    Style = 3,

    /// <summary>A class outside the configured class allowlist.</summary>
    CssClass = 4,

    /// <summary>An HTML comment. Comments are removed under every profile.</summary>
    Comment = 5,
}
