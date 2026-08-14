using System.Text.Json;

using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Content;

/// <inheritdoc cref="IPageService" />
/// <param name="context">The application database context.</param>
/// <param name="tree">Owns the materialized path; nothing else may write it.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="logger">Log for page creation and for saves that lost a race.</param>
public sealed class PageService(
    ApplicationDbContext context,
    IPageTreeService tree,
    ICmsAuthorization authorization,
    ILogger<PageService> logger) : IPageService
{
    /// <inheritdoc />
    public async Task<CmsResult<PageDetail>> CreateAsync(
        CreatePageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return Forbidden("Creating pages is not permitted.");
        }

        var template = await context.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == request.TemplateId, cancellationToken);

        // Invalid rather than not-found: the address of this request is the page collection, and the
        // template is a value in the body. Answering 404 would tell a client the page endpoint does
        // not exist.
        if (template is null)
        {
            return CmsResult<PageDetail>.Invalid(
                PageCodes.TemplateNotFound,
                $"No template has id {request.TemplateId}.",
                nameof(CreatePageRequest.TemplateId));
        }

        if (!template.IsEnabled)
        {
            return CmsResult<PageDetail>.Invalid(
                PageCodes.TemplateDisabled,
                $"Template '{template.Key}' is disabled, so no new page can be created from it.",
                nameof(CreatePageRequest.TemplateId));
        }

        var parent = request.ParentId is null
            ? null
            : await context.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == request.ParentId, cancellationToken);

        // The query filter is left in place, so a page in the recycle bin is not an available
        // parent: a live child under a deleted parent is a subtree the tree UI cannot show and a
        // restore would have to reason about.
        if (request.ParentId is not null && parent is null)
        {
            return CmsResult<PageDetail>.Invalid(
                PageCodes.ParentNotFound,
                $"No page has id {request.ParentId}, or it is in the recycle bin.",
                nameof(CreatePageRequest.ParentId));
        }

        var diagnostics = new List<ValidationDiagnostic>();
        diagnostics.AddRange(ValidateTitle(request.Title, nameof(CreatePageRequest.Title)));

        // Generated from the title only when none was supplied, and validated the same way either
        // way — a generator whose output its own validator rejects fails for titles nobody tried.
        var slug = Slugs.Normalize(
            string.IsNullOrWhiteSpace(request.Slug) ? Slugs.Generate(request.Title) : request.Slug);

        diagnostics.AddRange(
            Slugs.Validate(slug, request.ParentId is null, nameof(CreatePageRequest.Slug)).Diagnostics);

        if ((parent?.Depth ?? -1) + 1 > IPageTreeService.MaxDepth)
        {
            diagnostics.Add(new ValidationDiagnostic(
                PageCodes.TooDeep,
                $"A page may sit at most {IPageTreeService.MaxDepth} levels below the root.",
                ValidationSeverity.Error,
                nameof(CreatePageRequest.ParentId)));
        }

        var checks = ValidationResult.From(diagnostics);

        // Errors block; warnings do not. A slug outside ASCII is accepted with a homograph warning
        // that has to survive the save, so the result is judged on severity rather than on whether
        // anything was said (spec section 10.3).
        if (checks.HasErrors) return CmsResult<PageDetail>.Invalid(checks);

        if (await SiblingSlugExistsAsync(request.ParentId, slug, excludingPageId: null, cancellationToken))
        {
            return DuplicateSlug(slug, request.ParentId);
        }

        var page = await InsertAsync(request, template, parent, slug, cancellationToken);

        logger.LogInformation(
            "Page {PageId} created from template {TemplateKey} revision {Revision} with slug '{Slug}'.",
            page.Id,
            template.Key,
            template.CurrentRevision,
            slug);

        // Reloaded rather than projected from the entities in hand, so that what the caller receives
        // is what a subsequent read returns — database-assigned timestamps and the row version a
        // client echoes back as If-Match included. Deliberately not routed through GetAsync, which
        // would re-authorize the caller against Content.Read; creating a page and then being told
        // it cannot be read would be an absurd answer to a request that succeeded.
        var detail = await LoadDetailAsync(page.Id, cancellationToken);

        return detail is null ? NotFound(page.Id) : CmsResult<PageDetail>.Success(detail, checks);
    }

    /// <inheritdoc />
    public async Task<CmsResult<PageDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden("Reading pages is not permitted.");
        }

        var detail = await LoadDetailAsync(id, cancellationToken);

        return detail is null ? NotFound(id) : CmsResult<PageDetail>.Success(detail);
    }

    /// <inheritdoc />
    public async Task<CmsResult<PageDetail>> PatchMetadataAsync(
        int id,
        PatchPageMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return Forbidden("Editing pages is not permitted.");
        }

        var page = await context.Pages
            .Include(candidate => candidate.Template)
            .Include(candidate => candidate.DraftVersion)
            .Include(candidate => candidate.PublishedVersion)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (page?.DraftVersion is null) return NotFound(id);

        var draft = page.DraftVersion;
        var diagnostics = new List<ValidationDiagnostic>();

        var title = request.Title.Or(draft.Title);
        if (request.Title.IsSet)
        {
            diagnostics.AddRange(ValidateTitle(title, nameof(PatchPageMetadataRequest.Title)));
        }

        var slug = page.Slug;
        if (request.Slug.IsSet)
        {
            slug = Slugs.Normalize(request.Slug.Value);
            diagnostics.AddRange(
                Slugs.Validate(slug, page.ParentId is null, nameof(PatchPageMetadataRequest.Slug)).Diagnostics);
        }

        var useExplicitUrl = request.UseExplicitUrl.Or(page.UseExplicitUrl);
        var explicitUrl = Clean(request.ExplicitUrl.Or(page.ExplicitUrl));
        diagnostics.AddRange(ValidateExplicitUrl(useExplicitUrl, explicitUrl));

        var owner = request.OwnerUserId.Or(page.OwnerUserId);
        if (request.OwnerUserId.IsSet && owner is not null &&
            !await context.Users.AnyAsync(user => user.Id == owner, cancellationToken))
        {
            // Checked rather than left to the foreign key: a constraint violation reaches the client
            // as a 500 about a database it should not know exists, and the ordinary way to get here
            // is picking someone whose account was removed while the form was open.
            diagnostics.Add(new ValidationDiagnostic(
                PageCodes.OwnerNotFound,
                $"No user has id {owner}.",
                ValidationSeverity.Error,
                nameof(PatchPageMetadataRequest.OwnerUserId)));
        }

        var seo = ApplySeo(request, draft);
        diagnostics.AddRange(ValidateSeo(seo, request.InternalNotes.Or(page.InternalNotes)));

        var checks = ValidationResult.From(diagnostics);

        if (checks.HasErrors) return CmsResult<PageDetail>.Invalid(checks);

        if (!string.Equals(slug, page.Slug, StringComparison.Ordinal) &&
            await SiblingSlugExistsAsync(page.ParentId, slug, page.Id, cancellationToken))
        {
            return DuplicateSlug(slug, page.ParentId);
        }

        page.Slug = slug;
        page.UseExplicitUrl = useExplicitUrl;
        page.ExplicitUrl = useExplicitUrl ? explicitUrl : null;
        page.ShowInNavigation = request.ShowInNavigation.Or(page.ShowInNavigation);
        page.OwnerUserId = owner;
        page.ReviewByDate = request.ReviewByDate.Or(page.ReviewByDate);
        page.InternalNotes = Clean(request.InternalNotes.Or(page.InternalNotes));

        // Written to the draft version, never to the published one, which is the whole point of the
        // two-pointer model: correcting a title in the backoffice reaches the public site only when
        // someone publishes (spec section 11.1).
        draft.Title = title!.Trim();
        WriteSeo(draft, seo);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("A metadata patch to page {PageId} lost a race and was refused.", page.Id);

            return CmsResult<PageDetail>.Conflict(
                PageCodes.ConcurrentChange,
                "This page was changed by someone else while your change was being saved. Reload " +
                "the page and apply the change again.");
        }

        var hasChildren = await context.Pages
            .AnyAsync(candidate => candidate.ParentId == page.Id, cancellationToken);

        return CmsResult<PageDetail>.Success(ToDetail(page, draft, hasChildren), checks);
    }

    /// <summary>
    /// Inserts the page and the draft version it starts life with, in one transaction.
    /// </summary>
    /// <param name="request">The page to create.</param>
    /// <param name="template">The template, already checked.</param>
    /// <param name="parent">The parent page, or null for a page at the root.</param>
    /// <param name="slug">The resolved slug.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The inserted page.</returns>
    /// <remarks>
    /// Three statements, because two of the values cannot be known until the rows exist: the
    /// materialized path contains the page's own id, and <see cref="Page.DraftVersionId"/> points at
    /// a row that itself points back (spec section 23.5). All three are one transaction — a page
    /// with no draft pointer is one no editor can open, and no query would report it as broken.
    /// </remarks>
    private async Task<Page> InsertAsync(
        CreatePageRequest request,
        Template template,
        Page? parent,
        string slug,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // A transient failure re-runs this whole lambda. Anything the failed attempt left in the
            // change tracker would be inserted a second time beside the fresh entities below, so the
            // slate is cleared first; nothing tracked reaches this method, since every query above
            // is AsNoTracking.
            context.ChangeTracker.Clear();

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var page = new Page
            {
                PublicId = Guid.NewGuid(),
                ParentId = parent?.Id,
                Slug = slug,
                // Overwritten below. The column is not nullable, and a path is only derivable once
                // the database has assigned the identity it contains.
                Path = string.Empty,
                Depth = (parent?.Depth ?? -1) + 1,
                SortOrder = await NextSortOrderAsync(parent?.Id, cancellationToken),
                TemplateId = template.Id,
                ShowInNavigation = request.ShowInNavigation,
            };

            context.Pages.Add(page);
            await context.SaveChangesAsync(cancellationToken);

            await tree.AttachAsync(page, cancellationToken);

            var draft = new PageVersion
            {
                PageId = page.Id,
                VersionNumber = 1,
                Status = PageVersionStatus.Draft,
                Title = request.Title!.Trim(),
                // Empty and schema-valid whatever the template requires: every zone is absent rather
                // than present-and-null, and a required zone blocks only a publish (spec section 8.3).
                ContentJson = ContentPayload
                    .CreateEmpty(template.Key, template.CurrentRevision)
                    .ToJson(),
                TemplateId = template.Id,
                TemplateRevision = template.CurrentRevision,
            };

            context.PageVersions.Add(draft);
            await context.SaveChangesAsync(cancellationToken);

            page.DraftVersionId = draft.Id;
            await context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return page;
        });
    }

    /// <summary>
    /// Reads a page and its draft into the shape the API returns.
    /// </summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The page, or null when there is none — or when it has no draft to show.</returns>
    private async Task<PageDetail?> LoadDetailAsync(int id, CancellationToken cancellationToken)
    {
        var page = await context.Pages
            .AsNoTracking()
            .Include(candidate => candidate.Template)
            .Include(candidate => candidate.DraftVersion)
            .Include(candidate => candidate.PublishedVersion)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (page is null) return null;

        // Null only between the statements of the creating transaction, which no reader can observe.
        // Reaching here without a draft means the row was written by something other than this
        // service, and there is nothing to open.
        if (page.DraftVersion is null)
        {
            logger.LogError("Page {PageId} has no draft version, so it cannot be opened.", page.Id);

            return null;
        }

        var hasChildren = await context.Pages
            .AnyAsync(candidate => candidate.ParentId == page.Id, cancellationToken);

        return ToDetail(page, page.DraftVersion, hasChildren);
    }

    /// <summary>Places a new page after its existing siblings.</summary>
    private async Task<int> NextSortOrderAsync(int? parentId, CancellationToken cancellationToken)
    {
        // Deleted siblings are excluded by the query filter, so a page created after a delete takes
        // the gap rather than a number beyond it. Ordering among siblings is a display concern and
        // duplicates are harmless; what matters is that a new page lands at the end.
        var highest = await context.Pages
            .Where(candidate => candidate.ParentId == parentId)
            .Select(candidate => (int?)candidate.SortOrder)
            .MaxAsync(cancellationToken);

        return (highest ?? -1) + 1;
    }

    /// <summary>
    /// Whether a live sibling already uses the slug.
    /// </summary>
    /// <param name="parentId">The parent whose children are checked, or null for the root.</param>
    /// <param name="slug">The slug being claimed.</param>
    /// <param name="excludingPageId">The page doing the claiming, so it does not collide with itself.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns><see langword="true"/> when the slug is taken.</returns>
    /// <remarks>
    /// Siblings, because a tree-derived URL is its ancestors' slugs joined and two siblings sharing
    /// a segment is the only way two of them can collide. Deleted siblings are deliberately not
    /// counted: holding a URL for a page in the recycle bin would make a delete irreversible in
    /// practice, and reconciling a restore against a slug taken in the meantime is
    /// <c>RecycleBinService</c>'s problem (spec section 14.10).
    /// <para>
    /// <strong>This is a check, not yet a guarantee.</strong> Phase 2's schema carries no unique
    /// index on <c>(ParentId, Slug)</c>, so two simultaneous creates can both pass it; what closes
    /// the window is <c>PageRoute.UrlHash WHERE IsPublished = 1</c> in P3-01, where a URL becomes a
    /// materialized row rather than something derived from the tree. Recorded here rather than
    /// papered over with a catch block, because there is currently no constraint for one to catch.
    /// </para>
    /// </remarks>
    private Task<bool> SiblingSlugExistsAsync(
        int? parentId,
        string slug,
        int? excludingPageId,
        CancellationToken cancellationToken) =>
        context.Pages.AnyAsync(
            candidate => candidate.ParentId == parentId &&
                candidate.Slug == slug &&
                candidate.Id != excludingPageId,
            cancellationToken);

    private static IReadOnlyList<ValidationDiagnostic> ValidateTitle(string? title, string path)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return
            [
                new ValidationDiagnostic(
                    PageCodes.TitleRequired,
                    "A title is required.",
                    ValidationSeverity.Error,
                    path),
            ];
        }

        if (title.Trim().Length > FieldLengths.ContentTitle)
        {
            return
            [
                new ValidationDiagnostic(
                    PageCodes.TooLong,
                    $"A title may be at most {FieldLengths.ContentTitle} characters.",
                    ValidationSeverity.Error,
                    path),
            ];
        }

        return [];
    }

    /// <summary>Checks the opt-out from tree-derived URLs (spec section 10.2).</summary>
    private static IReadOnlyList<ValidationDiagnostic> ValidateExplicitUrl(bool useExplicitUrl, string? explicitUrl)
    {
        const string UrlPath = nameof(PatchPageMetadataRequest.ExplicitUrl);

        if (!useExplicitUrl)
        {
            // A stored URL with the flag off is not an error — it is what a page that opted back
            // into the tree leaves behind, and the caller clears it rather than refusing the save.
            return [];
        }

        if (string.IsNullOrWhiteSpace(explicitUrl))
        {
            return
            [
                new ValidationDiagnostic(
                    PageCodes.ExplicitUrlMismatch,
                    "A page that states its own URL has to say what that URL is.",
                    ValidationSeverity.Error,
                    UrlPath),
            ];
        }

        if (explicitUrl.Length > FieldLengths.Url)
        {
            return
            [
                new ValidationDiagnostic(
                    PageCodes.TooLong,
                    $"A URL may be at most {FieldLengths.Url} characters.",
                    ValidationSeverity.Error,
                    UrlPath),
            ];
        }

        if (!explicitUrl.StartsWith('/') ||
            explicitUrl.Any(char.IsWhiteSpace) ||
            explicitUrl.Contains("//", StringComparison.Ordinal))
        {
            return
            [
                new ValidationDiagnostic(
                    PageCodes.ExplicitUrlFormat,
                    "A URL is site-relative: it starts with '/', contains no spaces, and has no " +
                    "empty segments.",
                    ValidationSeverity.Error,
                    UrlPath),
            ];
        }

        var firstSegment = explicitUrl.AsSpan(1);
        var end = firstSegment.IndexOf('/');
        if (end >= 0) firstSegment = firstSegment[..end];

        if (Slugs.Reserved.Contains(firstSegment.ToString()))
        {
            return
            [
                new ValidationDiagnostic(
                    PageCodes.SlugReserved,
                    $"'/{firstSegment}' is served by the application itself and cannot be claimed " +
                    "by a page.",
                    ValidationSeverity.Error,
                    UrlPath),
            ];
        }

        return [];
    }

    /// <summary>Checks the lengths and formats the SEO columns cannot enforce for themselves.</summary>
    private static List<ValidationDiagnostic> ValidateSeo(PageSeo seo, string? internalNotes)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        void CheckLength(string? value, int limit, string path)
        {
            if (value?.Length > limit)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    PageCodes.TooLong,
                    $"This value may be at most {limit} characters.",
                    ValidationSeverity.Error,
                    path));
            }
        }

        CheckLength(seo.MetaTitle, FieldLengths.EntityName, nameof(PageSeo.MetaTitle));
        CheckLength(seo.MetaDescription, FieldLengths.MetaDescription, nameof(PageSeo.MetaDescription));
        CheckLength(seo.CanonicalUrl, FieldLengths.Url, nameof(PageSeo.CanonicalUrl));
        CheckLength(seo.OgTitle, FieldLengths.EntityName, nameof(PageSeo.OgTitle));
        CheckLength(seo.OgDescription, FieldLengths.MetaDescription, nameof(PageSeo.OgDescription));
        CheckLength(seo.OgType, FieldLengths.SocialCardType, nameof(PageSeo.OgType));
        CheckLength(seo.TwitterCard, FieldLengths.SocialCardType, nameof(PageSeo.TwitterCard));
        CheckLength(seo.ChangeFreq, FieldLengths.ChangeFrequency, nameof(PageSeo.ChangeFreq));
        CheckLength(internalNotes, FieldLengths.InternalNotes, nameof(PatchPageMetadataRequest.InternalNotes));

        if (seo.Priority is { } priority && (priority < 0m || priority > 1m))
        {
            diagnostics.Add(new ValidationDiagnostic(
                PageCodes.OutOfRange,
                "A sitemap priority is between 0.0 and 1.0.",
                ValidationSeverity.Error,
                nameof(PageSeo.Priority)));
        }

        if (!string.IsNullOrWhiteSpace(seo.StructuredDataJson) && !IsWellFormedJson(seo.StructuredDataJson))
        {
            // Checked here rather than on the way out, because JSON-LD is emitted into the page head
            // and a malformed document reaches every visitor of a published page.
            diagnostics.Add(new ValidationDiagnostic(
                PageCodes.MalformedJson,
                "The structured data is not well-formed JSON.",
                ValidationSeverity.Error,
                nameof(PageSeo.StructuredDataJson)));
        }

        return diagnostics;
    }

    private static bool IsWellFormedJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Folds the SEO half of a patch onto what the draft currently holds.</summary>
    private static PageSeo ApplySeo(PatchPageMetadataRequest request, PageVersion draft) =>
        new(
            Clean(request.MetaTitle.Or(draft.MetaTitle)),
            Clean(request.MetaDescription.Or(draft.MetaDescription)),
            Clean(request.CanonicalUrl.Or(draft.CanonicalUrl)),
            request.RobotsIndex.Or(draft.RobotsIndex),
            request.RobotsFollow.Or(draft.RobotsFollow),
            Clean(request.OgTitle.Or(draft.OgTitle)),
            Clean(request.OgDescription.Or(draft.OgDescription)),
            draft.OgImageMediaId,
            Clean(request.OgType.Or(draft.OgType)),
            Clean(request.TwitterCard.Or(draft.TwitterCard)),
            Clean(request.StructuredDataJson.Or(draft.StructuredDataJson)),
            Clean(request.ChangeFreq.Or(draft.ChangeFreq)),
            request.Priority.Or(draft.Priority));

    private static void WriteSeo(PageVersion draft, PageSeo seo)
    {
        draft.MetaTitle = seo.MetaTitle;
        draft.MetaDescription = seo.MetaDescription;
        draft.CanonicalUrl = seo.CanonicalUrl;
        draft.RobotsIndex = seo.RobotsIndex;
        draft.RobotsFollow = seo.RobotsFollow;
        draft.OgTitle = seo.OgTitle;
        draft.OgDescription = seo.OgDescription;
        draft.OgType = seo.OgType;
        draft.TwitterCard = seo.TwitterCard;
        draft.StructuredDataJson = seo.StructuredDataJson;
        draft.ChangeFreq = seo.ChangeFreq;
        draft.Priority = seo.Priority;
    }

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

    private static PageDetail ToDetail(Page page, PageVersion draft, bool hasChildren) =>
        new(
            new PageSummary(
                page.Id,
                page.PublicId,
                page.ParentId,
                draft.Title,
                page.Slug,
                page.Depth,
                page.SortOrder,
                page.TemplateId,
                page.Template.Key,
                draft.Status.ToString(),
                HasUnpublishedChanges(draft, page.PublishedVersion),
                draft.VersionNumber,
                page.PublishedVersion?.VersionNumber,
                page.ShowInNavigation,
                hasChildren,
                page.ModifiedOn ?? page.CreatedOn),
            draft.ContentJson,
            draft.TemplateRevision,
            page.UseExplicitUrl,
            page.ExplicitUrl,
            page.OwnerUserId,
            page.ReviewByDate,
            page.InternalNotes,
            ReadSeo(draft),
            Convert.ToBase64String(draft.RowVersion ?? []));

    /// <summary>
    /// Whether the draft has moved on from what is published.
    /// </summary>
    /// <remarks>
    /// Compared by timestamp rather than by payload: publishing snapshots the draft into a new row,
    /// so a version created at publish time is never older than the draft it was copied from, and
    /// any later edit to the draft moves it past. Comparing the payloads instead would mean reading
    /// two JSON documents per row of a page list.
    /// </remarks>
    private static bool HasUnpublishedChanges(PageVersion draft, PageVersion? published) =>
        published is not null &&
        (draft.ModifiedOn ?? draft.CreatedOn) > (published.CreatedOn ?? DateTimeOffset.MinValue);

    /// <summary>Trims a value and treats whitespace as absent, so a cleared box stores as null.</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CmsResult<PageDetail> NotFound(int id) =>
        CmsResult<PageDetail>.NotFound($"No page has id {id}.", PageCodes.NotFound);

    private static CmsResult<PageDetail> Forbidden(string message) =>
        CmsResult<PageDetail>.Forbidden(message, PageCodes.Forbidden);

    private static CmsResult<PageDetail> DuplicateSlug(string slug, int? parentId) =>
        CmsResult<PageDetail>.Conflict(
            PageCodes.SlugDuplicate,
            parentId is null
                ? $"A page at the root of the site already uses the segment '{slug}'."
                : $"Another child of page {parentId} already uses the segment '{slug}'.",
            nameof(CreatePageRequest.Slug));
}
