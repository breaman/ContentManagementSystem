using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Publishing;

/// <summary>
/// Compares two versions of a page (task P2-14, spec section 11.4).
/// </summary>
/// <remarks>
/// Structural rather than textual. A JSON text diff of two payloads is unreadable in exactly the
/// cases an editor needs one — a reordered list of blocks reads as everything after the moved block
/// being deleted and re-added — so blocks are matched on their stable GUID and only then compared.
/// <para>
/// <strong>On demand, never in the publish path.</strong> The cost of a diff is unbounded in the
/// size of the content; the publish transaction has to be quick and atomic, and nothing about it
/// needs to know what changed.
/// </para>
/// </remarks>
public interface IContentDiffService
{
    /// <summary>
    /// Compares two versions of the same page.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="fromVersionId">The earlier version.</param>
    /// <param name="toVersionId">The later version.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The difference, or a not-found result when either version is not this page's.</returns>
    Task<CmsResult<ContentDiff>> CompareAsync(
        int pageId,
        int fromVersionId,
        int toVersionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares an unsaved payload against the page's stored draft (task P6-19).
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="contentJson">The payload the caller holds, which was never written.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>What the caller's copy would change, or a not-found result when the page has none.</returns>
    /// <remarks>
    /// The open-diff half of a save conflict, and the only comparison the version diff cannot make:
    /// both copies of a contested draft belong to the same version row, so there is no second
    /// version id to name and one of the two documents has to be sent in.
    /// <para>
    /// The stored draft is the <em>earlier</em> side, deliberately. The question a losing editor is
    /// asking is "what would mine change", and reversing the sides answers the opposite question with
    /// the same words.
    /// </para>
    /// </remarks>
    Task<CmsResult<ContentDiff>> CompareDraftAsync(
        int pageId,
        string? contentJson,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IContentDiffService" />
/// <param name="context">The application database context.</param>
/// <param name="registry">The field types, which is what renders a value comparably.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="acl">Where in the tree the caller may do it (task P7-06, spec section 21.2).</param>
public sealed class ContentDiffService(
    ApplicationDbContext context,
    IFieldTypeRegistry registry,
    ICmsAuthorization authorization,
    IAclService acl) : IContentDiffService
{
    /// <summary>
    /// The comparison itself, which needs no database and is tested without one (task P2-25).
    /// </summary>
    private readonly PayloadDiff _payloads = new(registry);

    /// <inheritdoc />
    public async Task<CmsResult<ContentDiff>> CompareAsync(
        int pageId,
        int fromVersionId,
        int toVersionId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<ContentDiff>.Forbidden("Reading pages is not permitted.", PageCodes.Forbidden);
        }

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentRead, pageId, cancellationToken))
        {
            return CmsResult<ContentDiff>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);
        }

        var versions = await context.PageVersions
            .AsNoTracking()
            .Where(version => version.PageId == pageId &&
                (version.Id == fromVersionId || version.Id == toVersionId))
            .ToListAsync(cancellationToken);

        var from = versions.FirstOrDefault(version => version.Id == fromVersionId);
        var to = versions.FirstOrDefault(version => version.Id == toVersionId);

        if (from is null || to is null)
        {
            return CmsResult<ContentDiff>.NotFound(
                $"Page {pageId} does not have both version {fromVersionId} and version {toVersionId}.",
                PageCodes.VersionNotFound);
        }

        return CmsResult<ContentDiff>.Success(new ContentDiff(
            pageId,
            from.Id,
            to.Id,
            from.VersionNumber,
            to.VersionNumber,
            CompareMetadata(from, to),
            CompareZones(from, to)));
    }

    /// <inheritdoc />
    public async Task<CmsResult<ContentDiff>> CompareDraftAsync(
        int pageId,
        string? contentJson,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<ContentDiff>.Forbidden("Reading pages is not permitted.", PageCodes.Forbidden);
        }

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentRead, pageId, cancellationToken))
        {
            return CmsResult<ContentDiff>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);
        }

        if (!ContentPayload.TryParse(contentJson, out var mine) || !mine.IsObject)
        {
            return CmsResult<ContentDiff>.Invalid(
                PageCodes.MalformedPayload,
                "The content sent is not a well-formed payload document.",
                nameof(DiffDraftRequest.ContentJson));
        }

        var draft = await context.Pages
            .AsNoTracking()
            .Where(page => page.Id == pageId)
            .Select(page => page.DraftVersion)
            .FirstOrDefaultAsync(cancellationToken);

        if (draft is null)
        {
            return CmsResult<ContentDiff>.NotFound(
                $"No page has id {pageId}.",
                PageCodes.NotFound);
        }

        // Both sides name the same version row, which is not a mistake: a save conflict is two copies
        // of one draft, and the losing one was never written anywhere that could give it an id.
        return CmsResult<ContentDiff>.Success(new ContentDiff(
            pageId,
            draft.Id,
            draft.Id,
            draft.VersionNumber,
            draft.VersionNumber,
            // Nothing to compare: title and the SEO fields are on the version row, and the caller
            // sent a payload rather than a version. Reporting them as unchanged would be a claim
            // about values this comparison never saw.
            [],
            _payloads.Compare(
                ContentPayload.TryParse(draft.ContentJson, out var stored) ? stored : null,
                mine)));
    }

    /// <summary>
    /// Compares the versioned metadata as a flat property list.
    /// </summary>
    /// <remarks>
    /// Deliberately hand-listed rather than reflected over the entity. Reflection would sweep in the
    /// row version, the audit stamps, and the foreign keys, and every one of those differs between
    /// two versions by definition — a diff in which everything always changed says nothing.
    /// </remarks>
    private static IReadOnlyList<MetadataChange> CompareMetadata(PageVersion from, PageVersion to)
    {
        var changes = new List<MetadataChange>();

        void Compare(string name, string? before, string? after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                changes.Add(new MetadataChange(name, before, after));
            }
        }

        Compare(nameof(PageVersion.Title), from.Title, to.Title);
        Compare(
            nameof(PageVersion.TemplateRevision),
            from.TemplateRevision.ToString(),
            to.TemplateRevision.ToString());
        Compare(nameof(PageVersion.MetaTitle), from.MetaTitle, to.MetaTitle);
        Compare(nameof(PageVersion.MetaDescription), from.MetaDescription, to.MetaDescription);
        Compare(nameof(PageVersion.CanonicalUrl), from.CanonicalUrl, to.CanonicalUrl);
        Compare(nameof(PageVersion.RobotsIndex), from.RobotsIndex.ToString(), to.RobotsIndex.ToString());
        Compare(nameof(PageVersion.RobotsFollow), from.RobotsFollow.ToString(), to.RobotsFollow.ToString());
        Compare(nameof(PageVersion.OgTitle), from.OgTitle, to.OgTitle);
        Compare(nameof(PageVersion.OgDescription), from.OgDescription, to.OgDescription);
        Compare(
            nameof(PageVersion.OgImageMediaId),
            from.OgImageMediaId?.ToString(),
            to.OgImageMediaId?.ToString());
        Compare(nameof(PageVersion.OgType), from.OgType, to.OgType);
        Compare(nameof(PageVersion.TwitterCard), from.TwitterCard, to.TwitterCard);
        Compare(nameof(PageVersion.StructuredDataJson), from.StructuredDataJson, to.StructuredDataJson);
        Compare(nameof(PageVersion.ChangeFreq), from.ChangeFreq, to.ChangeFreq);
        Compare(nameof(PageVersion.Priority), from.Priority?.ToString(), to.Priority?.ToString());

        return changes;
    }

    /// <summary>Parses both stored documents and hands them to the comparison.</summary>
    private IReadOnlyList<ZoneChange> CompareZones(PageVersion from, PageVersion to) =>
        _payloads.Compare(
            ContentPayload.TryParse(from.ContentJson, out var parsedFrom) ? parsedFrom : null,
            ContentPayload.TryParse(to.ContentJson, out var parsedTo) ? parsedTo : null);
}
