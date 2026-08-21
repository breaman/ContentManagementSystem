using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Appearance;

/// <inheritdoc cref="IPublishedStylesheetReader" />
/// <param name="context">The application database context.</param>
public sealed class PublishedStylesheetReader(ApplicationDbContext context) : IPublishedStylesheetReader
{
    /// <inheritdoc />
    public async Task<PublishedStylesheet?> GetPublishedAsync(
        CancellationToken cancellationToken = default)
    {
        var row = await context.SiteStylesheets
            .AsNoTracking()
            .Where(sheet => sheet.Id == SiteStylesheet.SingletonId && sheet.PublishedCss != null)
            .Select(sheet => new
            {
                sheet.PublishedCss,
                sheet.PublishedHash,
                sheet.PublishedOn,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row?.PublishedCss is null || row.PublishedHash is null) return null;

        return new PublishedStylesheet(
            row.PublishedCss,
            FormatETag(row.PublishedHash),
            row.PublishedOn ?? DateTimeOffset.UnixEpoch);
    }

    /// <inheritdoc />
    public async Task<string?> GetPublishedETagAsync(CancellationToken cancellationToken = default)
    {
        var hash = await context.SiteStylesheets
            .AsNoTracking()
            .Where(sheet => sheet.Id == SiteStylesheet.SingletonId)
            .Select(sheet => sheet.PublishedHash)
            .FirstOrDefaultAsync(cancellationToken);

        return hash is null ? null : FormatETag(hash);
    }

    /// <summary>
    /// Formats the published hash as a strong entity tag.
    /// </summary>
    /// <remarks>
    /// Hex rather than Base64: an entity tag travels in a header where <c>+</c> and <c>/</c> are
    /// legal but routinely mangled by intermediaries, and a tag that comes back different is a tag
    /// that never matches. Truncated to sixteen bytes, which is more collision resistance than a
    /// cache validator needs and half the header.
    /// </remarks>
    private static string FormatETag(byte[] hash) =>
        $"\"{Convert.ToHexStringLower(hash.AsSpan(0, Math.Min(16, hash.Length)))}\"";
}
