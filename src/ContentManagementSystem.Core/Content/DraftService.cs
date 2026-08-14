using System.Text.Json;

using ContentManagementSystem.Core.Content.Schema;
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

/// <summary>
/// Reads and writes the one mutable version of a page (task P2-10, spec section 11.3).
/// </summary>
/// <remarks>
/// Every write here mutates the draft row in place and creates no version row. That is the mechanism
/// behind the requirement's central promise: the published version is a different row, nothing in
/// this service can reach it, and so "editing the draft leaves the published version byte-for-byte
/// unchanged" is structural rather than a rule somebody has to remember (acceptance criterion P2 #4).
/// <para>
/// The one exception is a named checkpoint, which inserts an <c>Archived</c> row deliberately — a
/// bookmark an editor takes before a rewrite, without publishing anything.
/// </para>
/// </remarks>
public interface IDraftService
{
    /// <summary>Reads a page's draft.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The draft, or a not-found result.</returns>
    Task<CmsResult<DraftState>> GetAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a payload to the draft.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="request">The payload, and the row version the caller last saw.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// The saved draft with any non-blocking warnings, an invalid result carrying every schema
    /// problem found, or a conflict carrying the stored draft when the save lost a race.
    /// </returns>
    Task<CmsResult<DraftSaveResult>> SaveAsync(
        int pageId,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the draft to a copy of what is published.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The reset draft, or an invalid result when nothing has ever been published.</returns>
    /// <remarks>
    /// A copy, not a pointer. The draft keeps its own row and its own version number; only its
    /// content and its versioned metadata are overwritten, so the timeline stays forward-moving and
    /// the published row stays untouched (spec section 11.5).
    /// </remarks>
    Task<CmsResult<DraftState>> DiscardAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bookmarks the draft as a named, frozen version.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="label">What the editor is marking, such as "before the big rewrite".</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The draft, unchanged — the checkpoint is a copy taken beside it.</returns>
    Task<CmsResult<DraftState>> CheckpointAsync(
        int pageId,
        string? label,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IDraftService" />
/// <param name="context">The application database context.</param>
/// <param name="validator">Checks a payload against the schema it was authored against.</param>
/// <param name="references">Rewrites the draft's reference rows from its payload.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="logger">Log for saves that lost a race and for checkpoints.</param>
public sealed class DraftService(
    ApplicationDbContext context,
    IContentSchemaValidator validator,
    IContentReferenceProjector references,
    ICmsAuthorization authorization,
    ILogger<DraftService> logger) : IDraftService
{
    /// <inheritdoc />
    public async Task<CmsResult<DraftState>> GetAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden<DraftState>("Reading pages is not permitted.");
        }

        var draft = await context.Pages
            .AsNoTracking()
            .Where(page => page.Id == pageId)
            .Select(page => page.DraftVersion)
            .FirstOrDefaultAsync(cancellationToken);

        return draft is null
            ? NotFound<DraftState>(pageId)
            : CmsResult<DraftState>.Success(ToState(draft));
    }

    /// <inheritdoc />
    public async Task<CmsResult<DraftSaveResult>> SaveAsync(
        int pageId,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return Forbidden<DraftSaveResult>("Editing pages is not permitted.");
        }

        var page = await LoadForWriteAsync(pageId, cancellationToken);

        if (page?.DraftVersion is null) return NotFound<DraftSaveResult>(pageId);

        var draft = page.DraftVersion;

        if (!ContentPayload.TryParse(request.ContentJson, out var payload) || !payload.IsObject)
        {
            return CmsResult<DraftSaveResult>.Invalid(
                PageCodes.MalformedPayload,
                "The content sent is not a well-formed payload document.",
                nameof(SaveDraftRequest.ContentJson));
        }

        if (CheckEnvelope(payload, page, draft) is { } envelopeProblem)
        {
            return envelopeProblem;
        }

        var report = await validator.ValidateAsync(payload, ValidationMode.Draft, cancellationToken);

        if (report.HasErrors)
        {
            // Errors block a draft save only when they are structural — an unfilled required zone is
            // not one of them, because an editor must always be able to save half-finished work
            // (spec section 8.3). ValidationMode.Draft is what draws that line, upstream of here.
            return CmsResult<DraftSaveResult>.Invalid(ToValidationResult(report));
        }

        draft.ContentJson = payload.ToJson();
        draft.TemplateRevision = payload.TemplateRevision!.Value;

        var referenceCount = await references.ProjectAsync(
            ContentSourceType.PageVersion,
            draft.Id,
            payload,
            cancellationToken);

        if (TryApplyExpectedRowVersion(context.Entry(draft), request.ExpectedRowVersion) is false)
        {
            return CmsResult<DraftSaveResult>.Invalid(
                PageCodes.ConcurrentChange,
                "The supplied row version is not a value this server issued.",
                nameof(SaveDraftRequest.ExpectedRowVersion));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("A draft save on page {PageId} lost a race and was refused.", pageId);

            return await ConflictAsync(pageId, cancellationToken);
        }

        return CmsResult<DraftSaveResult>.Success(
            new DraftSaveResult(
                ToState(draft),
                ApiDiagnostics.Project(ToValidationResult(report), ValidationSeverity.Warning),
                referenceCount));
    }

    /// <inheritdoc />
    public async Task<CmsResult<DraftState>> DiscardAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return Forbidden<DraftState>("Editing pages is not permitted.");
        }

        var page = await LoadForWriteAsync(pageId, cancellationToken);

        if (page?.DraftVersion is null) return NotFound<DraftState>(pageId);

        if (page.PublishedVersion is null)
        {
            return CmsResult<DraftState>.Invalid(
                PageCodes.NothingPublished,
                "This page has never been published, so there is no published version to reset to.");
        }

        var draft = page.DraftVersion;
        var published = page.PublishedVersion;

        // Copied field by field rather than by repointing DraftVersionId at the published row. The
        // published version is immutable, and a draft that *is* the published row would be mutable
        // the moment somebody typed into it — the one thing this whole model exists to prevent.
        draft.ContentJson = published.ContentJson;
        draft.Title = published.Title;
        draft.TemplateId = published.TemplateId;
        draft.TemplateRevision = published.TemplateRevision;
        CopySeo(published, draft);

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
            "The draft of page {PageId} was reset to published version {VersionNumber}.",
            pageId,
            published.VersionNumber);

        return CmsResult<DraftState>.Success(ToState(draft));
    }

    /// <inheritdoc />
    public async Task<CmsResult<DraftState>> CheckpointAsync(
        int pageId,
        string? label,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return Forbidden<DraftState>("Editing pages is not permitted.");
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return CmsResult<DraftState>.Invalid(
                PageCodes.TitleRequired,
                "A checkpoint needs a label, or it is indistinguishable from every other one.",
                nameof(label));
        }

        if (label.Trim().Length > FieldLengths.EntityName)
        {
            return CmsResult<DraftState>.Invalid(
                PageCodes.TooLong,
                $"A label may be at most {FieldLengths.EntityName} characters.",
                nameof(label));
        }

        var page = await LoadForWriteAsync(pageId, cancellationToken);

        if (page?.DraftVersion is null) return NotFound<DraftState>(pageId);

        var draft = page.DraftVersion;
        var checkpoint = Copy(draft, await NextVersionNumberAsync(pageId, cancellationToken));

        // Archived rather than a status of its own: it is a frozen copy nobody will publish from
        // directly, which is exactly what Archived means, and the Label is what tells it apart from
        // a superseded publish in the history list (spec section 11.3).
        checkpoint.Status = PageVersionStatus.Archived;
        checkpoint.Label = label.Trim();

        context.PageVersions.Add(checkpoint);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Checkpoint '{Label}' taken on page {PageId} as version {VersionNumber}.",
            checkpoint.Label,
            pageId,
            checkpoint.VersionNumber);

        return CmsResult<DraftState>.Success(ToState(draft));
    }

    /// <summary>
    /// Checks the parts of the envelope a client is not allowed to decide for itself.
    /// </summary>
    /// <returns>The refusal, or null when the envelope is acceptable.</returns>
    /// <remarks>
    /// The payload declares which schema it is to be judged against, which makes these two members
    /// a privilege boundary rather than data: a client free to name any template and any revision
    /// can pick one whose rules its content happens to satisfy.
    /// </remarks>
    private static CmsResult<DraftSaveResult>? CheckEnvelope(
        ContentPayload payload,
        Page page,
        PageVersion draft)
    {
        if (!string.Equals(payload.TemplateKey, page.Template.Key, StringComparison.Ordinal))
        {
            return CmsResult<DraftSaveResult>.Invalid(
                PageCodes.TemplateMismatch,
                $"This page is laid out by template '{page.Template.Key}', and the content sent " +
                $"names '{payload.TemplateKey}'.",
                ContentPayloadMembers.TemplateKey);
        }

        // Either the revision the draft already holds, or the one the template is on now — the
        // second being how an editor adopts a structural change. Anything else is a client
        // inventing the schema its own content will be judged against.
        if (payload.TemplateRevision is not { } revision ||
            (revision != draft.TemplateRevision && revision != page.Template.CurrentRevision))
        {
            return CmsResult<DraftSaveResult>.Invalid(
                PageCodes.TemplateRevisionInvalid,
                $"Content for this page captures revision {draft.TemplateRevision} of template " +
                $"'{page.Template.Key}', or its current revision {page.Template.CurrentRevision}.",
                ContentPayloadMembers.TemplateRevision);
        }

        return null;
    }

    /// <summary>
    /// Tells EF which row version this save is claiming to overwrite.
    /// </summary>
    /// <returns>False when the supplied token is not decodable, true otherwise.</returns>
    /// <remarks>
    /// Setting the original value is what makes the check happen against the value the <em>caller</em>
    /// last saw, rather than against the one this request just read. Without it the window between
    /// this request's read and its write is unguarded, which is precisely the window two editors
    /// saving at once occupy (spec section 11.8).
    /// </remarks>
    private static bool? TryApplyExpectedRowVersion(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<PageVersion> entry,
        string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return null;

        Span<byte> buffer = stackalloc byte[16];

        if (!Convert.TryFromBase64String(expected, buffer, out var written)) return false;

        entry.Property(version => version.RowVersion).OriginalValue = buffer[..written].ToArray();

        return true;
    }

    /// <summary>Reloads the stored draft so a conflict can hand back the copy that won.</summary>
    private async Task<CmsResult<DraftSaveResult>> ConflictAsync(
        int pageId,
        CancellationToken cancellationToken)
    {
        // A fresh context view: the tracked entity carries the values this request tried to write,
        // and handing those back as "theirs" would show the losing editor their own text.
        context.ChangeTracker.Clear();

        var stored = await context.Pages
            .AsNoTracking()
            .Where(page => page.Id == pageId)
            .Select(page => page.DraftVersion)
            .FirstOrDefaultAsync(cancellationToken);

        return CmsResult<DraftSaveResult>.Conflict(
            PageCodes.ConcurrentChange,
            "This draft was saved by someone else after you opened it. Compare the two versions and " +
            "choose which to keep.",
            nameof(SaveDraftRequest.ExpectedRowVersion),
            stored is null ? null : new DraftSaveResult(ToState(stored), [], 0));
    }

    private Task<Page?> LoadForWriteAsync(int pageId, CancellationToken cancellationToken) =>
        context.Pages
            .Include(page => page.Template)
            .Include(page => page.DraftVersion)
            .Include(page => page.PublishedVersion)
            .FirstOrDefaultAsync(page => page.Id == pageId, cancellationToken);

    private async Task<int> NextVersionNumberAsync(int pageId, CancellationToken cancellationToken) =>
        (await context.PageVersions
            .Where(version => version.PageId == pageId)
            .Select(version => (int?)version.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;

    /// <summary>Copies a version's content and metadata into a new, unsaved row.</summary>
    internal static PageVersion Copy(PageVersion source, int versionNumber)
    {
        var copy = new PageVersion
        {
            PageId = source.PageId,
            VersionNumber = versionNumber,
            Status = source.Status,
            Title = source.Title,
            ContentJson = source.ContentJson,
            TemplateId = source.TemplateId,
            TemplateRevision = source.TemplateRevision,
        };

        CopySeo(source, copy);

        return copy;
    }

    /// <summary>
    /// Copies the versioned search and social metadata from one version to another.
    /// </summary>
    /// <remarks>
    /// One definition, because three operations copy a version — publishing, discarding a draft, and
    /// restoring an old one — and a field missed in any of them is a field that silently reverts.
    /// </remarks>
    internal static void CopySeo(PageVersion source, PageVersion target)
    {
        target.MetaTitle = source.MetaTitle;
        target.MetaDescription = source.MetaDescription;
        target.CanonicalUrl = source.CanonicalUrl;
        target.RobotsIndex = source.RobotsIndex;
        target.RobotsFollow = source.RobotsFollow;
        target.OgTitle = source.OgTitle;
        target.OgDescription = source.OgDescription;
        target.OgImageMediaId = source.OgImageMediaId;
        target.OgType = source.OgType;
        target.TwitterCard = source.TwitterCard;
        target.StructuredDataJson = source.StructuredDataJson;
        target.ChangeFreq = source.ChangeFreq;
        target.Priority = source.Priority;
    }

    /// <summary>Re-expresses a payload report as the diagnostic shape the API returns.</summary>
    internal static ValidationResult ToValidationResult(ContentValidationReport report) =>
        ValidationResult.From(report.Diagnostics.Select(diagnostic =>
            new ValidationDiagnostic(
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.Severity,
                diagnostic.Path)));

    private static DraftState ToState(PageVersion draft)
    {
        var payload = ContentPayload.TryParse(draft.ContentJson, out var parsed) ? parsed : null;

        return new DraftState(
            draft.PageId,
            draft.Id,
            draft.VersionNumber,
            draft.ContentJson,
            payload?.TemplateKey ?? string.Empty,
            draft.TemplateRevision,
            Convert.ToBase64String(draft.RowVersion ?? []),
            draft.ModifiedOn ?? draft.CreatedOn);
    }

    private static CmsResult<T> NotFound<T>(int pageId) =>
        CmsResult<T>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);

    private static CmsResult<T> Forbidden<T>(string message) =>
        CmsResult<T>.Forbidden(message, PageCodes.Forbidden);
}
