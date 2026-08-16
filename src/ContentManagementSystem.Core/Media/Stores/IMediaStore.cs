namespace ContentManagementSystem.Core.Media.Stores;

/// <summary>
/// Where the bytes of an original or a rendition live (spec section 13.2).
/// </summary>
/// <remarks>
/// The abstraction exists so that the development machine's disk and the production blob container
/// are the same thing to everything above it — the upload pipeline, the rendition generator, and the
/// delivery endpoint all address content by key and never learn which one they are talking to.
/// <para>
/// <strong>Nothing an implementation stores is under <c>wwwroot</c>, and nothing is served
/// directly.</strong> Every byte reaches a visitor through the media endpoint, which is what makes
/// authorization, content-type pinning, and lazy rendition generation apply to all of it rather than
/// to the paths somebody remembered to route (spec section 13.2). A store that could be reached by
/// static file middleware would quietly bypass all three.
/// </para>
/// <para>
/// Keys are always server-generated and content-addressed — see <see cref="MediaStorageKeys"/>.
/// Implementations must nevertheless validate what they are handed, because a key read back from
/// the database is still an untrusted string as far as the filesystem is concerned.
/// </para>
/// </remarks>
public interface IMediaStore
{
    /// <summary>
    /// Stores content under a key, overwriting whatever was there.
    /// </summary>
    /// <param name="key">Server-generated storage key.</param>
    /// <param name="content">The bytes to store, read from the current position.</param>
    /// <param name="contentType">The sniffed media type, for stores that record one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was stored.</returns>
    /// <remarks>
    /// Overwriting is safe precisely because keys are content-addressed: the same key means the same
    /// bytes, so a second put of an already-present key is a re-upload of identical content. It is
    /// also why a partially written object must never become visible under its final key — an
    /// interrupted upload that left half a file where the hash promises a whole one would be served
    /// forever afterwards without anything noticing.
    /// </remarks>
    Task<MediaStoreResult> PutAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens the content stored under a key.
    /// </summary>
    /// <param name="key">Storage key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A readable stream the caller disposes, or null when nothing is stored there.</returns>
    Task<Stream?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Reports whether anything is stored under a key.</summary>
    /// <param name="key">Storage key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the key holds content.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the content stored under a key. Succeeds when there was none.
    /// </summary>
    /// <param name="key">Storage key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the key holds nothing.</returns>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// The URL a client could fetch this key from directly, when the store has one.
    /// </summary>
    /// <param name="key">Storage key.</param>
    /// <param name="validFor">How long the URL should remain valid, for stores that sign them.</param>
    /// <returns>A directly addressable URL, or null when the store has none.</returns>
    /// <remarks>
    /// Null is the ordinary answer and the only one v1 acts on: delivery goes through the media
    /// endpoint so that the safety headers of spec section 20.7 are applied by something that cannot
    /// be routed around. The method exists so a later CDN offload has somewhere to live rather than
    /// because anything calls it today.
    /// </remarks>
    Uri? GetPublicUrl(string key, TimeSpan? validFor = null);
}

/// <summary>What a <see cref="IMediaStore.PutAsync"/> stored.</summary>
/// <param name="Key">The key the content is addressable by.</param>
/// <param name="SizeBytes">Number of bytes written.</param>
/// <param name="ContentType">The media type recorded against the object.</param>
public readonly record struct MediaStoreResult(string Key, long SizeBytes, string ContentType);
