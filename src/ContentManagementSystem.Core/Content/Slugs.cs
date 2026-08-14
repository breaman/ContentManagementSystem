using System.Globalization;
using System.Text;

using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// The shape a page's URL segment must have, and how one is derived from a title
/// (spec sections 10.2 and 10.3).
/// </summary>
/// <remarks>
/// One definition, because a generated slug and a hand-typed one end up in the same column and the
/// same URL. A generator whose output its own validator would reject is a bug that only appears for
/// titles nobody tried.
/// <para>
/// Note what this does <em>not</em> decide. Whether a slug is already taken is a question for the
/// database — asked of the page's live siblings, since a full URL is its ancestors' slugs joined and
/// two siblings sharing a segment is the only way tree-derived URLs can collide. The unique index
/// that makes that a guarantee rather than a check arrives with <c>PageRoute</c> in P3-01.
/// </para>
/// </remarks>
public static class Slugs
{
    /// <summary>
    /// First URL segments the application already serves, which a root-level page may not claim.
    /// </summary>
    /// <remarks>
    /// The list from spec section 10.3, and it applies only at the root: these are first segments,
    /// and <c>/products/admin</c> reaches no framework endpoint. Four of them
    /// (<c>_blazor</c>, <c>_framework</c>, <c>sitemap.xml</c>, <c>robots.txt</c>) cannot survive
    /// <see cref="Validate"/>'s format rule anyway; they are listed so that this is the whole answer
    /// to "what is reserved" rather than most of it.
    /// </remarks>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "api", "media", "_blazor", "_framework", "account", "health", "alive",
        "sitemap.xml", "robots.txt", "preview",
    };

    /// <summary>
    /// Derives a slug from a title.
    /// </summary>
    /// <param name="title">The page title, as the editor typed it.</param>
    /// <returns>
    /// The slug, or an empty string when the title holds nothing a URL segment can be made of.
    /// </returns>
    /// <remarks>
    /// Accents are folded to their base letters — <c>Café</c> becomes <c>cafe</c> — which is the
    /// "normalized to ASCII where unambiguous" half of spec section 10.2. Letters that have no
    /// unambiguous ASCII form are kept as they are rather than dropped, because dropping them turns
    /// a title in a non-Latin script into an empty slug; spec section 10.3 permits a Unicode slug,
    /// and <see cref="Validate"/> warns about the homograph risk instead.
    /// </remarks>
    /// <example>
    /// <code>
    /// Slugs.Generate("Our Café — 2026 Réview!"); // "our-cafe-2026-review"
    /// </code>
    /// </example>
    public static string Generate(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        // Decomposed so that a combining accent becomes its own character and can be dropped,
        // leaving the base letter behind. Recomposition is unnecessary afterwards: what survives is
        // either ASCII or a letter that had no decomposition to begin with.
        var decomposed = title.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;

        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark) continue;

            if (Rune.IsLetterOrDigit(rune))
            {
                // Deferred rather than appended when it is seen, so a run of punctuation collapses
                // to one hyphen and a trailing run leaves none at all.
                if (pendingSeparator && builder.Length > 0) builder.Append('-');

                pendingSeparator = false;
                builder.Append(Rune.ToLowerInvariant(rune));

                continue;
            }

            pendingSeparator = true;
        }

        return Truncate(builder.ToString().Normalize(NormalizationForm.FormC));
    }

    /// <summary>
    /// Checks a slug's shape.
    /// </summary>
    /// <param name="slug">The slug, already normalized by <see cref="Normalize"/>.</param>
    /// <param name="isRootLevel">
    /// Whether the page sits at the root of the site, where the reserved first segments apply.
    /// </param>
    /// <param name="path">Name of the member being checked, reported with any diagnostic.</param>
    /// <returns>
    /// Diagnostics found. A non-ASCII slug produces a <see cref="ValidationSeverity.Warning"/> and
    /// is otherwise accepted.
    /// </returns>
    public static ValidationResult Validate(string? slug, bool isRootLevel, string path = "slug")
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return ValidationResult.Error(
                PageCodes.SlugRequired,
                "A URL segment is required, and none could be derived from the title. Type one.",
                path);
        }

        if (slug.Length > FieldLengths.Slug)
        {
            return ValidationResult.Error(
                PageCodes.TooLong,
                $"A URL segment may be at most {FieldLengths.Slug} characters.",
                path);
        }

        if (!IsWellFormed(slug))
        {
            return ValidationResult.Error(
                PageCodes.SlugFormat,
                "A URL segment is lowercase letters and digits in hyphen-separated words, with no " +
                "spaces, slashes, or leading, trailing, or repeated hyphens.",
                path);
        }

        if (isRootLevel && Reserved.Contains(slug))
        {
            return ValidationResult.Error(
                PageCodes.SlugReserved,
                $"'/{slug}' is served by the application itself, so a page at the root of the site " +
                "cannot use it. Choose another segment, or create the page under a parent.",
                path);
        }

        if (!Ascii.IsValid(slug))
        {
            // Permitted, and warned about, exactly as spec section 10.3 asks: a Unicode slug is a
            // legitimate choice for a site in a non-Latin script and an impersonation risk for one
            // that is not, and only the person typing it can tell those apart.
            return ValidationResult.Warning(
                PageCodes.SlugHomograph,
                $"'{slug}' contains characters outside the ASCII range. They are permitted, but " +
                "characters that look alike in different scripts make a URL easy to impersonate.",
                path);
        }

        return ValidationResult.Success;
    }

    /// <summary>
    /// Puts a supplied slug into the form <see cref="Validate"/> and the column expect.
    /// </summary>
    /// <param name="slug">The slug as supplied.</param>
    /// <returns>The trimmed, lowercased, NFC-normalized slug.</returns>
    /// <remarks>
    /// Case and normalization form are folded here rather than reported as errors, because neither
    /// is a mistake an editor made: URLs are lowercase by configuration already, and two byte
    /// sequences that render identically must not be able to occupy two rows.
    /// </remarks>
    public static string Normalize(string? slug) =>
        string.IsNullOrWhiteSpace(slug)
            ? string.Empty
            : slug.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormC);

    /// <summary>Whether a slug is one usable URL segment.</summary>
    private static bool IsWellFormed(string slug)
    {
        var previousWasHyphen = true;

        foreach (var rune in slug.EnumerateRunes())
        {
            if (rune.Value == '-')
            {
                // Catches both a leading hyphen (the flag starts set) and a doubled one.
                if (previousWasHyphen) return false;

                previousWasHyphen = true;

                continue;
            }

            if (!Rune.IsLetterOrDigit(rune)) return false;

            // Uppercase would be folded by Normalize; reaching here means the caller skipped it,
            // and a slug that differs from the stored one only by case is a duplicate waiting to
            // happen.
            if (Rune.ToLowerInvariant(rune) != rune) return false;

            previousWasHyphen = false;
        }

        return !previousWasHyphen;
    }

    /// <summary>Cuts a slug to the column length without splitting a character or ending on a hyphen.</summary>
    private static string Truncate(string slug)
    {
        if (slug.Length <= FieldLengths.Slug) return slug.TrimEnd('-');

        var cut = FieldLengths.Slug;

        // A surrogate pair straddling the limit would otherwise be cut into an unpaired half, which
        // is not valid text and which the column would store as a replacement character.
        if (char.IsLowSurrogate(slug[cut])) cut--;

        return slug[..cut].TrimEnd('-');
    }
}
