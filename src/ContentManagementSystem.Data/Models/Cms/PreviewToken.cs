namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A capability to view one unpublished page version, held by whoever has the link.
/// </summary>
/// <remarks>
/// Spec section 12.2. The point is stakeholders with no account: a bearer link is the only way to
/// show an unpublished page to a client without provisioning them an identity, and the row is what
/// bounds the damage when that link is forwarded — an expiry, a use count, and a revocation switch.
/// <para>
/// <strong>Only <see cref="TokenHash"/> is stored, never the token.</strong> The secret exists in
/// the response that created it and nowhere else, so a database backup, a read-only reporting
/// account, or a leaked audit export cannot be turned into access to unpublished content. That also
/// means a lost link cannot be recovered, only reissued — which is the correct trade and is asserted
/// as acceptance criterion P3 #10.
/// </para>
/// <para>
/// It is deliberately <em>not</em> a session. The token names one page version; following an
/// internal link from inside the preview leaves it and reaches the public site.
/// </para>
/// </remarks>
public class PreviewToken : FingerPrintEntityBase
{
    /// <summary>SHA-256 of the issued token, carrying the unique index.</summary>
    /// <remarks>
    /// A single unsalted hash rather than a password KDF, and that is correct here: the token is 32
    /// bytes of CSPRNG output, so there is no dictionary to run and no work factor that would make
    /// guessing meaningfully harder than it already is. What a KDF would add is per-request cost on
    /// a lookup that has to be indexed anyway.
    /// </remarks>
    public byte[] TokenHash { get; set; } = null!;

    /// <summary>Page the token grants a view of.</summary>
    public int PageId { get; set; }

    /// <summary>Page the token grants a view of.</summary>
    public Page Page { get; set; } = null!;

    /// <summary>
    /// The exact version served. Never resolved at request time.
    /// </summary>
    /// <remarks>
    /// Pinned rather than following the page's current draft, because a link shared for review must
    /// show the reviewer what the sender was looking at. A token that tracked the draft would change
    /// what it shows every time somebody saved.
    /// </remarks>
    public int PageVersionId { get; set; }

    /// <summary>The exact version served.</summary>
    public PageVersion PageVersion { get; set; } = null!;

    /// <summary>When the token stops working. Default 7 days out, maximum 30 (spec section 12.2).</summary>
    public DateTimeOffset ExpiresOn { get; set; }

    /// <summary>Maximum number of views, or null for unlimited within the expiry.</summary>
    public int? MaxUses { get; set; }

    /// <summary>Views so far.</summary>
    public int UseCount { get; set; }

    /// <summary>When the token was revoked, or null while it is live.</summary>
    /// <remarks>
    /// A timestamp rather than a flag, and the row is kept rather than deleted, because "this link
    /// was revoked on the 3rd" is the answer somebody will need when a stakeholder reports that a
    /// link stopped working.
    /// </remarks>
    public DateTimeOffset? RevokedOn { get; set; }

    /// <summary>Who revoked it. Null while the token is live.</summary>
    public int? RevokedBy { get; set; }

    /// <summary>Editor-facing note recording who the link was shared with and why.</summary>
    public string? Notes { get; set; }
}
