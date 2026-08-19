using System.Text;

namespace ContentManagementSystem.Core.Search;

/// <summary>
/// Turns what an editor typed into a search condition <c>CONTAINS</c> will accept (task P8-18).
/// </summary>
/// <remarks>
/// <strong>A search box's contents are not a search condition.</strong> <c>CONTAINS</c> has its own
/// grammar — quotes, <c>AND</c>, <c>NEAR</c>, <c>*</c>, parentheses — and passing a raw phrase
/// through gives a syntax error for the everyday case of two words with a space between them. This
/// builds the condition instead: each word becomes a quoted prefix term, and the terms are ANDed.
/// <para>
/// The parameter is still a parameter, so this is not an injection defence — it is a usability one.
/// The characters dropped here are dropped because they mean something to the full-text parser that
/// the person typing them did not intend.
/// </para>
/// </remarks>
internal static class FullTextQuery
{
    /// <summary>Longest condition this will build, in characters.</summary>
    /// <remarks>
    /// A guard against a pasted document arriving in the search box and becoming a thousand ANDed
    /// terms. Words past the limit are dropped, which narrows nothing: the terms are ANDed, so the
    /// first dozen already select almost exactly the same set.
    /// </remarks>
    private const int MaxTerms = 12;

    /// <summary>
    /// Builds the condition for a phrase.
    /// </summary>
    /// <param name="text">What the editor typed.</param>
    /// <returns>
    /// The condition, or null when nothing usable was left — an empty box, or a line of punctuation.
    /// A null return means "do not filter by text", never "match nothing".
    /// </returns>
    public static string? Build(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var builder = new StringBuilder();
        var terms = 0;

        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // Letters and digits only. Everything else is either punctuation the person typed as
            // prose or an operator they did not mean to use, and both read better as a word break.
            var cleaned = new string([.. word.Where(char.IsLetterOrDigit)]);

            if (cleaned.Length == 0) continue;

            if (terms > 0) builder.Append(" AND ");

            // A prefix term, because a backoffice search box is used while typing: "gear" has to
            // find "gearbox" or the screen looks broken for the first six keystrokes.
            builder.Append('"').Append(cleaned).Append("*\"");

            if (++terms == MaxTerms) break;
        }

        return terms == 0 ? null : builder.ToString();
    }
}
