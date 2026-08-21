using ContentManagementSystem.Core.Appearance;
using ContentManagementSystem.Shared.Contracts.Appearance;

using FluentAssertions;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Tests.Appearance;

/// <summary>
/// The refusal corpus of spec section 30.5 (task P10-13).
/// </summary>
/// <remarks>
/// Each refused construct appears in several spellings — plainly, behind a CSS escape, split by a
/// comment, inside an at-rule block — because a validator that only matches the plain form is a
/// validator with a documented bypass. The escape cases are the ones that matter: <c>\69 mport</c>
/// is <c>import</c> to a browser and something else entirely to a regular expression.
/// <para>
/// <strong>The negative controls are half the suite.</strong> A validator that refused everything
/// would pass every refusal test here and be useless, so ordinary CSS — media queries, custom
/// properties, relative <c>url()</c>, a <c>data:</c> URI — is asserted to survive.
/// </para>
/// </remarks>
public class CssValidatorTests
{
    private static CssValidator Validator(int maxBytes = SiteStylesheetOptions.DefaultMaxBytes) =>
        new(Options.Create(new SiteStylesheetOptions { MaxBytes = maxBytes }));

    [Test]
    [Arguments("@import url('https://cdn.example.com/theme.css');")]
    [Arguments("@IMPORT \"theme.css\";")]
    [Arguments("@\\69 mport url(theme.css);")]
    [Arguments("/* a comment */ @import 'theme.css';")]
    [Arguments("@media screen { @import 'theme.css'; }")]
    public void ImportIsRefusedHoweverItIsSpelled(string css)
    {
        Validator().Validate(css).Should().Contain(
            diagnostic => diagnostic.Code == CssDiagnosticCodes.Import,
            "an @import fetches a stylesheet nothing here has reviewed");
    }

    [Test]
    [Arguments("body { background: url(https://cdn.example.com/hero.png); }")]
    [Arguments("body { background: url('//cdn.example.com/hero.png'); }")]
    [Arguments("body { background: url( \"http://example.com/x.png\" ); }")]
    [Arguments("@font-face { src: url(https://fonts.example.com/x.woff2) format('woff2'); }")]
    public void AnOffOriginUrlIsRefused(string css)
    {
        Validator().Validate(css).Should().Contain(
            diagnostic => diagnostic.Code == CssDiagnosticCodes.OffOriginUrl,
            "a stylesheet that can fetch from anywhere can report every visitor to a third party");
    }

    [Test]
    [Arguments("body { width: expression(alert(1)); }")]
    [Arguments("body { width: expre\\73 sion(alert(1)); }")]
    [Arguments("a { behavior: url(evil.htc); }")]
    [Arguments("a { -moz-binding: url(evil.xml#x); }")]
    [Arguments("a { background: url('javascript:alert(1)'); }")]
    [Arguments("a { background: url(java\\73 cript:alert(1)); }")]
    [Arguments("a::after { content: 'javascript:alert(1)'; }")]
    public void ScriptingConstructsAreRefused(string css)
    {
        Validator().Validate(css).Should().Contain(
            diagnostic => diagnostic.Code == CssDiagnosticCodes.Script,
            "nothing in a stylesheet may execute");
    }

    [Test]
    public void AControlCharacterInsideASchemeDoesNotHideIt()
    {
        // A browser's URL parser strips control characters before reading the scheme, so
        // `javascript:` is a working scheme to it and gibberish to a check that did not.
        Validator().Validate("a { background: url('javascript:alert(1)'); }")
            .Should().Contain(diagnostic => diagnostic.Code == CssDiagnosticCodes.Script);
    }

    [Test]
    public void AStylesheetOverTheCapIsRefused()
    {
        var css = new string('a', 128) + " { color: red; }";

        Validator(maxBytes: 32).Validate(css)
            .Should().Contain(diagnostic => diagnostic.Code == CssDiagnosticCodes.TooLarge);
    }

    [Test]
    public void AnUnterminatedCommentIsReported()
    {
        // Everything after it is a comment to the browser and a declaration to anything else reading
        // the file, which is how a validator and a renderer end up disagreeing.
        Validator().Validate("body { color: red; } /* never closed")
            .Should().Contain(diagnostic => diagnostic.Code == CssDiagnosticCodes.Unterminated);
    }

    [Test]
    public void ARefusalNamesTheLineAndColumn()
    {
        var diagnostics = Validator().Validate("body { color: red; }\n\n@import 'x.css';");

        var refusal = diagnostics.Should().ContainSingle().Subject;

        refusal.Line.Should().Be(3, "a diagnostic without a location is an instruction to search");
        refusal.Column.Should().Be(1);
    }

    [Test]
    public void EveryProblemIsReportedRatherThanOnlyTheFirst()
    {
        // An administrator fixing one @import per save round trip would rewrite a pasted stylesheet
        // a line at a time.
        var diagnostics = Validator().Validate(
            "@import 'a.css';\n@import 'b.css';\nbody { background: url(https://x.example/y.png); }");

        diagnostics.Should().HaveCount(3);
    }

    [Test]
    [Arguments(":root { --brand: #0a5; }")]
    [Arguments("body { color: var(--brand); font-family: Georgia, serif; }")]
    [Arguments("@media (min-width: 48rem) { .cms-page { padding-inline: 2rem; } }")]
    [Arguments("@supports (display: grid) { main { display: grid; } }")]
    [Arguments(".hero { background-image: url(/media/12/hero.webp); }")]
    [Arguments(".hero { background-image: url('../media/12/hero.webp'); }")]
    [Arguments(".icon { background: url(\"data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=\"); }")]
    [Arguments("/* nothing to see */ .a { color: red } .b:hover { color: blue }")]
    [Arguments("")]
    public void OrdinaryStylesheetsSurvive(string css)
    {
        // The negative control. Without it every assertion above is satisfied by a validator that
        // refuses its own input unconditionally.
        Validator().Validate(css).Should().BeEmpty();
    }

    [Test]
    public void AWordThatOnlyLooksLikeARefusedDeclarationSurvives()
    {
        // `behavior` is refused as a declaration name. The word appearing as a class, a custom
        // property, or a string is ordinary CSS and must survive, or the rule becomes a
        // spell-checker.
        Validator().Validate(".behavior { --behavior: none; content: 'behavior'; }")
            .Should().BeEmpty();
    }
}
