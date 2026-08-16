using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Media.Delivery;

/// <summary>
/// What a stored <c>mediaId</c> actually points at, at the moment it is rendered (task P5-20).
/// </summary>
/// <param name="Id">The item the content named.</param>
/// <param name="Kind">What the file is, decided from the sniffed bytes.</param>
/// <param name="ContentType">The sniffed media type, which decides the fallback format.</param>
/// <param name="OriginalFileName">The uploaded name, used for readable URLs and downloads.</param>
/// <param name="Width">Pixel width of the stored original, after orientation was baked in.</param>
/// <param name="Height">Pixel height of the stored original, after orientation was baked in.</param>
/// <param name="AltText">The item's own alternative text, or null when it carries none.</param>
/// <param name="IsDecorative">Whether the item renders <c>alt=""</c>.</param>
/// <param name="Title">Editor-facing title, used as a link label for non-images.</param>
/// <param name="Caption">Caption a template may render alongside the image.</param>
/// <param name="Credit">Attribution line.</param>
/// <param name="Edits">The library-scope edits, which every usage inherits.</param>
/// <param name="EditsVersion">The edits generation, which is folded into every signature.</param>
/// <remarks>
/// The library's answer, not the placement's. A usage may override the alternative text, the focal
/// point, and the crop, and those live in the page payload — which is why they are absent here: a
/// resolver that merged them would make one item's record depend on which page asked for it
/// (spec section 13.4).
/// <para>
/// <see cref="Edits"/> is carried rather than re-read from JSON at each usage. The renderer needs it
/// to work out how large the picture actually is — a 90° library rotation swaps the dimensions this
/// record reports — and parsing the same document once per placement would be the cheapest possible
/// way to make a gallery slow.
/// </para>
/// </remarks>
public sealed record ResolvedMedia(
    int Id,
    MediaKind Kind,
    string ContentType,
    string OriginalFileName,
    int? Width,
    int? Height,
    string? AltText,
    bool IsDecorative,
    string? Title,
    string? Caption,
    string? Credit,
    MediaEdits Edits,
    int EditsVersion);

/// <summary>
/// Turns the media ids stored in content into everything a renderer needs to emit a picture
/// (task P5-20, spec section 13.6).
/// </summary>
/// <remarks>
/// The media counterpart of <see cref="Routing.ILinkResolver"/>, and it exists for the same reason:
/// content stores an id and nothing else about the file, so a page that shows an image is only
/// correct because something resolves that id late. Replacing the picture in the library, fixing its
/// alt text, or rotating it changes every page showing it with nothing having gone back to rewrite a
/// payload (spec section 13.1).
/// <para>
/// Batched, for the reason link resolution is: a gallery is a dozen ids and a page may hold several,
/// and one query per image is the N+1 that only shows up under real content.
/// </para>
/// <para>
/// The soft-delete query filter is left in place. An item in the recycle bin resolves to nothing and
/// renders as the spec section 15.3 placeholder rather than as a broken image — which is the same
/// answer the public site and preview both need, since neither audience should be shown a file an
/// editor has withdrawn.
/// </para>
/// </remarks>
public interface IMediaResolver
{
    /// <summary>
    /// Resolves every media id a render is about to need.
    /// </summary>
    /// <param name="mediaIds">The ids, in any order and with any repeats.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>One entry per distinct id that names an item that still exists.</returns>
    Task<IReadOnlyDictionary<int, ResolvedMedia>> ResolveAsync(
        IEnumerable<int> mediaIds,
        CancellationToken cancellationToken = default);
}
