namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One published state of the site stylesheet, kept so an administrator can compare against it and
/// revert to it (spec section 30.2).
/// </summary>
/// <remarks>
/// Cut <strong>on publish only</strong>, exactly as a page version is. A save is a save: revisions
/// recording every keystroke-level edit would be storage bought in exchange for a history nobody can
/// read.
/// </remarks>
public class SiteStylesheetRevision : EntityBase
{
    /// <summary>The stylesheet this revision belongs to. Always <see cref="SiteStylesheet.SingletonId"/>.</summary>
    public int SiteStylesheetId { get; set; }

    /// <summary>The stylesheet row, for the navigation EF needs to build the relationship.</summary>
    public SiteStylesheet SiteStylesheet { get; set; } = null!;

    /// <summary>The CSS exactly as it was published.</summary>
    public string Css { get; set; } = string.Empty;

    /// <summary>SHA-256 of <see cref="Css"/>, so "is this the same as what is live" is a comparison.</summary>
    public byte[] Hash { get; set; } = [];

    /// <summary>Byte length of <see cref="Css"/>, so the revision list can show a delta without reading every revision.</summary>
    public int ByteLength { get; set; }

    /// <summary>
    /// What the administrator said this change was for. Optional, and the question a revert is
    /// trying to answer.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>When it was published.</summary>
    public DateTimeOffset CreatedOn { get; set; }

    /// <summary>Who published it.</summary>
    public int CreatedByUserId { get; set; }
}
