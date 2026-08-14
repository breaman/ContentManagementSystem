using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Publishing;

/// <summary>
/// Takes a draft live, and takes it back down again (task P2-11, spec section 5.5).
/// </summary>
/// <remarks>
/// Publishing <em>snapshots</em> the draft into a new immutable row rather than promoting the draft
/// row itself. That is the whole mechanism: the draft survives the publish and stays editable, and
/// nothing an editor does afterwards can reach the row delivery is reading
/// (acceptance criterion P2 #4).
/// <para>
/// <strong>Every step is one transaction.</strong> A publish that inserted the new version and then
/// failed to repoint the page would leave a version marked <c>Published</c> that nobody is serving;
/// one that repointed the page and then failed to write the reference rows would leave a live page
/// invisible to cache invalidation. Neither state has a repair path that does not begin with someone
/// noticing (risk R4).
/// </para>
/// </remarks>
public interface IPublishingService
{
    /// <summary>
    /// Runs the publish checks without publishing.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>What a publish would find, or a not-found result.</returns>
    Task<CmsResult<PublishValidation>> ValidateAsync(
        int pageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the current draft.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="acknowledgeWarnings">
    /// Whether the caller has seen the non-blocking diagnostics and still wants to proceed. False
    /// turns a warning into a refusal carrying it, so an unattended client cannot publish past a
    /// problem a person would have looked at (spec section 14.6).
    /// </param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// What the publish did, or an invalid result carrying every problem that stopped it.
    /// </returns>
    Task<CmsResult<PublishResult>> PublishAsync(
        int pageId,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a page from the public site.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The version that was retired, or an invalid result when nothing was live.</returns>
    /// <remarks>
    /// The version is archived, not deleted: the draft is untouched, the history keeps every row,
    /// and re-publishing is an ordinary publish rather than an undo.
    /// </remarks>
    Task<CmsResult<int>> UnpublishAsync(int pageId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IPublishingService" />
/// <param name="context">The application database context.</param>
/// <param name="validator">Checks a payload against the schema it was authored against.</param>
/// <param name="references">Rewrites the published version's reference rows from its payload.</param>
/// <param name="indexer">Walks a payload to check the entities it points at still exist.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="users">Identity of the caller, recorded on the published version.</param>
/// <param name="clock">Source of the current time.</param>
/// <param name="logger">Log for every publish and every failure to publish.</param>
public sealed class PublishingService(
    ApplicationDbContext context,
    IContentSchemaValidator validator,
    IContentReferenceProjector references,
    IReferenceIndexer indexer,
    ICmsAuthorization authorization,
    IUserService users,
    TimeProvider clock,
    ILogger<PublishingService> logger) : IPublishingService
{
    /// <inheritdoc />
    public async Task<CmsResult<PublishValidation>> ValidateAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<PublishValidation>.Forbidden(
                "Reading pages is not permitted.",
                PageCodes.Forbidden);
        }

        var page = await LoadAsync(pageId, tracked: false, cancellationToken);

        if (page?.DraftVersion is null)
        {
            return CmsResult<PublishValidation>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);
        }

        var checks = await CheckAsync(page, page.DraftVersion, cancellationToken);

        return CmsResult<PublishValidation>.Success(new PublishValidation(
            !checks.HasErrors,
            ApiDiagnostics.Project(checks, ValidationSeverity.Error),
            ApiDiagnostics.Project(checks, ValidationSeverity.Warning)));
    }

    /// <inheritdoc />
    public async Task<CmsResult<PublishResult>> PublishAsync(
        int pageId,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return CmsResult<PublishResult>.Forbidden("Publishing is not permitted.", PageCodes.Forbidden);
        }

        var page = await LoadAsync(pageId, tracked: true, cancellationToken);

        if (page?.DraftVersion is null)
        {
            return CmsResult<PublishResult>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);
        }

        var draft = page.DraftVersion;
        var checks = await CheckAsync(page, draft, cancellationToken);

        if (checks.HasErrors)
        {
            logger.LogInformation(
                "Publishing page {PageId} was refused by {Count} validation errors.",
                pageId,
                checks.Diagnostics.Count(diagnostic => diagnostic.Severity is ValidationSeverity.Error));

            return CmsResult<PublishResult>.Invalid(checks);
        }

        if (!acknowledgeWarnings && !checks.IsValid)
        {
            return CmsResult<PublishResult>.Invalid(checks);
        }

        if (!ContentPayload.TryParse(draft.ContentJson, out var payload))
        {
            return CmsResult<PublishResult>.Invalid(
                PageCodes.MalformedPayload,
                "The draft's stored content is not a well-formed payload document.");
        }

        var result = await CommitAsync(page, draft, payload, checks, cancellationToken);

        logger.LogInformation(
            "Page {PageId} published as version {VersionNumber}, superseding {Archived}, with " +
            "{ReferenceCount} reference rows.",
            pageId,
            result.VersionNumber,
            result.ArchivedVersionNumber?.ToString() ?? "nothing",
            result.ReferenceCount);

        return CmsResult<PublishResult>.Success(result, checks);
    }

    /// <inheritdoc />
    public async Task<CmsResult<int>> UnpublishAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return CmsResult<int>.Forbidden("Publishing is not permitted.", PageCodes.Forbidden);
        }

        var page = await LoadAsync(pageId, tracked: true, cancellationToken);

        if (page is null) return CmsResult<int>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);

        if (page.PublishedVersion is null)
        {
            return CmsResult<int>.Invalid(
                PageCodes.AlreadyUnpublished,
                "This page is not published, so there is nothing to retire.");
        }

        var retired = page.PublishedVersion;
        retired.Status = PageVersionStatus.Archived;
        page.PublishedVersionId = null;
        page.PublishedVersion = null;

        await context.SaveChangesAsync(cancellationToken);

        // The public route rows and the redirect that replaces them arrive with PageRoute in P3-01.
        // Until then a page's URL is derived from the tree at request time, so retiring the pointer
        // is the whole of what "unpublished" means and delivery has nothing left to find.
        logger.LogInformation(
            "Page {PageId} was unpublished; version {VersionNumber} is archived.",
            pageId,
            retired.VersionNumber);

        return CmsResult<int>.Success(retired.VersionNumber);
    }

    /// <summary>
    /// Writes the publish, all of it or none of it.
    /// </summary>
    /// <remarks>
    /// Four steps, each its own <c>SaveChangesAsync</c> inside one explicit transaction. Batching
    /// them into a single call would be marginally faster and would make the failure modes
    /// indistinguishable; keeping them apart is what lets <c>P2-12</c> force a failure at each step
    /// and assert that the whole thing rolls back.
    /// <para>
    /// Wrapped in the execution strategy because Aspire enables connection retries, and a manual
    /// transaction without one throws the moment a connection blips. The change tracker is cleared
    /// on entry for the same reason it is in <c>PageService</c>: a retry re-runs the lambda, and the
    /// failed attempt's entities would otherwise be written twice.
    /// </para>
    /// </remarks>
    private async Task<PublishResult> CommitAsync(
        Page page,
        PageVersion draft,
        ContentPayload payload,
        ValidationResult checks,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var now = clock.GetUtcNow();
            var previous = page.PublishedVersion;

            // Step 1 — snapshot the draft into a new immutable row. Copied rather than promoted:
            // promoting would make the live row the one an editor keeps typing into.
            var published = DraftService.Copy(draft, await NextVersionNumberAsync(page.Id, cancellationToken));
            published.Status = PageVersionStatus.Published;
            published.PublishedOn = now;
            published.PublishedBy = users.UserId;
            published.Label = null;

            context.PageVersions.Add(published);
            await context.SaveChangesAsync(cancellationToken);

            // Step 2 — retire the version this one supersedes and repoint the page at the new one.
            if (previous is not null)
            {
                previous.Status = PageVersionStatus.Archived;
            }

            page.PublishedVersionId = published.Id;
            await context.SaveChangesAsync(cancellationToken);

            // Step 3 — project the reference rows for the version that is now live. Cache
            // invalidation, where-used, and the delete guards all read these, and a live version
            // with none of them is stale content waiting to happen (spec section 7.3).
            var referenceCount = await references.ProjectAsync(
                ContentSourceType.PageVersion,
                published.Id,
                payload,
                cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            // Step 4 — the outbox row that drives cache invalidation belongs here (spec section 5.5).
            // OutboxMessage arrives with the caching work in P8; until then delivery reads through
            // no cache, so there is nothing yet to invalidate. The step is kept as its own point in
            // the sequence so adding it is an insertion rather than a restructuring.
            await context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new PublishResult(
                page.Id,
                published.Id,
                published.VersionNumber,
                now,
                previous?.VersionNumber,
                referenceCount,
                ApiDiagnostics.Project(checks, ValidationSeverity.Warning));
        });
    }

    /// <summary>
    /// Runs every check a publish has to pass.
    /// </summary>
    /// <remarks>
    /// Shared by the dry run and the real thing, so the two cannot disagree — and the direction they
    /// would disagree in is a green check followed by a refused publish, which is the version an
    /// editor reports as a bug.
    /// </remarks>
    private async Task<ValidationResult> CheckAsync(
        Page page,
        PageVersion draft,
        CancellationToken cancellationToken)
    {
        if (!ContentPayload.TryParse(draft.ContentJson, out var payload))
        {
            return ValidationResult.Error(
                PageCodes.MalformedPayload,
                "The draft's stored content is not a well-formed payload document.");
        }

        var report = await validator.ValidateAsync(payload, ValidationMode.Publish, cancellationToken);
        var diagnostics = new List<ValidationDiagnostic>(
            DraftService.ToValidationResult(report).Diagnostics);

        diagnostics.AddRange(await CheckReferencedPagesAsync(payload, cancellationToken));

        if (!page.Template.IsEnabled)
        {
            diagnostics.Add(new ValidationDiagnostic(
                PageCodes.TemplateDisabled,
                $"Template '{page.Template.Key}' is disabled, so pages using it cannot be published.",
                ValidationSeverity.Error));
        }

        return ValidationResult.From(diagnostics);
    }

    /// <summary>
    /// Checks that every page this content links to still exists and is not in the recycle bin.
    /// </summary>
    /// <remarks>
    /// The link-integrity half of spec section 5.5. Media and reusable content are checked the same
    /// way once those tables exist (P5 and P4); their references are extracted already, so this
    /// becomes two more queries rather than a new mechanism.
    /// <para>
    /// A link to a page that exists but is not itself published is a <em>warning</em>, not an error.
    /// Publishing a section top-down is ordinary work, and refusing it would mean an editor could
    /// never publish a landing page before the pages it links to.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<ValidationDiagnostic>> CheckReferencedPagesAsync(
        ContentPayload payload,
        CancellationToken cancellationToken)
    {
        var targets = new HashSet<int>();

        foreach (var reference in indexer.Extract(payload))
        {
            if (reference.TargetType is ContentReferenceTargetType.Page)
            {
                targets.Add(reference.TargetId);
            }
        }

        if (targets.Count == 0) return [];

        var live = await context.Pages
            .AsNoTracking()
            .Where(candidate => targets.Contains(candidate.Id))
            .Select(candidate => new { candidate.Id, IsPublished = candidate.PublishedVersionId != null })
            .ToListAsync(cancellationToken);

        var diagnostics = new List<ValidationDiagnostic>();

        foreach (var target in targets.Order())
        {
            var match = live.FirstOrDefault(candidate => candidate.Id == target);

            if (match is null)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    PageCodes.NotFound,
                    $"This content links to page {target}, which no longer exists or is in the " +
                    "recycle bin.",
                    ValidationSeverity.Error));
            }
            else if (!match.IsPublished)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    PageCodes.NothingPublished,
                    $"This content links to page {target}, which is not published yet. The link " +
                    "will not resolve until it is.",
                    ValidationSeverity.Warning));
            }
        }

        return diagnostics;
    }

    private async Task<Page?> LoadAsync(int pageId, bool tracked, CancellationToken cancellationToken)
    {
        var query = context.Pages
            .Include(page => page.Template)
            .Include(page => page.DraftVersion)
            .Include(page => page.PublishedVersion)
            .AsQueryable();

        if (!tracked) query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(page => page.Id == pageId, cancellationToken);
    }

    private async Task<int> NextVersionNumberAsync(int pageId, CancellationToken cancellationToken) =>
        (await context.PageVersions
            .Where(version => version.PageId == pageId)
            .Select(version => (int?)version.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;
}
