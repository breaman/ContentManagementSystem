using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Shared.Content;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Delivery;

/// <summary>
/// Turns a placement — an item id and an optional pinned version — into the content to render
/// (task P4-06, spec section 9.2).
/// </summary>
/// <remarks>
/// The delivery path's only door into the reusable-content library, and deliberately as narrow as
/// <see cref="IPublishedContentService"/>: it answers with a <em>published</em> version or with
/// nothing, and the published filter is in the query rather than in a check afterwards. A resolver
/// that loaded the item and then decided which version to hand back would already have put an
/// unreviewed draft footer in memory beside the response of every page on the site.
/// <para>
/// <strong>Late binding is the default and it is what delivers goal G4.</strong> An unpinned
/// placement reads <c>ReusableContent.PublishedVersionId</c> at the moment the page is served, so
/// publishing a new version of a banner changes every page showing it without any of them being
/// republished (acceptance criterion P4 #2). A pinned placement names a version instead and stops
/// following the item, which is the escape hatch for content that must be reproducible under audit.
/// </para>
/// </remarks>
public interface IReusableContentResolver
{
    /// <summary>
    /// Resolves one placement.
    /// </summary>
    /// <param name="reusableContentId">The item the placement names.</param>
    /// <param name="pinnedVersionId">
    /// The version the placement pins, or null to render whatever is currently published.
    /// </param>
    /// <param name="chain">
    /// The items already being rendered above this placement, which is what makes the depth and
    /// cycle guards possible. Pass <see cref="ReusableResolutionChain.Root"/> at the top of a page.
    /// </param>
    /// <param name="includeUnpublished">
    /// Whether an item with nothing published may fall back to its draft. True only in preview,
    /// where an editor is checking a page before either it or the fragment has gone live; on the
    /// public site this is false and an unpublished item renders nothing (spec section 15.3).
    /// </param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>
    /// The content to render, or an unresolved result saying why there is none. Never null and never
    /// an exception: a placement that cannot be resolved is an ordinary condition on a public
    /// request, and the page around it still renders.
    /// </returns>
    Task<ResolvedReusableContent> ResolveAsync(
        int reusableContentId,
        int? pinnedVersionId,
        ReusableResolutionChain chain,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IReusableContentResolver" />
/// <param name="contexts">Makes a context of its own for each resolve (ADR-0022).</param>
/// <param name="schemas">Resolves the block type revision the payload names.</param>
/// <remarks>
/// <strong>A context per resolve, not the request's.</strong> This runs from a field renderer's
/// <c>OnParametersSetAsync</c>, and Blazor starts sibling renderers' asynchronous lifecycle methods
/// concurrently — a page with a media placement and a reusable footer has two of them in flight at
/// once. Sharing the request's scoped context is what EF Core reports as "a second operation was
/// started on this context instance". Everything read here is <c>AsNoTracking</c> and nothing is
/// written, so there is no unit of work to preserve by sharing one.
/// </remarks>
public sealed class ReusableContentResolver(
    IDbContextFactory<ApplicationDbContext> contexts,
    IContentSchemaCatalog schemas) : IReusableContentResolver
{
    /// <inheritdoc />
    public async Task<ResolvedReusableContent> ResolveAsync(
        int reusableContentId,
        int? pinnedVersionId,
        ReusableResolutionChain chain,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);

        if (reusableContentId <= 0)
        {
            return ResolvedReusableContent.Unresolved(ReusableResolutionStatus.NotFound, reusableContentId);
        }

        // Both guards are asked before the query, not after. A loop that reached the database would
        // otherwise be one round trip per level until the depth guard caught it, and the depth guard
        // is a far coarser instrument than knowing the id is already on the stack.
        if (chain.Contains(reusableContentId))
        {
            return ResolvedReusableContent.Unresolved(ReusableResolutionStatus.Cycle, reusableContentId);
        }

        if (chain.IsAtMaxDepth)
        {
            return ResolvedReusableContent.Unresolved(ReusableResolutionStatus.TooDeep, reusableContentId);
        }

        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        // The soft-delete query filter stays in place, which is what makes "deleting an item stops it
        // rendering" true without this method having to remember to ask.
        var item = await context.ReusableContents
            .AsNoTracking()
            .Where(candidate => candidate.Id == reusableContentId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Key,
                candidate.Name,
                BlockTypeKey = candidate.BlockType.Key,
                candidate.DraftVersionId,
                candidate.PublishedVersionId,
                PublishedVersionNumber = candidate.PublishedVersion == null
                    ? (int?)null
                    : candidate.PublishedVersion.VersionNumber,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return ResolvedReusableContent.Unresolved(ReusableResolutionStatus.NotFound, reusableContentId);
        }

        var (versionId, status) = Select(item.PublishedVersionId, item.DraftVersionId, pinnedVersionId, includeUnpublished);

        if (versionId is null) return ResolvedReusableContent.Unresolved(status, reusableContentId);

        // The item id is part of the predicate, not merely of the result. A payload can name any
        // version id at all, and a pin that happened to quote another item's version would otherwise
        // render that item's content under this item's cache tag.
        var version = await context.ReusableContentVersions
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == versionId && candidate.ReusableContentId == reusableContentId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.VersionNumber,
                candidate.ContentJson,
                candidate.BlockTypeRevision,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null)
        {
            return ResolvedReusableContent.Unresolved(
                pinnedVersionId is null ? ReusableResolutionStatus.NotPublished : ReusableResolutionStatus.PinnedVersionMissing,
                reusableContentId);
        }

        // Malformed stored content is a logged, degraded render rather than an exception on a public
        // request — the same rule the page delivery path follows (spec section 15.3).
        if (!ContentPayload.TryParse(version.ContentJson, out var payload))
        {
            return ResolvedReusableContent.Unresolved(ReusableResolutionStatus.NotFound, reusableContentId);
        }

        return new ResolvedReusableContent(
            ReusableResolutionStatus.Resolved,
            item.Id,
            item.Key,
            item.Name,
            version.Id,
            version.VersionNumber,
            pinnedVersionId is not null,
            version.Id == item.PublishedVersionId,
            item.PublishedVersionNumber,
            item.BlockTypeKey,
            version.BlockTypeRevision,
            payload,
            schemas.TryGetBlockType(item.BlockTypeKey, version.BlockTypeRevision, out var schema)
                ? schema
                : null);
    }

    /// <summary>
    /// Chooses which version a placement renders.
    /// </summary>
    /// <remarks>
    /// The whole of spec section 9.2 in one expression, kept apart from the queries so it can be
    /// read as the rule it is: a pin names a version outright, and everything else follows the item.
    /// <para>
    /// The draft fallback is the one addition, and it is preview-only. An editor checking a page
    /// before either it or the fragment is live needs to see the fragment; an anonymous visitor must
    /// not, which is why the flag is a parameter of the resolve rather than a property of the item.
    /// </para>
    /// </remarks>
    private static (int? VersionId, ReusableResolutionStatus Status) Select(
        int? publishedVersionId,
        int? draftVersionId,
        int? pinnedVersionId,
        bool includeUnpublished)
    {
        if (pinnedVersionId is > 0)
        {
            return (pinnedVersionId, ReusableResolutionStatus.Resolved);
        }

        if (publishedVersionId is not null)
        {
            return (publishedVersionId, ReusableResolutionStatus.Resolved);
        }

        return includeUnpublished && draftVersionId is not null
            ? (draftVersionId, ReusableResolutionStatus.Resolved)
            : (null, ReusableResolutionStatus.NotPublished);
    }
}
