using System.Globalization;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Common;

/// <summary>
/// Counts what an author has written, against a soft limit and a hard one (task P6-12).
/// </summary>
/// <remarks>
/// <strong>Two limits, and the difference between them is the point.</strong> <c>maxLength</c> is
/// enforced: publishing fails past it, so the counter says so in the same words validation will.
/// <c>softLimit</c> is advisory and nothing on the server reads it — it is for guidance an author
/// wants while typing ("a meta description over 160 characters gets truncated in results") that
/// should never stop them publishing.
/// <para>
/// Characters are counted in UTF-16 code units, matching <c>TextFieldTypeBase</c> and therefore
/// matching what the database would refuse. Counting text elements would read more naturally to an
/// author but would let a value pass this counter and then fail to store, which is the worse
/// surprise.
/// </para>
/// </remarks>
public partial class FieldCounter : ComponentBase
{
    /// <summary>The text being counted.</summary>
    [Parameter]
    public string? Text { get; set; }

    /// <summary>Element id, so a control can point <c>aria-describedby</c> at the count.</summary>
    [Parameter]
    public string? Id { get; set; }

    /// <summary>Whether to count words as well as characters.</summary>
    /// <remarks>
    /// Off for a single-line field, where the word count of a headline is noise, and on for prose,
    /// where it is the number an author is actually working to.
    /// </remarks>
    [Parameter]
    public bool ShowWords { get; set; }

    /// <summary>The enforced maximum, or null when the slot configures none.</summary>
    [Parameter]
    public int? MaxLength { get; set; }

    /// <summary>The advisory maximum, or null when the slot configures none.</summary>
    [Parameter]
    public int? SoftLimit { get; set; }

    /// <summary>How many characters have been written.</summary>
    public int Characters => Text?.Length ?? 0;

    /// <summary>
    /// How many words have been written.
    /// </summary>
    /// <remarks>
    /// Whitespace-separated runs, which over-counts markdown and markup — <c>**bold**</c> is one
    /// word and <c>&lt;p&gt;hello&lt;/p&gt;</c> counts as one too. Stripping markup first would mean
    /// converting the source on every keystroke to produce a number nobody uses to more than two
    /// significant figures.
    /// </remarks>
    public int Words => Text is { Length: > 0 } text
        ? text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length
        : 0;

    /// <summary>The limit shown beside the count, preferring the one an author is working to.</summary>
    /// <remarks>
    /// The soft limit wins when both are configured: it is the smaller number and the one being
    /// aimed at, and printing "of 160" beside a hard limit of 4000 would answer a question nobody
    /// asked.
    /// </remarks>
    private int? Limit => SoftLimit ?? MaxLength;

    /// <summary>Whether the enforced maximum has been passed.</summary>
    private bool IsOverMax => MaxLength is { } max && Characters > max;

    /// <summary>Whether the advisory maximum has been passed but the enforced one has not.</summary>
    private bool IsOverSoft => !IsOverMax && SoftLimit is { } soft && Characters > soft;

    private string StateClass => IsOverMax
        ? "cms-field-count--over"
        : IsOverSoft
            ? "cms-field-count--warning"
            : string.Empty;

    private string Icon => IsOverMax ? "bi-exclamation-octagon-fill" : "bi-exclamation-triangle-fill";

    /// <summary>
    /// What the live region says, or null while nothing has been crossed.
    /// </summary>
    /// <remarks>
    /// Phrased as the consequence rather than as the number. "23 over" tells an author what they
    /// already counted; "publishing will refuse this" tells them whether they have to act.
    /// </remarks>
    private string? Message => IsOverMax
        ? $"{Format(Characters - MaxLength!.Value, "character")} too long — publishing will refuse this."
        : IsOverSoft
            ? $"Longer than the {SoftLimit} characters suggested here. You can still publish it."
            : null;

    /// <summary>A count and its noun, pluralised.</summary>
    /// <param name="count">The number.</param>
    /// <param name="noun">The singular noun.</param>
    /// <returns>Text such as "1 word" or "12 words".</returns>
    private static string Format(int count, string noun)
    {
        var number = count.ToString("N0", CultureInfo.CurrentCulture);

        return count == 1 ? $"{number} {noun}" : $"{number} {noun}s";
    }
}
