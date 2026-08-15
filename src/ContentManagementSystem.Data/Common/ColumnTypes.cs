namespace ContentManagementSystem.Data.Common;

/// <summary>
/// SQL Server column types applied consistently across the model.
/// </summary>
/// <remarks>
/// Centralising these avoids the classic defect where one money column is <c>decimal(18,2)</c> and
/// another defaults to <c>decimal(18,0)</c>, silently truncating cents.
/// </remarks>
public static class ColumnTypes
{
    /// <summary>Monetary amounts.</summary>
    public const string Money = "decimal(18,2)";

    /// <summary>UTC instants.</summary>
    public const string Timestamp = "datetimeoffset(7)";

    /// <summary>Business dates with no time component, such as a needed-by date.</summary>
    public const string BusinessDate = "date";

    /// <summary>
    /// Unbounded text holding a serialised JSON document — content payloads, structural snapshots,
    /// and field-type configuration.
    /// </summary>
    /// <remarks>
    /// These columns are written and read whole and are never queried into. Should payload-internal
    /// querying become necessary, SQL Server's JSON functions over computed, persisted, indexed
    /// columns are the migration path — no table restructuring required (spec section 23.5).
    /// </remarks>
    public const string Json = "nvarchar(max)";

    /// <summary>
    /// Sitemap priority — a single digit either side of the point, holding 0.0 through 1.0.
    /// </summary>
    /// <remarks>
    /// Overrides the model-wide <see cref="Money"/> convention. Sitemaps.org defines the value to
    /// one decimal place, and storing it as <c>decimal(18,2)</c> would invite a 0.55 that no search
    /// engine reads back the way it was written.
    /// </remarks>
    public const string SitemapPriority = "decimal(2,1)";

    /// <summary>
    /// A SHA-256 digest, used to carry a unique index over a column too wide to index directly.
    /// </summary>
    /// <remarks>
    /// Fixed-width <c>binary</c> rather than <c>varbinary</c>: every value is exactly 32 bytes, so
    /// the length prefix a variable column carries would be storage and comparison cost bought for
    /// nothing. See <c>SiteUrls.Hash</c> for why URL columns need this at all (spec section 23.5).
    /// </remarks>
    public const string Sha256Hash = "binary(32)";

    /// <summary>
    /// Unbounded free text with no imposed structure, such as the body of <c>robots.txt</c>.
    /// </summary>
    /// <remarks>
    /// Physically identical to <see cref="Json"/>. Kept separate because the two answer different
    /// questions — a search for every JSON column should not also return prose.
    /// </remarks>
    public const string UnboundedText = "nvarchar(max)";
}