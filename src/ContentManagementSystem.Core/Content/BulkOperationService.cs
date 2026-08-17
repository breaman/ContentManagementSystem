using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Runs one editorial operation over many pages (task P6-29, spec section 14.11).
/// </summary>
/// <remarks>
/// At real content volumes per-item actions are not enough, and the naive alternative — a screen
/// that fires forty requests — fails in three ways at once: it ties up the browser, it reports one
/// aggregate outcome for forty different answers, and it publishes pages in whatever order the
/// responses come back.
/// <para>
/// <strong>Nothing here reimplements an operation.</strong> Each item runs through the same
/// <c>IPublishingService</c>, <c>IRecycleBinService</c>, or <c>IPageService</c> a single-item request
/// runs through, in a scope of its own. That is what makes a bulk publish subject to the same
/// validation, the same permission checks, and the same audit rows as forty individual publishes —
/// and it is why a batch cannot quietly become a way around a rule.
/// </para>
/// </remarks>
public interface IBulkOperationService
{
    /// <summary>
    /// Reports what an operation would run over, without running any of it.
    /// </summary>
    /// <param name="request">The operation and the selection.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The resolved selection, or a refusal.</returns>
    /// <remarks>
    /// The number worth confirming is rarely the number an editor selected: a branch publish of three
    /// sections is forty-one pages, and a delete of one is its whole subtree. Resolving that
    /// server-side, by the same code the run uses, is what stops the confirmation and the consequence
    /// disagreeing (the rule task P6-03 set for the move dialog).
    /// </remarks>
    Task<CmsResult<BulkImpact>> DescribeAsync(
        BulkOperationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts an operation over a selection.
    /// </summary>
    /// <param name="request">The operation and the selection.</param>
    /// <param name="cancellationToken">
    /// Token observed while the selection is resolved, and while a small batch runs inline. A batch
    /// large enough to run in the background deliberately stops observing it: the token is the
    /// request's, and the request ends the moment the job is accepted.
    /// </param>
    /// <returns>
    /// The job — finished, for a batch small enough to have run inside the request, and running for
    /// one that was not.
    /// </returns>
    Task<CmsResult<BulkJobStatus>> StartAsync(
        BulkOperationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reports where a job has got to.</summary>
    /// <param name="jobId">Identity of the job.</param>
    /// <returns>The job's progress and per-item results, or a not-found result.</returns>
    CmsResult<BulkJobStatus> Get(Guid jobId);
}

/// <inheritdoc cref="IBulkOperationService" />
/// <param name="context">The application database context, used to resolve the selection.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="jobs">Where a job's progress and per-item results are held.</param>
/// <param name="scopes">Carries the caller's identity into the background.</param>
/// <param name="clock">Source of the current time.</param>
/// <param name="logger">Log for every batch and every item that failed inside one.</param>
public sealed class BulkOperationService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    BulkOperationJobs jobs,
    IBulkOperationScopeFactory scopes,
    TimeProvider clock,
    ILogger<BulkOperationService> logger) : IBulkOperationService
{
    /// <summary>
    /// Above this many items, a batch runs as a background job rather than inside the request.
    /// </summary>
    /// <remarks>
    /// Spec section 14.11's number, shared with the client through <see cref="BulkLimits"/>. It is a
    /// count rather than a duration because the caller has to decide whether to poll <em>before</em>
    /// the work starts, and "how long will 30 publishes take" is not knowable then.
    /// </remarks>
    public const int BackgroundThreshold = BulkLimits.BackgroundThreshold;

    /// <summary>The largest selection one job will accept.</summary>
    /// <remarks>
    /// A ceiling rather than a paging scheme: past a few hundred pages the request is almost always a
    /// filter somebody meant to narrow, and a job that quietly accepted "every page on the site" would
    /// be discovered by its side effects.
    /// </remarks>
    public const int MaxSelection = BulkLimits.MaxSelection;

    /// <inheritdoc />
    public async Task<CmsResult<BulkImpact>> DescribeAsync(
        BulkOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (RequiredPermission(request.Operation) is { } permission &&
            !authorization.HasPermission(permission))
        {
            return CmsResult<BulkImpact>.Forbidden(Refusal(request.Operation), PageCodes.Forbidden);
        }

        var resolution = await ResolveAsync(request, cancellationToken);

        if (resolution.Refusal is { } refused) return CmsResult<BulkImpact>.Invalid(refused);

        return CmsResult<BulkImpact>.Success(new BulkImpact(
            request.Operation,
            resolution.Items,
            resolution.SelectedCount,
            resolution.Items.Count(item => item.IsPublished),
            RunsInBackground(request.Operation, resolution.Items),
            ApiDiagnostics.Project(
                ValidationResult.From(resolution.Warnings),
                ValidationSeverity.Warning)));
    }

    /// <inheritdoc />
    public async Task<CmsResult<BulkJobStatus>> StartAsync(
        BulkOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (RequiredPermission(request.Operation) is { } permission &&
            !authorization.HasPermission(permission))
        {
            return CmsResult<BulkJobStatus>.Forbidden(Refusal(request.Operation), PageCodes.Forbidden);
        }

        var resolution = await ResolveAsync(request, cancellationToken);

        if (resolution.Refusal is { } refused) return CmsResult<BulkJobStatus>.Invalid(refused);

        // Only the items the operation actually applies to. A delete is subtree-aware already, so its
        // descendants are shown in the preview and never queued as items of their own — queueing them
        // would have item two through forty ask the recycle bin to delete pages item one has already
        // taken, and report forty "no such page" failures for a batch that worked perfectly.
        var items = Executable(request.Operation, resolution.Items);

        if (items.Count == 0)
        {
            return CmsResult<BulkJobStatus>.Invalid(
                PageCodes.SelectionEmpty,
                "Nothing in that selection still exists.",
                nameof(BulkOperationRequest.Selection));
        }

        var job = jobs.Start(request.Operation, items.Count, clock.GetUtcNow());

        // Captured here, on the request thread, while there is still a caller to capture. Everything
        // an item does authorizes that caller and stamps their identity on its audit rows.
        var caller = scopes.CaptureCaller();

        logger.LogInformation(
            "Bulk {Operation} job {JobId} accepted over {ItemCount} page(s), running {Mode}.",
            request.Operation,
            job.Id,
            items.Count,
            items.Count > BackgroundThreshold ? "in the background" : "inline");

        if (items.Count <= BackgroundThreshold)
        {
            await RunAsync(job, request, items, caller, cancellationToken);

            return CmsResult<BulkJobStatus>.Success(job.Snapshot());
        }

        // Deliberately not awaited, and deliberately not given the request's token: the request is
        // about to end, and cancelling the batch when its response is written is precisely the
        // behaviour spec section 14.11 asks to avoid. Faults are caught inside RunAsync and recorded
        // on the job, so nothing escapes to an unobserved task.
        _ = Task.Run(
            () => RunAsync(job, request, items, caller, CancellationToken.None),
            CancellationToken.None);

        return CmsResult<BulkJobStatus>.Success(job.Snapshot());
    }

    /// <inheritdoc />
    public CmsResult<BulkJobStatus> Get(Guid jobId)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<BulkJobStatus>.Forbidden(
                "Reading pages is not permitted.",
                PageCodes.Forbidden);
        }

        return jobs.Find(jobId) is { } job
            ? CmsResult<BulkJobStatus>.Success(job.Snapshot())
            : CmsResult<BulkJobStatus>.NotFound(
                $"No bulk job has id {jobId}. Jobs are kept for the life of the process.",
                PageCodes.JobNotFound);
    }

    /// <summary>
    /// Works through a job's items, recording each outcome as it happens.
    /// </summary>
    /// <remarks>
    /// One item's failure never stops the next one. That is the promise spec section 14.11 makes —
    /// "a partial failure leaves the successful items applied and reports the rest" — and it is only
    /// true because each item is a scope of its own: a failed publish leaves nothing tracked behind
    /// for item eleven's context to save on its behalf.
    /// </remarks>
    private async Task RunAsync(
        BulkJob job,
        BulkOperationRequest request,
        IReadOnlyList<BulkImpactItem> items,
        ICapturedCaller caller,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await caller.RunAsync(
                    async (services, token) =>
                        job.Record(await ApplyAsync(services, request, item, token)),
                    cancellationToken);
            }

            job.Finish(BulkJobState.Completed, clock.GetUtcNow());

            var snapshot = job.Snapshot();

            logger.LogInformation(
                "Bulk {Operation} job {JobId} finished: {Succeeded} succeeded, {Failed} failed.",
                request.Operation,
                job.Id,
                snapshot.Succeeded,
                snapshot.Failed);
        }
        catch (Exception exception)
        {
            // Faulted rather than completed, because the items after this point were never attempted
            // and an editor reading "18 of 40 succeeded" would otherwise have no way to tell that
            // from twenty-two failures with reasons attached.
            job.Finish(BulkJobState.Faulted, clock.GetUtcNow());

            logger.LogError(
                exception,
                "Bulk {Operation} job {JobId} stopped after {Completed} of {Total} item(s).",
                request.Operation,
                job.Id,
                job.Snapshot().Completed,
                items.Count);
        }
    }

    /// <summary>
    /// Applies the operation to one page, through the service that owns it.
    /// </summary>
    /// <remarks>
    /// Every branch resolves its service from the item's own scope rather than closing over one from
    /// the request, which is what makes the database context per item rather than per batch.
    /// </remarks>
    private async Task<BulkItemResult> ApplyAsync(
        IServiceProvider services,
        BulkOperationRequest request,
        BulkImpactItem item,
        CancellationToken cancellationToken)
    {
        ValidationResult diagnostics;

        try
        {
            diagnostics = request.Operation switch
            {
                BulkOperation.Publish => (await services.GetRequiredService<IPublishingService>()
                    .PublishAsync(item.PageId, request.AcknowledgeWarnings, cancellationToken))
                    .Diagnostics,

                BulkOperation.Unpublish => (await services.GetRequiredService<IPublishingService>()
                    .UnpublishAsync(item.PageId, cancellationToken))
                    .Diagnostics,

                BulkOperation.Delete => (await services.GetRequiredService<IRecycleBinService>()
                    .DeleteAsync(item.PageId, cancellationToken))
                    .Diagnostics,

                _ => (await services.GetRequiredService<IPageService>()
                    .PatchMetadataAsync(item.PageId, MetadataPatch(request), null, cancellationToken))
                    .Diagnostics,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An item that threw is this item's failure, not the batch's. The alternative — letting it
            // reach RunAsync — would abandon every page after it over one page's bad data.
            logger.LogError(
                exception,
                "Bulk {Operation} failed on page {PageId}.",
                request.Operation,
                item.PageId);

            diagnostics = ValidationResult.Error(
                PageCodes.ContentInvalid,
                "This page could not be processed. The failure has been logged.");
        }

        var errors = ApiDiagnostics.Project(diagnostics, ValidationSeverity.Error);

        return new BulkItemResult(item.PageId, item.Title, errors.Count == 0, errors);
    }

    /// <summary>Builds the metadata patch for the owner and review-date operations.</summary>
    /// <remarks>
    /// One member set, and only one. Sending the rest would reinstate this request's copy of twenty
    /// fields over whatever somebody else changed in the meantime, which is the failure
    /// <c>Patch&lt;T&gt;</c> exists to prevent and is worse in a batch than in a form.
    /// </remarks>
    private static PatchPageMetadataRequest MetadataPatch(BulkOperationRequest request) =>
        request.Operation is BulkOperation.SetOwner
            ? new PatchPageMetadataRequest { OwnerUserId = new Patch<int?>(request.OwnerUserId) }
            : new PatchPageMetadataRequest { ReviewByDate = new Patch<DateOnly?>(request.ReviewByDate) };

    /// <summary>
    /// Turns a selection into the list of pages an operation would touch.
    /// </summary>
    /// <remarks>
    /// Selected pages keep the order the editor picked them in, and each one's descendants follow it
    /// immediately, shallowest first. That ordering is load-bearing for a branch publish: a child
    /// published before its parent is a live page under an unpublished one, which is a URL that
    /// resolves to a page the site's navigation cannot reach.
    /// </remarks>
    private async Task<Resolution> ResolveAsync(
        BulkOperationRequest request,
        CancellationToken cancellationToken)
    {
        var selected = request.Selection?.PageIds?.Distinct().ToList() ?? [];

        if (selected.Count == 0)
        {
            return Resolution.Refused(ValidationResult.Error(
                PageCodes.SelectionEmpty,
                "Select at least one page.",
                nameof(BulkOperationRequest.Selection)));
        }

        if (selected.Count > MaxSelection)
        {
            return Resolution.Refused(ValidationResult.Error(
                PageCodes.SelectionTooLarge,
                $"A single operation covers at most {MaxSelection} selected pages. " +
                "Narrow the filter and run it again.",
                nameof(BulkOperationRequest.Selection)));
        }

        var roots = await context.Pages
            .AsNoTracking()
            .Where(page => selected.Contains(page.Id))
            .Include(page => page.DraftVersion)
            .ToDictionaryAsync(page => page.Id, cancellationToken);

        var warnings = new List<ValidationDiagnostic>();
        var items = new List<BulkImpactItem>();
        var seen = new HashSet<int>();
        var selectedCount = 0;

        foreach (var id in selected)
        {
            if (!roots.TryGetValue(id, out var page))
            {
                // The ordinary way to reach this is a selection that went stale while the editor read
                // the confirmation. One missing page is not a reason to drop the other thirty-nine.
                warnings.Add(new ValidationDiagnostic(
                    PageCodes.SelectionStale,
                    $"Page {id} no longer exists and has been left out.",
                    ValidationSeverity.Warning));

                continue;
            }

            selectedCount++;

            if (seen.Add(page.Id))
            {
                items.Add(Describe(page, wasSelected: true));
            }

            if (!ExpandsDescendants(request)) continue;

            // One indexed prefix match per selected root, which is the same query the recycle bin and
            // the delete preview walk. A selection of a handful of branch roots is a handful of
            // queries; the alternative — one query with a disjunction over every path — is a plan the
            // database cannot use the index for.
            var descendants = await context.Pages
                .AsNoTracking()
                .Where(candidate => candidate.Id != page.Id && candidate.Path.StartsWith(page.Path))
                .Include(candidate => candidate.DraftVersion)
                .OrderBy(candidate => candidate.Depth)
                .ThenBy(candidate => candidate.SortOrder)
                .ToListAsync(cancellationToken);

            foreach (var descendant in descendants)
            {
                if (seen.Add(descendant.Id))
                {
                    items.Add(Describe(descendant, wasSelected: false));
                }
            }
        }

        return new Resolution(items, selectedCount, warnings, null);
    }

    /// <summary>Whether the resolution walks each selected page's subtree.</summary>
    /// <remarks>
    /// A delete always does, whatever the selection said, because a delete always takes the subtree
    /// with it — showing a count of one for an operation that removes forty pages is the confirmation
    /// acceptance criterion P6 #10 exists to prevent.
    /// </remarks>
    private static bool ExpandsDescendants(BulkOperationRequest request) =>
        request.Operation is BulkOperation.Delete ||
        request.Selection is { IncludeDescendants: true };

    /// <summary>The subset of the resolved pages that are queued as items in their own right.</summary>
    private static IReadOnlyList<BulkImpactItem> Executable(
        BulkOperation operation,
        IReadOnlyList<BulkImpactItem> items) =>
        operation is BulkOperation.Delete
            ? [.. items.Where(item => item.WasSelected)]
            : items;

    /// <summary>Whether a batch of this size runs in the background.</summary>
    private static bool RunsInBackground(BulkOperation operation, IReadOnlyList<BulkImpactItem> items) =>
        Executable(operation, items).Count > BackgroundThreshold;

    /// <summary>Projects a page row onto the preview's shape.</summary>
    private static BulkImpactItem Describe(Page page, bool wasSelected) =>
        new(page.Id, page.DraftVersion?.Title ?? page.Slug, page.PublishedVersionId is not null, wasSelected);

    /// <summary>What a caller must hold to run this operation.</summary>
    /// <remarks>
    /// The door, not the lock. Every item re-checks the same permission inside the service that owns
    /// it, so an operation reached another way is refused just the same; this is here so a batch of
    /// 400 is refused once rather than 400 times.
    /// </remarks>
    private static string RequiredPermission(BulkOperation operation) => operation switch
    {
        BulkOperation.Publish or BulkOperation.Unpublish => CmsPermissions.ContentPublish,
        BulkOperation.Delete => CmsPermissions.ContentDelete,
        _ => CmsPermissions.ContentEdit,
    };

    /// <summary>How a refusal is phrased, without naming what the caller would have needed.</summary>
    private static string Refusal(BulkOperation operation) => operation switch
    {
        BulkOperation.Publish => "Publishing pages is not permitted.",
        BulkOperation.Unpublish => "Unpublishing pages is not permitted.",
        BulkOperation.Delete => "Deleting pages is not permitted.",
        _ => "Editing pages is not permitted.",
    };

    /// <summary>A resolved selection, or the reason it could not be resolved.</summary>
    /// <param name="Items">Every page the operation would touch.</param>
    /// <param name="SelectedCount">How many of the selected pages still exist.</param>
    /// <param name="Warnings">What was odd about the selection but did not stop it.</param>
    /// <param name="Refusal">Why the selection was refused outright, or null.</param>
    private sealed record Resolution(
        IReadOnlyList<BulkImpactItem> Items,
        int SelectedCount,
        IReadOnlyList<ValidationDiagnostic> Warnings,
        ValidationResult? Refusal)
    {
        /// <summary>A selection that was refused before anything was resolved.</summary>
        public static Resolution Refused(ValidationResult refusal) => new([], 0, [], refusal);
    }
}
