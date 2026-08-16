using System.Diagnostics.CodeAnalysis;

using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Media.Upload;

/// <summary>
/// One accepted file type: the extension an editor may upload, what its bytes must be, and what it
/// is stored as.
/// </summary>
/// <param name="Extension">Canonical lower-case extension including the dot.</param>
/// <param name="MimeType">The type served back, pinned from here rather than from the upload.</param>
/// <param name="Format">The byte format the file's header must match.</param>
/// <param name="Kind">How the library classifies it.</param>
public sealed record MediaTypeDescriptor(
    string Extension,
    string MimeType,
    MediaByteFormat Format,
    MediaKind Kind);

/// <summary>
/// The upload allowlist — every file type this CMS accepts (spec section 13.3 step 2).
/// </summary>
/// <remarks>
/// An allowlist, and the only place one exists. The set is small on purpose: each entry is a decoder
/// somewhere downstream — a browser's, an operating system's preview handler, an editor's — that is
/// now reachable by anything an author can upload, so adding a type is a decision about attack
/// surface rather than a convenience.
/// <para>
/// The MIME type served back comes from this table and never from the request. A client-declared
/// content type is an uploader-controlled string; echoing it back is how a stored file gets served
/// as <c>text/html</c> from the site's own origin (spec section 20.7).
/// </para>
/// <para>
/// <strong>AVIF is absent deliberately</strong> and is refused with its own diagnostic rather than
/// falling through as an unknown extension (spec section 13.9.1).
/// </para>
/// </remarks>
public static class MediaTypeCatalog
{
    private static readonly MediaTypeDescriptor[] Descriptors =
    [
        new(".jpg", "image/jpeg", MediaByteFormat.Jpeg, MediaKind.Image),
        new(".jpeg", "image/jpeg", MediaByteFormat.Jpeg, MediaKind.Image),
        new(".png", "image/png", MediaByteFormat.Png, MediaKind.Image),
        new(".gif", "image/gif", MediaByteFormat.Gif, MediaKind.Image),
        new(".webp", "image/webp", MediaByteFormat.Webp, MediaKind.Image),
        new(".svg", "image/svg+xml", MediaByteFormat.Svg, MediaKind.Image),
        new(".pdf", "application/pdf", MediaByteFormat.Pdf, MediaKind.Document),

        // Both OOXML types are ZIP containers, and the sniffer says so rather than guessing which.
        // The extension decides which of the two it is stored as; the bytes decide only that it is
        // a ZIP at all, which is the honest limit of a magic-number check here.
        new(".docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            MediaByteFormat.Zip,
            MediaKind.Document),
        new(".xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            MediaByteFormat.Zip,
            MediaKind.Document),

        new(".mp4", "video/mp4", MediaByteFormat.Mp4, MediaKind.Video),
    ];

    private static readonly Dictionary<string, MediaTypeDescriptor> ByExtension =
        Descriptors.ToDictionary(descriptor => descriptor.Extension, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every accepted type.</summary>
    public static IReadOnlyList<MediaTypeDescriptor> All => Descriptors;

    /// <summary>Every accepted extension, for display in the picker and in error messages.</summary>
    public static IReadOnlyCollection<string> AllowedExtensions => ByExtension.Keys;

    /// <summary>
    /// Looks up the accepted type a file name claims to be.
    /// </summary>
    /// <param name="fileName">The client-supplied file name.</param>
    /// <param name="descriptor">The matching allowlist entry.</param>
    /// <returns><see langword="true"/> when the extension is on the allowlist.</returns>
    /// <remarks>
    /// Only the final extension is considered, which is the one the operating system and the browser
    /// act on. A name like <c>invoice.pdf.exe</c> therefore matches nothing and is refused, rather
    /// than matching on the <c>.pdf</c> in the middle the way a "contains" check would.
    /// </remarks>
    public static bool TryGetByFileName(
        string? fileName,
        [NotNullWhen(true)] out MediaTypeDescriptor? descriptor)
    {
        descriptor = null;

        if (string.IsNullOrWhiteSpace(fileName)) return false;

        var extension = Path.GetExtension(fileName);

        return !string.IsNullOrEmpty(extension) && ByExtension.TryGetValue(extension, out descriptor);
    }

    /// <summary>
    /// Looks up an accepted type by canonical extension.
    /// </summary>
    /// <param name="extension">Extension including the dot.</param>
    /// <param name="descriptor">The matching allowlist entry.</param>
    /// <returns><see langword="true"/> when the extension is on the allowlist.</returns>
    public static bool TryGetByExtension(
        string? extension,
        [NotNullWhen(true)] out MediaTypeDescriptor? descriptor)
    {
        descriptor = null;

        return !string.IsNullOrWhiteSpace(extension) && ByExtension.TryGetValue(extension, out descriptor);
    }
}
