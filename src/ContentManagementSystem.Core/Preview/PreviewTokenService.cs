using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Preview;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Preview;

/// <inheritdoc cref="IPreviewTokenService" />
/// <param name="context">The application database context.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="users">Who the caller is, for the revocation stamp.</param>
/// <param name="clock">Source of the current time, so expiry is testable without waiting.</param>
/// <param name="logger">Log for issuance and revocation, which are auditable acts.</param>
/// <remarks>
/// Every write here is a disclosure decision — issuing one makes an unpublished page readable by
/// anybody holding a URL — so both the issue and the revoke are logged with the page, the version,
/// and the caller. The secret itself never reaches a log, for the same reason it never reaches the
/// database.
/// </remarks>
public sealed class PreviewTokenService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    IUserService users,
    TimeProvider clock,
    ILogger<PreviewTokenService> logger) : IPreviewTokenService
{
    /// <inheritdoc />
    public async Task<CmsResult<IssuedPreviewToken>> IssueAsync(
        CreatePreviewTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return CmsResult<IssuedPreviewToken>.Forbidden(
                "Issuing preview links is not permitted.",
                PreviewCodes.Forbidden);
        }

        var days = request.ExpiresInDays ?? PreviewTokens.DefaultExpiryDays;

        if (days is < 1 or > PreviewTokens.MaxExpiryDays)
        {
            // Refused rather than clamped: a link somebody believes lasts a year and which actually
            // lasts thirty days is a support ticket on day thirty-one, and this request is the last
            // moment the misunderstanding is visible.
            return CmsResult<IssuedPreviewToken>.Invalid(
                PreviewCodes.ExpiryInvalid,
                $"A preview link lasts between 1 and {PreviewTokens.MaxExpiryDays} days.",
                nameof(CreatePreviewTokenRequest.ExpiresInDays));
        }

        if (request.MaxUses is <= 0)
        {
            return CmsResult<IssuedPreviewToken>.Invalid(
                PreviewCodes.MaxUsesInvalid,
                "A use limit must be at least 1, or absent for unlimited views.",
                nameof(CreatePreviewTokenRequest.MaxUses));
        }

        if (request.Notes is { Length: > FieldLengths.ShortDescription })
        {
            return CmsResult<IssuedPreviewToken>.Invalid(
                PreviewCodes.NoteTooLong,
                $"The note is longer than {FieldLengths.ShortDescription} characters.",
                nameof(CreatePreviewTokenRequest.Notes));
        }

        // The version is resolved through the page, so a version id belonging to somebody else's
        // page cannot be shared under this one. Defaulting to the draft is what the editor means by
        // "share this for review" — it is the version they are looking at.
        var page = await context.Pages
            .AsNoTracking()
            .Where(candidate => candidate.Id == request.PageId)
            .Select(candidate => new { candidate.Id, candidate.DraftVersionId, candidate.PublishedVersionId })
            .FirstOrDefaultAsync(cancellationToken);

        if (page is null)
        {
            return CmsResult<IssuedPreviewToken>.NotFound(
                $"No page has id {request.PageId}.",
                PreviewCodes.NotFound);
        }

        var versionId = request.VersionId ?? page.DraftVersionId ?? page.PublishedVersionId;

        if (versionId is null)
        {
            return CmsResult<IssuedPreviewToken>.NotFound(
                $"Page {request.PageId} has no version to share.",
                PreviewCodes.VersionNotFound);
        }

        var version = await context.PageVersions
            .AsNoTracking()
            .Where(candidate => candidate.Id == versionId && candidate.PageId == page.Id)
            .Select(candidate => new { candidate.Id, candidate.VersionNumber, candidate.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null)
        {
            return CmsResult<IssuedPreviewToken>.NotFound(
                $"Page {request.PageId} has no version {versionId}.",
                PreviewCodes.VersionNotFound);
        }

        var (secret, hash) = PreviewTokens.Create();
        var now = clock.GetUtcNow();

        var token = new PreviewToken
        {
            TokenHash = hash,
            PageId = page.Id,
            PageVersionId = version.Id,
            ExpiresOn = now.AddDays(days),
            MaxUses = request.MaxUses,
            UseCount = 0,
            Notes = request.Notes,
        };

        context.PreviewTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Preview link {TokenId} issued for page {PageId} version {VersionId}, expiring {ExpiresOn:O}.",
            token.Id,
            token.PageId,
            token.PageVersionId,
            token.ExpiresOn);

        var summary = Project(token, version.VersionNumber, version.Status, now);

        return CmsResult<IssuedPreviewToken>.Success(
            new IssuedPreviewToken(summary, secret, PreviewTokens.UrlFor(secret)));
    }

    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<PreviewTokenSummary>>> ListAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<PreviewTokenSummary>>.Forbidden(
                "Reading preview links is not permitted.",
                PreviewCodes.Forbidden);
        }

        // IgnoreQueryFilters on the page, so the links of a recycled page are still listable: the
        // first thing to do about a page that was deleted while links to it were out is to revoke
        // them, and a list that hid them would make that impossible.
        var exists = await context.Pages
            .IgnoreQueryFilters()
            .AnyAsync(candidate => candidate.Id == pageId, cancellationToken);

        if (!exists)
        {
            return CmsResult<IReadOnlyList<PreviewTokenSummary>>.NotFound(
                $"No page has id {pageId}.",
                PreviewCodes.NotFound);
        }

        var now = clock.GetUtcNow();

        var rows = await context.PreviewTokens
            .AsNoTracking()
            .Where(token => token.PageId == pageId)
            .OrderByDescending(token => token.Id)
            .Select(token => new
            {
                Token = token,
                token.PageVersion.VersionNumber,
                token.PageVersion.Status,
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<PreviewTokenSummary> summaries =
        [
            .. rows.Select(row => Project(row.Token, row.VersionNumber, row.Status, now))
        ];

        return CmsResult<IReadOnlyList<PreviewTokenSummary>>.Success(summaries);
    }

    /// <inheritdoc />
    public async Task<CmsResult<PreviewTokenSummary>> RevokeAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return CmsResult<PreviewTokenSummary>.Forbidden(
                "Revoking preview links is not permitted.",
                PreviewCodes.Forbidden);
        }

        var token = await context.PreviewTokens
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (token is null)
        {
            return CmsResult<PreviewTokenSummary>.NotFound(
                $"No preview link has id {id}.",
                PreviewCodes.NotFound);
        }

        var now = clock.GetUtcNow();

        // Revoking an already-revoked link is a success that changes nothing rather than a conflict.
        // The caller wanted the link to stop working and it has; two people clicking revoke on the
        // same row is not an error either of them can act on.
        if (token.RevokedOn is null)
        {
            token.RevokedOn = now;
            token.RevokedBy = users.UserId;

            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Preview link {TokenId} for page {PageId} revoked by user {UserId}.",
                token.Id,
                token.PageId,
                users.UserId);
        }

        var version = await context.PageVersions
            .AsNoTracking()
            .Where(candidate => candidate.Id == token.PageVersionId)
            .Select(candidate => new { candidate.VersionNumber, candidate.Status })
            .FirstAsync(cancellationToken);

        return CmsResult<PreviewTokenSummary>.Success(
            Project(token, version.VersionNumber, version.Status, now));
    }

    /// <inheritdoc />
    public async Task<CmsResult<int>> RevokeAllAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return CmsResult<int>.Forbidden(
                "Revoking preview links is not permitted.",
                PreviewCodes.Forbidden);
        }

        var now = clock.GetUtcNow();
        var userId = users.UserId;

        // A set-based update rather than a load-and-save loop. Bulk revocation is the panic button —
        // a draft went out to the wrong distribution list — and it should not be proportional to how
        // many links a page has accumulated.
        var revoked = await context.PreviewTokens
            .Where(token => token.PageId == pageId && token.RevokedOn == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedOn, now)
                    .SetProperty(token => token.RevokedBy, userId)
                    .SetProperty(token => token.ModifiedOn, now)
                    .SetProperty(token => token.ModifiedBy, userId),
                cancellationToken);

        if (revoked > 0)
        {
            logger.LogInformation(
                "{Count} preview link(s) for page {PageId} revoked by user {UserId}.",
                revoked,
                pageId,
                userId);
        }

        return CmsResult<int>.Success(revoked);
    }

    /// <inheritdoc />
    public async Task<PreviewRedemption> CheckAsync(
        string? token,
        CancellationToken cancellationToken = default) =>
        (await EvaluateAsync(token, cancellationToken)).Redemption;

    /// <inheritdoc />
    public async Task<PreviewRedemption> RedeemAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        var (redemption, tokenId) = await EvaluateAsync(token, cancellationToken);

        if (!redemption.IsValid) return redemption;

        // The check above and this increment are two statements, so the guard is repeated inside the
        // update. Two simultaneous requests for a MaxUses = 1 link both pass the check; only one of
        // them updates a row here, and the other is told the link is exhausted — which is the whole
        // meaning of a single-use link.
        var claimed = await context.PreviewTokens
            .Where(candidate => candidate.Id == tokenId &&
                                candidate.RevokedOn == null &&
                                (candidate.MaxUses == null || candidate.UseCount < candidate.MaxUses))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.UseCount, candidate => candidate.UseCount + 1),
                cancellationToken);

        return claimed == 0
            ? PreviewRedemption.Refused(PreviewRedemptionOutcome.Exhausted)
            : redemption;
    }

    /// <summary>
    /// Decides what a presented token grants, without changing anything.
    /// </summary>
    /// <param name="token">The base64url secret from the URL.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The answer, and the row's id for the caller that goes on to record a use.</returns>
    private async Task<(PreviewRedemption Redemption, int TokenId)> EvaluateAsync(
        string? token,
        CancellationToken cancellationToken)
    {
        // Shape first, so a crawler walking /preview/s/{anything} is refused without a query.
        if (!PreviewTokens.TryHash(token, out var hash))
        {
            return (PreviewRedemption.Refused(PreviewRedemptionOutcome.Invalid), 0);
        }

        // IgnoreQueryFilters so a token whose page has been recycled is still found. Without it the
        // reviewer would be told the link is invalid, which sends them back to whoever shared it
        // instead of to the editor who deleted the page.
        var row = await context.PreviewTokens
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(candidate => candidate.TokenHash == hash)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.PageId,
                candidate.PageVersionId,
                candidate.ExpiresOn,
                candidate.MaxUses,
                candidate.UseCount,
                candidate.RevokedOn,
                PageIsDeleted = candidate.Page.IsDeleted,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null || row.RevokedOn is not null)
        {
            // One answer for both, deliberately. Confirming that a string was once a real token
            // narrows the search for anybody probing, and the person holding a revoked link has to
            // go back to whoever sent it either way (spec section 12.2).
            return (PreviewRedemption.Refused(PreviewRedemptionOutcome.Invalid), 0);
        }

        if (row.ExpiresOn <= clock.GetUtcNow())
        {
            return (PreviewRedemption.Refused(PreviewRedemptionOutcome.Expired), row.Id);
        }

        if (row.MaxUses is { } limit && row.UseCount >= limit)
        {
            return (PreviewRedemption.Refused(PreviewRedemptionOutcome.Exhausted), row.Id);
        }

        if (row.PageIsDeleted)
        {
            // Checked before a use could be recorded. Burning one of a single-use link's views on a
            // page that cannot be shown would leave the reviewer with a link that is now exhausted
            // as well as unavailable, and nothing to do about either.
            return (PreviewRedemption.Refused(PreviewRedemptionOutcome.PageUnavailable), row.Id);
        }

        return (
            new PreviewRedemption(
                PreviewRedemptionOutcome.Valid,
                row.PageId,
                row.PageVersionId,
                row.ExpiresOn),
            row.Id);
    }

    /// <summary>
    /// Projects a stored row into the shape the API returns.
    /// </summary>
    /// <param name="token">The stored row.</param>
    /// <param name="versionNumber">The shared version's number within the page.</param>
    /// <param name="status">The shared version's lifecycle status.</param>
    /// <param name="now">The current time, so a list is judged against one instant.</param>
    /// <remarks>
    /// Note what is not projected: <c>TokenHash</c>. The summary has no member that could carry it,
    /// so there is no path from the database to a client that leaks the material a link is made of
    /// even in part — which is half of acceptance criterion P3 #10, made structural.
    /// </remarks>
    private static PreviewTokenSummary Project(
        PreviewToken token,
        int versionNumber,
        PageVersionStatus status,
        DateTimeOffset now) =>
        new(
            token.Id,
            token.PageId,
            token.PageVersionId,
            versionNumber,
            status.ToString(),
            token.CreatedOn,
            token.CreatedBy,
            token.ExpiresOn,
            token.MaxUses,
            token.UseCount,
            token.RevokedOn,
            token.RevokedOn is null &&
                token.ExpiresOn > now &&
                (token.MaxUses is null || token.UseCount < token.MaxUses),
            token.Notes);
}
