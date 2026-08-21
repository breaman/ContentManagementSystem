namespace ContentManagementSystem.Core.Appearance;

/// <summary>
/// What the public site is currently serving as its custom stylesheet (spec section 30.4).
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="ISiteStylesheetService"/> and deliberately tiny. Delivery
/// runs anonymously and must never be one mistaken property access away from the draft: this
/// interface cannot express "the draft", so no delivery path can accidentally serve one.
/// </remarks>
public interface IPublishedStylesheetReader
{
    /// <summary>
    /// Reads what is published, or null when nothing is.
    /// </summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The CSS and its hash, or null.</returns>
    Task<PublishedStylesheet?> GetPublishedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads only the published copy's entity tag, without materialising the CSS.
    /// </summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The tag, or null when nothing is published.</returns>
    /// <remarks>
    /// The public document asks this on every uncached render, purely to decide whether to emit the
    /// <c>&lt;link&gt;</c> at all. Reading an <c>nvarchar(max)</c> column to answer a yes-or-no
    /// question would put the whole stylesheet on the wire once per page render.
    /// </remarks>
    Task<string?> GetPublishedETagAsync(CancellationToken cancellationToken = default);
}

/// <summary>The published stylesheet and the tag that identifies it.</summary>
/// <param name="Css">The CSS exactly as it was published.</param>
/// <param name="ETag">
/// A strong entity tag derived from the published hash. Strong rather than weak: two responses
/// sharing it are the same bytes, not merely equivalent.
/// </param>
/// <param name="PublishedOn">When it was published, for <c>Last-Modified</c>.</param>
public sealed record PublishedStylesheet(string Css, string ETag, DateTimeOffset PublishedOn);
