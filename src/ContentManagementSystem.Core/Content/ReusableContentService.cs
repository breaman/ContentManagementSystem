using System.Text.Json;

using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Media.Library;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Search;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Content;

/// <inheritdoc cref="IReusableContentService" />
/// <param name="context">The application database context.</param>
/// <param name="validator">Checks a payload against the schema it was authored against.</param>
/// <param name="schemas">Resolves the block type revision a payload names.</param>
/// <param name="references">Rewrites a version's reference rows from its payload.</param>
/// <param name="indexer">Walks a payload to report the entities it points at.</param>
/// <param name="impact">Answers where an item is used and what a change to it would touch.</param>
/// <param name="media">Checks the pictures this item places — existence, alt text, and the picker settings.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="users">Identity of the caller, recorded on the published version and the audit row.</param>
/// <param name="clock">Source of the current time.</param>
/// <param name="cacheInvalidation">Enqueues cache eviction inside the transaction that earned it.</param>
/// <param name="search">Enqueues the search reindex, in the same transaction (task P8-18).</param>
/// <param name="logger">Log for every publish, delete, and refusal.</param>
public sealed class ReusableContentService(
    ApplicationDbContext context,
    IContentSchemaValidator validator,
    IContentSchemaCatalog schemas,
    IContentReferenceProjector references,
    IReferenceIndexer indexer,
    IReferenceQueryService impact,
    IMediaContentValidator media,
    ICmsAuthorization authorization,
    IUserService users,
    TimeProvider clock,
    ICacheInvalidationQueue cacheInvalidation,
    ISearchIndexQueue search,
    ILogger<ReusableContentService> logger) : IReusableContentService
{
    /// <summary>
    /// The audit <c>Type</c> written for a publish (task P4-12, spec section 9.3).
    /// </summary>
    /// <remarks>
    /// A value of its own rather than one of <c>AuditType</c>'s three, because it records something
    /// the change interceptor structurally cannot: a publish's consequence is on rows it did not
    /// touch. The interceptor will faithfully log a new version row and a changed pointer, and
    /// neither answers "why did 40 pages change at 14:02?".
    /// </remarks>
    public const string PublishAuditType = "ReusablePublish";

    /// <summary>The audit <c>Type</c> written for an unpublish.</summary>
    public const string UnpublishAuditType = "ReusableUnpublish";

    /// <summary>How many levels of nesting the cycle check follows before giving up.</summary>
    /// <remarks>
    /// The same ceiling the resolver renders to and the impact walk counts to. A graph deeper than
    /// this cannot be rendered anyway, so refusing the save is the honest answer rather than
    /// accepting content the delivery path will truncate.
    /// </remarks>
    private const int MaxNestingDepth = ReferenceQueryService.MaxDepth;

    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<ReusableContentSummary>>> ListAsync(
        int? folderId = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden<IReadOnlyList<ReusableContentSummary>>("Reading reusable content is not permitted.");
        }

        var query = context.ReusableContents.AsNoTracking();

        if (folderId is not null) query = query.Where(item => item.FolderId == folderId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var fragment = search.Trim();

            // Matched by the database under its own collation, which is case-insensitive here. Doing
            // it in memory would mean loading the library to filter it.
            query = query.Where(item =>
                EF.Functions.Like(item.Name, $"%{fragment}%") ||
                EF.Functions.Like(item.Key, $"%{fragment}%"));
        }

        var items = await query
            .OrderBy(item => item.Name)
            .Select(Summary())
            .ToListAsync(cancellationToken);

        return CmsResult<IReadOnlyList<ReusableContentSummary>>.Success(items);
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReusableContentDetail>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden<ReusableContentDetail>("Reading reusable content is not permitted.");
        }

        var detail = await LoadDetailAsync(id, cancellationToken);

        return detail is null ? NotFound<ReusableContentDetail>(id) : CmsResult<ReusableContentDetail>.Success(detail);
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReusableContentDetail>> CreateAsync(
        CreateReusableContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return Forbidden<ReusableContentDetail>("Creating reusable content is not permitted.");
        }

        var blockType = await context.BlockTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == request.BlockTypeId, cancellationToken);

        // Invalid rather than not-found: the address of this request is the item collection, and the
        // block type is a value in the body.
        if (blockType is null)
        {
            return CmsResult<ReusableContentDetail>.Invalid(
                ReusableCodes.BlockTypeNotFound,
                $"No block type has id {request.BlockTypeId}.",
                nameof(CreateReusableContentRequest.BlockTypeId));
        }

        var diagnostics = new List<ValidationDiagnostic>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            diagnostics.Add(new ValidationDiagnostic(
                ReusableCodes.NameRequired,
                "A reusable item needs a name; it is how an editor finds it in the picker.",
                ValidationSeverity.Error,
                nameof(CreateReusableContentRequest.Name)));
        }
        else if (request.Name.Trim().Length > FieldLengths.EntityName)
        {
            diagnostics.Add(TooLong(nameof(CreateReusableContentRequest.Name), FieldLengths.EntityName));
        }

        if (request.Description?.Length > FieldLengths.ShortDescription)
        {
            diagnostics.Add(TooLong(
                nameof(CreateReusableContentRequest.Description),
                FieldLengths.ShortDescription));
        }

        // Generated from the name only when none was supplied, and checked the same way either way —
        // a generator whose output its own validator rejects fails for names nobody tried.
        var key = string.IsNullOrWhiteSpace(request.Key)
            ? Slugs.Generate(request.Name)
            : request.Key.Trim();

        foreach (var diagnostic in Structure.ContentKeys
            .Validate(key, nameof(CreateReusableContentRequest.Key))
            .Diagnostics)
        {
            // Re-coded into this vocabulary. The shape rules are the structure services', and the
            // remedy an editor is offered is not: nobody is editing a template here.
            diagnostics.Add(diagnostic with { Code = ReusableCodes.KeyInvalid });
        }

        if (diagnostics.Count > 0) return CmsResult<ReusableContentDetail>.Invalid(ValidationResult.From(diagnostics));

        // IgnoreQueryFilters because the unique index is unfiltered: an item in the recycle bin still
        // owns its key, and letting a second item take it would make the first unrestorable.
        if (await context.ReusableContents
            .IgnoreQueryFilters()
            .AnyAsync(candidate => candidate.Key == key, cancellationToken))
        {
            return CmsResult<ReusableContentDetail>.Invalid(
                ReusableCodes.KeyDuplicate,
                $"Another reusable item already uses the key '{key}', or one in the recycle bin does.",
                nameof(CreateReusableContentRequest.Key));
        }

        var item = await InsertAsync(request, blockType, key, cancellationToken);

        logger.LogInformation(
            "Reusable item {ReusableId} '{Key}' created from block type {BlockTypeKey} revision {Revision}.",
            item.Id,
            key,
            blockType.Key,
            blockType.CurrentRevision);

        var detail = await LoadDetailAsync(item.Id, cancellationToken);

        return detail is null
            ? NotFound<ReusableContentDetail>(item.Id)
            : CmsResult<ReusableContentDetail>.Success(detail);
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReusableContentDetail>> PatchAsync(
        int id,
        PatchReusableContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return Forbidden<ReusableContentDetail>("Editing reusable content is not permitted.");
        }

        var item = await context.ReusableContents
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (item is null) return NotFound<ReusableContentDetail>(id);

        var name = request.Name.Or(item.Name);

        if (string.IsNullOrWhiteSpace(name))
        {
            return CmsResult<ReusableContentDetail>.Invalid(
                ReusableCodes.NameRequired,
                "A reusable item needs a name.",
                nameof(PatchReusableContentRequest.Name));
        }

        if (name.Trim().Length > FieldLengths.EntityName)
        {
            return CmsResult<ReusableContentDetail>.Invalid(ValidationResult.From(
                [TooLong(nameof(PatchReusableContentRequest.Name), FieldLengths.EntityName)]));
        }

        var description = request.Description.Or(item.Description);

        if (description?.Length > FieldLengths.ShortDescription)
        {
            return CmsResult<ReusableContentDetail>.Invalid(ValidationResult.From(
                [TooLong(nameof(PatchReusableContentRequest.Description), FieldLengths.ShortDescription)]));
        }

        item.Name = name.Trim();
        item.Description = description;
        item.FolderId = request.FolderId.Or(item.FolderId);

        if (RowVersions.TryApply(context.Entry(item), request.ExpectedRowVersion) is false)
        {
            return CmsResult<ReusableContentDetail>.Invalid(
                ReusableCodes.ConcurrentChange,
                "The supplied row version is not a value this server issued.",
                nameof(PatchReusableContentRequest.ExpectedRowVersion));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CmsResult<ReusableContentDetail>.Conflict(
                ReusableCodes.ConcurrentChange,
                "This item was changed by someone else after you opened it. Reload and try again.",
                nameof(PatchReusableContentRequest.ExpectedRowVersion));
        }

        var detail = await LoadDetailAsync(id, cancellationToken);

        return detail is null
            ? NotFound<ReusableContentDetail>(id)
            : CmsResult<ReusableContentDetail>.Success(detail);
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReusableDraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return Forbidden<ReusableDraftSaveResult>("Editing reusable content is not permitted.");
        }

        var item = await LoadForWriteAsync(id, cancellationToken);

        if (item?.DraftVersion is null) return NotFound<ReusableDraftSaveResult>(id);

        var draft = item.DraftVersion;

        if (!ContentPayload.TryParse(request.ContentJson, out var payload) || !payload.IsObject)
        {
            return CmsResult<ReusableDraftSaveResult>.Invalid(
                ReusableCodes.MalformedPayload,
                "The content sent is not a well-formed payload document.",
                nameof(SaveDraftRequest.ContentJson));
        }

        if (CheckEnvelope(payload, item, draft) is { } envelopeProblem) return envelopeProblem;

        var revision = payload.TemplateRevision!.Value;
        var schema = ReusableContentSchema.For(schemas, item.BlockType.Key, revision);

        var report = schema is null
            ? ContentValidationReport.Empty
            : await validator.ValidateAsync(payload, schema, ValidationMode.Draft, cancellationToken);

        if (report.HasErrors)
        {
            return CmsResult<ReusableDraftSaveResult>.Invalid(DraftService.ToValidationResult(report));
        }

        // Checked before the write, not after: a cycle that reached the table would be discovered by
        // the renderer, whose only remaining option is to stop part-way down (P4 #7).
        if (await CreatesCycleAsync(id, payload, cancellationToken) is { } cycle)
        {
            return CmsResult<ReusableDraftSaveResult>.Invalid(cycle);
        }

        draft.ContentJson = payload.ToJson();
        draft.BlockTypeRevision = revision;

        var referenceCount = await references.ProjectAsync(
            ContentSourceType.ReusableContentVersion,
            draft.Id,
            payload,
            cancellationToken);

        // Reusable items are searchable in the backoffice like pages are, and from their working
        // content for the same reason (task P8-18).
        search.EnqueueReusable(id);

        if (RowVersions.TryApply(context.Entry(draft), request.ExpectedRowVersion) is false)
        {
            return CmsResult<ReusableDraftSaveResult>.Invalid(
                ReusableCodes.ConcurrentChange,
                "The supplied row version is not a value this server issued.",
                nameof(SaveDraftRequest.ExpectedRowVersion));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("A draft save on reusable item {ReusableId} lost a race and was refused.", id);

            return await DraftConflictAsync(id, cancellationToken);
        }

        return CmsResult<ReusableDraftSaveResult>.Success(new ReusableDraftSaveResult(
            ToDraftState(draft, item.BlockType.Key),
            ApiDiagnostics.Project(DraftService.ToValidationResult(report), ValidationSeverity.Warning),
            referenceCount));
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReusableDraftState>> DiscardDraftAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return Forbidden<ReusableDraftState>("Editing reusable content is not permitted.");
        }

        var item = await LoadForWriteAsync(id, cancellationToken);

        if (item?.DraftVersion is null) return NotFound<ReusableDraftState>(id);

        if (item.PublishedVersion is null)
        {
            return CmsResult<ReusableDraftState>.Invalid(
                ReusableCodes.NothingPublished,
                "This item has never been published, so there is no published version to reset to.");
        }

        var draft = item.DraftVersion;

        // Copied rather than repointed, for the reason DraftService gives: a draft that *is* the
        // published row would be mutable the moment somebody typed into it.
        draft.ContentJson = item.PublishedVersion.ContentJson;
        draft.BlockTypeRevision = item.PublishedVersion.BlockTypeRevision;

        if (ContentPayload.TryParse(draft.ContentJson, out var payload))
        {
            await references.ProjectAsync(
                ContentSourceType.ReusableContentVersion,
                draft.Id,
                payload,
                cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "The draft of reusable item {ReusableId} was reset to published version {VersionNumber}.",
            id,
            item.PublishedVersion.VersionNumber);

        return CmsResult<ReusableDraftState>.Success(ToDraftState(draft, item.BlockType.Key));
    }

    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<ReusableVersionSummary>>> ListVersionsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden<IReadOnlyList<ReusableVersionSummary>>("Reading reusable content is not permitted.");
        }

        var item = await context.ReusableContents
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new { candidate.DraftVersionId, candidate.PublishedVersionId })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null) return NotFound<IReadOnlyList<ReusableVersionSummary>>(id);

        var versions = await context.ReusableContentVersions
            .AsNoTracking()
            .Where(version => version.ReusableContentId == id)
            .OrderByDescending(version => version.VersionNumber)
            .Select(version => new ReusableVersionSummary(
                version.Id,
                version.VersionNumber,
                version.Status.ToString(),
                version.Label,
                version.BlockTypeRevision,
                version.Id == item.DraftVersionId,
                version.Id == item.PublishedVersionId,
                version.CreatedOn,
                version.CreatedBy,
                version.PublishedOn,
                version.PublishedBy))
            .ToListAsync(cancellationToken);

        return CmsResult<IReadOnlyList<ReusableVersionSummary>>.Success(versions);
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReusablePublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden<ReusablePublishValidation>("Reading reusable content is not permitted.");
        }

        var item = await LoadForReadAsync(id, cancellationToken);

        if (item?.DraftVersion is null) return NotFound<ReusablePublishValidation>(id);

        var blastRadius = await impact.WhereUsedAsync(
            ContentReferenceTargetType.ReusableContent,
            id,
            cancellationToken);

        var checks = await CheckAsync(item, item.DraftVersion, blastRadius, cancellationToken);

        return CmsResult<ReusablePublishValidation>.Success(new ReusablePublishValidation(
            !checks.HasErrors,
            ApiDiagnostics.Project(checks, ValidationSeverity.Error),
            ApiDiagnostics.Project(checks, ValidationSeverity.Warning),
            blastRadius));
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReusablePublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return Forbidden<ReusablePublishResult>("Publishing is not permitted.");
        }

        var item = await LoadForWriteAsync(id, cancellationToken);

        if (item?.DraftVersion is null) return NotFound<ReusablePublishResult>(id);

        var draft = item.DraftVersion;

        // Read before the write and carried into the result. Afterwards it would be a different
        // question: the references are the same rows, but the audit entry spec section 9.3 asks for
        // has to name what this publish changed, and by the time anyone reads it back the pages will
        // have moved on.
        var blastRadius = await impact.WhereUsedAsync(
            ContentReferenceTargetType.ReusableContent,
            id,
            cancellationToken);

        var checks = await CheckAsync(item, draft, blastRadius, cancellationToken);

        if (checks.HasErrors)
        {
            logger.LogInformation(
                "Publishing reusable item {ReusableId} was refused by {Count} validation errors.",
                id,
                checks.Diagnostics.Count(diagnostic => diagnostic.Severity is ValidationSeverity.Error));

            return CmsResult<ReusablePublishResult>.Invalid(checks);
        }

        // The confirmation of spec section 9.4 enforced by the server rather than trusted to the
        // screen. The blast radius is one of the warnings CheckAsync collects, so an unacknowledged
        // publish of an item that is on any published page is refused and carries the count back.
        if (!acknowledgeWarnings && !checks.IsValid) return CmsResult<ReusablePublishResult>.Invalid(checks);

        if (!ContentPayload.TryParse(draft.ContentJson, out var payload))
        {
            return CmsResult<ReusablePublishResult>.Invalid(
                ReusableCodes.MalformedPayload,
                "The draft's stored content is not a well-formed payload document.");
        }

        var result = await CommitPublishAsync(item, draft, payload, blastRadius, checks, cancellationToken);

        logger.LogInformation(
            "Reusable item {ReusableId} published as version {VersionNumber}, superseding {Archived}; " +
            "{PageCount} published page(s) change and {PinnedCount} pinned page(s) do not.",
            id,
            result.VersionNumber,
            result.ArchivedVersionNumber?.ToString() ?? "nothing",
            blastRadius.AffectedPageCount,
            blastRadius.PinnedPageCount);

        return CmsResult<ReusablePublishResult>.Success(result, checks);
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReusableUnpublishResult>> UnpublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return Forbidden<ReusableUnpublishResult>("Publishing is not permitted.");
        }

        var item = await LoadForWriteAsync(id, cancellationToken);

        if (item is null) return NotFound<ReusableUnpublishResult>(id);

        if (item.PublishedVersion is null)
        {
            return CmsResult<ReusableUnpublishResult>.Invalid(
                ReusableCodes.AlreadyUnpublished,
                "This item is not published, so there is nothing to retire.");
        }

        var blastRadius = await impact.WhereUsedAsync(
            ContentReferenceTargetType.ReusableContent,
            id,
            cancellationToken);

        // Gated the same way a publish is, and for a sharper reason: publishing changes what those
        // pages show, and unpublishing empties it. The damage is entirely off-screen from here.
        if (!acknowledgeWarnings && blastRadius.RequiresConfirmation)
        {
            return CmsResult<ReusableUnpublishResult>.Invalid(ValidationResult.From(
                [BlastRadiusWarning(blastRadius, "stop showing this item and render nothing in its place")]));
        }

        var retired = item.PublishedVersion;
        retired.Status = PageVersionStatus.Archived;
        item.PublishedVersionId = null;
        item.PublishedVersion = null;

        WriteAudit(UnpublishAuditType, item.Id, retired.VersionNumber, blastRadius);
        cacheInvalidation.EnqueueReusable(item.Id);
        search.EnqueueReusable(item.Id);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Reusable item {ReusableId} was unpublished; version {VersionNumber} is archived and " +
            "{PageCount} published page(s) now render nothing in its place.",
            id,
            retired.VersionNumber,
            blastRadius.AffectedPageCount);

        return CmsResult<ReusableUnpublishResult>.Success(
            new ReusableUnpublishResult(id, retired.VersionNumber, blastRadius));
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReusableDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentDelete))
        {
            return Forbidden<ReusableDeleteResult>("Deleting reusable content is not permitted.");
        }

        var item = await context.ReusableContents
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (item is null) return NotFound<ReusableDeleteResult>(id);

        var blastRadius = await impact.WhereUsedAsync(
            ContentReferenceTargetType.ReusableContent,
            id,
            cancellationToken);

        // Blocked outright, including for a reference held only by a draft: the editor's remedy is
        // to replace the placements first, and a draft placement becomes a broken published one the
        // moment that page is published (spec section 9.4).
        if (blastRadius.IsReferenced)
        {
            logger.LogInformation(
                "Deleting reusable item {ReusableId} was refused; {PageCount} page(s) and " +
                "{ItemCount} item(s) still place it.",
                id,
                blastRadius.AffectedPages.Count,
                blastRadius.AffectedReusableItems.Count);

            return CmsResult<ReusableDeleteResult>.Conflict(
                ReusableCodes.StillReferenced,
                Describe(blastRadius),
                path: null,
                new ReusableDeleteResult(id, item.PublishedVersionId is not null));
        }

        var wasPublished = item.PublishedVersionId is not null;

        // Retired from the public site in the same act. An item nobody places cannot blank a zone by
        // going, but leaving PublishedVersionId set on a deleted row would make a later restore
        // silently republish content nobody reviewed.
        if (item.PublishedVersion is not null) item.PublishedVersion.Status = PageVersionStatus.Archived;

        item.PublishedVersionId = null;
        item.IsDeleted = true;
        item.DeletedOn = clock.GetUtcNow();
        item.DeletedBy = users.UserId;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Reusable item {ReusableId} was moved to the recycle bin.", id);

        return CmsResult<ReusableDeleteResult>.Success(new ReusableDeleteResult(id, wasPublished));
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReusableContentDetail>> RestoreAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentDelete))
        {
            return Forbidden<ReusableContentDetail>("Restoring reusable content is not permitted.");
        }

        var item = await context.ReusableContents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (item is null) return NotFound<ReusableContentDetail>(id);

        if (!item.IsDeleted)
        {
            return CmsResult<ReusableContentDetail>.Invalid(
                ReusableCodes.NotFound,
                "This item is not in the recycle bin, so there is nothing to restore.");
        }

        item.IsDeleted = false;
        item.DeletedOn = null;
        item.DeletedBy = null;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Reusable item {ReusableId} was restored, unpublished.", id);

        var detail = await LoadDetailAsync(id, cancellationToken);

        return detail is null
            ? NotFound<ReusableContentDetail>(id)
            : CmsResult<ReusableContentDetail>.Success(detail);
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReferenceImpact>> WhereUsedAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden<ReferenceImpact>("Reading reusable content is not permitted.");
        }

        if (!await context.ReusableContents
            .IgnoreQueryFilters()
            .AnyAsync(candidate => candidate.Id == id, cancellationToken))
        {
            return NotFound<ReferenceImpact>(id);
        }

        return CmsResult<ReferenceImpact>.Success(
            await impact.WhereUsedAsync(ContentReferenceTargetType.ReusableContent, id, cancellationToken));
    }

    /// <summary>
    /// Writes the publish, all of it or none of it.
    /// </summary>
    /// <remarks>
    /// The same four-step shape <c>PublishingService</c> uses, minus the URL step a reusable item has
    /// no need of. Each step keeps its own <c>SaveChangesAsync</c> so a forced failure at any of them
    /// can be shown to roll the whole thing back, and the execution strategy wraps it because Aspire
    /// enables connection retries and a manual transaction without one throws on the first blip.
    /// </remarks>
    private async Task<ReusablePublishResult> CommitPublishAsync(
        ReusableContent item,
        ReusableContentVersion draft,
        ContentPayload payload,
        ReferenceImpact blastRadius,
        ValidationResult checks,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var now = clock.GetUtcNow();
            var previous = item.PublishedVersion;

            // Step 1 — snapshot the draft into a new immutable row. Copied rather than promoted, so
            // the draft survives the publish and stays editable while forty pages render the copy.
            var published = new ReusableContentVersion
            {
                ReusableContentId = item.Id,
                VersionNumber = await VersionNumbers.NextForReusableAsync(
                    context,
                    item.Id,
                    cancellationToken),
                Status = PageVersionStatus.Published,
                ContentJson = draft.ContentJson,
                BlockTypeRevision = draft.BlockTypeRevision,
                PublishedOn = now,
                PublishedBy = users.UserId,
            };

            context.ReusableContentVersions.Add(published);
            await context.SaveChangesAsync(cancellationToken);

            // Step 2 — retire the version this supersedes and repoint the item. This single pointer
            // is the whole of the fan-out: every late-bound placement reads it at render time.
            if (previous is not null) previous.Status = PageVersionStatus.Archived;

            item.PublishedVersionId = published.Id;
            await context.SaveChangesAsync(cancellationToken);

            // Step 3 — project the reference rows for the version that is now live, so that an item
            // nested inside this one is reachable by the next impact walk and by cache invalidation.
            var referenceCount = await references.ProjectAsync(
                ContentSourceType.ReusableContentVersion,
                published.Id,
                payload,
                cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            // Step 4 — the audit entry carrying the impact list (task P4-12, spec section 9.3) and
            // the outbox row that drives cache invalidation (task P8-09). Both belong inside the
            // transaction: an impact list committed beside a publish that rolled back describes a
            // change that never happened, and an eviction enqueued outside one would fire for it.
            //
            // One tag, however many pages render this item. Each of them added `ru:{id}` while it
            // rendered, so the fan-out is already recorded on their cache entries and nothing here
            // has to walk the reference table to find them (spec section 16.2).
            WriteAudit(PublishAuditType, item.Id, published.VersionNumber, blastRadius);
            cacheInvalidation.EnqueueReusable(item.Id);
            search.EnqueueReusable(item.Id);
            await context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new ReusablePublishResult(
                item.Id,
                published.Id,
                published.VersionNumber,
                now,
                previous?.VersionNumber,
                referenceCount,
                blastRadius,
                ApiDiagnostics.Project(checks, ValidationSeverity.Warning));
        });
    }

    /// <summary>
    /// Runs every check a publish has to pass.
    /// </summary>
    /// <remarks>
    /// Shared by the dry run and the real thing so the two cannot disagree — and the direction they
    /// would disagree in is a green check followed by a refused publish.
    /// </remarks>
    private async Task<ValidationResult> CheckAsync(
        ReusableContent item,
        ReusableContentVersion draft,
        ReferenceImpact blastRadius,
        CancellationToken cancellationToken)
    {
        if (!ContentPayload.TryParse(draft.ContentJson, out var payload))
        {
            return ValidationResult.Error(
                ReusableCodes.MalformedPayload,
                "The draft's stored content is not a well-formed payload document.");
        }

        var diagnostics = new List<ValidationDiagnostic>();
        var schema = ReusableContentSchema.For(schemas, item.BlockType.Key, draft.BlockTypeRevision);

        if (schema is null)
        {
            // A warning rather than an error, matching the payload walk's treatment of a lost
            // schema: refusing the publish would strand content whose block type revision has been
            // pruned, and the values still carry everything needed to render them.
            diagnostics.Add(new ValidationDiagnostic(
                ContentValidationCodes.BlockTypeUnknown,
                $"No revision {draft.BlockTypeRevision} of block type '{item.BlockType.Key}' is known " +
                "to this deployment, so the content cannot be checked against its shape.",
                ValidationSeverity.Warning));
        }
        else
        {
            var report = await validator.ValidateAsync(payload, schema, ValidationMode.Publish, cancellationToken);

            diagnostics.AddRange(DraftService.ToValidationResult(report).Diagnostics);
        }

        diagnostics.AddRange(await CheckReferencedEntitiesAsync(payload, cancellationToken));

        // The same media rules a page publish applies, against the same schema this item's own
        // values were checked against. An item placed on forty pages must not be the way an
        // undescribed picture reaches all of them (task P5-21, spec section 13.7).
        diagnostics.AddRange(await media.ValidateAsync(payload, schema, cancellationToken));

        if (await CreatesCycleAsync(item.Id, payload, cancellationToken) is { } cycle)
        {
            diagnostics.AddRange(cycle.Diagnostics);
        }

        if (blastRadius.RequiresConfirmation)
        {
            diagnostics.Add(BlastRadiusWarning(blastRadius, "change immediately, without being republished"));
        }

        return ValidationResult.From(diagnostics);
    }

    /// <summary>
    /// Checks that every page and item this content points at still exists.
    /// </summary>
    /// <remarks>
    /// The link-integrity half of spec section 5.5 applied to a fragment. A placement of an
    /// unpublished item is a warning rather than an error for the same reason a link to an
    /// unpublished page is: publishing a section top-down is ordinary work.
    /// </remarks>
    private async Task<IReadOnlyList<ValidationDiagnostic>> CheckReferencedEntitiesAsync(
        ContentPayload payload,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var extracted = indexer.Extract(payload);

        var pageIds = extracted
            .Where(reference => reference.TargetType is ContentReferenceTargetType.Page)
            .Select(reference => reference.TargetId)
            .ToHashSet();

        if (pageIds.Count > 0)
        {
            var live = await context.Pages
                .AsNoTracking()
                .Where(page => pageIds.Contains(page.Id))
                .Select(page => page.Id)
                .ToListAsync(cancellationToken);

            diagnostics.AddRange(pageIds
                .Except(live)
                .Order()
                .Select(missing => new ValidationDiagnostic(
                    PageCodes.NotFound,
                    $"This content links to page {missing}, which no longer exists or is in the " +
                    "recycle bin.",
                    ValidationSeverity.Error)));
        }

        var itemIds = extracted
            .Where(reference => reference.TargetType is ContentReferenceTargetType.ReusableContent)
            .Select(reference => reference.TargetId)
            .ToHashSet();

        if (itemIds.Count == 0) return diagnostics;

        var known = await context.ReusableContents
            .AsNoTracking()
            .Where(candidate => itemIds.Contains(candidate.Id))
            .Select(candidate => new { candidate.Id, IsPublished = candidate.PublishedVersionId != null })
            .ToListAsync(cancellationToken);

        foreach (var targetId in itemIds.Order())
        {
            var match = known.FirstOrDefault(candidate => candidate.Id == targetId);

            if (match is null)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    ReusableCodes.NotFound,
                    $"This content places reusable item {targetId}, which no longer exists or is in " +
                    "the recycle bin.",
                    ValidationSeverity.Error));
            }
            else if (!match.IsPublished)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    ReusableCodes.NothingPublished,
                    $"This content places reusable item {targetId}, which is not published yet. It " +
                    "will render nothing until it is.",
                    ValidationSeverity.Warning));
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// Checks whether the item's content reaches the item itself, directly or through another item.
    /// </summary>
    /// <returns>The refusal, or null when the graph is acyclic.</returns>
    /// <remarks>
    /// Refused at write time, which is the only place it can be refused usefully. The renderer's
    /// depth guard is the backstop below this, and a backstop that fires renders half a footer and
    /// logs — a correct outcome, and not one anybody should be able to author (P4 #7).
    /// <para>
    /// The walk follows each item's <em>draft and published</em> versions alike. A cycle that exists
    /// only between two drafts is still a cycle the moment both are published, and refusing it at
    /// the second publish rather than at this save would mean rejecting content that was legal when
    /// it was written.
    /// </para>
    /// </remarks>
    private async Task<ValidationResult?> CreatesCycleAsync(
        int itemId,
        ContentPayload payload,
        CancellationToken cancellationToken)
    {
        var targets = indexer.Extract(payload)
            .Where(reference => reference.TargetType is ContentReferenceTargetType.ReusableContent)
            .Select(reference => reference.TargetId)
            .ToHashSet();

        if (targets.Count == 0) return null;

        if (targets.Contains(itemId))
        {
            return ValidationResult.Error(
                ReusableCodes.Cycle,
                "A reusable item cannot place itself.");
        }

        var seen = new HashSet<int>(targets);
        var frontier = targets.ToList();

        for (var depth = 0; depth < MaxNestingDepth && frontier.Count > 0; depth++)
        {
            // Only the versions an item is actually serving or editing. Archived history can hold a
            // placement that was removed years ago, and refusing today's save because of it would be
            // a refusal with no remedy — the old version is immutable.
            var versionIds = await context.ReusableContentVersions
                .AsNoTracking()
                .Where(version => frontier.Contains(version.ReusableContentId) &&
                    (version.ReusableContent.DraftVersionId == version.Id ||
                        version.ReusableContent.PublishedVersionId == version.Id))
                .Select(version => version.Id)
                .ToListAsync(cancellationToken);

            if (versionIds.Count == 0) return null;

            var next = await context.ContentReferences
                .AsNoTracking()
                .Where(row => row.SourceType == ContentSourceType.ReusableContentVersion &&
                    versionIds.Contains(row.SourceVersionId) &&
                    row.TargetType == ContentReferenceTargetType.ReusableContent)
                .Select(row => row.TargetId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (next.Contains(itemId))
            {
                return ValidationResult.Error(
                    ReusableCodes.Cycle,
                    "This placement closes a loop: an item this content places, or something it " +
                    "places in turn, already places this item.");
            }

            frontier = [.. next.Where(seen.Add)];
        }

        return null;
    }

    /// <summary>Inserts the item and its first draft in one transaction.</summary>
    private async Task<ReusableContent> InsertAsync(
        CreateReusableContentRequest request,
        BlockType blockType,
        string key,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // A transient failure re-runs this whole lambda, so anything the failed attempt left
            // tracked would be inserted a second time beside the fresh entities below.
            context.ChangeTracker.Clear();

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var item = new ReusableContent
            {
                Key = key,
                Name = request.Name!.Trim(),
                Description = request.Description,
                FolderId = request.FolderId,
                BlockTypeId = blockType.Id,
            };

            context.ReusableContents.Add(item);
            await context.SaveChangesAsync(cancellationToken);

            var draft = new ReusableContentVersion
            {
                ReusableContentId = item.Id,
                VersionNumber = VersionNumbers.First,
                Status = PageVersionStatus.Draft,
                // Empty and schema-valid whatever the block type requires: every property is absent
                // rather than present-and-null, and a required one blocks only a publish.
                ContentJson = ContentPayload
                    .CreateEmpty(blockType.Key, blockType.CurrentRevision)
                    .ToJson(),
                BlockTypeRevision = blockType.CurrentRevision,
            };

            context.ReusableContentVersions.Add(draft);
            await context.SaveChangesAsync(cancellationToken);

            item.DraftVersionId = draft.Id;
            await context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return item;
        });
    }

    /// <summary>
    /// Checks the parts of the envelope a client is not allowed to decide for itself.
    /// </summary>
    /// <remarks>
    /// The payload declares which schema it is to be judged against, which makes these two members a
    /// privilege boundary rather than data. For a reusable item they carry the block type key and
    /// revision — see <c>ReusableContentVersion.ContentJson</c> for why the envelope's member names
    /// say "template".
    /// </remarks>
    private static CmsResult<ReusableDraftSaveResult>? CheckEnvelope(
        ContentPayload payload,
        ReusableContent item,
        ReusableContentVersion draft)
    {
        if (!string.Equals(payload.TemplateKey, item.BlockType.Key, StringComparison.Ordinal))
        {
            return CmsResult<ReusableDraftSaveResult>.Invalid(
                ReusableCodes.BlockTypeMismatch,
                $"This item is shaped by block type '{item.BlockType.Key}', and the content sent " +
                $"names '{payload.TemplateKey}'.",
                ContentPayloadMembers.TemplateKey);
        }

        // Either the revision the draft already holds, or the one the block type is on now — the
        // second being how an editor adopts a structural change.
        if (payload.TemplateRevision is not { } revision ||
            (revision != draft.BlockTypeRevision && revision != item.BlockType.CurrentRevision))
        {
            return CmsResult<ReusableDraftSaveResult>.Invalid(
                ReusableCodes.BlockTypeRevisionInvalid,
                $"Content for this item captures revision {draft.BlockTypeRevision} of block type " +
                $"'{item.BlockType.Key}', or its current revision {item.BlockType.CurrentRevision}.",
                ContentPayloadMembers.TemplateRevision);
        }

        return null;
    }

    /// <summary>Reloads the stored draft so a conflict can hand back the copy that won.</summary>
    private async Task<CmsResult<ReusableDraftSaveResult>> DraftConflictAsync(
        int id,
        CancellationToken cancellationToken)
    {
        // A fresh view: the tracked entity carries the values this request tried to write, and
        // handing those back as "theirs" would show the losing editor their own work.
        context.ChangeTracker.Clear();

        var stored = await context.ReusableContents
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new { item.DraftVersion, BlockTypeKey = item.BlockType.Key })
            .FirstOrDefaultAsync(cancellationToken);

        return CmsResult<ReusableDraftSaveResult>.Conflict(
            ReusableCodes.ConcurrentChange,
            "This draft was saved by someone else after you opened it. Compare the two versions and " +
            "choose which to keep.",
            nameof(SaveDraftRequest.ExpectedRowVersion),
            stored?.DraftVersion is null
                ? null
                : new ReusableDraftSaveResult(ToDraftState(stored.DraftVersion, stored.BlockTypeKey), [], 0));
    }

    /// <summary>Records what a lifecycle action did to pages nobody touched (spec section 9.3).</summary>
    private void WriteAudit(string type, int itemId, int versionNumber, ReferenceImpact blastRadius)
    {
        context.AuditLogs.Add(new AuditLog
        {
            UserId = users.UserId,
            Type = type,
            TableName = nameof(ReusableContent),
            DateTime = clock.GetUtcNow(),
            PrimaryKey = JsonSerializer.Serialize(new { Id = itemId }),
            NewValues = JsonSerializer.Serialize(new
            {
                VersionNumber = versionNumber,
                blastRadius.AffectedPageCount,
                blastRadius.PinnedPageCount,
                // The ids rather than the whole shape. This row is read months later by somebody
                // asking why a page changed, and a page id is the part that is still true then —
                // titles and URLs will have moved on, and storing them would preserve a stale copy.
                AffectedPageIds = blastRadius.AffectedPages.Select(page => page.Id).ToArray(),
                PinnedPageIds = blastRadius.AffectedPages
                    .Where(page => page.IsPinned)
                    .Select(page => page.Id)
                    .ToArray(),
                blastRadius.IsTruncated,
            }),
        });
    }

    private static ValidationDiagnostic BlastRadiusWarning(ReferenceImpact blastRadius, string consequence)
    {
        var pinned = blastRadius.PinnedPageCount == 0
            ? string.Empty
            : $" {blastRadius.PinnedPageCount} pinned page" +
                $"{(blastRadius.PinnedPageCount == 1 ? " does" : "s do")} not.";

        return new ValidationDiagnostic(
            ReusableCodes.BlastRadius,
            $"{blastRadius.AffectedPageCount} published page" +
                $"{(blastRadius.AffectedPageCount == 1 ? " will" : "s will")} {consequence}.{pinned}",
            ValidationSeverity.Warning);
    }

    private static string Describe(ReferenceImpact blastRadius)
    {
        var parts = new List<string>(2);

        if (blastRadius.AffectedPages.Count > 0)
        {
            parts.Add($"{blastRadius.AffectedPages.Count} page" +
                $"{(blastRadius.AffectedPages.Count == 1 ? string.Empty : "s")} " +
                $"({string.Join(", ", blastRadius.AffectedPages.Select(page => page.Id))})");
        }

        if (blastRadius.AffectedReusableItems.Count > 0)
        {
            parts.Add($"{blastRadius.AffectedReusableItems.Count} reusable item" +
                $"{(blastRadius.AffectedReusableItems.Count == 1 ? string.Empty : "s")} " +
                $"({string.Join(", ", blastRadius.AffectedReusableItems.Select(item => item.Key))})");
        }

        return $"Stored content on {string.Join(" and ", parts)} still places this item. Replace or " +
            "remove those placements first.";
    }

    private Task<ReusableContent?> LoadForWriteAsync(int id, CancellationToken cancellationToken) =>
        context.ReusableContents
            .Include(item => item.BlockType)
            .Include(item => item.DraftVersion)
            .Include(item => item.PublishedVersion)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    private Task<ReusableContent?> LoadForReadAsync(int id, CancellationToken cancellationToken) =>
        context.ReusableContents
            .AsNoTracking()
            .Include(item => item.BlockType)
            .Include(item => item.DraftVersion)
            .Include(item => item.PublishedVersion)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    private async Task<ReusableContentDetail?> LoadDetailAsync(int id, CancellationToken cancellationToken)
    {
        var row = await context.ReusableContents
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                Summary = new ReusableContentSummary(
                    item.Id,
                    item.Key,
                    item.Name,
                    item.Description,
                    item.FolderId,
                    item.BlockTypeId,
                    item.BlockType.Key,
                    item.DraftVersion!.Status.ToString(),
                    item.PublishedVersion != null &&
                        item.DraftVersion.ContentJson != item.PublishedVersion.ContentJson,
                    item.DraftVersion.VersionNumber,
                    item.PublishedVersion == null ? null : item.PublishedVersion.VersionNumber,
                    item.ModifiedOn ?? item.CreatedOn),
                item.DraftVersion.ContentJson,
                item.DraftVersion.BlockTypeRevision,
                item.DraftVersion.RowVersion,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ReusableContentDetail(
                row.Summary,
                row.ContentJson,
                row.BlockTypeRevision,
                Convert.ToBase64String(row.RowVersion ?? []));
    }

    /// <summary>The library-list projection, written once so the list and the detail cannot diverge.</summary>
    private static System.Linq.Expressions.Expression<Func<ReusableContent, ReusableContentSummary>> Summary() =>
        item => new ReusableContentSummary(
            item.Id,
            item.Key,
            item.Name,
            item.Description,
            item.FolderId,
            item.BlockTypeId,
            item.BlockType.Key,
            item.DraftVersion!.Status.ToString(),
            item.PublishedVersion != null &&
                item.DraftVersion.ContentJson != item.PublishedVersion.ContentJson,
            item.DraftVersion.VersionNumber,
            item.PublishedVersion == null ? null : item.PublishedVersion.VersionNumber,
            item.ModifiedOn ?? item.CreatedOn);

    private static ReusableDraftState ToDraftState(ReusableContentVersion draft, string blockTypeKey) =>
        new(
            draft.ReusableContentId,
            draft.Id,
            draft.VersionNumber,
            draft.ContentJson,
            blockTypeKey,
            draft.BlockTypeRevision,
            Convert.ToBase64String(draft.RowVersion ?? []),
            draft.ModifiedOn ?? draft.CreatedOn);

    private static ValidationDiagnostic TooLong(string path, int maximum) =>
        new(
            ReusableCodes.TooLong,
            $"A value may be at most {maximum} characters.",
            ValidationSeverity.Error,
            path);

    private static CmsResult<T> NotFound<T>(int id) =>
        CmsResult<T>.NotFound($"No reusable item has id {id}.", ReusableCodes.NotFound);

    private static CmsResult<T> Forbidden<T>(string message) =>
        CmsResult<T>.Forbidden(message, ReusableCodes.Forbidden);
}
