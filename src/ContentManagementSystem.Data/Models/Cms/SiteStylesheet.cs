namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// The single row holding the administrator-authored site stylesheet: a working draft, the copy the
/// public site is currently serving, and the identity of the revision that copy came from
/// (spec section 30.2).
/// </summary>
/// <remarks>
/// The stylesheet is treated as content rather than as configuration (D27). It has a draft and a
/// published copy for the same reason a page does: an administrator has to be able to work on a
/// redesign while the site keeps serving what was last published, and has to be able to see the
/// redesign on real pages the whole time.
/// <para>
/// Singleton-ness is a check constraint pinning <see cref="EntityBase.Id"/> to
/// <see cref="SingletonId"/>, as it is on <see cref="SiteSettings"/>: "there is only ever one row"
/// is the kind of invariant that quietly stops being true and then has to be reconciled by hand.
/// </para>
/// </remarks>
public class SiteStylesheet : FingerPrintEntityBase
{
    /// <summary>The only primary key value this table ever holds.</summary>
    public const int SingletonId = 1;

    /// <summary>
    /// What the administrator is working on. Never served to an anonymous visitor; preview renders
    /// against this, delivery never does.
    /// </summary>
    public string DraftCss { get; set; } = string.Empty;

    /// <summary>
    /// What every anonymous visitor receives. Null means nothing has been published, in which case
    /// the public document omits the stylesheet link entirely rather than requesting an empty file.
    /// </summary>
    public string? PublishedCss { get; set; }

    /// <summary>
    /// SHA-256 of <see cref="PublishedCss"/>, which is the response's <c>ETag</c>.
    /// </summary>
    /// <remarks>
    /// Stored rather than computed per request: the delivery endpoint answers a conditional request
    /// from this column without materialising the CSS at all, and a hash recomputed on every
    /// revalidation would be work done to discover that nothing had changed.
    /// </remarks>
    public byte[]? PublishedHash { get; set; }

    /// <summary>When the current published copy was published.</summary>
    public DateTimeOffset? PublishedOn { get; set; }

    /// <summary>Who published the current copy.</summary>
    public int? PublishedByUserId { get; set; }

    /// <summary>
    /// The revision the current published copy was snapshotted into, or null after a revert to
    /// nothing.
    /// </summary>
    public int? PublishedRevisionId { get; set; }

    /// <summary>
    /// The revision itself, so a publish is a single <c>SaveChanges</c>.
    /// </summary>
    /// <remarks>
    /// Without the navigation, pointing the stylesheet at a revision that does not have an id yet
    /// takes two saves — and the second one is outside the transaction that enqueued the cache
    /// eviction, which is the arrangement P8-09 exists to avoid.
    /// </remarks>
    public SiteStylesheetRevision? PublishedRevision { get; set; }

    /// <summary>Revisions cut from this stylesheet, one per publish.</summary>
    public ICollection<SiteStylesheetRevision> Revisions { get; set; } = [];

    /// <summary>
    /// Optimistic concurrency token. Two administrators editing one stylesheet is rarer than two
    /// editors on one page and no less destructive (spec section 11.8).
    /// </summary>
    public byte[] RowVersion { get; set; } = [];
}
