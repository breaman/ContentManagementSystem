using System.Text.Json;

using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Routing;
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

/// <inheritdoc cref="IPageService" />
/// <param name="context">The application database context.</param>
/// <param name="tree">Owns the materialized path; nothing else may write it.</param>
/// <param name="urls">Owns the route table; every change to a slug or an explicit URL goes through it.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="acl">Where in the tree the caller may do it (task P7-06, spec section 21.2).</param>
/// <param name="clock">Reads the current time, so a lock's expiry is testable.</param>
/// <param name="logger">Log for page creation and for saves that lost a race.</param>
/// <remarks>
/// Every entry point asks two questions in the same order: may this caller do this at all, and may
/// they do it <em>here</em>. The second is asked on the id the caller supplied rather than on
/// anything derived from it, because an id in a request is a guess until something has checked it —
/// which is exactly what the IDOR sweep in task P7-07 goes looking for.
/// <para>
/// A refusal of <c>Content.Read</c> answers <em>not found</em> rather than <em>forbidden</em>: spec
/// section 21.2 says a subtree with read denied is hidden entirely, and a 403 that a 404 would not
/// have produced tells the caller the page exists. Refusals of every other permission answer
/// forbidden, because by then the caller has already been told the page is there.
/// </para>
/// </remarks>
public sealed class PageService(
    ApplicationDbContext context,
    IPageTreeService tree,
    IUrlService urls,
    ICmsAuthorization authorization,
    IAclService acl,
    TimeProvider clock,
    ILogger<PageService> logger) : IPageService
{
    /// <summary>Escape character used with <c>LIKE</c>, so a search term's wildcards are literal.</summary>
    private const string LikeEscape = "\\";

    /// <summary>Stands in for the synthetic site root when grouping pages by parent.</summary>
    private const int RootKey = 0;

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

        // A new page inherits its parent's access rules the moment it exists, so the rule that
        // governs creating it is the parent's. Creating at the root has no parent to ask and is
        // therefore governed by the global grant alone — which is the correct reading: an editor
        // confined to /products has an allow rule, and an allow rule refuses everywhere it does not
        // reach, the site root included.
        if (parent is not null
            && !await acl.IsAllowedAsync(CmsPermissions.ContentEdit, parent.Id, cancellationToken))
        {
            return Forbidden($"Adding pages under page {parent.Id} is not permitted.");
        }

        if (parent is null && !await acl.IsAllowedAtRootAsync(CmsPermissions.ContentEdit, cancellationToken))
        {
            return Forbidden("Adding pages at the root of the site is not permitted.");
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

        // Read refusals are indistinguishable from absence on purpose; see the class remarks.
        if (!await acl.IsAllowedAsync(CmsPermissions.ContentRead, id, cancellationToken))
        {
            return NotFound(id);
        }

        var detail = await LoadDetailAsync(id, cancellationToken);

        return detail is null ? NotFound(id) : CmsResult<PageDetail>.Success(detail);
    }

    /// <inheritdoc />
    public async Task<CmsResult<CursorPage<PageSummary>>> ListAsync(
        PageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<CursorPage<PageSummary>>.Forbidden(
                "Reading pages is not permitted.",
                PageCodes.Forbidden);
        }

        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CmsResult<CursorPage<PageSummary>>.Invalid(
                PageCodes.FilterInvalid,
                "That cursor could not be read. Start from the first page rather than assembling " +
                "one by hand.",
                nameof(PageQuery.Cursor));
        }

        PageVersionStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            // Refused rather than ignored: a mistyped status that quietly matched everything would
            // read as "every page is a draft", which is a far more expensive thing to debug.
            if (!Enum.TryParse(query.Status, ignoreCase: true, out PageVersionStatus parsed))
            {
                return CmsResult<CursorPage<PageSummary>>.Invalid(
                    PageCodes.FilterInvalid,
                    $"'{query.Status}' is not a page status. Try one of: " +
                    $"{string.Join(", ", Enum.GetNames<PageVersionStatus>())}.",
                    nameof(PageQuery.Status));
            }

            status = parsed;
        }

        var limit = Cursor.Clamp(query.Limit);

        var pages = context.Pages
            .AsNoTracking()
            .Include(candidate => candidate.Template)
            .Include(candidate => candidate.DraftVersion)
            .Include(candidate => candidate.PublishedVersion)
            .Where(candidate => candidate.Id > after);

        if (query.RootOnly)
        {
            pages = pages.Where(candidate => candidate.ParentId == null);
        }
        else if (query.ParentId is { } parentId)
        {
            pages = pages.Where(candidate => candidate.ParentId == parentId);
        }

        if (query.TemplateId is { } templateId)
        {
            pages = pages.Where(candidate => candidate.TemplateId == templateId);
        }

        if (status is { } wanted)
        {
            pages = pages.Where(candidate => candidate.DraftVersion!.Status == wanted);
        }

        if (query.ModifiedAfter is { } modifiedAfter)
        {
            pages = pages.Where(candidate =>
                (candidate.ModifiedOn ?? candidate.CreatedOn) > modifiedAfter);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var trimmed = query.Search.Trim();
            var term = $"%{Escape(trimmed)}%";

            // A term that is entirely digits also matches on identity. The tree's filter box is the
            // one place an editor arrives holding a page id — from a log line, a ticket, or a URL —
            // and a filter that answered "no results" for the id it was handed would be read as the
            // page being gone (spec section 14.2).
            var byId = int.TryParse(trimmed, out var id) ? id : (int?)null;

            pages = pages.Where(candidate =>
                EF.Functions.Like(candidate.DraftVersion!.Title, term, LikeEscape) ||
                EF.Functions.Like(candidate.Slug, term, LikeEscape) ||
                (byId != null && candidate.Id == byId));
        }

        // One row beyond the limit, so that "is there a next page" is answered by the same query
        // rather than by a count that would have to scan the whole filtered set.
        var found = await pages
            .OrderBy(candidate => candidate.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = found.Count > limit;
        if (hasMore) found.RemoveAt(found.Count - 1);

        // Taken before the access rules cut the list, because the cursor has to name the last row
        // that was *read*. Naming the last row that survived filtering would hand back a cursor
        // pointing at a page already served, and the client would loop over it forever whenever the
        // tail of a page was hidden.
        var lastRead = found.Count > 0 ? found[^1].Id : (int?)null;

        // Applied after the page has been cut rather than pushed into the query, which would mean
        // translating "deeper beats shallower, deny beats allow" into SQL and keeping two copies of
        // the precedence rules in step. The cost is that a page of results can come back short when
        // rules hide part of it; the cursor still advances by the last row read, so paging stays
        // correct and the client simply follows it.
        var readable = await acl.GetFilterAsync(CmsPermissions.ContentRead, cancellationToken);

        if (!readable.IsUnrestricted)
        {
            found = [.. found.Where(page => readable.Allows(page.Id, page.Path))];
        }

        var withChildren = await ParentsWithChildrenAsync(found, cancellationToken);
        var locks = await LiveLocksAsync([.. found.Select(page => page.Id)], cancellationToken);

        var items = found
            .Where(page => page.DraftVersion is not null)
            .Select(page => ToSummary(
                page,
                page.DraftVersion!,
                withChildren.Contains(page.Id),
                LockedBy(locks, page.Id)))
            .ToList();

        var next = hasMore && lastRead is { } cursor ? Cursor.Encode(cursor) : null;

        return CmsResult<CursorPage<PageSummary>>.Success(new CursorPage<PageSummary>(items, next));
    }

    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<PageTreeNode>>> TreeAsync(
        int? parentId,
        int depth = 1,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<PageTreeNode>>.Forbidden(
                "Reading pages is not permitted.",
                PageCodes.Forbidden);
        }

        var levels = Math.Clamp(depth, 1, IPageService.MaxTreeDepth);

        Page? parent = null;
        if (parentId is { } id)
        {
            parent = await context.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

            if (parent is null || !await acl.IsAllowedAsync(CmsPermissions.ContentRead, id, cancellationToken))
            {
                return CmsResult<IReadOnlyList<PageTreeNode>>.NotFound(
                    $"No page has id {id}.",
                    PageCodes.NotFound);
            }
        }

        // The root level is depth 0, so a page's own depth already counts the levels above it and
        // the bound below is an absolute depth rather than a relative one.
        var deepest = (parent?.Depth ?? -1) + levels;

        var descendants = context.Pages
            .AsNoTracking()
            .Include(candidate => candidate.Template)
            .Include(candidate => candidate.DraftVersion)
            .Include(candidate => candidate.PublishedVersion)
            .Where(candidate => candidate.Depth <= deepest);

        if (parent is not null)
        {
            var prefix = $"{parent.Path}%";

            // One indexed prefix match instead of a query per level, which is the whole reason the
            // path is materialized. Paths hold only digits and slashes, so nothing in them is a LIKE
            // metacharacter needing an escape.
            descendants = descendants.Where(candidate =>
                candidate.Id != parent.Id && EF.Functions.Like(candidate.Path, prefix));
        }

        var found = await descendants
            .OrderBy(candidate => candidate.Depth)
            .ThenBy(candidate => candidate.SortOrder)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        // A subtree the caller may not read is gone from the tree, not greyed out in it (criterion
        // P7 #6). Filtering the flat list is enough to take the descendants with it: the nodes below
        // a hidden page are only ever attached to the answer through their parent, so a parent that
        // never gets built is a branch that never appears. They are dropped here anyway, because an
        // inherited rule reaches them and this is one prefix test each rather than a query.
        var readable = await acl.GetFilterAsync(CmsPermissions.ContentRead, cancellationToken);

        if (!readable.IsUnrestricted)
        {
            found = [.. found.Where(page => readable.Allows(page.Id, page.Path))];
        }

        var withChildren = await ParentsWithChildrenAsync(found, cancellationToken);
        var locks = await LiveLocksAsync([.. found.Select(page => page.Id)], cancellationToken);

        // Keyed on the parent's id, with the synthetic site root as zero — no page has that identity,
        // and a nullable key would need its own comparer to say the same thing.
        var byParent = new Dictionary<int, List<PageTreeNode>>();

        // Walked deepest-first, so a node's children are already assembled by the time it is built —
        // the depth ordering above is what makes one pass enough.
        foreach (var page in Enumerable.Reverse(found))
        {
            if (page.DraftVersion is null) continue;

            var children = byParent.TryGetValue(page.Id, out var assembled) ? assembled : [];
            var node = new PageTreeNode(
                ToSummary(
                    page,
                    page.DraftVersion,
                    withChildren.Contains(page.Id),
                    LockedBy(locks, page.Id)),
                children);

            var key = page.ParentId ?? RootKey;

            if (!byParent.TryGetValue(key, out var siblings))
            {
                siblings = [];
                byParent[key] = siblings;
            }

            // Prepended, because the walk is reversed: siblings come back out in the order queried.
            siblings.Insert(0, node);
        }

        var roots = byParent.TryGetValue(parentId ?? RootKey, out var top) ? top : [];

        return CmsResult<IReadOnlyList<PageTreeNode>>.Success(roots);
    }

    /// <inheritdoc />
    public async Task<CmsResult<PageDetail>> PatchMetadataAsync(
        int id,
        PatchPageMetadataRequest request,
        string? expectedRowVersion = null,
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

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentEdit, id, cancellationToken))
        {
            return Forbidden($"Editing page {id} is not permitted.");
        }

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

        // Captured before the assignment, because a rebuild is only worth its queries when one of
        // the three things a URL is made of actually moved. A patch that changes a review date must
        // not walk the page's whole subtree.
        var urlAffecting =
            !string.Equals(slug, page.Slug, StringComparison.Ordinal) ||
            useExplicitUrl != page.UseExplicitUrl ||
            (useExplicitUrl && !string.Equals(explicitUrl, page.ExplicitUrl, StringComparison.Ordinal));

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

        if (RowVersions.TryApply(context.Entry(draft), expectedRowVersion) is false)
        {
            return CmsResult<PageDetail>.Invalid(
                PageCodes.ConcurrentChange,
                "The supplied row version is not a value this server issued.",
                nameof(PageDetail.RowVersion));
        }

        // The route rebuild reads the page back from the change tracker, so it sees the slug and
        // explicit URL just assigned. It writes route rows and redirects into the same change
        // tracker, which the SaveChanges below commits alongside the page itself — a slug that
        // changed while its URL did not is the defect this ordering exists to prevent.
        if (urlAffecting)
        {
            var sync = await urls.SyncAsync(page.Id, cancellationToken);

            if (sync.HasErrors) return CmsResult<PageDetail>.Conflict(sync.Diagnostics);

            if (sync.Changes.Count > 0)
            {
                logger.LogInformation(
                    "Page {PageId} moved {Count} URL(s) including its descendants.",
                    page.Id,
                    sync.Changes.Count);
            }
        }

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

        var locks = await LiveLocksAsync([page.Id], cancellationToken);

        return CmsResult<PageDetail>.Success(
            ToDetail(
                page,
                draft,
                hasChildren,
                LockedBy(locks, page.Id),
                await OwnerNameAsync(page.OwnerUserId, cancellationToken)),
            checks);
    }

    /// <inheritdoc />
    public async Task<CmsResult<PageMoveResult>> MoveAsync(
        int id,
        MovePageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return CmsResult<PageMoveResult>.Forbidden(
                "Moving pages is not permitted.",
                PageCodes.Forbidden);
        }

        var subject = await context.Pages
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (subject is null)
        {
            return CmsResult<PageMoveResult>.NotFound($"No page has id {id}.", PageCodes.NotFound);
        }

        // Both ends of the move are checked. Only asking about the page being moved would let an
        // editor confined to /products take a page out of it and drop it anywhere, and only asking
        // about the destination would let them take one out of a branch they may not touch.
        if (!await acl.IsAllowedAsync(CmsPermissions.ContentEdit, id, cancellationToken))
        {
            return CmsResult<PageMoveResult>.Forbidden(
                $"Moving page {id} is not permitted.",
                PageCodes.Forbidden);
        }

        var allowedAtDestination = request.ParentId is { } destination
            ? await acl.IsAllowedAsync(CmsPermissions.ContentEdit, destination, cancellationToken)
            : await acl.IsAllowedAtRootAsync(CmsPermissions.ContentEdit, cancellationToken);

        if (!allowedAtDestination)
        {
            return CmsResult<PageMoveResult>.Forbidden(
                request.ParentId is { } refused
                    ? $"Moving pages under page {refused} is not permitted."
                    : "Moving pages to the root of the site is not permitted.",
                PageCodes.Forbidden);
        }

        if (request.ParentId is { } parentId &&
            !await context.Pages.AnyAsync(candidate => candidate.Id == parentId, cancellationToken))
        {
            return CmsResult<PageMoveResult>.Invalid(
                PageCodes.ParentNotFound,
                $"No page has id {parentId}, or it is in the recycle bin.",
                nameof(MovePageRequest.ParentId));
        }

        // Checked before the transaction, because the refusal is about the page's own slug rather
        // than about anything the move computes: two children of one parent cannot share a URL
        // segment, and a move is the one operation that can create that collision without anybody
        // having typed a slug (spec section 10.3).
        if (request.ParentId != subject.ParentId &&
            await SiblingSlugExistsAsync(request.ParentId, subject.Slug, id, cancellationToken))
        {
            return CmsResult<PageMoveResult>.Conflict(
                PageCodes.SlugDuplicate,
                request.ParentId is null
                    ? $"A page at the root of the site already uses the segment '{subject.Slug}'."
                    : $"Another child of page {request.ParentId} already uses the segment " +
                      $"'{subject.Slug}'. Rename this page before moving it there.",
                nameof(MovePageRequest.ParentId));
        }

        return await ApplyMoveAsync(id, request, cancellationToken);
    }

    /// <summary>
    /// Runs the move inside a transaction, committing it or — for a preview — rolling it back.
    /// </summary>
    /// <param name="id">Identity of the page to move.</param>
    /// <param name="request">The new parent and position, and whether this is a preview.</param>
    /// <param name="cancellationToken">Token observed throughout.</param>
    /// <returns>What the move did, or the reason it was refused.</returns>
    private async Task<CmsResult<PageMoveResult>> ApplyMoveAsync(
        int id,
        MovePageRequest request,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Re-read inside the lambda, for the reason InsertAsync gives: a transient failure
            // re-runs all of this, and entities tracked by the failed attempt would be written on
            // top of the retry's own.
            context.ChangeTracker.Clear();

            await using var transaction =
                await context.Database.BeginTransactionAsync(cancellationToken);

            var page = await context.Pages.FirstAsync(
                candidate => candidate.Id == id,
                cancellationToken);

            var outcome = await tree.MoveAsync(page, request.ParentId, cancellationToken);

            if (outcome is not PageMoveOutcome.Moved)
            {
                await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();

                return Refuse(outcome, request.ParentId);
            }

            await ReorderSiblingsAsync(page, request.Position, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            // Rebuilds the routes of the page and every descendant, emitting a redirect at each URL
            // vacated. This is the whole reason a move needs a confirmation: one drag can rewrite
            // the addresses of a subtree that other sites link to.
            var sync = await urls.SyncAsync(page.Id, cancellationToken);

            if (sync.HasErrors)
            {
                await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();

                return CmsResult<PageMoveResult>.Invalid(sync.Diagnostics);
            }

            await context.SaveChangesAsync(cancellationToken);

            // Described before the rollback: the titles are read through the same transaction, so a
            // preview names the pages as they stand rather than as they were before it started.
            var changes = await DescribeAsync(sync.Changes, cancellationToken);

            var result = new PageMoveResult(
                page.Id,
                page.ParentId,
                page.SortOrder,
                changes,
                request.Preview);

            if (request.Preview)
            {
                await transaction.RollbackAsync(cancellationToken);

                // Without this the tracker still holds every row the preview changed, and the next
                // SaveChanges in this request scope would write a move nobody confirmed.
                context.ChangeTracker.Clear();
            }
            else
            {
                await transaction.CommitAsync(cancellationToken);

                logger.LogInformation(
                    "Page {PageId} moved under {ParentId} at position {Position}; " +
                    "{ChangeCount} URL(s) changed and {RedirectCount} redirect(s) created.",
                    page.Id,
                    page.ParentId,
                    page.SortOrder,
                    result.UrlChanges.Count,
                    result.RedirectCount);
            }

            return CmsResult<PageMoveResult>.Success(result);
        });
    }

    /// <summary>Turns a tree service refusal into something an editor can act on.</summary>
    private static CmsResult<PageMoveResult> Refuse(PageMoveOutcome outcome, int? parentId) =>
        outcome switch
        {
            PageMoveOutcome.ParentNotFound => CmsResult<PageMoveResult>.Invalid(
                PageCodes.ParentNotFound,
                $"No page has id {parentId}, or it is in the recycle bin.",
                nameof(MovePageRequest.ParentId)),

            PageMoveOutcome.WouldCreateCycle => CmsResult<PageMoveResult>.Invalid(
                PageCodes.MoveWouldCreateCycle,
                "A page cannot be moved inside itself or one of its own descendants.",
                nameof(MovePageRequest.ParentId)),

            PageMoveOutcome.TooDeep => CmsResult<PageMoveResult>.Invalid(
                PageCodes.TooDeep,
                $"That move would put some page more than {IPageTreeService.MaxDepth} levels below " +
                "the root.",
                nameof(MovePageRequest.ParentId)),

            _ => CmsResult<PageMoveResult>.Invalid(
                PageCodes.ParentNotFound,
                "That move could not be applied.",
                nameof(MovePageRequest.ParentId)),
        };

    /// <summary>
    /// Renumbers a level so the moved page sits at the requested position.
    /// </summary>
    /// <param name="moving">The page, already reparented and tracked.</param>
    /// <param name="position">Where it should land, or null to append it.</param>
    /// <param name="cancellationToken">Token observed while loading the siblings.</param>
    /// <remarks>
    /// The whole level is renumbered from zero rather than the moved page being given a fractional
    /// or a duplicate order. Sibling order is small, bounded by one level, and rewritten inside the
    /// same transaction — and a level whose numbers are exactly its positions is one where "move
    /// this up one" is arithmetic rather than a search for a gap.
    /// </remarks>
    private async Task ReorderSiblingsAsync(
        Page moving,
        int? position,
        CancellationToken cancellationToken)
    {
        var siblings = await context.Pages
            .Where(candidate => candidate.ParentId == moving.ParentId && candidate.Id != moving.Id)
            .OrderBy(candidate => candidate.SortOrder)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        // Clamped rather than refused. A position past the end is what a drag to the bottom of a
        // level produces, and "put it last" is unambiguously what was meant.
        var index = Math.Clamp(position ?? siblings.Count, 0, siblings.Count);

        siblings.Insert(index, moving);

        for (var order = 0; order < siblings.Count; order++)
        {
            siblings[order].SortOrder = order;
        }
    }

    /// <summary>Names the pages a route rebuild moved, so a confirmation can list them.</summary>
    private async Task<IReadOnlyList<PageUrlChangeSummary>> DescribeAsync(
        IReadOnlyList<PageUrlChange> changes,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0) return [];

        var ids = changes.Select(change => change.PageId).ToList();

        var titles = await context.Pages
            .AsNoTracking()
            .Where(candidate => ids.Contains(candidate.Id) && candidate.DraftVersion != null)
            .Select(candidate => new { candidate.Id, candidate.DraftVersion!.Title })
            .ToDictionaryAsync(row => row.Id, row => row.Title, cancellationToken);

        return
        [
            .. changes.Select(change => new PageUrlChangeSummary(
                change.PageId,
                titles.TryGetValue(change.PageId, out var title) ? title : $"Page {change.PageId}",
                change.OldUrl,
                change.NewUrl,
                change.RedirectCreated)),
        ];
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
                VersionNumber = VersionNumbers.First,
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

            // The page is not published, so this materializes only its draft route — which is what
            // lets preview address the page by URL from the moment it exists, rather than from the
            // moment it is first published (spec section 10.4). It cannot collide with anything:
            // the filtered unique index governs published routes alone.
            await urls.SyncAsync(page.Id, cancellationToken);
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

        var locks = await LiveLocksAsync([page.Id], cancellationToken);

        return ToDetail(
            page,
            page.DraftVersion,
            hasChildren,
            LockedBy(locks, page.Id),
            await OwnerNameAsync(page.OwnerUserId, cancellationToken));
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

    /// <summary>
    /// Finds which of a loaded set of pages have live children.
    /// </summary>
    /// <param name="pages">The pages just read.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>Identities of the pages that have at least one child.</returns>
    /// <remarks>
    /// One query for the whole set rather than a correlated subquery per row: the flag drives a
    /// tree control's expander, so it is needed for every row of every list and a per-row lookup
    /// would make a fifty-page list fifty-one round trips.
    /// </remarks>
    private async Task<HashSet<int>> ParentsWithChildrenAsync(
        IReadOnlyCollection<Page> pages,
        CancellationToken cancellationToken)
    {
        if (pages.Count == 0) return [];

        var ids = pages.Select(page => page.Id).ToList();

        var parents = await context.Pages
            .AsNoTracking()
            .Where(candidate => candidate.ParentId != null && ids.Contains(candidate.ParentId.Value))
            .Select(candidate => candidate.ParentId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. parents];
    }

    /// <summary>
    /// Finds who currently has any of these pages open in the editor (task P6-02).
    /// </summary>
    /// <param name="ids">Identities of the pages being projected.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>Page identity to the holder's display name, for live locks only.</returns>
    /// <remarks>
    /// One query for the whole set, for the reason <see cref="ParentsWithChildrenAsync"/> gives. The
    /// expiry cutoff is applied here rather than left to the reaper, exactly as
    /// <c>EditLockService</c> does: a tree that showed a padlock because nothing had swept the table
    /// in the last minute would teach editors to ignore the padlock.
    /// </remarks>
    private async Task<Dictionary<int, string?>> LiveLocksAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return [];

        var cutoff = clock.GetUtcNow() - IEditLockService.Expiry;

        var held = await context.EditLocks
            .AsNoTracking()
            .Where(candidate => ids.Contains(candidate.PageId) && candidate.HeartbeatOn > cutoff)
            .Select(candidate => new { candidate.PageId, candidate.User.UserName })
            .ToListAsync(cancellationToken);

        return held.ToDictionary(row => row.PageId, row => row.UserName);
    }

    /// <summary>
    /// Resolves an owner's display name.
    /// </summary>
    /// <remarks>
    /// A separate query rather than an <c>Include</c> on the owner navigation, for two reasons: it is
    /// one small read on a single page rather than a join on every path that loads one, and it gets
    /// the answer right in the patch path, where the owner has just been reassigned and the loaded
    /// navigation would still be pointing at whoever held it before.
    /// </remarks>
    private async Task<string?> OwnerNameAsync(int? ownerId, CancellationToken cancellationToken)
    {
        if (ownerId is not { } id) return null;

        var name = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => user.UserName)
            .FirstOrDefaultAsync(cancellationToken);

        // A page whose owner's account has since been removed still has an owner id. Saying so is
        // better than saying nothing, which reads as "unassigned" and invites nobody to fix it.
        return name ?? $"user {id}";
    }

    /// <summary>Reads one page's holder out of a lock lookup.</summary>
    private static string? LockedBy(Dictionary<int, string?> locks, int pageId) =>
        locks.TryGetValue(pageId, out var holder) ? holder ?? "another editor" : null;

    /// <summary>Escapes the wildcards of a caller-supplied search term (spec section 22.1).</summary>
    /// <remarks>
    /// Without this, a term containing <c>%</c> matches everything and one containing <c>_</c>
    /// matches more than it should — not an injection, but a filter that quietly stops filtering.
    /// </remarks>
    private static string Escape(string term) => term
        .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
        .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
        .Replace("_", LikeEscape + "_", StringComparison.Ordinal)
        .Replace("[", LikeEscape + "[", StringComparison.Ordinal);

    private static PageSummary ToSummary(
        Page page,
        PageVersion draft,
        bool hasChildren,
        string? lockedBy = null) =>
        new(
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
            page.ModifiedOn ?? page.CreatedOn,
            draft.PublishOn,
            lockedBy);

    private static PageDetail ToDetail(
        Page page,
        PageVersion draft,
        bool hasChildren,
        string? lockedBy = null,
        string? ownerName = null) =>
        new(
            ToSummary(page, draft, hasChildren, lockedBy),
            draft.ContentJson,
            draft.TemplateRevision,
            page.UseExplicitUrl,
            page.ExplicitUrl,
            page.OwnerUserId,
            page.ReviewByDate,
            page.InternalNotes,
            ReadSeo(draft),
            Convert.ToBase64String(draft.RowVersion ?? []),
            ownerName);

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
