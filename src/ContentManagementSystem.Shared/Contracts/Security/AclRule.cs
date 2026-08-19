namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// One access rule as the resolver reads it — the rule joined to the position of the page it hangs
/// on (spec section 21.2).
/// </summary>
/// <param name="PageId">The page the rule is attached to.</param>
/// <param name="PagePath">
/// That page's materialized ancestor path, such as <c>/1/8/44/</c>. Carried on the rule so that
/// "does this rule reach that page" is a string prefix test rather than a second query.
/// </param>
/// <param name="Depth">
/// How far down the tree the rule sits. The tie-break that makes the specific beat the general.
/// </param>
/// <param name="IsAllow">Whether the rule grants the permission or withdraws it.</param>
/// <param name="IsInherited">Whether the rule reaches descendants as well as its own page.</param>
public sealed record AclRule(int PageId, string PagePath, int Depth, bool IsAllow, bool IsInherited);
