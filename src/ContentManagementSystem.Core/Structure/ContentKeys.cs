using System.Text.RegularExpressions;

using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// The shape a content-model key must have, for templates, zones, block types, and compositions
/// alike.
/// </summary>
/// <remarks>
/// One definition because these keys are all the same thing: an identifier written into stored
/// payloads and never changed afterwards (spec section 8.5). A key that is awkward to work with is
/// awkward forever, so the rules are applied at the only moment they can be — creation.
/// <para>
/// Note what this does <em>not</em> decide. Whether a key is already taken is a question for the
/// database, whose unique index answers it under the server's collation; asking it here with an
/// ordinal comparison would let a case-only variant pass validation and then fail on insert.
/// </para>
/// </remarks>
public static partial class ContentKeys
{
    /// <summary>
    /// Letters and digits, in hyphen- or underscore-separated segments, beginning with a letter.
    /// </summary>
    /// <remarks>
    /// Covers both conventions already in the spec — <c>marketing-landing</c> for templates,
    /// <c>heroImage</c> for zones — without admitting the shapes that cause trouble later: a leading
    /// digit reads as an array index inside a validation path, a trailing or doubled separator is
    /// almost always a typo, and whitespace or punctuation would need escaping everywhere the key
    /// appears.
    /// </remarks>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*([-_][A-Za-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    /// <summary>
    /// Checks a key's shape.
    /// </summary>
    /// <param name="key">The key as supplied by the client.</param>
    /// <param name="path">Name of the member being checked, reported with any diagnostic.</param>
    /// <returns>
    /// Diagnostics found, or <see cref="ValidationResult.Success"/> when the key is usable.
    /// </returns>
    public static ValidationResult Validate(string? key, string path = "key")
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return ValidationResult.Error(StructureCodes.KeyRequired, "A key is required.", path);
        }

        if (key.Length > FieldLengths.ContentKey)
        {
            return ValidationResult.Error(
                StructureCodes.TooLong,
                $"A key may be at most {FieldLengths.ContentKey} characters.",
                path);
        }

        if (!KeyPattern().IsMatch(key))
        {
            return ValidationResult.Error(
                StructureCodes.KeyFormat,
                "A key starts with a letter and contains only letters, digits, hyphens, and " +
                "underscores, with no leading, trailing, or repeated separator.",
                path);
        }

        return ValidationResult.Success;
    }
}
