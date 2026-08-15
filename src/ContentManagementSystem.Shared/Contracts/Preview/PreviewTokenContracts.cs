namespace ContentManagementSystem.Shared.Contracts.Preview;

/// <summary>
/// Body of <c>POST /api/cms/v1/preview-tokens</c>.
/// </summary>
/// <param name="PageId">Page the link grants a view of.</param>
/// <param name="VersionId">
/// The exact version served, or null for the page's current draft. Pinned at issue time rather than
/// resolved per request, so the link keeps showing the version it was issued for however many times
/// the page is published afterwards (spec section 12.2).
/// <para>
/// Note what pinning can and cannot promise. Naming a frozen version — a published or archived one —
/// gives an unchanging document, which is what a sign-off needs. Naming the draft pins the draft
/// <em>row</em>, and that row is the one version a page is allowed to keep editing
/// (spec section 11.1), so the link follows those edits. Both are useful; which one the sender wants
/// is the choice this member exists for.
/// </para>
/// </param>
/// <param name="ExpiresInDays">
/// How long the link lasts, defaulting to seven days and capped at thirty (spec section 12.2).
/// </param>
/// <param name="MaxUses">Views allowed, or null for unlimited within the expiry.</param>
/// <param name="Notes">Who the link was shared with and why. Housekeeping only.</param>
public sealed record CreatePreviewTokenRequest(
    int PageId,
    int? VersionId = null,
    int? ExpiresInDays = null,
    int? MaxUses = null,
    string? Notes = null);

/// <summary>
/// One preview link, as the management API reports it.
/// </summary>
/// <remarks>
/// <strong>There is no token member.</strong> Only the SHA-256 hash is stored, so this shape cannot
/// carry the secret even if a caller wanted it to — which is exactly the property acceptance
/// criterion P3 #10 asserts. The secret exists once, in <see cref="IssuedPreviewToken"/>, in the
/// response to the request that created it.
/// </remarks>
/// <param name="Id">Identity of the token row, used to revoke it.</param>
/// <param name="PageId">Page the link grants a view of.</param>
/// <param name="PageVersionId">The exact version served.</param>
/// <param name="VersionNumber">That version's number within the page.</param>
/// <param name="VersionStatus">Where that version sits in the editorial lifecycle.</param>
/// <param name="CreatedOn">When the link was issued.</param>
/// <param name="CreatedBy">Who issued it.</param>
/// <param name="ExpiresOn">When it stops working.</param>
/// <param name="MaxUses">Views allowed, or null for unlimited.</param>
/// <param name="UseCount">Views so far.</param>
/// <param name="RevokedOn">When it was revoked, or null while it is live.</param>
/// <param name="IsActive">Whether it would work if presented right now.</param>
/// <param name="Notes">Who it was shared with and why.</param>
public sealed record PreviewTokenSummary(
    int Id,
    int PageId,
    int PageVersionId,
    int VersionNumber,
    string VersionStatus,
    DateTimeOffset? CreatedOn,
    int CreatedBy,
    DateTimeOffset ExpiresOn,
    int? MaxUses,
    int UseCount,
    DateTimeOffset? RevokedOn,
    bool IsActive,
    string? Notes);

/// <summary>
/// A freshly issued preview link: the row, and the one and only sight of its secret.
/// </summary>
/// <param name="Summary">The stored row.</param>
/// <param name="Token">
/// The base64url secret, 32 bytes of CSPRNG output. Held nowhere on the server — only its SHA-256
/// hash is stored — so this response is the single opportunity to copy it. A lost link is reissued,
/// never recovered (spec section 12.2).
/// </param>
/// <param name="Url">
/// The site-relative URL to share, <c>/preview/s/{token}</c>, assembled here so no caller has to
/// know the shape of the path and get it subtly wrong.
/// </param>
public sealed record IssuedPreviewToken(PreviewTokenSummary Summary, string Token, string Url);
