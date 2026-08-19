namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Diagnostic codes for the authored-output accessibility rules of spec section 28 (task P9-10).
/// </summary>
/// <remarks>
/// Every one of these is a <em>warning</em>, and that is the design rather than a softening. Each
/// describes markup that renders correctly and reads badly, and a publish an editor cannot complete
/// because a link says "read more" is a publish that happens through whatever route skips the check.
/// The one accessibility rule that does block is alt text (spec section 13.7), because an undescribed
/// picture is invisible rather than merely awkward and nothing downstream will ever notice it.
/// </remarks>
public static class AccessibilityCodes
{
    /// <summary>A heading level was skipped — an <c>h2</c> followed by an <c>h4</c>.</summary>
    /// <remarks>
    /// Screen-reader navigation is by heading level, so a skipped level reads as a missing section
    /// rather than as a typographic choice. The template owns <c>h1</c>, which is why authored
    /// content starting at <c>h3</c> counts as a skip.
    /// </remarks>
    public const string HeadingSkipped = "a11y.heading-skipped";

    /// <summary>A link's text does not say where it goes.</summary>
    /// <remarks>
    /// "Click here" and its family are the canonical failure: a screen reader can list every link on
    /// a page, and a list of eleven entries reading "read more" is a list of nothing. A bare URL is
    /// the same problem wearing different clothes — it is read out character by character.
    /// </remarks>
    public const string LinkTextUninformative = "a11y.link-text";

    /// <summary>A table has no header cells at all.</summary>
    public const string TableWithoutHeaders = "a11y.table-no-headers";

    /// <summary>A header cell does not say what it heads.</summary>
    /// <remarks>
    /// <c>scope</c> is what associates a header with its row or its column. Without it a screen
    /// reader guesses, and it guesses wrong on any table that is not the simplest possible shape.
    /// </remarks>
    public const string TableHeaderWithoutScope = "a11y.table-header-scope";
}
