namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// The single row of site-wide configuration an administrator edits without a deployment.
/// </summary>
/// <remarks>
/// Singleton-ness is enforced by a check constraint pinning <see cref="EntityBase.Id"/> to
/// <see cref="SingletonId"/> rather than by convention, because "there is only ever one row" is the
/// kind of invariant that quietly stops being true and then has to be reconciled by hand.
/// </remarks>
public class SiteSettings : FingerPrintEntityBase
{
    /// <summary>The only primary key value this table ever holds.</summary>
    public const int SingletonId = 1;

    /// <summary>Site name used in page titles and Open Graph output.</summary>
    public string SiteName { get; set; } = null!;

    /// <summary>
    /// BCP-47 culture the site renders in. Fixed at <c>en-US</c> for v1 — localization is out of
    /// scope (Q1, spec section 19) — but stored rather than hard-coded so adding it later is a
    /// schema no-op.
    /// </summary>
    public string Culture { get; set; } = "en-US";

    /// <summary>
    /// Time zone that editor-facing dates are displayed in. Instants are always stored UTC.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// Body served at <c>/robots.txt</c>. Null falls back to the generated default.
    /// </summary>
    public string? RobotsTxt { get; set; }

    /// <summary>Approval ceremony applied to publishing across the site.</summary>
    public WorkflowMode WorkflowMode { get; set; } = WorkflowMode.None;

    /// <summary>
    /// Page served at the site root. Untyped <c>int</c> until <c>Page</c> arrives in Phase 2, when
    /// the foreign key is added.
    /// </summary>
    public int? HomePageId { get; set; }

    /// <summary>CMS page rendered for unresolved URLs. Foreign key added in Phase 2.</summary>
    public int? NotFoundPageId { get; set; }

    /// <summary>
    /// How long superseded versions are kept before the retention job prunes them
    /// (spec section 11.7). Zero keeps everything.
    /// </summary>
    public int VersionRetentionDays { get; set; }

    /// <summary>
    /// Fallback Open Graph image for pages that specify none. Foreign key added in Phase 5 with
    /// <c>MediaItem</c>.
    /// </summary>
    public int? DefaultOgImageMediaId { get; set; }

    /// <summary>Search-console verification token rendered into the page head.</summary>
    public string? GoogleSiteVerification { get; set; }
}
