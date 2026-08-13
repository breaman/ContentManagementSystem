using System.Globalization;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Builds the paths a field type reports its diagnostics and references against.
/// </summary>
/// <remarks>
/// Paths are relative to the value being validated, never absolute: a field type cannot know where
/// in the document it sits, so the schema walk prefixes the zone and block it visited on the way in
/// (spec section 6.2). The segment syntax matches the payload — <c>items[2].properties.image</c> —
/// so a prefixed path reads as one expression rather than as two conventions joined.
/// </remarks>
internal static class RelativePaths
{
    /// <summary>Appends a member to a path.</summary>
    /// <param name="prefix">The path so far, or null for the value as a whole.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The combined path.</returns>
    public static string Member(string? prefix, string member) =>
        string.IsNullOrEmpty(prefix) ? member : $"{prefix}.{member}";

    /// <summary>Appends a path a nested field type reported against its own value.</summary>
    /// <param name="prefix">The path of the nested value.</param>
    /// <param name="relative">
    /// The path the nested field type reported, or null when it spoke about its value as a whole.
    /// </param>
    /// <returns>The combined path.</returns>
    public static string Combine(string prefix, string? relative) =>
        string.IsNullOrEmpty(relative) ? prefix : $"{prefix}.{relative}";

    /// <summary>Appends an array index to a path.</summary>
    /// <param name="prefix">The path of the list.</param>
    /// <param name="index">Zero-based position in the list.</param>
    /// <returns>The combined path.</returns>
    public static string Index(string prefix, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}[{index}]");
}
