using System.Net;
using System.Text;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Reduces authored text to what belongs in the search index.
/// </summary>
/// <remarks>
/// Deliberately not a parse. The index wants words, not structure, and the alternative — an
/// AngleSharp parse per property per publish — buys accuracy nobody can perceive in a result list at
/// a cost that lands on the publish path. The cases that would justify a real parser are handled
/// explicitly: <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c> bodies are dropped rather than
/// indexed, because a tag-stripper that keeps them puts CSS selectors and JavaScript identifiers
/// into the index.
/// </remarks>
internal static class SearchText
{
    /// <summary>Strips markup, decodes entities, and collapses whitespace.</summary>
    /// <param name="html">Authored markup, or plain text containing no markup at all.</param>
    /// <returns>Indexable text. Never null.</returns>
    public static string FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var builder = new StringBuilder(html.Length);
        var index = 0;

        while (index < html.Length)
        {
            var open = html.IndexOf('<', index);

            if (open < 0)
            {
                builder.Append(html, index, html.Length - index);
                break;
            }

            builder.Append(html, index, open - index);

            var close = OpensATag(html, open) ? html.IndexOf('>', open) : -1;

            if (close < 0)
            {
                // Prose, not markup: "a < b", or a '<' truncated off the end of a value. Kept as
                // text, and the scan resumes after it so a later real tag is still recognised.
                builder.Append('<');
                index = open + 1;
                continue;
            }

            var name = TagName(html, open, close);

            if (name is "script" or "style")
            {
                index = SkipElementBody(html, close + 1, name);
            }
            else
            {
                // A tag is a word boundary: without this, "<p>one</p><p>two</p>" indexes "onetwo".
                builder.Append(' ');
                index = close + 1;
            }
        }

        return Collapse(WebUtility.HtmlDecode(builder.ToString()));
    }

    /// <summary>Collapses runs of whitespace to single spaces and trims the ends.</summary>
    /// <param name="text">Text to normalize.</param>
    /// <returns>Normalized text. Never null.</returns>
    public static string Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether a <c>&lt;</c> begins a tag rather than being prose.
    /// </summary>
    /// <param name="html">The markup being scanned.</param>
    /// <param name="open">Index of the <c>&lt;</c>.</param>
    /// <returns><see langword="true"/> when what follows can start a tag.</returns>
    /// <remarks>
    /// The same rule the HTML tokenizer uses: a tag opens only on a name character, a closing
    /// slash, or a markup declaration. Without it, "a &lt; b and c &gt; d" loses everything between
    /// the two comparisons.
    /// </remarks>
    private static bool OpensATag(string html, int open)
    {
        var next = open + 1;

        return next < html.Length && (char.IsAsciiLetter(html[next]) || html[next] is '/' or '!' or '?');
    }

    private static string TagName(string html, int open, int close)
    {
        var start = open + 1;

        if (start < close && html[start] == '/') start++;

        var end = start;

        while (end < close && !char.IsWhiteSpace(html[end]) && html[end] != '/')
        {
            end++;
        }

        return html[start..end].ToLowerInvariant();
    }

    private static int SkipElementBody(string html, int from, string name)
    {
        var closing = html.IndexOf($"</{name}", from, StringComparison.OrdinalIgnoreCase);

        if (closing < 0) return html.Length;

        var close = html.IndexOf('>', closing);

        return close < 0 ? html.Length : close + 1;
    }
}
