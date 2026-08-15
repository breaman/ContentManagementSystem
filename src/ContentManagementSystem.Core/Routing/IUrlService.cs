using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Routing;

/// <summary>One page whose URL changed during a rebuild.</summary>
/// <param name="PageId">Identity of the page.</param>
/// <param name="OldUrl">The URL it served before, or null if it had none.</param>
/// <param name="NewUrl">The URL it serves now.</param>
/// <param name="RedirectCreated">Whether an automatic redirect was left behind at the old URL.</param>
public sealed record PageUrlChange(int PageId, string? OldUrl, string NewUrl, bool RedirectCreated);

/// <summary>What a route rebuild did.</summary>
/// <param name="Changes">Every page whose URL moved, the subject page first.</param>
/// <param name="Diagnostics">
/// Why the rebuild refused, when it did. A rebuild that reports errors has written nothing — it is
/// the caller's transaction that must not be committed, and the caller is the layer holding it.
/// </param>
public sealed record UrlSyncResult(IReadOnlyList<PageUrlChange> Changes, ValidationResult Diagnostics)
{
    /// <summary>A rebuild that found nothing to change.</summary>
    public static UrlSyncResult Unchanged { get; } = new([], ValidationResult.Success);

    /// <summary>Whether the rebuild was refused.</summary>
    public bool HasErrors => Diagnostics.HasErrors;
}

/// <summary>
/// Owns the <c>PageRoute</c> table: what URL a page has, and what happens to the old one.
/// </summary>
/// <remarks>
/// Routes are materialized rather than computed per request, so resolution is one indexed lookup
/// (spec section 10.4). The price is paid here: any change to a slug, an explicit URL, or a page's
/// position rewrites the routes of that page <em>and every descendant</em>, and does it in one
/// transaction — a subtree half-way through a rename is a set of URLs that resolve to the wrong
/// pages.
/// <para>
/// Nothing here calls <c>SaveChanges</c>, for the same reason <c>PageTreeService</c> does not: a
/// move, its route rebuild, and the redirects it emits are one atomic act, and the caller owns the
/// transaction they belong to.
/// </para>
/// <para>
/// <strong>Two routes per page, not one.</strong> A draft route (<c>IsPublished = 0</c>) always
/// exists so preview can address the page by URL; the published route exists only while the page is
/// published, and is the one the filtered unique index governs. Keeping them apart is what lets an
/// editor prepare a replacement at the URL a live page is still serving.
/// </para>
/// </remarks>
public interface IUrlService
{
    /// <summary>
    /// Computes the URL a page should have, without touching anything stored.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while loading ancestors.</param>
    /// <returns>The normalized URL, or null when no such page exists.</returns>
    Task<string?> ComputeAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the routes of a page and every descendant, emitting redirects for URLs vacated.
    /// </summary>
    /// <param name="pageId">Identity of the page at the top of the subtree.</param>
    /// <param name="cancellationToken">Token observed while loading and querying.</param>
    /// <returns>What moved, or the reasons the rebuild was refused.</returns>
    /// <remarks>
    /// Idempotent: running it against an unchanged tree writes nothing and reports no changes, which
    /// is what makes it safe to call from every path that might have moved a page rather than only
    /// from the ones that certainly did.
    /// </remarks>
    Task<UrlSyncResult> SyncAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws a page's published routes, for the page and every descendant.
    /// </summary>
    /// <param name="pageId">Identity of the page at the top of the subtree.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>The URLs withdrawn.</returns>
    /// <remarks>
    /// Called on unpublish and on recycling. The draft routes stay: an editor still has to be able
    /// to find and preview the page they just took down.
    /// <para>
    /// No redirect is emitted. An unpublished page has not moved anywhere, and inventing a
    /// destination for it would be guessing — spec section 10.6 says the URL becomes a 404 and the
    /// <c>NotFoundLog</c> report is where somebody decides what should happen to it instead.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<string>> WithdrawAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every route belonging to a page, published and draft alike.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while deleting.</param>
    /// <remarks>
    /// For a permanent delete only. A soft-deleted page keeps its draft route, because the recycle
    /// bin restores it and a restore that had to rebuild routes from nothing would silently drop a
    /// page's explicit URL.
    /// </remarks>
    Task PurgeAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the URL a page would have under a given ancestor URL.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="parentUrl">The parent's computed URL, or null for a page at the root.</param>
    /// <returns>The normalized URL.</returns>
    /// <remarks>
    /// A page that states its own URL ignores the ancestor entirely — that is what opting out of the
    /// tree means (spec section 10.2). Its <em>descendants</em> still build on it, so the subtree
    /// below an explicit URL stays coherent.
    /// </remarks>
    static string Build(Page page, string? parentUrl)
    {
        ArgumentNullException.ThrowIfNull(page);

        return page.UseExplicitUrl && !string.IsNullOrWhiteSpace(page.ExplicitUrl)
            ? Shared.Common.SiteUrls.Normalize(page.ExplicitUrl)
            : Shared.Common.SiteUrls.Combine(parentUrl, page.Slug);
    }
}
