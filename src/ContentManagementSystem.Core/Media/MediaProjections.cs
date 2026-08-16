using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Media;

/// <summary>
/// Turns a stored media row into the shape the API reports.
/// </summary>
/// <remarks>
/// One projection, shared by the upload pipeline and the library service, because the two answer the
/// same question at different moments — "what is this item now" — and two copies would drift the
/// first time a column was added. That drift is invisible from either side: an upload response
/// missing a member the browser fills in from the list endpoint looks like a UI bug for weeks.
/// </remarks>
internal static class MediaProjections
{
    /// <summary>Projects a stored item into the API's shape.</summary>
    /// <param name="item">The stored item.</param>
    /// <returns>The detail record.</returns>
    /// <remarks>
    /// Never carries the storage key. A client addresses an item by id and gets its picture through
    /// signed rendition URLs; handing out the key would expose the content-addressed layout and
    /// invite requests that bypass the media endpoint entirely (spec section 13.5).
    /// </remarks>
    public static MediaDetail ToDetail(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var edits = MediaEdits.Parse(item.EditsJson);

        return new MediaDetail(
            item.Id,
            item.FolderId,
            item.FileName,
            item.OriginalFileName,
            item.ContentType,
            item.MediaKind.ToString(),
            item.SizeBytes,
            item.Width,
            item.Height,
            item.AltText,
            item.IsDecorative,
            item.Title,
            item.Caption,
            item.Credit,
            item.FocalPointX,
            item.FocalPointY,
            item.EditsVersion,
            // Null rather than an all-defaults document, so a client can ask "has this been edited"
            // without comparing four members against their defaults.
            edits == MediaEdits.None ? null : edits,
            item.CreatedOn,
            Convert.ToBase64String(item.RowVersion ?? []));
    }
}
