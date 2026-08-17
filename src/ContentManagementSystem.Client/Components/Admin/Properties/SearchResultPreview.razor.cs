using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Properties;

/// <summary>
/// The search-result preview beside the SEO fields (task P6-17, spec section 18.1).
/// </summary>
/// <remarks>
/// It exists to make two rules visible that no character counter can explain on its own: a meta
/// title falls back to the page title, and both fields are <em>truncated</em> rather than refused
/// when they run long. An editor who can see the sentence stop mid-word writes a shorter one; an
/// editor told "163 of 160" argues with the number.
/// <para>
/// The limits are the conventional rendering widths rather than anything this system enforces — the
/// server has no opinion about either field's length beyond its column size, which is why they are
/// <c>softLimit</c> guidance on the counters beside the boxes rather than validation.
/// </para>
/// </remarks>
public partial class SearchResultPreview : ComponentBase
{
    /// <summary>Roughly where a result's title stops being shown.</summary>
    public const int TitleLimit = 60;

    /// <summary>Roughly where a result's description stops being shown.</summary>
    public const int DescriptionLimit = 160;

    /// <summary>Distinguishes this widget's caption from another on the same screen.</summary>
    [Parameter]
    public string Id { get; set; } = "cms-serp";

    /// <summary>The address the result would link to.</summary>
    [Parameter]
    public string? Url { get; set; }

    /// <summary>The meta title, when one is set.</summary>
    [Parameter]
    public string? MetaTitle { get; set; }

    /// <summary>The page's own title, which is what a blank meta title falls back to.</summary>
    [Parameter]
    public string? PageTitle { get; set; }

    /// <summary>The meta description, when one is set.</summary>
    [Parameter]
    public string? Description { get; set; }

    /// <summary>Whether search engines are allowed to index the page at all.</summary>
    [Parameter]
    public bool RobotsIndex { get; set; } = true;

    /// <summary>Whether the title shown is the page's rather than a meta title of its own.</summary>
    private bool IsFallbackTitle => string.IsNullOrWhiteSpace(MetaTitle);

    /// <summary>What a result would show as its title.</summary>
    private string Title => IsFallbackTitle ? PageTitle ?? string.Empty : MetaTitle!;

    /// <summary>
    /// Cuts a value where a result would cut it.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="limit">Where it stops.</param>
    /// <returns>The text, ellipsised when it runs past the limit.</returns>
    /// <remarks>
    /// Cut at the last whole word rather than mid-character, which is what a result does and what
    /// makes the preview believable. An ellipsis rather than a hard stop, so the difference between
    /// "this is all of it" and "there is more you cannot see" is visible.
    /// </remarks>
    private static string Truncate(string? value, int limit)
    {
        if (value is not { Length: > 0 } text || text.Length <= limit) return value ?? string.Empty;

        var cut = text[..limit];
        var lastSpace = cut.LastIndexOf(' ');

        return (lastSpace > limit / 2 ? cut[..lastSpace] : cut).TrimEnd() + "…";
    }
}
