using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Security;

/// <summary>
/// Resolves section-level access rules for the caller of the current request (task P7-04,
/// spec section 21.2).
/// </summary>
/// <remarks>
/// Inheritance is an indexed prefix match on <c>Page.Path</c>, never a walk: a rule on
/// <c>/products</c> reaches <c>/products/bikes/frames</c> because the latter's path begins with the
/// former's, which is one string comparison rather than three round trips. Precedence — deeper beats
/// shallower, deny beats allow at the same depth — lives in <see cref="AclFilter"/> so that the rule
/// the tree applies to a hundred siblings and the rule one service applies to one page are the same
/// code.
/// <para>
/// <strong>Scoped, and cached for the length of one request</strong> (task P7-05). A request that
/// expands a deep branch asks the same question about every node in it; without the cache that is a
/// query per node, which is risk R15 exactly. Nothing is cached beyond the request, because an
/// administrator revoking access must take effect on the next one.
/// </para>
/// <para>
/// <c>Administrator</c> bypasses the rules entirely, which is what stops a deny rule from locking
/// every human out of a branch. A bypass is logged at warning level when — and only when — a rule
/// would otherwise have refused: logging every administrator request would bury the events spec
/// section 21.2 asks to be able to find.
/// </para>
/// </remarks>
/// <param name="context">The application database context.</param>
/// <param name="authorization">The caller's identity and global grants.</param>
/// <param name="users">The caller's user id, which user-scoped rules are addressed to.</param>
/// <param name="logger">Log for administrator bypasses.</param>
public sealed class AclService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    IUserService users,
    ILogger<AclService> logger) : IAclService
{
    private readonly Dictionary<string, AclFilter> _filters = new(StringComparer.Ordinal);
    private readonly Dictionary<int, PagePosition?> _positions = [];
    private IReadOnlyList<int>? _roleIds;

    /// <inheritdoc />
    public async ValueTask<bool> IsAllowedAsync(
        string permission,
        int pageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        var filter = await GetFilterAsync(permission, cancellationToken);

        if (filter.IsUnrestricted) return true;

        var position = await GetPositionAsync(pageId, cancellationToken);

        // A page nobody can find is not a page anybody is being refused. Answering "forbidden" for
        // an id that does not exist would let an outsider map the content tree by watching which
        // guesses come back 403 and which come back 404.
        if (position is null) return true;

        if (filter.WouldRefuseWithoutBypass(pageId, position.Path))
        {
            logger.LogWarning(
                "Administrator {UserId} exercised {Permission} on page {PageId}, which an access rule refuses.",
                users.UserId,
                permission,
                pageId);
        }

        return filter.Allows(pageId, position.Path);
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsAllowedAtRootAsync(
        string permission,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        var filter = await GetFilterAsync(permission, cancellationToken);

        if (filter.IsUnrestricted || filter.IsBypassed) return true;

        // No rule can name the synthetic root, so the only question left is whether the caller has
        // been narrowed to an allowlist that the root is by definition outside of.
        if (!filter.HasAllowRule) return true;

        logger.LogDebug(
            "User {UserId} was refused {Permission} at the site root by an access rule.",
            users.UserId,
            permission);

        return false;
    }

    /// <inheritdoc />
    public async ValueTask<AclFilter> GetFilterAsync(
        string permission,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        if (_filters.TryGetValue(permission, out var cached)) return cached;

        var filter = await ResolveAsync(permission, cancellationToken);
        _filters[permission] = filter;

        return filter;
    }

    private async Task<AclFilter> ResolveAsync(string permission, CancellationToken cancellationToken)
    {
        // An administrator's rules are loaded like anyone else's, even though the answer is already
        // known: what would have happened without the bypass is the thing spec section 21.2 asks to
        // be able to log, and a filter carrying no rules cannot say.
        var isAdministrator = authorization.Roles.Contains(CmsRoles.Administrator, StringComparer.Ordinal);

        var userId = users.UserId;
        var roleIds = await GetRoleIdsAsync(cancellationToken);

        // Rules are joined to their page here rather than carried on the row, because a page that
        // moves takes its subtree's rules with it and a denormalized path would go stale silently.
        // IgnoreQueryFilters so a rule on a recycled page still governs it — the recycle bin is a
        // place an editor can act on content, so it is a place access rules have to reach.
        var rules = await context.PageAcls
            .AsNoTracking()
            .Where(acl => acl.Permission == permission)
            .Where(acl =>
                (acl.PrincipalType == AclPrincipalType.User && acl.PrincipalId == userId)
                || (acl.PrincipalType == AclPrincipalType.Role && roleIds.Contains(acl.PrincipalId)))
            .Join(
                context.Pages.IgnoreQueryFilters(),
                acl => acl.PageId,
                page => page.Id,
                (acl, page) => new AclRule(acl.PageId, page.Path, page.Depth, acl.IsAllow, acl.IsInherited))
            .ToListAsync(cancellationToken);

        return rules.Count == 0 && !isAdministrator
            ? AclFilter.Unrestricted
            : new AclFilter(rules, isAdministrator);
    }

    private async ValueTask<IReadOnlyList<int>> GetRoleIdsAsync(CancellationToken cancellationToken)
    {
        if (_roleIds is not null) return _roleIds;

        var names = authorization.Roles;

        if (names.Count == 0) return _roleIds = [];

        // Read rather than derived from the seeded ids: a deployment is free to add roles of its
        // own, and a role whose id this could not resolve would have its rules quietly ignored —
        // which for a deny rule means granting access the administrator believed they had removed.
        var normalized = names.Select(name => name.ToUpperInvariant()).ToArray();

        return _roleIds = await context.Roles
            .AsNoTracking()
            .Where(role => role.NormalizedName != null && normalized.Contains(role.NormalizedName))
            .Select(role => role.Id)
            .ToListAsync(cancellationToken);
    }

    private async ValueTask<PagePosition?> GetPositionAsync(int pageId, CancellationToken cancellationToken)
    {
        if (_positions.TryGetValue(pageId, out var cached)) return cached;

        var position = await context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.Id == pageId)
            .Select(page => new PagePosition(page.Path, page.Depth))
            .FirstOrDefaultAsync(cancellationToken);

        _positions[pageId] = position;

        return position;
    }

    /// <summary>Where a page sits in the tree, which is all the resolver needs of it.</summary>
    private sealed record PagePosition(string Path, int Depth);
}
