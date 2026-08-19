using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Search;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tags;

/// <inheritdoc cref="ITagService" />
/// <param name="context">The application database context.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="search">Re-indexes the pages a rename or a delete changed.</param>
/// <param name="logger">Log for renames and deletes, which reach every page carrying the tag.</param>
/// <remarks>
/// A tag's <c>Slug</c> is its identity and its <c>Name</c> is its label. That is what makes
/// "Product" and "product" one tag rather than two rows nobody can tell apart in a picker, and it is
/// why a rename that lands on an existing slug is a merge rather than a duplicate-key error.
/// </remarks>
public sealed class TagService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    ISearchIndexQueue search,
    ILogger<TagService> logger) : ITagService
{
    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<TagSummary>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<TagSummary>>.Forbidden(
                "Reading tags is not permitted.",
                TagCodes.Forbidden);
        }

        var tags = await context.Tags
            .AsNoTracking()
            .OrderBy(tag => tag.Name)
            .Select(tag => new TagSummary(tag.Id, tag.Name, tag.Slug, tag.Pages.Count))
            .ToListAsync(cancellationToken);

        return CmsResult<IReadOnlyList<TagSummary>>.Success(tags);
    }

    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<TagSummary>>> SuggestAsync(
        string? prefix,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<TagSummary>>.Forbidden(
                "Reading tags is not permitted.",
                TagCodes.Forbidden);
        }

        var take = Math.Clamp(limit, 1, 50);
        var tags = context.Tags.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            // A prefix match rather than a contains: this is completing a word somebody is typing,
            // and an index can answer it.
            var normalized = Slugs.Generate(prefix);

            tags = normalized.Length == 0
                ? tags.Where(_ => false)
                : tags.Where(tag => tag.Slug.StartsWith(normalized));
        }

        // Most used first, so an empty box offers the vocabulary the site actually has rather than
        // whatever sorts first alphabetically.
        var found = await tags
            .OrderByDescending(tag => tag.Pages.Count)
            .ThenBy(tag => tag.Name)
            .Take(take)
            .Select(tag => new TagSummary(tag.Id, tag.Name, tag.Slug, tag.Pages.Count))
            .ToListAsync(cancellationToken);

        return CmsResult<IReadOnlyList<TagSummary>>.Success(found);
    }

    /// <inheritdoc />
    public async Task<CmsResult<RenameTagResult>> RenameAsync(
        int id,
        RenameTagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return CmsResult<RenameTagResult>.Forbidden(
                "Editing tags is not permitted.",
                TagCodes.Forbidden);
        }

        var slug = Slugs.Generate(request.Name);

        if (slug.Length == 0)
        {
            return CmsResult<RenameTagResult>.Invalid(
                TagCodes.InvalidName,
                "A tag needs at least one letter or digit in its name.",
                nameof(RenameTagRequest.Name));
        }

        var tag = await context.Tags
            .Include(candidate => candidate.Pages)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (tag is null)
        {
            return CmsResult<RenameTagResult>.NotFound($"No tag has id {id}.", TagCodes.NotFound);
        }

        var affected = tag.Pages.Select(applied => applied.PageId).Distinct().ToArray();
        var target = await context.Tags
            .Include(candidate => candidate.Pages)
            .FirstOrDefaultAsync(
                candidate => candidate.Slug == slug && candidate.Id != id,
                cancellationToken);

        var merged = target is not null;

        if (target is null)
        {
            tag.Name = request.Name.Trim();
            tag.Slug = slug;
        }
        else
        {
            // The merge. Every page carrying the old tag gains the new one unless it already had it,
            // and the old rows go — which is what makes "rename to a name that exists" mean the
            // obvious thing rather than an error message.
            var alreadyTagged = target.Pages.Select(applied => applied.PageId).ToHashSet();

            foreach (var applied in tag.Pages)
            {
                if (alreadyTagged.Add(applied.PageId))
                {
                    context.PageTags.Add(new PageTag { PageId = applied.PageId, TagId = target.Id });
                }
            }

            context.PageTags.RemoveRange(tag.Pages);
            context.Tags.Remove(tag);
        }

        // The tag's name is in every affected page's keywords, so the index is wrong until they are
        // rebuilt. Enqueued in this transaction, like every other index message.
        search.EnqueuePages(affected);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            merged
                ? "Tag {TagId} was merged into '{Slug}' across {PageCount} page(s)."
                : "Tag {TagId} was renamed to '{Slug}' across {PageCount} page(s).",
            id,
            slug,
            affected.Length);

        var result = target ?? tag;

        return CmsResult<RenameTagResult>.Success(
            new RenameTagResult(
                new TagSummary(
                    result.Id,
                    result.Name,
                    result.Slug,
                    await context.PageTags.CountAsync(applied => applied.TagId == result.Id, cancellationToken)),
                merged,
                affected.Length));
    }

    /// <inheritdoc />
    public async Task<CmsResult<int>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return CmsResult<int>.Forbidden("Editing tags is not permitted.", TagCodes.Forbidden);
        }

        var tag = await context.Tags
            .Include(candidate => candidate.Pages)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (tag is null) return CmsResult<int>.NotFound($"No tag has id {id}.", TagCodes.NotFound);

        var affected = tag.Pages.Select(applied => applied.PageId).Distinct().ToArray();

        // The join rows go first: PageTag → Tag is Restrict, deliberately, so that deleting a tag is
        // an act with a page count attached rather than a cascade nobody sees.
        context.PageTags.RemoveRange(tag.Pages);
        context.Tags.Remove(tag);

        search.EnqueuePages(affected);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Tag '{Slug}' was deleted and removed from {PageCount} page(s).",
            tag.Slug,
            affected.Length);

        return CmsResult<int>.Success(affected.Length);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ApplyAsync(
        int pageId,
        IReadOnlyList<string>? tags,
        CancellationToken cancellationToken = default)
    {
        // Slug is identity, so the same label twice in one request is one tag. Keyed by slug and
        // holding the label, because the label is what an editor sees and the last spelling they
        // typed is the one they meant.
        var wanted = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var label in tags ?? [])
        {
            var slug = Slugs.Generate(label);

            if (slug.Length == 0) continue;

            wanted[slug] = label.Trim();
        }

        var applied = await context.PageTags
            .Include(row => row.Tag)
            .Where(row => row.PageId == pageId)
            .ToListAsync(cancellationToken);

        foreach (var row in applied.Where(row => !wanted.ContainsKey(row.Tag.Slug)))
        {
            context.PageTags.Remove(row);
        }

        var missing = wanted.Keys
            .Except(applied.Select(row => row.Tag.Slug), StringComparer.Ordinal)
            .ToArray();

        if (missing.Length > 0)
        {
            var known = await context.Tags
                .Where(tag => missing.Contains(tag.Slug))
                .ToDictionaryAsync(tag => tag.Slug, cancellationToken);

            foreach (var slug in missing)
            {
                if (!known.TryGetValue(slug, out var tag))
                {
                    tag = new Tag { Name = wanted[slug], Slug = slug };

                    context.Tags.Add(tag);
                }

                // The tag may be brand new and have no id yet, so the row is linked by reference and
                // EF fills the key in when it inserts both in the same save.
                context.PageTags.Add(new PageTag { PageId = pageId, Tag = tag });
            }
        }

        return [.. wanted.Values.Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ForPageAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        await context.PageTags
            .AsNoTracking()
            .Where(row => row.PageId == pageId)
            .OrderBy(row => row.Tag.Name)
            .Select(row => row.Tag.Name)
            .ToListAsync(cancellationToken);
}
