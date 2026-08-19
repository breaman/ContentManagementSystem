namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One page carrying one tag (spec section 17.1).
/// </summary>
/// <remarks>
/// Fingerprinted rather than a bare join row: who tagged a page and when is an editorial act, and it
/// is the sort of thing somebody asks about a page that turned up in the wrong filter.
/// </remarks>
public class PageTag : FingerPrintEntityBase
{
    /// <summary>The tagged page.</summary>
    public int PageId { get; set; }

    /// <summary>The tagged page.</summary>
    public Page Page { get; set; } = null!;

    /// <summary>The tag.</summary>
    public int TagId { get; set; }

    /// <summary>The tag.</summary>
    public Tag Tag { get; set; } = null!;
}
