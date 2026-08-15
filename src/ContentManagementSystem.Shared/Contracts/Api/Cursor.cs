using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace ContentManagementSystem.Shared.Contracts.Api;

/// <summary>
/// Encodes and decodes the opaque <c>?cursor=</c> token of a keyset-paginated collection.
/// </summary>
/// <remarks>
/// The token carries the sort key of the last item the caller received — for the page collections in
/// Phase 2 that is one identity, because they are ordered by the primary key. It is Base64Url rather
/// than a bare number so that a client cannot reasonably hand-assemble one, and so that widening the
/// key later (a composite <c>(modifiedOn, id)</c> keyset, say) is a change to this class rather than
/// a change to the shape of every URL that has been bookmarked.
/// <para>
/// <strong>Not a security boundary.</strong> A decoded cursor is only ever used as a
/// <c>WHERE Id &gt; @cursor</c> bound, inside a query that already applies the caller's permissions,
/// so a forged token can reveal nothing the caller could not have reached by paging. It is not
/// signed, and it should not grow into something worth signing.
/// </para>
/// </remarks>
public static class Cursor
{
    /// <summary>How many items a collection returns when the caller does not say.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Most items a collection will return however large a limit is asked for.</summary>
    /// <remarks>
    /// A ceiling rather than a refusal: a client asking for a thousand rows gets two hundred and a
    /// cursor, which is a working answer, whereas an error teaches it to ask for exactly the maximum
    /// and gains nobody anything.
    /// </remarks>
    public const int MaxLimit = 200;

    /// <summary>
    /// Builds the token that resumes a collection after a given key.
    /// </summary>
    /// <param name="lastKey">Sort key of the last item returned.</param>
    /// <returns>The opaque token.</returns>
    public static string Encode(int lastKey) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(lastKey.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// Reads a token back, treating anything unreadable as a refusal rather than as the start.
    /// </summary>
    /// <param name="cursor">The token as it arrived, or null when the caller wants the first page.</param>
    /// <param name="lastKey">The key the collection should resume after.</param>
    /// <returns>
    /// <see langword="false"/> only when a token was supplied and could not be read. An absent
    /// cursor succeeds with a <paramref name="lastKey"/> of zero, which is the first page.
    /// </returns>
    /// <remarks>
    /// A malformed cursor is reported rather than ignored. Silently restarting from the top would
    /// turn a client's paging bug into an infinite loop over the first page, which is far harder to
    /// notice than a 422 naming the parameter.
    /// </remarks>
    public static bool TryDecode(string? cursor, out int lastKey)
    {
        lastKey = 0;

        if (string.IsNullOrWhiteSpace(cursor)) return true;

        try
        {
            var decoded = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));

            return int.TryParse(decoded, CultureInfo.InvariantCulture, out lastKey) && lastKey >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Clamps a caller-supplied limit into the range a collection will serve.</summary>
    /// <param name="limit">What the caller asked for, or null for the default.</param>
    /// <returns>A usable page size.</returns>
    public static int Clamp(int? limit) =>
        limit is null or < 1 ? DefaultLimit : Math.Min(limit.Value, MaxLimit);
}
