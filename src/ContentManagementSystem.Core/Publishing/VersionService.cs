using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Publishing;

/// <summary>
/// Version history, rollback, and retention (task P2-13, spec sections 11.5 and 11.7).
/// </summary>
/// <remarks>
/// Restoring an old version <em>copies</em> it into the draft rather than resurrecting the row as
/// published. The timeline stays strictly forward-moving, the history never gains a cycle, and the
/// editor reviews and publishes normally — which is also what keeps the audit chain readable
/// (spec section 11.5).
/// </remarks>
public interface IVersionService
{
    /// <summary>Lists a page's versions, newest first.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The history, or a not-found result.</returns>
    Task<CmsResult<IReadOnlyList<PageVersionSummary>>> ListAsync(
        int pageId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one version with its payload.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="versionId">Identity of the version.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The version, or a not-found result.</returns>
    Task<CmsResult<PageVersionDetail>> GetAsync(
        int pageId,
        int versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a version's content and metadata into the draft.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="versionId">Identity of the version to restore.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The draft as it now stands, or a not-found result.</returns>
    /// <remarks>
    /// The published version is untouched (acceptance criterion P2 #7). Restoring is an edit to the
    /// draft and nothing more; what reaches the public site is decided by a later publish.
    /// </remarks>
    Task<CmsResult<DraftState>> RestoreAsync(
        int pageId,
        int versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prunes version history according to the retention policy.
    /// </summary>
    /// <param name="cancellationToken">Token observed while deleting.</param>
    /// <returns>What was removed.</returns>
    /// <remarks>
    /// The policy from spec section 11.7, and every clause of it exists to protect something an
    /// editor would be upset to lose: everything inside the retention window, the last twenty
    /// versions of every page, every version that was ever published, every named checkpoint, and
    /// everything belonging to a page in the recycle bin — because a restore that came back with no
    /// history is not a restore.
    /// </remarks>
    Task<RetentionSweepResult> PruneAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IVersionService" />
/// <param name="context">The application database context.</param>
/// <param name="references">Rewrites the draft's reference rows after a restore.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="acl">Where in the tree the caller may do it (task P7-06, spec section 21.2).</param>
/// <param name="clock">Source of the current time, so the retention window is testable.</param>
/// <param name="logger">Log for restores and for what the retention sweep removed.</param>
public sealed class VersionService(
    ApplicationDbContext context,
    IContentReferenceProjector references,
    ICmsAuthorization authorization,
    IAclService acl,
    TimeProvider clock,
    ILogger<VersionService> logger) : IVersionService
{
    /// <summary>Versions kept per page beyond the retention window (spec section 11.7).</summary>
    public const int KeepPerPage = RetentionPolicy.KeepPerPage;

    /// <summary>Retention window used when <c>SiteSettings</c> does not name one.</summary>
    public const int DefaultRetentionDays = RetentionPolicy.DefaultRetentionDays;

    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<PageVersionSummary>>> ListAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<PageVersionSummary>>.Forbidden(
                "Reading pages is not permitted.",
                PageCodes.Forbidden);
        }

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentRead, pageId, cancellationToken))
        {
            return CmsResult<IReadOnlyList<PageVersionSummary>>.NotFound(
                $"No page has id {pageId}.",
                PageCodes.NotFound);
        }

        // IgnoreQueryFilters, because the recycle bin lists the history of a deleted page and the
        // filter on Page would otherwise hide the very rows a restore has to show.
        var page = await context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(candidate => candidate.Id == pageId)
            .Select(candidate => new { candidate.DraftVersionId, candidate.PublishedVersionId })
            .FirstOrDefaultAsync(cancellationToken);

        if (page is null)
        {
            return CmsResult<IReadOnlyList<PageVersionSummary>>.NotFound(
                $"No page has id {pageId}.",
                PageCodes.NotFound);
        }

        var versions = await context.PageVersions
            .AsNoTracking()
            .Where(version => version.PageId == pageId)
            .OrderByDescending(version => version.VersionNumber)
            .ToListAsync(cancellationToken);

        var summaries = versions
            .Select(version => ToSummary(version, page.DraftVersionId, page.PublishedVersionId))
            .ToList();

        return CmsResult<IReadOnlyList<PageVersionSummary>>.Success(summaries);
    }

    /// <inheritdoc />
    public async Task<CmsResult<PageVersionDetail>> GetAsync(
        int pageId,
        int versionId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<PageVersionDetail>.Forbidden(
                "Reading pages is not permitted.",
                PageCodes.Forbidden);
        }

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentRead, pageId, cancellationToken))
        {
            return CmsResult<PageVersionDetail>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);
        }

        var page = await context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(candidate => candidate.Id == pageId)
            .Select(candidate => new { candidate.DraftVersionId, candidate.PublishedVersionId })
            .FirstOrDefaultAsync(cancellationToken);

        if (page is null)
        {
            return CmsResult<PageVersionDetail>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);
        }

        var version = await context.PageVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == versionId && candidate.PageId == pageId,
                cancellationToken);

        // A version id belonging to another page is reported as not found rather than as a mismatch:
        // the pair is the address, and answering otherwise confirms a row the caller did not ask for.
        if (version is null)
        {
            return CmsResult<PageVersionDetail>.NotFound(
                $"Page {pageId} has no version with id {versionId}.",
                PageCodes.VersionNotFound);
        }

        return CmsResult<PageVersionDetail>.Success(new PageVersionDetail(
            ToSummary(version, page.DraftVersionId, page.PublishedVersionId),
            version.ContentJson,
            ReadSeo(version)));
    }

    /// <inheritdoc />
    public async Task<CmsResult<DraftState>> RestoreAsync(
        int pageId,
        int versionId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return CmsResult<DraftState>.Forbidden("Editing pages is not permitted.", PageCodes.Forbidden);
        }

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentEdit, pageId, cancellationToken))
        {
            return CmsResult<DraftState>.Forbidden(
                $"Editing page {pageId} is not permitted.",
                PageCodes.Forbidden);
        }

        var page = await context.Pages
            .Include(candidate => candidate.DraftVersion)
            .FirstOrDefaultAsync(candidate => candidate.Id == pageId, cancellationToken);

        if (page?.DraftVersion is null)
        {
            return CmsResult<DraftState>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);
        }

        var source = await context.PageVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == versionId && candidate.PageId == pageId,
                cancellationToken);

        if (source is null)
        {
            return CmsResult<DraftState>.NotFound(
                $"Page {pageId} has no version with id {versionId}.",
                PageCodes.VersionNotFound);
        }

        var draft = page.DraftVersion;

        if (draft.Id == versionId)
        {
            return CmsResult<DraftState>.Invalid(
                PageCodes.VersionNotFound,
                "That version is the draft, so restoring it would do nothing.");
        }

        // Copied into the draft's own row, which keeps its identity, its version number, and its
        // row version. Nothing about the published version changes (acceptance criterion P2 #7).
        draft.ContentJson = source.ContentJson;
        draft.Title = source.Title;
        draft.TemplateId = source.TemplateId;
        draft.TemplateRevision = source.TemplateRevision;
        DraftService.CopySeo(source, draft);

        if (ContentPayload.TryParse(draft.ContentJson, out var payload))
        {
            await references.ProjectAsync(
                ContentSourceType.PageVersion,
                draft.Id,
                payload,
                cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Version {VersionNumber} of page {PageId} was copied into the draft.",
            source.VersionNumber,
            pageId);

        return CmsResult<DraftState>.Success(new DraftState(
            draft.PageId,
            draft.Id,
            draft.VersionNumber,
            draft.ContentJson,
            ContentPayload.TryParse(draft.ContentJson, out var parsed) ? parsed.TemplateKey ?? string.Empty : string.Empty,
            draft.TemplateRevision,
            Convert.ToBase64String(draft.RowVersion ?? []),
            draft.ModifiedOn ?? draft.CreatedOn));
    }

    /// <inheritdoc />
    public async Task<RetentionSweepResult> PruneAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = await context.SiteSettings
            .AsNoTracking()
            .Select(settings => (int?)settings.VersionRetentionDays)
            .FirstOrDefaultAsync(cancellationToken);

        var cutoff = RetentionPolicy.CutoffFrom(clock.GetUtcNow(), retentionDays);

        // Deleted pages are excluded outright rather than filtered later: a page in the recycle bin
        // keeps every version it had, because a restore that came back with no history is not one
        // (spec section 11.7).
        var pages = await context.Pages
            .AsNoTracking()
            .Select(page => new { page.Id, page.DraftVersionId, page.PublishedVersionId })
            .ToListAsync(cancellationToken);

        var removed = new List<int>();

        foreach (var page in pages)
        {
            var versions = await context.PageVersions
                .AsNoTracking()
                .Where(version => version.PageId == page.Id)
                .OrderByDescending(version => version.VersionNumber)
                .Select(version => new
                {
                    version.Id,
                    version.VersionNumber,
                    version.Status,
                    version.Label,
                    version.PublishedOn,
                    version.CreatedOn,
                })
                .ToListAsync(cancellationToken);

            var rank = 0;

            foreach (var version in versions)
            {
                rank++;

                // Which clause spared a version is decided by RetentionPolicy, so the rules can be
                // asserted one at a time without arranging ninety days of history first (P2-24).
                var candidate = new RetentionCandidate(
                    version.Id,
                    rank,
                    version.Status,
                    version.Label,
                    version.PublishedOn,
                    version.CreatedOn,
                    version.Id == page.DraftVersionId || version.Id == page.PublishedVersionId);

                if (RetentionPolicy.Decide(candidate, cutoff) is RetentionReason.Prunable)
                {
                    removed.Add(version.Id);
                }
            }
        }

        if (removed.Count > 0)
        {
            // The reference rows go with them. They are derived data keyed on the version, and a row
            // pointing at a version that no longer exists makes every delete guard permanently
            // cautious about a page nothing actually references.
            await context.ContentReferences
                .Where(row => row.SourceType == ContentSourceType.PageVersion &&
                    removed.Contains(row.SourceVersionId))
                .ExecuteDeleteAsync(cancellationToken);

            await context.PageVersions
                .Where(version => removed.Contains(version.Id))
                .ExecuteDeleteAsync(cancellationToken);

            logger.LogInformation(
                "Retention pruned {Count} page versions older than {Cutoff:u}.",
                removed.Count,
                cutoff);
        }

        return new RetentionSweepResult(pages.Count, removed.Count, removed);
    }

    private static PageVersionSummary ToSummary(PageVersion version, int? draftId, int? publishedId) =>
        new(
            version.Id,
            version.VersionNumber,
            version.Status.ToString(),
            version.Label,
            version.Title,
            version.TemplateRevision,
            version.Id == draftId,
            version.Id == publishedId,
            version.CreatedOn,
            version.CreatedBy,
            version.PublishedOn,
            version.PublishedBy);

    private static PageSeo ReadSeo(PageVersion version) =>
        new(
            version.MetaTitle,
            version.MetaDescription,
            version.CanonicalUrl,
            version.RobotsIndex,
            version.RobotsFollow,
            version.OgTitle,
            version.OgDescription,
            version.OgImageMediaId,
            version.OgType,
            version.TwitterCard,
            version.StructuredDataJson,
            version.ChangeFreq,
            version.Priority);
}
