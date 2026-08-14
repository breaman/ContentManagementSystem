namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// Where a <see cref="PageVersion"/> sits in the editorial lifecycle (spec section 11.1).
/// </summary>
/// <remarks>
/// Exactly one version per page is <see cref="Draft"/> and it is the only mutable one. Every other
/// state is a frozen record, which is what lets an editor keep working while the published version
/// stays byte-for-byte as it was.
/// <para>
/// Stored as the underlying <c>tinyint</c>. The numbers are the contract — they appear in indexes
/// and in the API — so members are never renumbered.
/// </para>
/// </remarks>
public enum PageVersionStatus : byte
{
    /// <summary>The single mutable working copy. Always present on a page.</summary>
    Draft = 0,

    /// <summary>Submitted for approval and frozen while it is under review.</summary>
    InReview = 1,

    /// <summary>Approved, awaiting a publish action or a scheduled window.</summary>
    Approved = 2,

    /// <summary>Currently served to anonymous visitors.</summary>
    Published = 3,

    /// <summary>
    /// Previously published and superseded, or a named checkpoint an editor bookmarked. Retained
    /// per the retention policy in spec section 11.7.
    /// </summary>
    Archived = 4,

    /// <summary>Sent back with comments. Its content is copied into a fresh draft, never reopened.</summary>
    Rejected = 5,
}
