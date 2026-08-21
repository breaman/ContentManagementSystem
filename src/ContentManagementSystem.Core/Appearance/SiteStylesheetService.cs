using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Appearance;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Appearance;

/// <inheritdoc cref="ISiteStylesheetService" />
/// <param name="context">The application database context.</param>
/// <param name="validator">Decides what may be stored and served.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="users">The signed-in editor's own id, recorded on every publish.</param>
/// <param name="clock">Injected so a test can publish at an instant it chose.</param>
/// <param name="cacheInvalidation">Enqueues the stylesheet's eviction inside the publishing save.</param>
/// <param name="options">The stylesheet's configured limits.</param>
/// <param name="logger">Log every publish: it is a change every visitor sees immediately.</param>
public sealed class SiteStylesheetService(
    ApplicationDbContext context,
    ICssValidator validator,
    ICmsAuthorization authorization,
    IUserService users,
    TimeProvider clock,
    ICacheInvalidationQueue cacheInvalidation,
    IOptions<SiteStylesheetOptions> options,
    ILogger<SiteStylesheetService> logger) : ISiteStylesheetService
{
    /// <inheritdoc />
    public async Task<CmsResult<SiteStylesheetDetail>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.AppearanceEdit)) return Forbidden();

        var sheet = await LoadAsync(cancellationToken);

        return CmsResult<SiteStylesheetDetail>.Success(await ProjectAsync(sheet, cancellationToken));
    }

    /// <inheritdoc />
    public Task<CmsResult<CssValidationReport>> ValidateAsync(
        string? css,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.AppearanceEdit))
        {
            return Task.FromResult(CmsResult<CssValidationReport>.Forbidden(
                "Editing the site stylesheet is not permitted.",
                SiteStylesheetCodes.Forbidden));
        }

        var diagnostics = validator.Validate(css);

        return Task.FromResult(CmsResult<CssValidationReport>.Success(new CssValidationReport(
            diagnostics.Count == 0,
            ByteLength(css),
            options.Value.MaxBytes,
            diagnostics)));
    }

    /// <inheritdoc />
    public async Task<CmsResult<SiteStylesheetDetail>> SaveDraftAsync(
        string css,
        string? expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(css);

        if (!authorization.HasPermission(CmsPermissions.AppearanceEdit)) return Forbidden();

        // Validated before anything is written, and the whole save is refused. Storing a draft the
        // publish would then reject leaves an administrator with a file that saved and cannot go
        // live, and no indication of which of the two operations was lying to them.
        if (Refuse(validator.Validate(css)) is { } refusal) return refusal;

        var sheet = await LoadAsync(cancellationToken);
        var entry = context.Entry(sheet);

        if (RowVersions.TryApply(entry, expectedRowVersion) is false)
        {
            return CmsResult<SiteStylesheetDetail>.Invalid(
                SiteStylesheetCodes.Conflict,
                "The supplied concurrency token is not one this server issued.");
        }

        sheet.DraftCss = css;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Somebody else saved between this caller's read and its write. Hand back what is
            // stored so the editor can offer keep-mine / take-theirs rather than a banner.
            context.ChangeTracker.Clear();

            var winner = await LoadAsync(cancellationToken);

            return CmsResult<SiteStylesheetDetail>.Conflict(
                SiteStylesheetCodes.Conflict,
                "Somebody else saved the site stylesheet while you were editing it.",
                value: await ProjectAsync(winner, cancellationToken));
        }

        return CmsResult<SiteStylesheetDetail>.Success(await ProjectAsync(sheet, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<CmsResult<SiteStylesheetDetail>> PublishAsync(
        string? note,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.AppearanceEdit)) return Forbidden();

        var sheet = await LoadAsync(cancellationToken);

        // Re-validated rather than trusted. A draft can arrive by a path this service never ran — a
        // restore, an environment promotion, a hand-written UPDATE — and publish is the last point
        // before it reaches every anonymous visitor.
        if (Refuse(validator.Validate(sheet.DraftCss)) is { } refusal) return refusal;

        var hash = Hash(sheet.DraftCss);

        if (sheet.PublishedHash is { } current && current.AsSpan().SequenceEqual(hash))
        {
            return CmsResult<SiteStylesheetDetail>.Invalid(
                SiteStylesheetCodes.NothingToPublish,
                "The draft is already what the site is serving.");
        }

        var now = clock.GetUtcNow();
        var wasPublished = sheet.PublishedCss is not null;

        var revision = new SiteStylesheetRevision
        {
            SiteStylesheetId = SiteStylesheet.SingletonId,
            Css = sheet.DraftCss,
            Hash = hash,
            ByteLength = ByteLength(sheet.DraftCss),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedOn = now,
            CreatedByUserId = users.UserId,
        };

        context.SiteStylesheetRevisions.Add(revision);

        Apply(sheet, sheet.DraftCss, hash, now, users.UserId);
        sheet.PublishedRevision = revision;

        // Enqueued into this save, so a publish that rolls back leaves no eviction behind and one
        // that commits always has one waiting — including if the process dies in between (P8-09).
        Evict(wasPublished, nowPublished: true);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Site stylesheet published: revision {RevisionId}, {ByteLength} bytes, by user {UserId}.",
            revision.Id,
            revision.ByteLength,
            users.UserId);

        return CmsResult<SiteStylesheetDetail>.Success(await ProjectAsync(sheet, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<CmsResult<SiteStylesheetDetail>> RevertAsync(
        int? revisionId,
        bool copyToDraft,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.AppearanceEdit)) return Forbidden();

        var sheet = await LoadAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var wasPublished = sheet.PublishedCss is not null;

        if (revisionId is null)
        {
            // Publishing nothing. The public document stops linking a second stylesheet at all and
            // the site is the design the deployment ships — the recovery path that always exists,
            // from a screen this stylesheet cannot affect.
            Apply(sheet, css: null, hash: null, now, publishedBy: null);
            sheet.PublishedRevision = null;
            sheet.PublishedRevisionId = null;
        }
        else
        {
            var revision = await context.SiteStylesheetRevisions
                .AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == revisionId.Value, cancellationToken);

            if (revision is null)
            {
                return CmsResult<SiteStylesheetDetail>.NotFound(
                    string.Create(CultureInfo.InvariantCulture, $"Revision {revisionId.Value} does not exist."),
                    SiteStylesheetCodes.RevisionNotFound);
            }

            // Validated on the way back out. A revision published before a rule was added is
            // exactly the stylesheet that rule was added for.
            if (Refuse(validator.Validate(revision.Css)) is { } refusal) return refusal;

            Apply(sheet, revision.Css, revision.Hash, now, users.UserId);
            sheet.PublishedRevisionId = revision.Id;

            if (copyToDraft) sheet.DraftCss = revision.Css;
        }

        Evict(wasPublished, nowPublished: revisionId is not null);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Site stylesheet reverted to revision {RevisionId} by user {UserId}.",
            revisionId,
            users.UserId);

        return CmsResult<SiteStylesheetDetail>.Success(await ProjectAsync(sheet, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<SiteStylesheetRevisionSummary>>> ListRevisionsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.AppearanceEdit))
        {
            return CmsResult<IReadOnlyList<SiteStylesheetRevisionSummary>>.Forbidden(
                "Editing the site stylesheet is not permitted.",
                SiteStylesheetCodes.Forbidden);
        }

        var sheet = await LoadAsync(cancellationToken);

        var rows = await context.SiteStylesheetRevisions
            .AsNoTracking()
            .OrderByDescending(revision => revision.CreatedOn)
            .ThenByDescending(revision => revision.Id)
            .Select(revision => new
            {
                revision.Id,
                revision.ByteLength,
                revision.Note,
                revision.CreatedOn,
                revision.CreatedByUserId,
            })
            .ToListAsync(cancellationToken);

        var names = await NamesAsync(rows.Select(row => row.CreatedByUserId), cancellationToken);

        var summaries = rows
            .Select(row => new SiteStylesheetRevisionSummary(
                row.Id,
                row.ByteLength,
                row.Note,
                row.CreatedOn,
                names.GetValueOrDefault(row.CreatedByUserId),
                row.Id == sheet.PublishedRevisionId))
            .ToList();

        return CmsResult<IReadOnlyList<SiteStylesheetRevisionSummary>>.Success(summaries);
    }

    /// <inheritdoc />
    public async Task<CmsResult<string>> GetRevisionCssAsync(
        int revisionId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.AppearanceEdit))
        {
            return CmsResult<string>.Forbidden(
                "Editing the site stylesheet is not permitted.",
                SiteStylesheetCodes.Forbidden);
        }

        var css = await context.SiteStylesheetRevisions
            .AsNoTracking()
            .Where(revision => revision.Id == revisionId)
            .Select(revision => revision.Css)
            .FirstOrDefaultAsync(cancellationToken);

        return css is null
            ? CmsResult<string>.NotFound(
                string.Create(CultureInfo.InvariantCulture, $"Revision {revisionId} does not exist."),
                SiteStylesheetCodes.RevisionNotFound)
            : CmsResult<string>.Success(css);
    }

    /// <summary>
    /// Enqueues the eviction a publish or a revert needs.
    /// </summary>
    /// <param name="wasPublished">Whether a stylesheet was published before this operation.</param>
    /// <param name="nowPublished">Whether one is published after it.</param>
    /// <remarks>
    /// Ordinarily only the stylesheet's own tag: the URL in every page's <c>&lt;head&gt;</c> does not
    /// change when the CSS does, which is exactly why it is stable rather than fingerprinted
    /// (spec section 30.4).
    /// <para>
    /// <strong>The exception is the transition.</strong> The document omits the <c>&lt;link&gt;</c>
    /// entirely while nothing is published, so the very first publish — and a revert to nothing —
    /// changes the <em>markup</em> of every page rather than only the bytes the link points at. A
    /// cached page rendered before that transition has no link in it and would go on having none
    /// until its hour was up. That case, and only that case, evicts the site.
    /// </para>
    /// </remarks>
    private void Evict(bool wasPublished, bool nowPublished) =>
        cacheInvalidation.Enqueue(wasPublished == nowPublished
            ? [CacheTags.SiteStylesheet]
            : [CacheTags.SiteStylesheet, CacheTags.All]);

    /// <summary>UTF-8 byte length, which is what the cap and the response are measured in.</summary>
    internal static int ByteLength(string? css) =>
        string.IsNullOrEmpty(css) ? 0 : Encoding.UTF8.GetByteCount(css);

    /// <summary>SHA-256 of the CSS, which becomes the response's entity tag.</summary>
    internal static byte[] Hash(string css) => SHA256.HashData(Encoding.UTF8.GetBytes(css));

    private static void Apply(
        SiteStylesheet sheet,
        string? css,
        byte[]? hash,
        DateTimeOffset now,
        int? publishedBy)
    {
        sheet.PublishedCss = css;
        sheet.PublishedHash = hash;
        sheet.PublishedOn = css is null ? null : now;
        sheet.PublishedByUserId = css is null ? null : publishedBy;
    }

    private static CmsResult<SiteStylesheetDetail> Forbidden() =>
        CmsResult<SiteStylesheetDetail>.Forbidden(
            "Editing the site stylesheet is not permitted.",
            SiteStylesheetCodes.Forbidden);

    /// <summary>
    /// Turns validator diagnostics into a refusal, or returns null when there is nothing to refuse.
    /// </summary>
    /// <remarks>
    /// Every diagnostic is carried, not just the first: an administrator fixing one <c>@import</c>
    /// per round trip would rewrite a pasted stylesheet a line at a time. The line and column travel
    /// in the diagnostic path, which is where the API's <c>errors</c> array puts a location.
    /// </remarks>
    private static CmsResult<SiteStylesheetDetail>? Refuse(IReadOnlyList<CssDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0) return null;

        var mapped = new List<ValidationDiagnostic>(diagnostics.Count);

        foreach (var diagnostic in diagnostics)
        {
            mapped.Add(new ValidationDiagnostic(
                diagnostic.Code,
                diagnostic.Message,
                ValidationSeverity.Error,
                diagnostic.Line > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"line {diagnostic.Line}, column {diagnostic.Column}")
                    : null));
        }

        return CmsResult<SiteStylesheetDetail>.Invalid(ValidationResult.From(mapped));
    }

    /// <summary>
    /// Reads the singleton, creating it if a database predates the seed row.
    /// </summary>
    private async Task<SiteStylesheet> LoadAsync(CancellationToken cancellationToken)
    {
        var sheet = await context.SiteStylesheets
            .FirstOrDefaultAsync(row => row.Id == SiteStylesheet.SingletonId, cancellationToken);

        if (sheet is not null) return sheet;

        sheet = new SiteStylesheet { Id = SiteStylesheet.SingletonId, DraftCss = string.Empty };
        context.SiteStylesheets.Add(sheet);

        return sheet;
    }

    private async Task<SiteStylesheetDetail> ProjectAsync(
        SiteStylesheet sheet,
        CancellationToken cancellationToken)
    {
        var names = sheet.PublishedByUserId is { } publisher
            ? await NamesAsync([publisher], cancellationToken)
            : [];

        var draftHash = Hash(sheet.DraftCss);
        var published = sheet.PublishedHash;

        return new SiteStylesheetDetail(
            sheet.DraftCss,
            sheet.PublishedCss,
            published is null
                ? sheet.DraftCss.Length > 0
                : !published.AsSpan().SequenceEqual(draftHash),
            ByteLength(sheet.DraftCss),
            ByteLength(sheet.PublishedCss),
            options.Value.MaxBytes,
            sheet.PublishedOn,
            sheet.PublishedByUserId is { } id ? names.GetValueOrDefault(id) : null,
            validator.Validate(sheet.DraftCss),
            Convert.ToBase64String(sheet.RowVersion ?? []));
    }

    private async Task<Dictionary<int, string>> NamesAsync(
        IEnumerable<int> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();

        if (ids.Count == 0) return [];

        return await context.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(
                user => user.Id,
                user => user.UserName ?? user.Email ?? string.Empty,
                cancellationToken);
    }
}
