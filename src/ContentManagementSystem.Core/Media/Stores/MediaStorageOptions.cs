namespace ContentManagementSystem.Core.Media.Stores;

/// <summary>
/// Where a deployment keeps media bytes (spec section 13.2).
/// </summary>
public sealed class MediaStorageOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Cms:MediaStorage";

    /// <summary>Directory name that may never appear in a filesystem store's root.</summary>
    public const string ForbiddenRootSegment = "wwwroot";

    /// <summary>
    /// Root directory of the filesystem store.
    /// </summary>
    /// <remarks>
    /// Must be outside <c>wwwroot</c>, and <see cref="FileSystemMediaStore"/> refuses to start when
    /// it is not. Serving uploads as static files would bypass the content-type pinning, the
    /// <c>nosniff</c> header, and the authorization the media endpoint applies — a stored HTML file
    /// would execute on the site's own origin (spec section 20.7).
    /// <para>
    /// Relative paths resolve against the content root, which is what makes the default work on a
    /// development machine without configuration.
    /// </para>
    /// </remarks>
    public string FileSystemRoot { get; set; } = "App_Data/media";

    /// <summary>
    /// Name of the blob container the Azure store reads and writes.
    /// </summary>
    /// <remarks>
    /// The container must be private. Every byte reaches a visitor through the media endpoint, so a
    /// container with public blob access would hand out unprocessed originals — including ones whose
    /// pages are still unpublished — under URLs the application never signed.
    /// </remarks>
    public string BlobContainerName { get; set; } = "cms-media";
}
