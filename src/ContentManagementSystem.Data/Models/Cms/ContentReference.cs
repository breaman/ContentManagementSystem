using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// The kind of content version a reference was found in.
/// </summary>
/// <remarks>
/// Stored as the underlying <c>tinyint</c>. Both source kinds are versioned content whose payload is
/// walked on every save, which is why one table serves them rather than two.
/// </remarks>
public enum ContentSourceType : byte
{
    /// <summary>A version of a page.</summary>
    PageVersion = 0,

    /// <summary>A version of an independently published reusable content item (Phase 4).</summary>
    ReusableContentVersion = 1,
}

/// <summary>
/// One edge from a stored content value to an entity it depends on, projected out of the payload.
/// </summary>
/// <remarks>
/// A derived table: it is rebuilt wholesale from the payload on every save and publish, and holds no
/// information the payload does not already contain. What it adds is direction. The payload answers
/// "what does this page contain"; these rows answer "which pages contain this", which is what makes
/// where-used, link integrity, cache-tag derivation, and the permanent-delete guard indexed queries
/// rather than a scan across every content document on the site (spec section 6.2).
/// <para>
/// <strong>Both ends are polymorphic, so neither carries a foreign key.</strong>
/// <see cref="TargetId"/> means a page, a media item, or a reusable item depending on
/// <see cref="TargetType"/>, and there is no way to express that as a constraint. The guards that
/// depend on these rows are therefore queries, not constraints — and because the indexer
/// deliberately over-reports rather than under-reports, a stale row makes a delete guard cautious
/// while a missing one makes a page go quietly stale (spec section 7.3).
/// </para>
/// </remarks>
public class ContentReference : EntityBase
{
    /// <summary>Kind of content version this reference was found in.</summary>
    public ContentSourceType SourceType { get; set; }

    /// <summary>Identity of the version this reference was found in.</summary>
    public int SourceVersionId { get; set; }

    /// <summary>Kind of entity referenced.</summary>
    public ContentReferenceTargetType TargetType { get; set; }

    /// <summary>Identity of the referenced entity.</summary>
    public int TargetId { get; set; }

    /// <summary>Zone the reference sits in. Null for a reference outside any zone.</summary>
    public string? ZoneKey { get; set; }

    /// <summary>Block instance the reference sits in, or null when the zone holds it directly.</summary>
    /// <remarks>
    /// The block's stable GUID rather than its index, so the row survives a reorder — the same
    /// identity the diff matches on (spec section 11.4).
    /// </remarks>
    public Guid? BlockId { get; set; }

    /// <summary>Property within the block that holds the reference.</summary>
    public string? PropertyKey { get; set; }

    /// <summary>
    /// Whether the reference is pinned to a specific version of its target rather than following it.
    /// </summary>
    /// <remarks>
    /// Reusable content only, and unused until Phase 4. Stored here rather than in the payload
    /// because "which pages pin an old version of this item" is a question the reusable-content
    /// editor has to answer before publishing a change.
    /// </remarks>
    public bool IsPinned { get; set; }

    /// <summary>Version of the target this reference is pinned to. Null unless <see cref="IsPinned"/>.</summary>
    public int? PinnedVersionId { get; set; }
}
