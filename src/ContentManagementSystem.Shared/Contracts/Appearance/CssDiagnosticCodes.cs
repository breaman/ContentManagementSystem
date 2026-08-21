namespace ContentManagementSystem.Shared.Contracts.Appearance;

/// <summary>
/// The <see cref="CssDiagnostic.Code"/> values the validator produces (spec section 30.5).
/// </summary>
/// <remarks>
/// A closed set, named in one place because both halves of the feature switch on them: the editor
/// decides which explanation to show beside the line, and the tests assert that a given payload is
/// refused <em>for the stated reason</em> rather than merely refused. A corpus that only checks
/// "something was reported" passes against a validator that reports the wrong thing.
/// </remarks>
public static class CssDiagnosticCodes
{
    /// <summary>An <c>@import</c> at-rule.</summary>
    public const string Import = "css.import";

    /// <summary>A <c>url()</c> naming a host other than this one.</summary>
    public const string OffOriginUrl = "css.offOriginUrl";

    /// <summary>A construct that executes script — <c>expression()</c>, <c>behavior</c>, <c>-moz-binding</c>, a <c>javascript:</c> value.</summary>
    public const string Script = "css.script";

    /// <summary>The stylesheet is larger than the cap.</summary>
    public const string TooLarge = "css.tooLarge";

    /// <summary>A comment, string, or <c>url()</c> that is never closed.</summary>
    /// <remarks>
    /// Reported because an unterminated construct is the classic way to make a validator and a
    /// browser disagree about where a value ends: everything after it is a comment to one of them
    /// and a declaration to the other.
    /// </remarks>
    public const string Unterminated = "css.unterminated";
}
