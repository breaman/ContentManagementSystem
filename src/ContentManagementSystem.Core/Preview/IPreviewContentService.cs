using ContentManagementSystem.Core.Delivery;

namespace ContentManagementSystem.Core.Preview;

/// <summary>
/// What the preview toolbar says about the version on screen (spec section 12.1).
/// </summary>
/// <param name="PageId">Identity of the page.</param>
/// <param name="VersionId">Identity of the version being previewed.</param>
/// <param name="Title">Page title as at this version.</param>
/// <param name="VersionNumber">That version's number within the page.</param>
/// <param name="Status">Where it sits in the editorial lifecycle.</param>
/// <param name="IsPublished">Whether this exact version is the one anonymous visitors see.</param>
/// <param name="IsDraft">Whether this is the page's one mutable working version.</param>
/// <remarks>
/// Deliberately not folded into <c>PublishedContent</c>. That record is the delivery result, cached
/// per spec section 16.1 and narrowed to what a renderer may read; the lifecycle status is chrome,
/// wanted by exactly one component and by nothing on the public path.
/// </remarks>
public sealed record PreviewVersionInfo(
    int PageId,
    int VersionId,
    string Title,
    int VersionNumber,
    string Status,
    bool IsPublished,
    bool IsDraft);

/// <summary>
/// Loads <em>any</em> version of a page for rendering, published or not (task P3-16).
/// </summary>
/// <remarks>
/// The preview counterpart of <c>IPublishedContentService</c>, and deliberately a separate service
/// rather than a flag on that one. The whole value of the published service is that its projection
/// selects through <c>page.PublishedVersion</c> and never mentions the draft, which is what makes
/// acceptance criterion <c>P3 #3</c> a property of the SQL; adding an "include drafts" parameter
/// would put the draft row back in the result set of the query the public site runs, one boolean
/// away from being served (spec section 20.1).
/// <para>
/// <strong>It authorizes nothing.</strong> Two callers reach it and they prove their right to be
/// there in completely different ways — an editor by a cookie and a <c>Content.Read</c> policy on
/// the endpoint, a stakeholder by holding a token this service knows nothing about. A permission
/// check in here would have to be satisfied by the anonymous path, which means it would have to be
/// bypassable, which means it would not be a check.
/// </para>
/// </remarks>
public interface IPreviewContentService
{
    /// <summary>
    /// Loads one version of a page, whatever its status.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="versionId">
    /// The exact version to load, or null for the page's draft — falling back to its published
    /// version for a page that somehow has none, so that preview of a live page never comes back
    /// empty.
    /// </param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>
    /// The loaded version, or null when the page, the version, or the content cannot be read. A
    /// version belonging to another page is null rather than an error: the pair is the address.
    /// </returns>
    Task<PublishedContent?> GetAsync(
        int pageId,
        int? versionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads what the toolbar says about a version, without loading its content.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="versionId">The exact version, or null for the page's draft.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The version's label and status, or null when there is no such version.</returns>
    /// <remarks>
    /// A separate read because the chrome and the content are separate requests: the toolbar frames
    /// an <c>iframe</c> and never needs the payload, and loading one to render the other would parse
    /// a whole document to print a version number.
    /// </remarks>
    Task<PreviewVersionInfo?> DescribeAsync(
        int pageId,
        int? versionId = null,
        CancellationToken cancellationToken = default);
}
