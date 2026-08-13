using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>How a configured <c>pattern</c> answered.</summary>
internal enum PatternOutcome
{
    /// <summary>The value matched.</summary>
    Match,

    /// <summary>The value did not match.</summary>
    NoMatch,

    /// <summary>The pattern could not be used — it does not compile, or it did not finish in time.</summary>
    Unusable,
}

/// <summary>
/// Runs the author-supplied <c>pattern</c> configuration of a text field type.
/// </summary>
/// <remarks>
/// A <c>pattern</c> is written by a developer in the backoffice and then evaluated on every save and
/// publish of every page using that property, which makes it two things at once: a hot path and
/// untrusted input. Both are handled here rather than at each call site.
/// <list type="bullet">
/// <item>
/// Every match carries a timeout. A catastrophically backtracking pattern is easy to write by
/// accident — <c>(a+)+$</c> is the classic — and without a timeout it takes the request thread with
/// it. The pattern is content-model configuration, so the same one runs for every editor on that
/// template.
/// </item>
/// <item>
/// Compiled instances are cached, bounded. Uncached, every property visit would recompile.
/// Unbounded, a pattern edited repeatedly would leak an entry per revision.
/// </item>
/// </list>
/// <see cref="RegexOptions.Compiled"/> is deliberately not used: it costs a code-generation pass per
/// pattern, and these run against short strings where the interpreted engine is faster overall.
/// </remarks>
internal static class FieldPatterns
{
    /// <summary>How long a single match may run before the pattern is treated as unusable.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Ceiling on cached patterns. High enough that a real content model never evicts, low enough
    /// that a pathological one cannot grow the cache without bound.
    /// </summary>
    private const int CacheCapacity = 512;

    private static readonly ConcurrentDictionary<string, Regex?> Cache = new(StringComparer.Ordinal);

    /// <summary>Whether a configured pattern compiles into something that can be run.</summary>
    /// <param name="pattern">The configured regular expression.</param>
    /// <returns><see langword="false"/> when the pattern is not a usable expression.</returns>
    /// <remarks>
    /// How configuration validation (P1-12) refuses a pattern before it is stored, using the same
    /// compilation and the same cache the content path will use to run it. It cannot speak to
    /// runtime cost: catastrophic backtracking is a property of the pattern <em>and</em> the input,
    /// so a pattern that compiles here can still time out later, which is why
    /// <see cref="Evaluate"/> keeps its timeout.
    /// </remarks>
    public static bool IsUsable(string pattern) => GetOrCreate(pattern) is not null;

    /// <summary>Evaluates a configured pattern against a value.</summary>
    /// <param name="pattern">The configured regular expression.</param>
    /// <param name="value">The value being validated.</param>
    /// <returns>Whether the value matched, or that the pattern could not be used.</returns>
    public static PatternOutcome Evaluate(string pattern, string value)
    {
        var regex = GetOrCreate(pattern);

        if (regex is null) return PatternOutcome.Unusable;

        try
        {
            return regex.IsMatch(value) ? PatternOutcome.Match : PatternOutcome.NoMatch;
        }
        catch (RegexMatchTimeoutException)
        {
            // Reported to the author rather than thrown: the content is not at fault, the pattern
            // is, and the editor saving the page can do nothing about either.
            return PatternOutcome.Unusable;
        }
    }

    private static Regex? GetOrCreate(string pattern)
    {
        if (Cache.TryGetValue(pattern, out var cached)) return cached;

        Regex? regex;

        try
        {
            regex = new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException)
        {
            // Cached as null so a bad pattern is not re-parsed on every save. Configuration
            // validation (P1-12) is what stops one being persisted in the first place; this is the
            // path for patterns that predate it.
            regex = null;
        }

        if (Cache.Count < CacheCapacity)
        {
            Cache.TryAdd(pattern, regex);
        }

        return regex;
    }
}
