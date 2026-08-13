using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Data.Seeding;

/// <summary>
/// The structural rows the CMS cannot function without: site settings and the built-in block types.
/// </summary>
/// <remarks>
/// Seeded through EF Core's <c>HasData</c> rather than a startup routine, so the rows arrive with
/// the migration that creates their tables. That makes seeding idempotent by construction — it runs
/// exactly once per database, in the same transaction as the schema, and the Aspire
/// <c>ef-migrations</c> resource needs no extra step to apply it.
/// <para>
/// Every value here must be deterministic. A <c>DateTimeOffset.UtcNow</c> in seed data changes the
/// model snapshot on each build and EF then reports a pending model change forever.
/// </para>
/// </remarks>
public static class CmsSeedData
{
    /// <summary>Key of the built-in block type backing free-form HTML reusable content.</summary>
    public const string RawHtmlBlockTypeKey = "rawHtml";

    /// <summary>Key of the single property on the <see cref="RawHtmlBlockTypeKey"/> block type.</summary>
    public const string RawHtmlContentPropertyKey = "content";

    /// <summary>Identity of the built-in raw HTML block type.</summary>
    private const int RawHtmlBlockTypeId = 1;

    /// <summary>
    /// The one row of site configuration. Culture is fixed at <c>en-US</c> for v1 (Q1,
    /// spec section 19).
    /// </summary>
    public static SiteSettings SiteSettings => new()
    {
        Id = Models.Cms.SiteSettings.SingletonId,
        SiteName = "Content Management System",
        Culture = "en-US",
        TimeZoneId = "UTC",
        WorkflowMode = WorkflowMode.None,
        // Zero keeps every superseded version until an administrator sets a real policy. Defaulting
        // to a finite window would quietly delete history on a site nobody had configured yet.
        VersionRetentionDays = 0,
    };

    /// <summary>
    /// The built-in block type that holds a single sanitized HTML fragment.
    /// </summary>
    /// <remarks>
    /// Reusable content needs a shape, and the commonest one — a footer or a banner authored as
    /// markup — should not require a developer to define a block type before the CMS is usable at
    /// all (spec section 9.1). Flagged built-in so it cannot be deleted.
    /// </remarks>
    public static BlockType RawHtmlBlockType => new()
    {
        Id = RawHtmlBlockTypeId,
        Key = RawHtmlBlockTypeKey,
        Name = "Raw HTML",
        Description = "A single block of HTML, sanitized on save and again on render.",
        IconKey = "code",
        SummaryTemplate = $"{{{RawHtmlContentPropertyKey}}}",
        IsBuiltIn = true,
        CurrentRevision = 1,
    };

    /// <summary>The single property on <see cref="RawHtmlBlockType"/>.</summary>
    public static BlockTypeProperty RawHtmlContentProperty => new()
    {
        Id = 1,
        BlockTypeId = RawHtmlBlockTypeId,
        Key = RawHtmlContentPropertyKey,
        Name = "HTML",
        FieldTypeKey = "html",
        IsRequired = true,
        SortOrder = 0,
    };

    /// <summary>
    /// Revision 1 of <see cref="RawHtmlBlockType"/>, so content authored against it has a captured
    /// schema to render through like any other block type.
    /// </summary>
    public static BlockTypeRevision RawHtmlBlockTypeRevision => new()
    {
        Id = 1,
        BlockTypeId = RawHtmlBlockTypeId,
        RevisionNumber = 1,
        PropertySnapshotJson =
            """
            {"properties":[{"key":"content","name":"HTML","fieldTypeKey":"html","isRequired":true,"sortOrder":0}]}
            """,
        Notes = "Initial built-in definition.",
    };
}
