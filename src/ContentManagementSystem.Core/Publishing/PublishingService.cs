using System.Diagnostics;

using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Routing;
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
/// <param name="schemas">Supplies the property configuration the allowed-templates check reads.</param>
/// <param name="urls">Materializes the public route a publish creates and withdraws it on unpublish.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="users">Identity of the caller, recorded on the published version.</param>
/// <param name="clock">Source of the current time.</param>
/// <param name="metrics">Counter and histogram of publish attempts (spec section 24.1).</param>
/// <param name="logger">Log for every publish and every failure to publish.</param>
public sealed class PublishingService(
    ApplicationDbContext context,
    IContentSchemaValidator validator,
    IContentReferenceProjector references,
    IReferenceIndexer indexer,
    IContentSchemaCatalog schemas,
    IUrlService urls,
    ICmsAuthorization authorization,
    IUserService users,
    TimeProvider clock,
    CmsMetrics metrics,
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
    /// <remarks>
    /// Wrapped in the span and the two instruments of spec section 24.1. The measurement is taken in
    /// a <c>finally</c> so a publish that threw is recorded as <c>failed</c> rather than not recorded
    /// at all: an operation that vanishes from the counter when it breaks is worse than no counter,
    /// because the graph stays flat and healthy while publishing is down.
    /// </remarks>
    public async Task<CmsResult<PublishResult>> PublishAsync(
        int pageId,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = CmsTelemetry.Source.StartActivity(
            CmsTelemetry.PublishActivityName,
            ActivityKind.Internal);

        activity?.SetTag(CmsTelemetry.PageIdTag, pageId);

        var started = Stopwatch.GetTimestamp();
        var outcome = CmsTelemetry.PublishResults.Failed;

        try
        {
            var result = await PublishCoreAsync(pageId, acknowledgeWarnings, cancellationToken);

            outcome = Outcome(result);

            activity?.SetTag(CmsMetrics.ResultTag, outcome);

            if (result.IsSuccess)
            {
                activity?.SetTag(CmsTelemetry.VersionNumberTag, result.Value!.VersionNumber);
            }
            else
            {
                // A refusal is an error for the span even though it is an ordinary outcome for the
                // editor: a trace is read to find out why a request did not do what was asked.
                activity?.SetStatus(ActivityStatusCode.Error, outcome);
            }

            return result;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

            throw;
        }
        finally
        {
            metrics.RecordPublish(outcome, Stopwatch.GetElapsedTime(started));
        }
    }

    /// <summary>How a finished attempt is tagged (spec section 24.1).</summary>
    private static string Outcome(CmsResult<PublishResult> result) => result.Outcome switch
    {
        CmsOutcome.Success => CmsTelemetry.PublishResults.Published,
        CmsOutcome.Forbidden => CmsTelemetry.PublishResults.Forbidden,
        CmsOutcome.NotFound => CmsTelemetry.PublishResults.NotFound,
        _ => CmsTelemetry.PublishResults.Refused,
    };

    private async Task<CmsResult<PublishResult>> PublishCoreAsync(
        int pageId,
        bool acknowledgeWarnings,
        CancellationToken cancellationToken)
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

        // The published routes of this page and every descendant go with it, in the same save. No
        // redirect is left behind: an unpublished page has not moved anywhere, and the URL becoming
        // a 404 is what puts it in the NotFoundLog report where somebody decides what should
        // actually happen to it (spec section 10.6).
        var withdrawn = await urls.WithdrawAsync(pageId, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Page {PageId} was unpublished; version {VersionNumber} is archived and {UrlCount} URL(s) withdrawn.",
            pageId,
            retired.VersionNumber,
            withdrawn.Count);

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
            var published = DraftService.Copy(
                draft,
                await VersionNumbers.NextAsync(context, page.Id, cancellationToken));
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

            // Step 2b — materialize the public route. The page now has a published version, so the
            // rebuild adds the row the filtered unique index governs; before this statement the page
            // had only its draft route and was unreachable anonymously (spec section 10.4).
            //
            // A collision here is refused by throwing rather than returned, which is deliberate: the
            // publish checks in CheckAsync already asked whether the URL was free, so reaching this
            // with a taken URL means somebody published the other page in between. Rolling the
            // transaction back is the only correct answer, and a caught DbUpdateException would say
            // less about why.
            var sync = await urls.SyncAsync(page.Id, cancellationToken);

            if (sync.HasErrors)
            {
                throw new InvalidOperationException(
                    "Publishing was rolled back because the page's URL is served by another page: " +
                    string.Join(" ", sync.Diagnostics.Diagnostics.Select(diagnostic => diagnostic.Message)));
            }

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
        diagnostics.AddRange(await CheckUrlAvailableAsync(page, cancellationToken));

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
    /// Checks that no other published page already serves the URL this page would take.
    /// </summary>
    /// <remarks>
    /// Asked here, on the shared check path, so the dry run reports it rather than letting an editor
    /// discover it as a failed publish. The filtered unique index is still the guarantee — this is a
    /// question about a moment, and two publishes racing can both pass it — but a check that catches
    /// it ninety-nine times out of a hundred and names the offending page is worth the query.
    /// </remarks>
    private async Task<IReadOnlyList<ValidationDiagnostic>> CheckUrlAvailableAsync(
        Page page,
        CancellationToken cancellationToken)
    {
        var url = await urls.ComputeAsync(page.Id, cancellationToken);

        if (url is null) return [];

        var hash = SiteUrls.Hash(url);

        var holder = await context.PageRoutes
            .AsNoTracking()
            .Where(route => route.IsPublished && route.UrlHash == hash && route.PageId != page.Id)
            .Select(route => (int?)route.PageId)
            .FirstOrDefaultAsync(cancellationToken);

        return holder is null
            ? []
            : [
                new ValidationDiagnostic(
                    RoutingCodes.UrlTaken,
                    $"Page {holder} is already published at '{url}'. Change this page's slug, or " +
                    "unpublish the other page first.",
                    ValidationSeverity.Error),
            ];
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
        var references = indexer.Extract(payload)
            .Where(reference => reference.TargetType is ContentReferenceTargetType.Page)
            .ToList();

        var targets = references.Select(reference => reference.TargetId).ToHashSet();

        if (targets.Count == 0) return [];

        var live = await context.Pages
            .AsNoTracking()
            .Where(candidate => targets.Contains(candidate.Id))
            .Select(candidate => new
            {
                candidate.Id,
                IsPublished = candidate.PublishedVersionId != null,
                TemplateKey = candidate.Template.Key,
            })
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

        diagnostics.AddRange(CheckAllowedTemplates(payload, references, live.ToDictionary(
            row => row.Id,
            row => row.TemplateKey)));

        return diagnostics;
    }

    /// <summary>
    /// Checks each page reference against the <c>allowedTemplates</c> its property declares.
    /// </summary>
    /// <remarks>
    /// Enforced here rather than inside <c>PageReferenceFieldType</c> for a structural reason: a
    /// field type is a stateless singleton with no database, and the question "what template does
    /// page 44 use" cannot be answered from the stored value alone (spec section 7). This is the
    /// same seam that checks a link target still exists.
    /// <para>
    /// An error rather than a warning. A property restricted to article templates and filled with a
    /// landing page renders through a component that was written for the other shape, and the
    /// failure surfaces on the public site rather than here.
    /// </para>
    /// </remarks>
    private IReadOnlyList<ValidationDiagnostic> CheckAllowedTemplates(
        ContentPayload payload,
        IReadOnlyList<Shared.Contracts.Fields.ContentReference> references,
        Dictionary<int, string> templateKeysByPageId)
    {
        // A payload with no template key or no captured revision has already been reported by the
        // schema walk, which refuses an unknown revision outright — there is no schema to read a
        // configuration out of, and a second diagnostic about it would say nothing further.
        if (payload.TemplateKey is not { } templateKey ||
            payload.TemplateRevision is not { } revision ||
            !schemas.TryGetTemplate(templateKey, revision, out var schema))
        {
            // The walk already reported the unknown revision as an error of its own; adding a second
            // complaint about a schema nobody can load says nothing further.
            return [];
        }

        var diagnostics = new List<ValidationDiagnostic>();

        foreach (var reference in references)
        {
            var location = ReferencePath.Parse(reference.Path, payload);

            if (location.ZoneKey is not { } zoneKey) continue;

            // Only zone-level references are checked. A reference inside a block belongs to a block
            // type property, and reaching its schema means resolving the block's own captured
            // revision — which is P4's business, since that is where nested structure is walked for
            // anything other than validation.
            if (location.BlockId is not null) continue;

            if (schema.FindZone(zoneKey) is not { } property) continue;

            var allowed = property.Configuration.GetStringArray("allowedTemplates");

            if (allowed.Length == 0) continue;

            if (!templateKeysByPageId.TryGetValue(reference.TargetId, out var targetTemplate)) continue;

            if (Array.IndexOf(allowed, targetTemplate) >= 0) continue;

            diagnostics.Add(new ValidationDiagnostic(
                FieldValidationCodes.NotAllowed,
                $"'{property.Name}' accepts pages using {string.Join(", ", allowed)}, but page " +
                $"{reference.TargetId} uses '{templateKey}'.",
                ValidationSeverity.Error,
                reference.Path));
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
}
