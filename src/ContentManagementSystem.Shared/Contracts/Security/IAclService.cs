namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// Answers permission questions about a particular page, where <see cref="ICmsAuthorization"/>
/// answers them about the site (spec section 21.2).
/// </summary>
/// <remarks>
/// The two are asked in that order and both must agree. A role grant says an editor may edit; an
/// access rule says which branch of the tree they may edit. Neither alone is a decision, and a
/// service that checked only the first would hand every editor the whole site — which is precisely
/// the defect the IDOR sweep in task P7-07 goes looking for.
/// <para>
/// Every method is asked once per operation, in the service layer, on the id the caller supplied —
/// not at the endpoint and never in the client. An id in a URL is a guess until something has
/// checked it.
/// </para>
/// </remarks>
public interface IAclService
{
    /// <summary>
    /// Whether the caller may exercise a permission on one page.
    /// </summary>
    /// <param name="permission">One of the <see cref="CmsPermissions"/> constants.</param>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while reading rules.</param>
    /// <returns>
    /// <see langword="true"/> when no access rule refuses. A page that does not exist answers
    /// <see langword="true"/>: whether it exists is the caller's question to ask, and answering
    /// "forbidden" here would turn every access check into an existence oracle.
    /// </returns>
    ValueTask<bool> IsAllowedAsync(string permission, int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the caller may exercise a permission at the root of the site, where there is no page
    /// for a rule to hang on.
    /// </summary>
    /// <param name="permission">One of the <see cref="CmsPermissions"/> constants.</param>
    /// <param name="cancellationToken">Token observed while reading rules.</param>
    /// <returns><see langword="true"/> when no access rule refuses.</returns>
    /// <remarks>
    /// Asked when creating a top-level page. The answer follows from the same rule as everywhere
    /// else — an allow rule anywhere makes the permission an allowlist, and the site root is outside
    /// every allowlist — but the question has to be asked separately because the synthetic root is
    /// the absence of a page rather than a page with an id.
    /// </remarks>
    ValueTask<bool> IsAllowedAtRootAsync(string permission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves every rule bearing on the caller for one permission, to decide many pages at once.
    /// </summary>
    /// <param name="permission">One of the <see cref="CmsPermissions"/> constants.</param>
    /// <param name="cancellationToken">Token observed while reading rules.</param>
    /// <returns>The filter, which decides each page in memory.</returns>
    /// <remarks>
    /// What the content tree and every list endpoint use. Deciding a hundred siblings one at a time
    /// is a hundred round trips against a table whose whole content is usually a handful of rows
    /// (risk R15).
    /// </remarks>
    ValueTask<AclFilter> GetFilterAsync(string permission, CancellationToken cancellationToken = default);
}
