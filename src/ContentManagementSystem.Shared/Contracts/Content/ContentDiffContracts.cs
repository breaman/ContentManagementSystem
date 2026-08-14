namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// What happened to one part of a payload between two versions (spec section 11.4).
/// </summary>
public enum ContentChangeKind
{
    /// <summary>Present in both, and the same.</summary>
    Unchanged = 0,

    /// <summary>Present only in the later version.</summary>
    Added = 1,

    /// <summary>Present only in the earlier version.</summary>
    Removed = 2,

    /// <summary>Present in both, with a different value.</summary>
    Changed = 3,

    /// <summary>
    /// The same block in a different position.
    /// </summary>
    /// <remarks>
    /// The distinction the whole diff exists for. Matching blocks by their stable GUID is what lets
    /// a reordered list read as "this block moved" rather than as every block after it being
    /// removed and re-added, which is what a positional comparison reports and what makes a diff
    /// useless on exactly the edit people make most.
    /// </remarks>
    Moved = 4,
}

/// <summary>One run of text, and whether it survived the change.</summary>
/// <param name="Text">The words, including the whitespace that separated them.</param>
/// <param name="Kind">
/// <see cref="ContentChangeKind.Unchanged"/>, <see cref="ContentChangeKind.Added"/>, or
/// <see cref="ContentChangeKind.Removed"/>. A word-level diff has no other outcomes.
/// </param>
public sealed record TextSegment(string Text, ContentChangeKind Kind);

/// <summary>
/// A change to one property of a block, or to a zone value taken as a whole.
/// </summary>
/// <param name="Key">Property or zone key.</param>
/// <param name="FieldTypeKey">Field type that wrote the value, as stored.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Before">A human-readable rendering of the earlier value.</param>
/// <param name="After">A human-readable rendering of the later value.</param>
/// <param name="Segments">
/// Word-level segments for a text-valued property, empty otherwise. This is what the inline
/// highlighting is drawn from.
/// </param>
public sealed record PropertyChange(
    string Key,
    string? FieldTypeKey,
    ContentChangeKind Kind,
    string? Before,
    string? After,
    IReadOnlyList<TextSegment> Segments);

/// <summary>
/// A change to one block instance inside a <c>blocks</c> zone.
/// </summary>
/// <param name="BlockId">The block's stable GUID, which is what it is matched on.</param>
/// <param name="BlockTypeKey">Key of its block type.</param>
/// <param name="Kind">What happened to it.</param>
/// <param name="BeforeIndex">Its position in the earlier version, or null when it was not there.</param>
/// <param name="AfterIndex">Its position in the later version, or null when it is gone.</param>
/// <param name="Properties">Property-level changes, empty for a block that only moved.</param>
public sealed record BlockChange(
    Guid BlockId,
    string? BlockTypeKey,
    ContentChangeKind Kind,
    int? BeforeIndex,
    int? AfterIndex,
    IReadOnlyList<PropertyChange> Properties);

/// <summary>
/// A change to one zone.
/// </summary>
/// <param name="ZoneKey">The zone key.</param>
/// <param name="FieldTypeKey">Field type that fills it, as stored.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Before">A human-readable rendering of the earlier value.</param>
/// <param name="After">A human-readable rendering of the later value.</param>
/// <param name="Segments">Word-level segments for a text-valued zone.</param>
/// <param name="Blocks">Block-level changes for a container zone.</param>
public sealed record ZoneChange(
    string ZoneKey,
    string? FieldTypeKey,
    ContentChangeKind Kind,
    string? Before,
    string? After,
    IReadOnlyList<TextSegment> Segments,
    IReadOnlyList<BlockChange> Blocks);

/// <summary>A change to one versioned metadata field.</summary>
/// <param name="Name">Name of the field, as the editor knows it.</param>
/// <param name="Before">Its earlier value.</param>
/// <param name="After">Its later value.</param>
public sealed record MetadataChange(string Name, string? Before, string? After);

/// <summary>
/// The structural difference between two versions of a page.
/// </summary>
/// <param name="PageId">Page the versions belong to.</param>
/// <param name="FromVersionId">Identity of the earlier version.</param>
/// <param name="ToVersionId">Identity of the later version.</param>
/// <param name="FromVersionNumber">Its version number.</param>
/// <param name="ToVersionNumber">The later version's number.</param>
/// <param name="Metadata">Title, template revision, and the SEO fields, as a flat list.</param>
/// <param name="Zones">Zone-level changes, with block-level detail inside container zones.</param>
/// <remarks>
/// Computed over the payload, not over the raw JSON text: two documents that differ only in member
/// order or whitespace are the same content, and a textual diff of a JSON document is unreadable in
/// the cases anyone actually needs it (spec section 11.4).
/// <para>
/// <strong>Computed on demand and never in the publish path.</strong> A diff is something a person
/// asks for; putting it inside the publish transaction would add an unbounded cost to the one
/// operation that has to be fast and atomic.
/// </para>
/// </remarks>
public sealed record ContentDiff(
    int PageId,
    int FromVersionId,
    int ToVersionId,
    int FromVersionNumber,
    int ToVersionNumber,
    IReadOnlyList<MetadataChange> Metadata,
    IReadOnlyList<ZoneChange> Zones)
{
    /// <summary>Whether anything at all differs.</summary>
    public bool HasChanges => Metadata.Count > 0 || Zones.Count > 0;
}
