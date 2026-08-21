namespace ContentManagementSystem.Shared.Contracts.Appearance;

/// <summary>
/// One construct the site stylesheet may not contain, located where it was written
/// (spec section 30.5).
/// </summary>
/// <param name="Code">
/// Stable machine-readable discriminator — <c>css.import</c>, <c>css.offOriginUrl</c>,
/// <c>css.script</c>, <c>css.tooLarge</c>, <c>css.unterminated</c>. The editor switches on this; the
/// message is for a person and may be reworded freely.
/// </param>
/// <param name="Message">
/// What was found and what to do instead. Phrased for an administrator writing CSS, not for the
/// engineer who wrote the validator.
/// </param>
/// <param name="Line">1-based line the construct starts on. Zero when the diagnostic is about the file as a whole.</param>
/// <param name="Column">1-based column the construct starts at. Zero when it is about the file as a whole.</param>
/// <param name="Snippet">The offending text, truncated, so the message can quote it back.</param>
public readonly record struct CssDiagnostic(
    string Code,
    string Message,
    int Line,
    int Column,
    string? Snippet = null);
