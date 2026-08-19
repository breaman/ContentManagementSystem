namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A label an editor puts on pages, for finding them again (spec section 17.1).
/// </summary>
/// <remarks>
/// A row rather than free text on the page, so that the backoffice can offer the tags that exist and
/// a rename reaches every page carrying it. The <see cref="Slug"/> is what a URL or a filter uses,
/// and it is unique; the <see cref="Name"/> is what an editor reads and may be recased freely.
/// </remarks>
public class Tag : FingerPrintEntityBase
{
    /// <summary>The label as an editor typed it.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Normalized form, unique across the site.</summary>
    public string Slug { get; set; } = null!;

    /// <summary>Pages carrying this tag.</summary>
    public ICollection<PageTag> Pages { get; set; } = [];
}
