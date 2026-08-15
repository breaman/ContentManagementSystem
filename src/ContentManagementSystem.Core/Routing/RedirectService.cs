using System.Globalization;
using System.Text;

using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Routing;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Routing;

/// <inheritdoc cref="IRedirectService" />
/// <param name="context">The application database context.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="clock">Source of the current time.</param>
/// <param name="logger">Log for created redirects, refused loops, and cycles found at resolve time.</param>
public sealed class RedirectService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    TimeProvider clock,
    ILogger<RedirectService> logger) : IRedirectService
{
    /// <summary>Header line written by <see cref="ExportAsync"/> and expected by <see cref="ImportAsync"/>.</summary>
    private const string CsvHeader = "from,to,status,notes";

    /// <summary>The two status codes a redirect may answer with (spec section 10.5).</summary>
    private static readonly short[] AllowedStatusCodes = [301, 302];

    /// <inheritdoc />
    public async Task<RedirectMatch?> ResolveAsync(
        string? url,
        CancellationToken cancellationToken = default)
    {
        var current = SiteUrls.Normalize(url);
        var seen = new HashSet<string>(StringComparer.Ordinal) { current };

        int firstId = 0;
        short firstStatus = 0;

        for (var hop = 0; hop < IRedirectService.MaxChainDepth; hop++)
        {
            var hash = SiteUrls.Hash(current);

            var redirect = await context.Redirects
                .AsNoTracking()
                .Where(candidate => candidate.FromUrlHash == hash && candidate.IsEnabled)
                .Select(candidate => new
                {
                    candidate.Id,
                    candidate.StatusCode,
                    candidate.ToUrl,
                    // Projected in the same round trip rather than loaded as a navigation, because
                    // a page destination is the common case and a second query per hop would make
                    // the cheapest response on the site the one that costs the most.
                    PageUrl = candidate.ToPage == null
                        ? null
                        : context.PageRoutes
                            .Where(route => route.PageId == candidate.ToPageId && route.IsPublished)
                            .OrderByDescending(route => route.IsPrimary)
                            .Select(route => route.Url)
                            .FirstOrDefault(),
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (redirect is null)
            {
                // Nothing claims this URL. On the first hop that means no redirect at all; on a
                // later one it means the chain ended here, which is the ordinary way a chain ends.
                return hop == 0 ? null : new RedirectMatch(firstId, current, firstStatus);
            }

            if (hop == 0)
            {
                firstId = redirect.Id;
                firstStatus = redirect.StatusCode;
            }

            var target = SiteUrls.Normalize(redirect.PageUrl ?? redirect.ToUrl);

            // A page destination whose page is not published resolves to nothing. Sending a visitor
            // to the site root instead would be worse than the 404 they asked for: it looks like the
            // link worked.
            if (redirect.PageUrl is null && string.IsNullOrWhiteSpace(redirect.ToUrl))
            {
                logger.LogWarning(
                    "Redirect {RedirectId} from '{FromUrl}' has no reachable destination.",
                    redirect.Id,
                    current);

                return null;
            }

            if (!seen.Add(target))
            {
                logger.LogError(
                    "Redirect cycle reached at '{Url}' while resolving. Serving 404 instead.",
                    target);

                return null;
            }

            current = target;
        }

        logger.LogError(
            "Redirect chain from '{Url}' exceeded {MaxDepth} hops. Serving 404 instead.",
            SiteUrls.Normalize(url),
            IRedirectService.MaxChainDepth);

        return null;
    }

    /// <inheritdoc />
    public async Task RecordHitAsync(int redirectId, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = clock.GetUtcNow();

            // ExecuteUpdate rather than load-modify-save: the increment is relative, so concurrent
            // hits add up, and it never enters the change tracker of a context that may be doing
            // something else.
            await context.Redirects
                .Where(redirect => redirect.Id == redirectId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(redirect => redirect.HitCount, redirect => redirect.HitCount + 1)
                        .SetProperty(redirect => redirect.LastHitOn, now),
                    cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Housekeeping must never be the reason a visitor does not reach the page they asked
            // for. The count is an input to a pruning report, not to the response.
            logger.LogWarning(
                exception,
                "Failed to count a hit on redirect {RedirectId}.",
                redirectId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RecordAutomaticAsync(
        string fromUrl,
        int toPageId,
        CancellationToken cancellationToken = default)
    {
        var source = SiteUrls.Normalize(fromUrl);
        var hash = SiteUrls.Hash(source);

        var existing = await context.Redirects
            .FirstOrDefaultAsync(candidate => candidate.FromUrlHash == hash, cancellationToken);

        if (existing is not null)
        {
            // A person's decision about this URL outranks a tree move (spec section 10.5).
            if (!existing.IsAutomatic) return false;

            existing.ToPageId = toPageId;
            existing.ToUrl = null;
            existing.IsEnabled = true;
        }
        else
        {
            context.Redirects.Add(new Redirect
            {
                FromUrl = source,
                FromUrlHash = hash,
                ToPageId = toPageId,
                StatusCode = 301,
                IsAutomatic = true,
                IsEnabled = true,
            });
        }

        await FlattenChainsIntoAsync(source, toPageId, toUrl: null, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<CmsResult<CursorPage<RedirectDetail>>> ListAsync(
        string? search = null,
        string? cursor = null,
        int limit = Cursor.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<CursorPage<RedirectDetail>>.Forbidden(
                "Reading redirects is not permitted.",
                RoutingCodes.Forbidden);
        }

        if (!Cursor.TryDecode(cursor, out var lastKey))
        {
            return CmsResult<CursorPage<RedirectDetail>>.Invalid(
                RoutingCodes.NotFound,
                "The paging cursor could not be read. Start the collection again without one.",
                nameof(cursor));
        }

        var take = Cursor.Clamp(limit);
        var query = context.Redirects.AsNoTracking().Where(redirect => redirect.Id > lastKey);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            query = query.Where(redirect =>
                EF.Functions.Like(redirect.FromUrl, $"%{term}%") ||
                (redirect.ToUrl != null && EF.Functions.Like(redirect.ToUrl, $"%{term}%")));
        }

        // One row more than asked for, so "is there another page" is answered without a count.
        var rows = await Project(query.OrderBy(redirect => redirect.Id).Take(take + 1))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var next = hasMore && rows.Count > 0 ? Cursor.Encode(rows[^1].Id) : null;

        return CmsResult<CursorPage<RedirectDetail>>.Success(new CursorPage<RedirectDetail>(rows, next));
    }

    /// <inheritdoc />
    public async Task<CmsResult<RedirectDetail>> CreateAsync(
        CreateRedirectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return CmsResult<RedirectDetail>.Forbidden(
                "Managing redirects is not permitted.",
                RoutingCodes.Forbidden);
        }

        var source = SiteUrls.Normalize(request.FromUrl);
        var checks = await ValidateAsync(
            request.FromUrl,
            source,
            request.ToPageId,
            request.ToUrl,
            request.StatusCode,
            cancellationToken);

        if (checks.HasErrors) return CmsResult<RedirectDetail>.Invalid(checks);

        var hash = SiteUrls.Hash(source);

        var existing = await context.Redirects
            .FirstOrDefaultAsync(candidate => candidate.FromUrlHash == hash, cancellationToken);

        if (existing is not null && !existing.IsAutomatic)
        {
            return CmsResult<RedirectDetail>.Conflict(
                RoutingCodes.SourceTaken,
                $"A redirect from '{source}' already exists. Edit it, or delete it first.",
                nameof(CreateRedirectRequest.FromUrl));
        }

        var destination = SiteUrls.Normalize(request.ToUrl);
        var loop = await FindLoopAsync(source, request.ToPageId, request.ToUrl, cancellationToken);

        if (loop is not null)
        {
            return CmsResult<RedirectDetail>.Invalid(
                RoutingCodes.Loop,
                loop,
                nameof(CreateRedirectRequest.ToUrl));
        }

        // An automatic redirect the system left behind is overwritten by a person stating what the
        // URL should do, rather than refused as a conflict: the automatic row is a default, and this
        // is somebody replacing the default.
        var redirect = existing ?? new Redirect { FromUrl = source, FromUrlHash = hash };

        redirect.ToPageId = request.ToPageId;
        redirect.ToUrl = request.ToPageId is null ? destination : null;
        redirect.StatusCode = request.StatusCode;
        redirect.Notes = request.Notes;
        redirect.IsAutomatic = false;
        redirect.IsEnabled = true;

        if (existing is null) context.Redirects.Add(redirect);

        await FlattenChainsIntoAsync(source, request.ToPageId, redirect.ToUrl, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Redirect {RedirectId} created from '{FromUrl}' with status {StatusCode}.",
            redirect.Id,
            source,
            redirect.StatusCode);

        return await LoadDetailAsync(redirect.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CmsResult<RedirectDetail>> UpdateAsync(
        int id,
        UpdateRedirectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return CmsResult<RedirectDetail>.Forbidden(
                "Managing redirects is not permitted.",
                RoutingCodes.Forbidden);
        }

        var redirect = await context.Redirects
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (redirect is null)
        {
            return CmsResult<RedirectDetail>.NotFound(
                $"No redirect has id {id}.",
                RoutingCodes.NotFound);
        }

        var toPageId = request.ToPageId ?? (request.ToUrl is null ? redirect.ToPageId : null);
        var toUrl = request.ToUrl ?? (request.ToPageId is null ? redirect.ToUrl : null);
        var status = request.StatusCode ?? redirect.StatusCode;

        var checks = await ValidateAsync(
            redirect.FromUrl,
            redirect.FromUrl,
            toPageId,
            toUrl,
            status,
            cancellationToken);

        if (checks.HasErrors) return CmsResult<RedirectDetail>.Invalid(checks);

        var loop = await FindLoopAsync(redirect.FromUrl, toPageId, toUrl, cancellationToken);

        if (loop is not null)
        {
            return CmsResult<RedirectDetail>.Invalid(
                RoutingCodes.Loop,
                loop,
                nameof(UpdateRedirectRequest.ToUrl));
        }

        redirect.ToPageId = toPageId;
        redirect.ToUrl = toPageId is null ? SiteUrls.Normalize(toUrl) : null;
        redirect.StatusCode = status;
        redirect.IsEnabled = request.IsEnabled ?? redirect.IsEnabled;
        redirect.Notes = request.Notes ?? redirect.Notes;

        // Editing an automatic redirect makes it somebody's decision, which is what protects it from
        // the next tree move overwriting the destination that was just chosen.
        redirect.IsAutomatic = false;

        await context.SaveChangesAsync(cancellationToken);

        return await LoadDetailAsync(redirect.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CmsResult<int>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return CmsResult<int>.Forbidden("Managing redirects is not permitted.", RoutingCodes.Forbidden);
        }

        var removed = await context.Redirects
            .Where(redirect => redirect.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return removed == 0
            ? CmsResult<int>.NotFound($"No redirect has id {id}.", RoutingCodes.NotFound)
            : CmsResult<int>.Success(id);
    }

    /// <inheritdoc />
    public async Task<CmsResult<RedirectImportResult>> ImportAsync(
        string csv,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return CmsResult<RedirectImportResult>.Forbidden(
                "Managing redirects is not permitted.",
                RoutingCodes.Forbidden);
        }

        var diagnostics = new List<ValidationDiagnostic>();
        var created = 0;
        var updated = 0;
        var skipped = 0;

        // Every source URL seen in this file, so a document that lists the same URL twice reports
        // the duplicate rather than failing on the unique index halfway through the save.
        var claimed = new Dictionary<string, Redirect>(StringComparer.Ordinal);
        var lines = (csv ?? string.Empty).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var lineNumber = index + 1;

            if (string.IsNullOrWhiteSpace(line)) continue;

            // The header is recognised rather than assumed to be line 1: an operator pasting a
            // fragment of a larger file has no header, and refusing that is unhelpful.
            if (index == 0 &&
                line.Replace(" ", string.Empty, StringComparison.Ordinal)
                    .StartsWith(CsvHeader, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cells = ReadCsvLine(line);

            if (cells.Count < 2 || string.IsNullOrWhiteSpace(cells[0]) || string.IsNullOrWhiteSpace(cells[1]))
            {
                skipped++;
                diagnostics.Add(Skipped(lineNumber, "a source and a destination are both required"));

                continue;
            }

            var source = SiteUrls.Normalize(cells[0]);
            var destination = SiteUrls.Normalize(cells[1]);

            if (source == destination)
            {
                skipped++;
                diagnostics.Add(Skipped(lineNumber, "the source and destination are the same URL"));

                continue;
            }

            short status = 301;

            if (cells.Count > 2 &&
                !string.IsNullOrWhiteSpace(cells[2]) &&
                (!short.TryParse(cells[2], CultureInfo.InvariantCulture, out status) ||
                 !AllowedStatusCodes.Contains(status)))
            {
                skipped++;
                diagnostics.Add(Skipped(lineNumber, $"'{cells[2]}' is not a status of 301 or 302"));

                continue;
            }

            if (claimed.ContainsKey(source))
            {
                skipped++;
                diagnostics.Add(Skipped(lineNumber, $"'{source}' appears earlier in this file"));

                continue;
            }

            var hash = SiteUrls.Hash(source);

            var existing = await context.Redirects
                .FirstOrDefaultAsync(candidate => candidate.FromUrlHash == hash, cancellationToken);

            // An existing row is updated whatever its origin, manual included. An import is itself
            // somebody stating what these URLs should do, with the same authority as the person who
            // typed the row — and refusing to touch manual rows would mean an export of this table
            // could not be re-imported, which is the one thing the CSV pair exists for.
            var redirect = existing ?? new Redirect { FromUrl = source, FromUrlHash = hash };

            redirect.ToUrl = destination;
            redirect.ToPageId = null;
            redirect.StatusCode = status;
            redirect.Notes = cells.Count > 3 ? Truncate(cells[3], FieldLengths.ShortDescription) : null;
            redirect.IsAutomatic = false;
            redirect.IsEnabled = true;

            if (existing is null)
            {
                context.Redirects.Add(redirect);
                created++;
            }
            else
            {
                updated++;
            }

            claimed[source] = redirect;
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Redirect import finished: {Created} created, {Updated} updated, {Skipped} skipped.",
            created,
            updated,
            skipped);

        return CmsResult<RedirectImportResult>.Success(
            new RedirectImportResult(created, updated, skipped),
            ValidationResult.From(diagnostics));
    }

    /// <inheritdoc />
    public async Task<CmsResult<string>> ExportAsync(CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<string>.Forbidden(
                "Reading redirects is not permitted.",
                RoutingCodes.Forbidden);
        }

        var rows = await Project(context.Redirects.AsNoTracking().OrderBy(redirect => redirect.FromUrl))
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine(CsvHeader);

        foreach (var row in rows)
        {
            builder
                .Append(Quote(row.FromUrl)).Append(',')
                .Append(Quote(row.ResolvedToUrl ?? string.Empty)).Append(',')
                .Append(row.StatusCode.ToString(CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(Quote(row.Notes ?? string.Empty));
        }

        return CmsResult<string>.Success(builder.ToString());
    }

    /// <summary>
    /// Rewrites every redirect whose destination is <paramref name="source"/> to the new target.
    /// </summary>
    /// <remarks>
    /// The flattening rule from spec section 10.5. Only literal destinations need it — a redirect
    /// pointing at a page already follows that page — so the query is over <c>ToUrl</c> alone.
    /// <para>
    /// Chains are collapsed rather than walked at resolve time so that a visitor pays one round
    /// trip instead of three, and so that depth cannot creep up as a site is reorganised repeatedly
    /// over years.
    /// </para>
    /// </remarks>
    private async Task FlattenChainsIntoAsync(
        string source,
        int? toPageId,
        string? toUrl,
        CancellationToken cancellationToken)
    {
        var inbound = await context.Redirects
            .Where(candidate => candidate.ToUrl == source)
            .ToListAsync(cancellationToken);

        foreach (var redirect in inbound)
        {
            redirect.ToPageId = toPageId;
            redirect.ToUrl = toPageId is null ? toUrl : null;
        }

        if (inbound.Count > 0)
        {
            logger.LogInformation(
                "Flattened {Count} redirect(s) that pointed at '{Source}'.",
                inbound.Count,
                source);
        }
    }

    /// <summary>
    /// Walks the chain a proposed redirect would start, looking for its own source URL.
    /// </summary>
    /// <returns>An explanation when the chain closes, or null when it does not.</returns>
    /// <remarks>
    /// Walks forward from the destination rather than checking only for the trivial self-reference,
    /// because the case that actually happens is <c>A → B</c> and <c>B → C</c> already stored and
    /// somebody adding <c>C → A</c>. Bounded by
    /// <see cref="IRedirectService.MaxChainDepth"/> so a cycle already in the data cannot make this
    /// check itself run forever.
    /// </remarks>
    private async Task<string?> FindLoopAsync(
        string source,
        int? toPageId,
        string? toUrl,
        CancellationToken cancellationToken)
    {
        var current = toPageId is not null
            ? await CurrentPageUrlAsync(toPageId.Value, cancellationToken)
            : SiteUrls.Normalize(toUrl);

        // An unpublished page destination has no URL to walk from, so there is no chain to close.
        if (current is null) return null;

        var seen = new HashSet<string>(StringComparer.Ordinal) { source };

        for (var hop = 0; hop < IRedirectService.MaxChainDepth; hop++)
        {
            if (!seen.Add(current))
            {
                return current == source
                    ? $"'{source}' would redirect to itself."
                    : $"This redirect closes a loop through '{current}'.";
            }

            var hash = SiteUrls.Hash(current);

            var next = await context.Redirects
                .AsNoTracking()
                .Where(candidate => candidate.FromUrlHash == hash)
                .Select(candidate => new { candidate.ToUrl, candidate.ToPageId })
                .FirstOrDefaultAsync(cancellationToken);

            if (next is null) return null;

            var target = next.ToPageId is not null
                ? await CurrentPageUrlAsync(next.ToPageId.Value, cancellationToken)
                : SiteUrls.Normalize(next.ToUrl);

            if (target is null) return null;

            current = target;
        }

        return $"The chain from '{source}' is longer than {IRedirectService.MaxChainDepth} hops.";
    }

    /// <summary>The published URL of a page, or null when it has none.</summary>
    private async Task<string?> CurrentPageUrlAsync(int pageId, CancellationToken cancellationToken) =>
        await context.PageRoutes
            .AsNoTracking()
            .Where(route => route.PageId == pageId && route.IsPublished)
            .OrderByDescending(route => route.IsPrimary)
            .Select(route => route.Url)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Checks everything about a proposed redirect that does not require walking the chain.</summary>
    private async Task<ValidationResult> ValidateAsync(
        string? suppliedSource,
        string source,
        int? toPageId,
        string? toUrl,
        short statusCode,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        if (string.IsNullOrWhiteSpace(suppliedSource) || source == SiteUrls.Root)
        {
            diagnostics.Add(new ValidationDiagnostic(
                RoutingCodes.SourceInvalid,
                "A redirect needs a site-relative source URL, and it cannot be the site root.",
                ValidationSeverity.Error,
                nameof(CreateRedirectRequest.FromUrl)));
        }

        if (source.Length > FieldLengths.Url)
        {
            diagnostics.Add(new ValidationDiagnostic(
                RoutingCodes.UrlTooLong,
                $"A URL may be at most {FieldLengths.Url} characters.",
                ValidationSeverity.Error,
                nameof(CreateRedirectRequest.FromUrl)));
        }

        var hasPage = toPageId is not null;
        var hasUrl = !string.IsNullOrWhiteSpace(toUrl);

        if (hasPage == hasUrl)
        {
            diagnostics.Add(new ValidationDiagnostic(
                RoutingCodes.DestinationInvalid,
                hasPage
                    ? "A redirect has one destination: name a page or a URL, not both."
                    : "A redirect needs a destination: name a page or a URL.",
                ValidationSeverity.Error,
                nameof(CreateRedirectRequest.ToUrl)));
        }

        if (hasPage)
        {
            var exists = await context.Pages
                .AsNoTracking()
                .AnyAsync(page => page.Id == toPageId, cancellationToken);

            if (!exists)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    RoutingCodes.DestinationNotFound,
                    $"No page has id {toPageId}, or it is in the recycle bin.",
                    ValidationSeverity.Error,
                    nameof(CreateRedirectRequest.ToPageId)));
            }
        }

        if (hasUrl && SiteUrls.Normalize(toUrl).Length > FieldLengths.Url)
        {
            diagnostics.Add(new ValidationDiagnostic(
                RoutingCodes.UrlTooLong,
                $"A URL may be at most {FieldLengths.Url} characters.",
                ValidationSeverity.Error,
                nameof(CreateRedirectRequest.ToUrl)));
        }

        if (!AllowedStatusCodes.Contains(statusCode))
        {
            diagnostics.Add(new ValidationDiagnostic(
                RoutingCodes.StatusInvalid,
                "A redirect answers with 301 (permanent) or 302 (temporary).",
                ValidationSeverity.Error,
                nameof(CreateRedirectRequest.StatusCode)));
        }

        return ValidationResult.From(diagnostics);
    }

    /// <summary>Loads a redirect in the shape the API returns.</summary>
    private async Task<CmsResult<RedirectDetail>> LoadDetailAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var detail = await Project(context.Redirects.AsNoTracking().Where(redirect => redirect.Id == id))
            .FirstOrDefaultAsync(cancellationToken);

        return detail is null
            ? CmsResult<RedirectDetail>.NotFound($"No redirect has id {id}.", RoutingCodes.NotFound)
            : CmsResult<RedirectDetail>.Success(detail);
    }

    /// <summary>
    /// Projects redirect rows into the API shape, resolving page destinations to current URLs.
    /// </summary>
    /// <remarks>
    /// One expression shared by the list, the single read, and the export, so the three cannot
    /// report a different destination for the same row.
    /// </remarks>
    private IQueryable<RedirectDetail> Project(IQueryable<Redirect> query) =>
        query.Select(redirect => new RedirectDetail(
            redirect.Id,
            redirect.FromUrl,
            redirect.ToUrl,
            redirect.ToPageId,
            redirect.ToUrl ?? context.PageRoutes
                .Where(route => route.PageId == redirect.ToPageId && route.IsPublished)
                .OrderByDescending(route => route.IsPrimary)
                .Select(route => route.Url)
                .FirstOrDefault(),
            redirect.StatusCode,
            redirect.IsAutomatic,
            redirect.IsEnabled,
            redirect.Notes,
            redirect.HitCount,
            redirect.LastHitOn));

    /// <summary>Builds the warning that reports one unusable import row.</summary>
    private static ValidationDiagnostic Skipped(int lineNumber, string reason) =>
        new(
            RoutingCodes.ImportRowInvalid,
            $"Line {lineNumber} was skipped: {reason}.",
            ValidationSeverity.Warning,
            $"csv[{lineNumber}]");

    /// <summary>
    /// Splits one CSV line, honouring quoted cells and doubled quotes inside them.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than taken from a package, and the reason is scope: the document this
    /// reads is four columns of URLs written by this application's own exporter or by a spreadsheet,
    /// and a CSV library would be a dependency in <c>Core</c> carried for one method. Embedded
    /// newlines inside quoted cells are the one thing it does not handle; a URL cannot contain one,
    /// and a note that does is truncated at the line break rather than corrupting the file.
    /// </remarks>
    private static List<string> ReadCsvLine(string line)
    {
        var cells = new List<string>(4);
        var cell = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (quoted)
            {
                if (character != '"')
                {
                    cell.Append(character);
                }
                else if (index + 1 < line.Length && line[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;

                    break;

                case ',':
                    cells.Add(cell.ToString().Trim());
                    cell.Clear();

                    break;

                default:
                    cell.Append(character);

                    break;
            }
        }

        cells.Add(cell.ToString().Trim());

        return cells;
    }

    /// <summary>Wraps a cell for export, quoting only when the content forces it.</summary>
    private static string Quote(string value) =>
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    /// <summary>Cuts an imported note to the column that stores it.</summary>
    private static string? Truncate(string value, int limit) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= limit ? value : value[..limit];
}
