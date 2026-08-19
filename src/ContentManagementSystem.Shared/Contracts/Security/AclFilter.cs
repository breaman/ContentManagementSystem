namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// Every access rule bearing on one caller and one permission, resolved once and then answered
/// in memory (spec section 21.2).
/// </summary>
/// <remarks>
/// The shape exists for risk R15. Deciding one page at a time would put a query behind every node of
/// a tree the editor is about to expand — depth ten, a hundred siblings — and the ACL check would
/// become the reason the content tree is slow. Loading the caller's rules once, per request and per
/// permission, turns every subsequent decision into a prefix comparison.
/// <para>
/// Instances are immutable and are safe to hold for the length of a request. They are <em>not</em>
/// safe to cache beyond one: an administrator editing a rule must take effect on the next request,
/// not when a cache entry happens to expire.
/// </para>
/// </remarks>
public sealed class AclFilter
{
    private readonly AclRule[] _rules;

    /// <summary>Builds a filter over the rules that bear on one caller and permission.</summary>
    /// <param name="rules">The rules, in any order.</param>
    /// <param name="isBypassed">
    /// Whether the caller is exempt from the rules altogether — an <c>Administrator</c>.
    /// </param>
    public AclFilter(IReadOnlyList<AclRule> rules, bool isBypassed = false)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _rules = [.. rules];
        IsBypassed = isBypassed;

        for (var i = 0; i < _rules.Length; i++)
        {
            if (_rules[i].IsAllow) { HasAllowRule = true; break; }
        }
    }

    /// <summary>A filter that permits everything, for a caller no rule mentions.</summary>
    public static AclFilter Unrestricted { get; } = new([]);

    /// <summary>Whether the caller bypasses access rules entirely.</summary>
    /// <remarks>
    /// True only for <c>Administrator</c>. <see cref="Allows"/> already honours it, so no caller has
    /// to remember to; it is public so the resolver can log the bypasses spec section 21.2 requires
    /// a record of — see <see cref="WouldRefuseWithoutBypass"/>.
    /// </remarks>
    public bool IsBypassed { get; }

    /// <summary>Whether no rule bears on this caller, so the decision is theirs globally.</summary>
    public bool IsUnrestricted => _rules.Length == 0;

    /// <summary>
    /// Whether any rule <em>grants</em> the permission, which switches the default to refusal.
    /// </summary>
    /// <remarks>
    /// The mechanism by which an ACL narrows rather than only widens: give an editor
    /// <c>Content.Edit</c> on <c>/products</c> and they are thereby refused everywhere else, which
    /// is what criterion P7 #5 asserts. A caller holding only deny rules keeps their global grant
    /// everywhere the denies do not reach.
    /// </remarks>
    public bool HasAllowRule { get; }

    /// <summary>
    /// Whether the caller may exercise the permission on one page.
    /// </summary>
    /// <param name="pageId">Identity of the page being decided.</param>
    /// <param name="pagePath">
    /// That page's materialized path, such as <c>/1/8/44/</c>, which is what makes an inherited rule
    /// a prefix test.
    /// </param>
    /// <returns><see langword="true"/> when no rule refuses, under the precedence in <c>PageAcl</c>.</returns>
    public bool Allows(int pageId, string pagePath) => IsBypassed || Verdict(pageId, pagePath);

    /// <summary>
    /// Whether the rules refused a page that the caller was let through anyway, because they are an
    /// <c>Administrator</c>.
    /// </summary>
    /// <param name="pageId">Identity of the page being decided.</param>
    /// <param name="pagePath">That page's materialized path.</param>
    /// <returns><see langword="true"/> for exactly the events worth logging.</returns>
    /// <remarks>
    /// Spec section 21.2 requires every administrator bypass to be audit-logged. Taken literally
    /// that would be a log line per administrator request, which buries the ones that mean anything;
    /// a bypass only <em>does</em> something when a rule was going to refuse, so that is what this
    /// reports and what gets written.
    /// </remarks>
    public bool WouldRefuseWithoutBypass(int pageId, string pagePath) =>
        IsBypassed && !Verdict(pageId, pagePath);

    private bool Verdict(int pageId, string pagePath)
    {
        if (IsUnrestricted) return true;

        ArgumentNullException.ThrowIfNull(pagePath);

        var bestDepth = int.MinValue;
        var allowed = !HasAllowRule;

        for (var i = 0; i < _rules.Length; i++)
        {
            var rule = _rules[i];

            if (!Applies(rule, pageId, pagePath)) continue;

            if (rule.Depth > bestDepth)
            {
                bestDepth = rule.Depth;
                allowed = rule.IsAllow;
            }
            else if (rule.Depth == bestDepth && !rule.IsAllow)
            {
                // Deny beats allow at the same depth, whichever order the rows arrived in. This is
                // the branch that makes the answer independent of the query plan.
                allowed = false;
            }
        }

        return allowed;
    }

    private static bool Applies(AclRule rule, int pageId, string pagePath) =>
        rule.PageId == pageId
        || (rule.IsInherited && pagePath.StartsWith(rule.PagePath, StringComparison.Ordinal));
}
