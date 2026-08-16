using System.Text.Json.Serialization;

using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Media;

/// <summary>
/// What the media browser asked for (task P5-23, spec section 22.1).
/// </summary>
/// <remarks>
/// A record rather than a parameter list because the filters travel together everywhere: the
/// endpoint binds them, the service applies them, and the response echoes the paging half back so a
/// client can tell "page 3 of many" from "everything there is".
/// </remarks>
public sealed record MediaQuery
{
    /// <summary>Largest page a caller may ask for.</summary>
    /// <remarks>
    /// The browser is a grid of thumbnails, and a library with fifty thousand items in it is exactly
    /// the deployment where an unbounded list would be requested once and never recovered from. The
    /// ceiling is applied by clamping rather than by refusing, so a client asking for more gets the
    /// most it can have instead of an error it has to learn to handle.
    /// </remarks>
    public const int MaxTake = 200;

    /// <summary>Page size used when the caller names none.</summary>
    public const int DefaultTake = 50;

    /// <summary>Folder to list, or null for the whole library.</summary>
    public int? FolderId { get; init; }

    /// <summary>
    /// Whether <see cref="FolderId"/> means "in this folder" or "anywhere beneath it".
    /// </summary>
    /// <remarks>
    /// Off by default, so the browser's folder view shows what a filesystem would. Turning it on is
    /// a prefix match on the folder's materialized path, which is the one query the folder tree is
    /// shaped to answer.
    /// </remarks>
    public bool IncludeDescendants { get; init; }

    /// <summary>Restrict to one kind of file — <c>Image</c>, <c>Document</c>, <c>Video</c>, <c>Audio</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>Free-text match over the display name, title, and alternative text.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// Restrict to items no published or draft content points at.
    /// </summary>
    /// <remarks>
    /// The library-tidying filter, and the reason it is a filter rather than a report: an editor
    /// clearing space wants to see the candidates in the browser they already know, with the same
    /// preview and the same delete button.
    /// </remarks>
    public bool UnusedOnly { get; init; }

    /// <summary>Whether to list the recycle bin instead of the live library.</summary>
    public bool DeletedOnly { get; init; }

    /// <summary>How many items to skip.</summary>
    public int Skip { get; init; }

    /// <summary>How many items to return, clamped to <see cref="MaxTake"/>.</summary>
    public int Take { get; init; } = DefaultTake;
}

/// <summary>
/// One page of the media library.
/// </summary>
/// <param name="Items">The items on this page, newest first.</param>
/// <param name="Total">
/// How many items match the filters in total, which is what a pager is built from rather than
/// <c>Items.Count</c>.
/// </param>
/// <param name="Skip">How many were skipped to produce this page.</param>
/// <param name="Take">How many were asked for, after clamping.</param>
public sealed record MediaListResult(
    IReadOnlyList<MediaDetail> Items,
    int Total,
    int Skip,
    int Take);

/// <summary>
/// A partial update to an item's editor-maintained metadata (spec section 22.1).
/// </summary>
/// <remarks>
/// Every member is a <see cref="Patch{T}"/>, so a client that changes the alt text does not clear
/// the credit line it never sent. Nothing here touches the bytes: geometry is
/// <see cref="SetMediaEditsRequest"/> and new bytes are the replace endpoint, which keeps "what the
/// picture is" and "what it says about itself" as two decisions with two audit trails.
/// </remarks>
public sealed record PatchMediaRequest
{
    /// <summary>Alternative text describing the image to someone who cannot see it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> AltText { get; init; }

    /// <summary>Whether the image carries no information and renders <c>alt=""</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<bool> IsDecorative { get; init; }

    /// <summary>Editor-facing title.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> Title { get; init; }

    /// <summary>Caption rendered alongside the image.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> Caption { get; init; }

    /// <summary>Attribution line.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string?> Credit { get; init; }

    /// <summary>Folder to file the item in, or null for the root of the library.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<int?> FolderId { get; init; }

    /// <summary>The item's <c>rowversion</c> as the caller last saw it, Base64-encoded.</summary>
    public string? ExpectedRowVersion { get; init; }
}

/// <summary>
/// New library-scope geometry for an item (task P5-10, spec section 13.4).
/// </summary>
/// <param name="Edits">
/// The complete edit document, not a delta. Rotating an image that already carries a crop sends both
/// — a partial document would make "no crop was sent" ambiguous between "leave it" and "remove it",
/// and the editor that composes these has the whole document in its hands anyway.
/// </param>
/// <param name="ExpectedRowVersion">The item's <c>rowversion</c> as the caller last saw it.</param>
/// <remarks>
/// Accepting this bumps <c>EditsVersion</c>, which is folded into every rendition signature — so one
/// call changes every URL the site emits for the item and busts browser and CDN caches with no purge
/// to run (ADR 0007).
/// </remarks>
public sealed record SetMediaEditsRequest(MediaEdits Edits, string? ExpectedRowVersion = null);

/// <summary>
/// What a delete did, and what it would have taken to make it permanent.
/// </summary>
/// <param name="Id">Identity of the item.</param>
/// <param name="WasPermanent">
/// Whether the row and its bytes are gone, as opposed to the item having been moved to the bin.
/// </param>
/// <param name="RemainingRenditions">How many derived encodings were discarded with it.</param>
public sealed record MediaDeleteResult(int Id, bool WasPermanent, int RemainingRenditions);

/// <summary>
/// One folder in the media library's organizing tree, with its children.
/// </summary>
/// <param name="Id">Identity of the folder.</param>
/// <param name="ParentId">Parent folder, or null at the root of the library.</param>
/// <param name="Name">Editor-facing folder name.</param>
/// <param name="SortOrder">Order among siblings.</param>
/// <param name="ItemCount">Live items filed directly in this folder.</param>
/// <param name="Children">Direct child folders, in sort order.</param>
/// <remarks>
/// The materialized path is deliberately absent. It is a list of row ids that exists to make subtree
/// queries indexable, and a client that learned to parse it would be reading an implementation
/// detail that a move rewrites underneath it.
/// </remarks>
public sealed record MediaFolderNode(
    int Id,
    int? ParentId,
    string Name,
    int SortOrder,
    int ItemCount,
    IReadOnlyList<MediaFolderNode> Children);

/// <summary>Creates a folder.</summary>
/// <param name="Name">Editor-facing folder name.</param>
/// <param name="ParentId">Folder to create it under, or null for the root of the library.</param>
public sealed record CreateMediaFolderRequest(string Name, int? ParentId = null);

/// <summary>Renames, reorders, or moves a folder.</summary>
public sealed record PatchMediaFolderRequest
{
    /// <summary>Editor-facing folder name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<string> Name { get; init; }

    /// <summary>New parent, or null to move the folder to the root of the library.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<int?> ParentId { get; init; }

    /// <summary>Order among siblings.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Patch<int> SortOrder { get; init; }
}
