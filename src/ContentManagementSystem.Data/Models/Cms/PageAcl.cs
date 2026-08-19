namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One rule narrowing — or withdrawing — a global permission over a branch of the content tree
/// (spec section 21.2).
/// </summary>
/// <remarks>
/// Role grants are site-wide: an <c>Editor</c> may edit, full stop. That is too coarse for any site
/// with more than one team, so these rows say <em>where</em>. A rule is attached to a page and, by
/// default, reaches every descendant of it — which <see cref="Page.Path"/> makes an indexed prefix
/// match rather than a walk.
/// <para>
/// The resolution rules are small and deliberately total, because an access decision that depends on
/// the order rows happen to come back in is not a decision:
/// </para>
/// <list type="bullet">
/// <item><description>A deeper rule beats a shallower one — the specific overrides the general.</description></item>
/// <item><description>At the same depth, <see cref="IsAllow"/> <see langword="false"/> wins. Deny
/// beats allow — including when the deny names a role and the allow names the person, which is what
/// makes a deny worth writing at all.</description></item>
/// <item><description>No rule anywhere for a principal changes nothing: they keep whatever their
/// roles gave them globally.</description></item>
/// <item><description>But one <em>allow</em> anywhere turns the permission into an allowlist for
/// that principal — an editor given <c>/products</c> is thereby refused <c>/about</c>. That is what
/// "ACLs narrow a global grant to a subtree" has to mean; a rule that only ever added access could
/// not narrow anything (criterion P7 #5, <c>ADR-0023</c>).</description></item>
/// </list>
/// <para>
/// <c>Administrator</c> is not subject to any of it (see <c>AclService</c>), which is what stops a
/// deny rule from locking every human out of a subtree. Each bypass is logged.
/// </para>
/// </remarks>
public class PageAcl : FingerPrintEntityBase
{
    /// <summary>The page the rule is attached to.</summary>
    public int PageId { get; set; }

    /// <summary>The page the rule is attached to.</summary>
    public Page Page { get; set; } = null!;

    /// <summary>Whether <see cref="PrincipalId"/> names a user or a role.</summary>
    public AclPrincipalType PrincipalType { get; set; }

    /// <summary>
    /// Identity of the user or role the rule is about.
    /// </summary>
    /// <remarks>
    /// Deliberately not a foreign key: it points at one of two tables depending on
    /// <see cref="PrincipalType"/>, and modelling that as two nullable keys would allow a row naming
    /// both. Deleting a user or role therefore leaves rules behind — harmless, because identity ids
    /// are never reused, and cleaned up by the ACL screens rather than by the database.
    /// </remarks>
    public int PrincipalId { get; set; }

    /// <summary>The <c>CmsPermissions</c> constant this rule is about.</summary>
    public string Permission { get; set; } = null!;

    /// <summary>Whether the rule grants the permission or withdraws it.</summary>
    public bool IsAllow { get; set; }

    /// <summary>
    /// Whether the rule reaches the page's descendants as well as the page itself.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="true"/>, which is what an editor means by "give this team the
    /// products section". Clearing it produces a rule about exactly one page — the way to punch a
    /// single hole in an inherited deny without also opening the branch below it.
    /// </remarks>
    public bool IsInherited { get; set; } = true;
}
