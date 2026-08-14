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

/// <summary>
/// Copies a page, or a whole section of the tree (task P2-09, spec section 14.12).
/// </summary>
/// <remarks>
/// Three rules make a duplicate useful rather than a source of confusion. A copy starts at
/// <em>version 1</em> with no history — the original's history belongs to the original. Media is
/// referenced, never duplicated. And links <strong>between pages inside the copied subtree</strong>
/// are rewritten to point at the new copies, while links out of it are left alone.
/// <para>
/// That last rule is the whole point of a deep copy: "duplicate last year's campaign for this year"
/// produces a section that navigates to itself, instead of one whose every internal link takes the
/// visitor back into last year's pages.
/// </para>
/// </remarks>
public interface IDuplicationService
{
    /// <summary>
    /// Copies a page, and optionally everything beneath it.
    /// </summary>
    /// <param name="pageId">Identity of the page to copy.</param>
    /// <param name="deep">Whether to copy the subtree as well.</param>
    /// <param name="parentId">
    /// Where the copy goes. Null puts it beside the original, which is what the backoffice's
    /// "duplicate" does; a value moves it elsewhere in one step.
    /// </param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The new page, or the reason it could not be made.</returns>
    Task<CmsResult<PageDetail>> DuplicateAsync(
        int pageId,
        bool deep = false,
        int? parentId = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IDuplicationService" />
/// <param name="context">The application database context.</param>
/// <param name="tree">Owns the materialized path; nothing else may write it.</param>
/// <param name="pages">Reads back the created page in the shape the API returns.</param>
/// <param name="remapper">Rewrites intra-subtree references in a copied payload.</param>
/// <param name="referenceProjector">Rewrites each copy's reference rows from its payload.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="logger">Log for what was copied and how much of it.</param>
public sealed class DuplicationService(
    ApplicationDbContext context,
    IPageTreeService tree,
    IPageService pages,
    IContentPayloadRemapper remapper,
    IContentReferenceProjector referenceProjector,
    ICmsAuthorization authorization,
    ILogger<DuplicationService> logger) : IDuplicationService
{
    /// <summary>Suffix appended to a copied page's title.</summary>
    public const string CopySuffix = " (copy)";

    /// <inheritdoc />
    public async Task<CmsResult<PageDetail>> DuplicateAsync(
        int pageId,
        bool deep = false,
        int? parentId = null,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return CmsResult<PageDetail>.Forbidden("Creating pages is not permitted.", PageCodes.Forbidden);
        }

        var source = await context.Pages
            .AsNoTracking()
            .Include(page => page.DraftVersion)
            .FirstOrDefaultAsync(page => page.Id == pageId, cancellationToken);

        if (source?.DraftVersion is null)
        {
            return CmsResult<PageDetail>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);
        }

        var targetParentId = parentId ?? source.ParentId;
        var targetParent = targetParentId is null
            ? null
            : await context.Pages
                .AsNoTracking()
                .FirstOrDefaultAsync(page => page.Id == targetParentId, cancellationToken);

        if (targetParentId is not null && targetParent is null)
        {
            return CmsResult<PageDetail>.Invalid(
                PageCodes.ParentNotFound,
                $"No page has id {targetParentId}, or it is in the recycle bin.",
                nameof(parentId));
        }

        // A deep copy under one of the pages being copied would copy the copies. The tree makes that
        // an indexed prefix check rather than a walk.
        if (deep && targetParent is not null &&
            targetParent.Path.StartsWith(source.Path, StringComparison.Ordinal))
        {
            return CmsResult<PageDetail>.Invalid(
                PageCodes.ParentNotFound,
                "A section cannot be duplicated into itself.",
                nameof(parentId));
        }

        var sources = deep
            ? await LoadSubtreeAsync(source, cancellationToken)
            : [source];

        var depthBudget = (targetParent?.Depth ?? -1) + 1 +
            (sources.Max(page => page.Depth) - source.Depth);

        if (depthBudget > IPageTreeService.MaxDepth)
        {
            return CmsResult<PageDetail>.Invalid(
                PageCodes.TooDeep,
                $"The copy would sit deeper than the {IPageTreeService.MaxDepth} levels the tree allows.",
                nameof(parentId));
        }

        var slug = await FindFreeSlugAsync(targetParentId, source.Slug, cancellationToken);

        if (Slugs.Validate(slug, targetParentId is null).HasErrors)
        {
            return CmsResult<PageDetail>.Invalid(
                PageCodes.SlugDuplicate,
                $"No free URL segment could be derived from '{source.Slug}'. Rename the original " +
                "or copy it somewhere else.");
        }

        var rootId = await CopyAsync(sources, source, targetParentId, slug, cancellationToken);

        logger.LogInformation(
            "Page {SourceId} was duplicated to page {CopyId} with {DescendantCount} descendants.",
            pageId,
            rootId,
            sources.Count - 1);

        return await pages.GetAsync(rootId, cancellationToken);
    }

    /// <summary>
    /// Writes every copy and then rewrites the links between them, in one transaction.
    /// </summary>
    /// <returns>Identity of the new root page.</returns>
    /// <remarks>
    /// Two passes, and it has to be two: a link inside the subtree can only be rewritten once every
    /// page in it has an identity, and identities are assigned on insert. A single pass would have
    /// to guess at the ids it was about to create.
    /// </remarks>
    private async Task<int> CopyAsync(
        IReadOnlyList<Page> sources,
        Page root,
        int? targetParentId,
        string rootSlug,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var map = new Dictionary<int, int>();
            var copies = new List<(Page Copy, PageVersion Draft)>(sources.Count);

            // Depth order, so a page's parent has always been created — and therefore has an id —
            // by the time the page itself is.
            foreach (var sourcePage in sources.OrderBy(page => page.Depth).ThenBy(page => page.SortOrder))
            {
                var isRoot = sourcePage.Id == root.Id;
                var parent = isRoot
                    ? targetParentId
                    : map.TryGetValue(sourcePage.ParentId ?? 0, out var mapped) ? mapped : targetParentId;

                var copy = new Page
                {
                    PublicId = Guid.NewGuid(),
                    ParentId = parent,
                    // Descendants keep their own segments: they land under a new parent, so nothing
                    // they could collide with is in scope. Only the root needs a free one.
                    Slug = isRoot ? rootSlug : sourcePage.Slug,
                    Path = string.Empty,
                    Depth = 0,
                    SortOrder = sourcePage.SortOrder,
                    TemplateId = sourcePage.TemplateId,
                    ShowInNavigation = sourcePage.ShowInNavigation,
                    UseExplicitUrl = false,
                    // Deliberately dropped. An explicit URL is unique by construction, and copying
                    // one would give two pages the same address the moment the copy was published.
                    ExplicitUrl = null,
                    OwnerUserId = sourcePage.OwnerUserId,
                    ReviewByDate = sourcePage.ReviewByDate,
                    InternalNotes = sourcePage.InternalNotes,
                };

                context.Pages.Add(copy);
                await context.SaveChangesAsync(cancellationToken);

                await tree.AttachAsync(copy, cancellationToken);

                var sourceDraft = sourcePage.DraftVersion!;
                var draft = DraftService.Copy(sourceDraft, versionNumber: 1);
                draft.PageId = copy.Id;
                draft.Status = PageVersionStatus.Draft;
                draft.Label = null;

                if (isRoot)
                {
                    draft.Title = Truncate(sourceDraft.Title + CopySuffix);
                }

                context.PageVersions.Add(draft);
                await context.SaveChangesAsync(cancellationToken);

                copy.DraftVersionId = draft.Id;
                await context.SaveChangesAsync(cancellationToken);

                map[sourcePage.Id] = copy.Id;
                copies.Add((copy, draft));
            }

            // Second pass: every id now exists, so a link from one copied page to another can be
            // pointed at the copy. Links out of the copied set fall through the map unchanged, which
            // is what leaves them pointing at the originals (spec section 14.12).
            foreach (var (copy, draft) in copies)
            {
                if (!ContentPayload.TryParse(draft.ContentJson, out var payload)) continue;

                var remapped = remapper.Remap(payload, (targetType, targetId) =>
                    targetType is ContentReferenceTargetType.Page && map.TryGetValue(targetId, out var replacement)
                        ? replacement
                        : targetId);

                // Reference equality means nothing was rewritten, so the row is left as written.
                if (!ReferenceEquals(remapped, payload))
                {
                    draft.ContentJson = remapped.ToJson();
                }

                await referenceProjector.ProjectAsync(
                    ContentSourceType.PageVersion,
                    draft.Id,
                    remapped,
                    cancellationToken);

                _ = copy;
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return map[root.Id];
        });
    }

    private async Task<List<Page>> LoadSubtreeAsync(Page root, CancellationToken cancellationToken)
    {
        var descendants = await context.Pages
            .AsNoTracking()
            .Include(page => page.DraftVersion)
            .Where(page => page.Id != root.Id && page.Path.StartsWith(root.Path))
            .ToListAsync(cancellationToken);

        return [root, .. descendants];
    }

    /// <summary>
    /// Finds a segment no live sibling is using.
    /// </summary>
    /// <remarks>
    /// <c>pricing</c> becomes <c>pricing-copy</c>, then <c>pricing-copy-2</c>. Bounded rather than
    /// unbounded: a hundred failed attempts means something is wrong with the tree, and looping
    /// forever on it would be a request that never returns.
    /// </remarks>
    private async Task<string> FindFreeSlugAsync(
        int? parentId,
        string slug,
        CancellationToken cancellationToken)
    {
        var stem = Trim(slug, FieldLengths.Slug - "-copy-99".Length);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = attempt switch
            {
                0 => $"{stem}-copy",
                _ => $"{stem}-copy-{attempt + 1}",
            };

            var taken = await context.Pages.AnyAsync(
                page => page.ParentId == parentId && page.Slug == candidate,
                cancellationToken);

            if (!taken) return candidate;
        }

        return string.Empty;
    }

    private static string Trim(string value, int length) =>
        value.Length <= length ? value : value[..length].TrimEnd('-');

    private static string Truncate(string title) =>
        title.Length <= FieldLengths.ContentTitle
            ? title
            : title[..FieldLengths.ContentTitle];
}
