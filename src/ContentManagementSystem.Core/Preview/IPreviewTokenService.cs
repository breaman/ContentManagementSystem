using ContentManagementSystem.Shared.Contracts.Preview;

namespace ContentManagementSystem.Core.Preview;

/// <summary>Why a presented preview link did or did not work.</summary>
/// <remarks>
/// A closed set, because the endpoint maps each member to one status and one sentence for a
/// stakeholder who has no account and no way to investigate. The distinction between them is
/// entirely about who that person should go and talk to.
/// </remarks>
public enum PreviewRedemptionOutcome
{
    /// <summary>The link is good and a use has been recorded.</summary>
    Valid = 0,

    /// <summary>No such token was issued, or it has been revoked (spec section 12.2).</summary>
    Invalid = 1,

    /// <summary>The token was issued but its expiry has passed.</summary>
    Expired = 2,

    /// <summary>The token has been viewed as many times as it was issued for.</summary>
    Exhausted = 3,

    /// <summary>The token is live but the page has been recycled, so there is nothing to show.</summary>
    PageUnavailable = 4,
}

/// <summary>
/// The answer to presenting a preview link.
/// </summary>
/// <param name="Outcome">Whether it worked, and why not when it did not.</param>
/// <param name="PageId">The page the link grants a view of; zero when it granted nothing.</param>
/// <param name="PageVersionId">The exact version to serve; zero when it granted nothing.</param>
/// <param name="ExpiresOn">When the link stops working, for the toolbar to show the reviewer.</param>
public sealed record PreviewRedemption(
    PreviewRedemptionOutcome Outcome,
    int PageId = 0,
    int PageVersionId = 0,
    DateTimeOffset ExpiresOn = default)
{
    /// <summary>Whether the link may be served.</summary>
    public bool IsValid => Outcome is PreviewRedemptionOutcome.Valid;

    /// <summary>A refusal carrying no page.</summary>
    /// <param name="outcome">Why it was refused.</param>
    public static PreviewRedemption Refused(PreviewRedemptionOutcome outcome) => new(outcome);
}

/// <summary>
/// Issues, lists, revokes, and redeems shareable preview links (task P3-17, spec section 12.2).
/// </summary>
/// <remarks>
/// The three management operations authorize the caller; <see cref="RedeemAsync"/> deliberately does
/// not, because the token <em>is</em> the authorization and its holder has no account by design.
/// <para>
/// Issuing needs <c>Content.Edit</c> rather than <c>Content.Publish</c>. Sharing work for review is
/// the ordinary act of the person doing the work, and requiring the publish permission would mean an
/// author could not get their own draft reviewed — which is the whole feature (spec section 21.1).
/// </para>
/// </remarks>
public interface IPreviewTokenService
{
    /// <summary>
    /// Issues a link, returning its secret for the only time it exists.
    /// </summary>
    /// <param name="request">Which version, for how long, and how many views.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The stored row and the secret, or why it was refused.</returns>
    Task<CmsResult<IssuedPreviewToken>> IssueAsync(
        CreatePreviewTokenRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the links issued for a page, newest first.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The links, including revoked and expired ones.</returns>
    /// <remarks>
    /// Revoked and expired rows are listed rather than filtered out. "This link was revoked on the
    /// 3rd" is the answer somebody needs when a stakeholder reports that a link stopped working, and
    /// a list that hides them can only answer "there is no such link".
    /// </remarks>
    Task<CmsResult<IReadOnlyList<PreviewTokenSummary>>> ListAsync(
        int pageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes one link.
    /// </summary>
    /// <param name="id">Identity of the token row.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The row as it now stands, or why the revocation was refused.</returns>
    /// <remarks>
    /// The row is kept and stamped, never deleted. Deleting it would make an audit of who could see
    /// an unpublished page impossible to reconstruct, and revocation is the moment that audit
    /// matters most.
    /// </remarks>
    Task<CmsResult<PreviewTokenSummary>> RevokeAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every live link for a page.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>How many links were revoked.</returns>
    Task<CmsResult<int>> RevokeAllAsync(
        int pageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a presented token without recording a use.
    /// </summary>
    /// <param name="token">The base64url secret from the URL.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>What the link would grant, or why it grants nothing.</returns>
    /// <remarks>
    /// The preview chrome — the toolbar and the device frame — is served through this, and the page
    /// inside the frame through <see cref="RedeemAsync"/>. <strong>A use is a view of the content,
    /// not a request for the furniture around it</strong>, so a single-use link spends its one view
    /// on the page rather than on the wrapper that goes and fetches it.
    /// </remarks>
    Task<PreviewRedemption> CheckAsync(
        string? token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a presented token and, if it is good, records the use.
    /// </summary>
    /// <param name="token">The base64url secret from the URL.</param>
    /// <param name="cancellationToken">Token observed while querying and updating.</param>
    /// <returns>What to serve, or why nothing will be.</returns>
    /// <remarks>
    /// Validation and the use count are one operation on purpose: a caller that checked first and
    /// incremented afterwards would let two simultaneous requests both pass a <c>MaxUses = 1</c>
    /// link, which is the one thing a single-use link is for.
    /// </remarks>
    Task<PreviewRedemption> RedeemAsync(
        string? token,
        CancellationToken cancellationToken = default);
}
