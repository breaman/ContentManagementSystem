using System.Globalization;
using System.Text;

using ContentManagementSystem.Shared.Contracts.Appearance;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Appearance;

/// <summary>
/// The site stylesheet validator of spec section 30.5.
/// </summary>
/// <remarks>
/// It <strong>tokenises</strong> the stylesheet rather than matching patterns against its text, and
/// that is the whole reason it can be trusted. CSS has three ways to write the same identifier —
/// literally, with a hex escape (<c>\69 mport</c>), or with a character escape (<c>\i</c>) — and two
/// contexts where a construct means nothing at all (inside a comment, inside a string). A regular
/// expression sees six different strings and one of them is a legitimate declaration; a tokeniser
/// sees one identifier in a known context.
/// <para>
/// It reports and never edits, which is where it parts company with the HTML sanitizer (D8). An
/// author who pasted a <c>&lt;script&gt;</c> out of Word cannot be asked to go and find it; an
/// administrator who typed an <c>@import</c> can.
/// </para>
/// <para>
/// Everything it refuses is refused for one of two reasons: it fetches from somewhere the content
/// security policy would have to be widened for, or it executes script. Widening this list is
/// therefore a CSP decision as well as a validator change (ADR 0026, ADR 0027).
/// </para>
/// </remarks>
public sealed class CssValidator : ICssValidator
{
    /// <summary>How much of an offending construct is quoted back in a diagnostic.</summary>
    private const int SnippetLength = 60;

    /// <summary>
    /// Declaration names that hand a stylesheet the ability to run code.
    /// </summary>
    /// <remarks>
    /// All historic, none supported by a current browser, and refused regardless: the cost of
    /// refusing is a rule nobody writes, and the cost of one surviving is arbitrary script on every
    /// public page of the site.
    /// </remarks>
    private static readonly HashSet<string> ScriptingProperties =
        new(StringComparer.OrdinalIgnoreCase) { "behavior", "-ms-behavior", "-moz-binding" };

    /// <summary>URL schemes a <c>url()</c> may name. Everything else is off-origin or executable.</summary>
    /// <remarks>
    /// <c>data:</c> is a payload rather than a host — nothing is fetched and nobody learns that a
    /// visitor arrived — and <c>img-src</c> already admits it (spec section 20.5). A relative or
    /// root-relative URL names no scheme at all and never reaches this set.
    /// </remarks>
    private static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "data" };

    /// <summary>Schemes that are not merely off-origin but executable.</summary>
    private static readonly HashSet<string> ScriptSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "javascript", "vbscript", "livescript", "mocha" };

    private readonly SiteStylesheetOptions options;

    /// <summary>Creates the validator.</summary>
    /// <param name="options">The stylesheet's configured limits.</param>
    public CssValidator(IOptions<SiteStylesheetOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.options = options.Value;
    }

    /// <inheritdoc />
    public IReadOnlyList<CssDiagnostic> Validate(string? css)
    {
        if (string.IsNullOrEmpty(css)) return [];

        var diagnostics = new List<CssDiagnostic>();

        // Checked first and reported on its own line, because a stylesheet over the cap is refused
        // whatever else is in it and the administrator's next action is to delete some of it rather
        // than to fix a construct.
        var byteCount = Encoding.UTF8.GetByteCount(css);

        if (byteCount > this.options.MaxBytes)
        {
            diagnostics.Add(new CssDiagnostic(
                CssDiagnosticCodes.TooLarge,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The stylesheet is {byteCount:N0} bytes; the limit is {this.options.MaxBytes:N0}."),
                0,
                0));
        }

        var scanner = new Scanner(css);

        while (!scanner.AtEnd)
        {
            var current = scanner.Current;

            if (current is '/' && scanner.Peek(1) is '*')
            {
                ReadComment(ref scanner, diagnostics);
                continue;
            }

            if (current is '"' or '\'')
            {
                var line = scanner.Line;
                var column = scanner.Column;
                var value = ReadString(ref scanner, diagnostics);

                InspectScheme(value, line, column, diagnostics, urlContext: false);
                continue;
            }

            if (current is '@')
            {
                var line = scanner.Line;
                var column = scanner.Column;

                scanner.Advance();

                var name = ReadIdentifier(ref scanner);

                if (string.Equals(name, "import", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new CssDiagnostic(
                        CssDiagnosticCodes.Import,
                        "@import fetches another stylesheet at render time, from wherever it names, "
                        + "and nothing reviews what arrives. Paste the rules in instead.",
                        line,
                        column,
                        "@" + name));
                }

                continue;
            }

            if (IsIdentifierStart(current))
            {
                ReadIdentifierInContext(ref scanner, diagnostics);
                continue;
            }

            scanner.Advance();
        }

        return diagnostics;
    }

    /// <summary>
    /// Reads an identifier and decides what it was: a function call, a declaration name, or neither.
    /// </summary>
    private static void ReadIdentifierInContext(ref Scanner scanner, List<CssDiagnostic> diagnostics)
    {
        var line = scanner.Line;
        var column = scanner.Column;
        var name = ReadIdentifier(ref scanner);

        if (name.Length == 0)
        {
            // Defensive: IsIdentifierStart said yes and the reader consumed nothing, which would
            // spin. Nothing currently produces this, and a validator that can hang is worse than one
            // that misses a construct.
            scanner.Advance();
            return;
        }

        if (scanner.Current is '(')
        {
            scanner.Advance();

            if (string.Equals(name, "url", StringComparison.OrdinalIgnoreCase))
            {
                var urlLine = scanner.Line;
                var urlColumn = scanner.Column;
                var url = ReadUrlToken(ref scanner, diagnostics);

                InspectScheme(url, urlLine, urlColumn, diagnostics, urlContext: true);
                return;
            }

            if (string.Equals(name, "expression", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new CssDiagnostic(
                    CssDiagnosticCodes.Script,
                    "expression() runs script from a stylesheet. It is refused whether or not any "
                    + "browser still evaluates it.",
                    line,
                    column,
                    name + "("));
            }

            return;
        }

        // A declaration name is an identifier followed by a colon, possibly across whitespace. It is
        // also what a pseudo-class looks like from here — but no pseudo-class is named `behavior` or
        // `-moz-binding`, so the ambiguity costs nothing this set can reach.
        if (!ScriptingProperties.Contains(name)) return;

        var lookahead = 0;

        while (IsWhitespace(scanner.Peek(lookahead))) lookahead++;

        if (scanner.Peek(lookahead) is not ':') return;

        diagnostics.Add(new CssDiagnostic(
            CssDiagnosticCodes.Script,
            $"`{name}` attaches script to an element from a stylesheet. It is refused whether or "
            + "not any browser still honours it.",
            line,
            column,
            name));
    }

    /// <summary>
    /// Decides whether a URL or string value names somewhere it may not.
    /// </summary>
    /// <param name="value">The decoded value — escapes already resolved.</param>
    /// <param name="line">Where it started.</param>
    /// <param name="column">Where it started.</param>
    /// <param name="diagnostics">Collector.</param>
    /// <param name="urlContext">
    /// Whether this came from a <c>url()</c>. A bare string elsewhere is content and only its scheme
    /// matters; inside a <c>url()</c> a protocol-relative or off-origin target matters too.
    /// </param>
    private static void InspectScheme(
        string value,
        int line,
        int column,
        List<CssDiagnostic> diagnostics,
        bool urlContext)
    {
        // Control characters are stripped before the scheme is read, because a browser's URL parser
        // strips them too — `java\0script:` is a working scheme to one reader and gibberish to a
        // check that did not.
        var trimmed = Strip(value);

        if (trimmed.Length == 0) return;

        var scheme = SchemeOf(trimmed);

        if (scheme is not null && ScriptSchemes.Contains(scheme))
        {
            diagnostics.Add(new CssDiagnostic(
                CssDiagnosticCodes.Script,
                $"`{scheme}:` executes script. It is refused everywhere in the stylesheet.",
                line,
                column,
                Snippet(value)));
            return;
        }

        if (!urlContext) return;

        // Protocol-relative: `//cdn.example.com/x.png` inherits the page's scheme and none of its
        // origin, which is the form that most often gets past a check written against `https://`.
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            diagnostics.Add(OffOrigin(line, column, value));
            return;
        }

        if (scheme is not null && !AllowedSchemes.Contains(scheme))
        {
            diagnostics.Add(OffOrigin(line, column, value));
        }
    }

    private static CssDiagnostic OffOrigin(int line, int column, string value) =>
        new(
            CssDiagnosticCodes.OffOriginUrl,
            "This url() names another site. The content security policy will not load it, and a "
            + "stylesheet that can fetch from anywhere can report every visitor to a third party by "
            + "asking for a background image. Upload the file to the media library and use its "
            + "/media/... URL.",
            line,
            column,
            Snippet(value));

    /// <summary>Returns the scheme of a URL, or null when it names none.</summary>
    private static string? SchemeOf(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];

            if (character is ':') return i == 0 ? null : value[..i];

            var valid = char.IsAsciiLetter(character)
                || (i > 0 && (char.IsAsciiDigit(character) || character is '+' or '-' or '.'));

            if (!valid) return null;
        }

        return null;
    }

    /// <summary>Removes whitespace and control characters, which URL parsers ignore and checks forget.</summary>
    private static string Strip(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character)) continue;

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string Snippet(string value) =>
        value.Length <= SnippetLength ? value : value[..SnippetLength] + "…";

    private static void ReadComment(ref Scanner scanner, List<CssDiagnostic> diagnostics)
    {
        var line = scanner.Line;
        var column = scanner.Column;

        scanner.Advance(2);

        while (!scanner.AtEnd)
        {
            if (scanner.Current is '*' && scanner.Peek(1) is '/')
            {
                scanner.Advance(2);
                return;
            }

            scanner.Advance();
        }

        diagnostics.Add(new CssDiagnostic(
            CssDiagnosticCodes.Unterminated,
            "This comment is never closed, so everything after it is a comment to the browser and a "
            + "declaration to anything reading the file.",
            line,
            column,
            "/*"));
    }

    /// <summary>Reads a quoted string and returns its decoded contents.</summary>
    private static string ReadString(ref Scanner scanner, List<CssDiagnostic> diagnostics)
    {
        var line = scanner.Line;
        var column = scanner.Column;
        var quote = scanner.Current;

        scanner.Advance();

        var builder = new StringBuilder();

        while (!scanner.AtEnd)
        {
            var current = scanner.Current;

            if (current == quote)
            {
                scanner.Advance();
                return builder.ToString();
            }

            if (current is '\\')
            {
                AppendEscape(ref scanner, builder);
                continue;
            }

            if (current is '\n')
            {
                // A raw newline ends a string in CSS and the declaration is dropped. Reported rather
                // than absorbed: it is a typo with a large blast radius.
                break;
            }

            builder.Append(current);
            scanner.Advance();
        }

        diagnostics.Add(new CssDiagnostic(
            CssDiagnosticCodes.Unterminated,
            "This string is never closed.",
            line,
            column,
            quote.ToString()));

        return builder.ToString();
    }

    /// <summary>Reads what is between <c>url(</c> and its <c>)</c>, quoted or not.</summary>
    private static string ReadUrlToken(ref Scanner scanner, List<CssDiagnostic> diagnostics)
    {
        while (IsWhitespace(scanner.Current)) scanner.Advance();

        if (scanner.Current is '"' or '\'')
        {
            var quoted = ReadString(ref scanner, diagnostics);

            while (!scanner.AtEnd && scanner.Current is not ')') scanner.Advance();

            if (!scanner.AtEnd) scanner.Advance();

            return quoted;
        }

        var builder = new StringBuilder();

        while (!scanner.AtEnd)
        {
            var current = scanner.Current;

            if (current is ')')
            {
                scanner.Advance();
                return builder.ToString();
            }

            if (current is '\\')
            {
                AppendEscape(ref scanner, builder);
                continue;
            }

            builder.Append(current);
            scanner.Advance();
        }

        diagnostics.Add(new CssDiagnostic(
            CssDiagnosticCodes.Unterminated,
            "This url() is never closed.",
            scanner.Line,
            scanner.Column,
            "url("));

        return builder.ToString();
    }

    /// <summary>Reads an identifier, resolving CSS escapes so `\69 mport` reads as `import`.</summary>
    private static string ReadIdentifier(ref Scanner scanner)
    {
        var builder = new StringBuilder();

        while (!scanner.AtEnd)
        {
            var current = scanner.Current;

            if (current is '\\')
            {
                AppendEscape(ref scanner, builder);
                continue;
            }

            if (!IsIdentifierPart(current)) break;

            builder.Append(current);
            scanner.Advance();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Consumes one CSS escape and appends what it stands for.
    /// </summary>
    /// <remarks>
    /// The hex form takes up to six digits and swallows one following whitespace character, which is
    /// the rule that makes <c>\69 mport</c> six characters rather than seven. Getting this wrong in
    /// either direction produces a validator that reads a different identifier than the browser
    /// does, which is the whole class of bypass this file exists to close.
    /// </remarks>
    private static void AppendEscape(ref Scanner scanner, StringBuilder builder)
    {
        scanner.Advance();

        if (scanner.AtEnd) return;

        if (!char.IsAsciiHexDigit(scanner.Current))
        {
            // `\@` and friends stand for the character itself. A backslash before a newline is a
            // line continuation and contributes nothing.
            if (scanner.Current is not '\n') builder.Append(scanner.Current);

            scanner.Advance();
            return;
        }

        var value = 0;
        var digits = 0;

        while (digits < 6 && !scanner.AtEnd && char.IsAsciiHexDigit(scanner.Current))
        {
            value = (value * 16) + Convert.ToInt32(scanner.Current.ToString(), 16);
            digits++;
            scanner.Advance();
        }

        if (IsWhitespace(scanner.Current)) scanner.Advance();

        // Surrogates and out-of-range values are replaced rather than dropped, so the identifier
        // keeps its shape and a comparison against `import` still fails for the right reason.
        builder.Append(value is > 0 and <= 0x10FFFF && !char.IsSurrogate((char)Math.Min(value, 0xFFFF))
            ? char.ConvertFromUtf32(value)
            : "�");
    }

    private static bool IsIdentifierStart(char character) =>
        char.IsAsciiLetter(character) || character is '_' or '-' or '\\' || character > 0x7F;

    private static bool IsIdentifierPart(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '_' or '-' || character > 0x7F;

    private static bool IsWhitespace(char character) =>
        character is ' ' or '\t' or '\n' or '\r' or '\f';

    /// <summary>
    /// A cursor over the stylesheet that knows where it is, because a diagnostic without a line
    /// number is an instruction to search.
    /// </summary>
    private struct Scanner(string source)
    {
        private readonly string source = source;
        private int index;

        public int Line { get; private set; } = 1;

        public int Column { get; private set; } = 1;

        public readonly bool AtEnd => this.index >= this.source.Length;

        /// <summary>The character under the cursor, or NUL past the end so callers need no bounds check.</summary>
        public readonly char Current => this.index < this.source.Length ? this.source[this.index] : '\0';

        public readonly char Peek(int offset) =>
            this.index + offset < this.source.Length ? this.source[this.index + offset] : '\0';

        public void Advance(int count = 1)
        {
            for (var i = 0; i < count && this.index < this.source.Length; i++)
            {
                if (this.source[this.index] is '\n')
                {
                    this.Line++;
                    this.Column = 1;
                }
                else
                {
                    this.Column++;
                }

                this.index++;
            }
        }
    }
}
