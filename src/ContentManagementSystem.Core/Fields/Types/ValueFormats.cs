using System.Globalization;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// The syntaxes a stored value or a configured bound may be written in.
/// </summary>
/// <remarks>
/// Shared between content validation and configuration validation on purpose. A <c>date</c> zone
/// configured <c>{ "min": "13/08/2026" }</c> is only a useful thing to refuse at zone save if the
/// refusal uses the same parser the field type will later use to read that bound — otherwise a
/// configuration accepted as valid is silently ignored on every save afterwards, which is the exact
/// failure spec section 7.2 asks configuration validation to prevent.
/// </remarks>
internal static class ValueFormats
{
    /// <summary>The one date syntax accepted, ISO-8601 calendar form.</summary>
    private const string IsoDateFormat = "yyyy-MM-dd";

    /// <summary><c>#RRGGBB</c> — a hash and six hex digits.</summary>
    private const int HexColorLength = 7;

    private static readonly char[] TimeSeparators = ['T', 't', ' '];

    /// <summary>Parses an ISO-8601 calendar date.</summary>
    /// <param name="value">The text to read.</param>
    /// <param name="date">The parsed date when the text is one.</param>
    /// <returns><see langword="true"/> when the text is a date in the accepted syntax.</returns>
    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, IsoDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Renders a date back in the syntax it is stored in.</summary>
    /// <param name="date">The date.</param>
    public static string FormatDate(DateOnly date) =>
        date.ToString(IsoDateFormat, CultureInfo.InvariantCulture);

    /// <summary>Parses an ISO-8601 instant.</summary>
    /// <param name="value">The text to read.</param>
    /// <param name="instant">The parsed instant when the text is one.</param>
    /// <returns><see langword="true"/> when the text parses at all, offset or not.</returns>
    public static bool TryParseInstant(string? value, out DateTimeOffset instant) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out instant);

    /// <summary>
    /// Whether the text carries a time zone designator of its own.
    /// </summary>
    /// <param name="value">The text, already known to parse.</param>
    /// <returns><see langword="true"/> when the instant it names is unambiguous.</returns>
    /// <remarks>
    /// Checked on the text rather than on the parse result, because
    /// <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>
    /// supplies the machine's local offset when the value carries none, leaving nothing in the
    /// parsed value to distinguish an author who wrote <c>+00:00</c> from a server that happens to
    /// run in UTC.
    /// </remarks>
    public static bool HasExplicitOffset(string value)
    {
        var separator = value.IndexOfAny(TimeSeparators);

        if (separator < 0) return false;

        var time = value.AsSpan(separator + 1).TrimEnd();

        // Within the time portion a '+' or '-' can only introduce the offset.
        return time.Length > 0 && (time[^1] is 'Z' or 'z' || time.LastIndexOfAny('+', '-') >= 0);
    }

    /// <summary>Renders an instant back in the syntax it is stored in.</summary>
    /// <param name="instant">The instant.</param>
    public static string FormatInstant(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Whether the text is a colour written as <c>#RRGGBB</c>.</summary>
    /// <param name="value">The text to check.</param>
    /// <remarks>
    /// One form only. Named colours, <c>rgb()</c>, and the three-digit shorthand are all real CSS
    /// and all excluded, so that comparing two stored colours never needs a colour model.
    /// </remarks>
    public static bool IsHexColor(string? value)
    {
        if (value is not { Length: HexColorLength } || value[0] != '#') return false;

        for (var index = 1; index < HexColorLength; index++)
        {
            if (!char.IsAsciiHexDigit(value[index])) return false;
        }

        return true;
    }
}
