using System.Text.Json;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Structure;

/// <inheritdoc cref="ITemplateService" />
/// <param name="context">The application database context.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="logger">Log for structural changes and unreadable stored JSON.</param>
public sealed class TemplateService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    ILogger<TemplateService> logger) : ITemplateService
{
    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<TemplateSummary>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<TemplateSummary>>.Forbidden("Reading templates is not permitted.");
        }

        var templates = await context.Templates
            .AsNoTracking()
            .OrderBy(template => template.SortOrder)
            .ThenBy(template => template.Name)
            .Select(template => new TemplateSummary(
                template.Id,
                template.Key,
                template.Name,
                template.Description,
                template.ComponentTypeName,
                template.IsOrphaned,
                template.IsEnabled,
                template.CurrentRevision,
                template.SortOrder,
                template.Zones.Count))
            .ToListAsync(cancellationToken);

        return CmsResult<IReadOnlyList<TemplateSummary>>.Success(templates);
    }

    /// <inheritdoc />
    public async Task<CmsResult<TemplateDetail>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<TemplateDetail>.Forbidden("Reading templates is not permitted.");
        }

        var template = await context.Templates
            .AsNoTracking()
            .Include(candidate => candidate.Zones)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return template is null
            ? CmsResult<TemplateDetail>.NotFound($"No template has id {id}.")
            : CmsResult<TemplateDetail>.Success(ToDetail(template));
    }

    /// <inheritdoc />
    public async Task<CmsResult<TemplateDetail>> CreateAsync(
        CreateTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<TemplateDetail>.Forbidden("Managing templates is not permitted.");
        }

        var diagnostics = new List<ValidationDiagnostic>();
        diagnostics.AddRange(ContentKeys.Validate(request.Key).Diagnostics);
        diagnostics.AddRange(ValidateMetadata(request.Name, request.Description));

        if (diagnostics.Count > 0)
        {
            return CmsResult<TemplateDetail>.Invalid(ValidationResult.From(diagnostics));
        }

        var key = request.Key!.Trim();

        if (await KeyExistsAsync(key, cancellationToken))
        {
            return DuplicateKey(key);
        }

        var template = new Template
        {
            Key = key,
            Name = request.Name!.Trim(),
            Description = SlotRules.Clean(request.Description),
            SortOrder = request.SortOrder,
            IsEnabled = true,
            // A template created here has no Razor component behind it — no deployed code declares
            // this key yet — and saying so is what the flag is for. The startup reconciler clears it
            // as soon as a component claims the key (spec section 8.4).
            IsOrphaned = true,
            CurrentRevision = 1,
        };

        // Revision 1 exists from the first moment, empty. A page created from this template captures
        // a revision number, and a template whose earliest content pointed at a revision that was
        // never cut would be unvalidatable and unrenderable (spec section 8.5).
        template.Revisions.Add(new TemplateRevision
        {
            RevisionNumber = 1,
            ZoneSnapshotJson = ContentSchemaSnapshot.WriteZones([]),
            Notes = "Template created.",
        });

        context.Templates.Add(template);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The pre-check above cannot close the window against a concurrent create; the unique
            // index does. Re-asking the question turns that into the same answer the caller would
            // have got a moment earlier, rather than a 500 that reads as a server fault. Asking the
            // database rather than matching on a provider error number keeps this from becoming a
            // SQL Server detail, and anything that was not a duplicate is still a real fault.
            if (await KeyExistsAsync(key, cancellationToken))
            {
                return DuplicateKey(key);
            }

            throw;
        }

        logger.LogInformation(
            "Template {TemplateKey} created with id {TemplateId} at revision 1.",
            template.Key,
            template.Id);

        return CmsResult<TemplateDetail>.Success(ToDetail(template));
    }

    /// <inheritdoc />
    public async Task<CmsResult<TemplateDetail>> UpdateAsync(
        int id,
        UpdateTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<TemplateDetail>.Forbidden("Managing templates is not permitted.");
        }

        var template = await context.Templates
            .Include(candidate => candidate.Zones)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (template is null)
        {
            return CmsResult<TemplateDetail>.NotFound($"No template has id {id}.");
        }

        var diagnostics = new List<ValidationDiagnostic>(ValidateMetadata(request.Name, request.Description));

        // An omitted key means "unchanged"; a supplied one must match, including in case. Content
        // addresses its template by this string, so a case-only edit orphans exactly as much as a
        // full rename does.
        if (!string.IsNullOrWhiteSpace(request.Key) &&
            !string.Equals(request.Key.Trim(), template.Key, StringComparison.Ordinal))
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.KeyImmutable,
                $"A template key cannot be changed. This template is '{template.Key}', and every " +
                "payload authored against it names that key. Create a new template instead.",
                ValidationSeverity.Error,
                nameof(UpdateTemplateRequest.Key)));
        }

        if (diagnostics.Count > 0)
        {
            return CmsResult<TemplateDetail>.Invalid(ValidationResult.From(diagnostics));
        }

        template.Name = request.Name!.Trim();
        template.Description = SlotRules.Clean(request.Description);
        template.IsEnabled = request.IsEnabled;
        template.SortOrder = request.SortOrder;

        await context.SaveChangesAsync(cancellationToken);

        return CmsResult<TemplateDetail>.Success(ToDetail(template));
    }

    /// <inheritdoc />
    public async Task<CmsResult<bool>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<bool>.Forbidden("Managing templates is not permitted.");
        }

        var template = await context.Templates
            .Include(candidate => candidate.Zones)
            .Include(candidate => candidate.Revisions)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (template is null)
        {
            return CmsResult<bool>.NotFound($"No template has id {id}.");
        }

        // IgnoreQueryFilters, so a page in the recycle bin counts. It keeps its TemplateId and can
        // be restored, and a restore that produces a page with no schema to render against is not
        // one — the foreign key would refuse the delete regardless (spec section 8.5).
        var users = await context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.TemplateId == id)
            .OrderBy(page => page.Id)
            .Select(page => new
            {
                Title = page.DraftVersion == null ? page.Slug : page.DraftVersion.Title,
                page.IsDeleted,
            })
            .ToListAsync(cancellationToken);

        if (users.Count > 0)
        {
            return InUse(template.Key, users.Count(page => !page.IsDeleted), [.. users.Select(page => page.Title)]);
        }

        // Removed explicitly because every structural foreign key is Restrict: cascading would take
        // zone definitions and captured revisions with a template deleted by accident, and those are
        // the only record of what stored content was validated against.
        context.Zones.RemoveRange(template.Zones);
        context.TemplateRevisions.RemoveRange(template.Revisions);
        context.Templates.Remove(template);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Template {TemplateKey} deleted with {ZoneCount} zones and {RevisionCount} revisions.",
            template.Key,
            template.Zones.Count,
            template.Revisions.Count);

        return CmsResult<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<TemplateRevisionSummary>>> ListRevisionsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<TemplateRevisionSummary>>.Forbidden(
                "Reading templates is not permitted.");
        }

        var template = await context.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (template is null)
        {
            return CmsResult<IReadOnlyList<TemplateRevisionSummary>>.NotFound($"No template has id {id}.");
        }

        var revisions = await context.TemplateRevisions
            .AsNoTracking()
            .Where(revision => revision.TemplateId == id)
            .OrderByDescending(revision => revision.RevisionNumber)
            .ToListAsync(cancellationToken);

        var summaries = revisions
            .Select(revision => ToSummary(revision, template))
            .ToList();

        return CmsResult<IReadOnlyList<TemplateRevisionSummary>>.Success(summaries);
    }

    /// <inheritdoc />
    public async Task<CmsResult<TemplateRevisionDetail>> GetRevisionAsync(
        int id,
        int revisionNumber,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<TemplateRevisionDetail>.Forbidden("Reading templates is not permitted.");
        }

        var template = await context.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (template is null)
        {
            return CmsResult<TemplateRevisionDetail>.NotFound($"No template has id {id}.");
        }

        var revision = await context.TemplateRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.TemplateId == id && candidate.RevisionNumber == revisionNumber,
                cancellationToken);

        if (revision is null)
        {
            return CmsResult<TemplateRevisionDetail>.NotFound(
                $"Template '{template.Key}' has no revision {revisionNumber}.");
        }

        return CmsResult<TemplateRevisionDetail>.Success(new TemplateRevisionDetail(
            ToSummary(revision, template),
            template.Key,
            StructureJson.Read(revision.ZoneSnapshotJson, logger, $"revision {revisionNumber} of template '{template.Key}'")));
    }

    /// <summary>Checks the key the way the unique index will, under the database's collation.</summary>
    private Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken) =>
        context.Templates.AnyAsync(template => template.Key == key, cancellationToken);

    /// <summary>
    /// Refuses a delete and says which pages stopped it.
    /// </summary>
    /// <param name="key">Key of the template.</param>
    /// <param name="live">How many of the pages are not in the recycle bin.</param>
    /// <param name="titles">Every page's title, of which a few are named.</param>
    /// <remarks>
    /// Named, not merely counted, and capped. "12 pages use this template" leaves the developer to
    /// go and find them; naming a handful is what makes the refusal actionable, and naming all
    /// twelve hundred would make it a wall of text nobody reads.
    /// </remarks>
    private static CmsResult<bool> InUse(string key, int live, IReadOnlyList<string> titles)
    {
        const int Named = 5;

        var named = string.Join(", ", titles.Take(Named).Select(title => $"'{title}'"));
        var rest = titles.Count > Named ? $", and {titles.Count - Named} more" : string.Empty;
        var recycled = titles.Count - live;

        var where = recycled == 0
            ? $"{titles.Count} page(s) use template '{key}'"
            : $"{titles.Count} page(s) use template '{key}', {recycled} of them in the recycle bin";

        return CmsResult<bool>.Conflict(
            StructureCodes.InUse,
            $"{where}: {named}{rest}. Move those pages to another template, and empty the recycle " +
            "bin of any that are in it, before deleting this one — content whose template row is " +
            "gone has no schema left to validate or render against.");
    }

    private static CmsResult<TemplateDetail> DuplicateKey(string key) =>
        CmsResult<TemplateDetail>.Conflict(
            StructureCodes.KeyDuplicate,
            $"A template already uses the key '{key}'.",
            nameof(CreateTemplateRequest.Key));

    /// <summary>Checks the values a template shares between create and update.</summary>
    private static IReadOnlyList<ValidationDiagnostic> ValidateMetadata(string? name, string? description)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        if (string.IsNullOrWhiteSpace(name))
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.NameRequired,
                "A display name is required.",
                ValidationSeverity.Error,
                nameof(CreateTemplateRequest.Name)));
        }
        else if (name.Trim().Length > FieldLengths.EntityName)
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.TooLong,
                $"A display name may be at most {FieldLengths.EntityName} characters.",
                ValidationSeverity.Error,
                nameof(CreateTemplateRequest.Name)));
        }

        if (description?.Trim().Length > FieldLengths.ShortDescription)
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.TooLong,
                $"A description may be at most {FieldLengths.ShortDescription} characters.",
                ValidationSeverity.Error,
                nameof(CreateTemplateRequest.Description)));
        }

        return diagnostics;
    }

    private static TemplateRevisionSummary ToSummary(TemplateRevision revision, Template template) =>
        new(
            revision.RevisionNumber,
            revision.RevisionNumber == template.CurrentRevision,
            StructureJson.CountSlots(revision.ZoneSnapshotJson),
            revision.CreatedOn,
            revision.CreatedBy,
            revision.Notes);

    private TemplateDetail ToDetail(Template template) =>
        new(
            new TemplateSummary(
                template.Id,
                template.Key,
                template.Name,
                template.Description,
                template.ComponentTypeName,
                template.IsOrphaned,
                template.IsEnabled,
                template.CurrentRevision,
                template.SortOrder,
                template.Zones.Count),
            template.Zones
                .OrderBy(zone => zone.SortOrder)
                .ThenBy(zone => zone.Key, StringComparer.Ordinal)
                .Select(zone => new ZoneDefinition(
                    zone.Id,
                    zone.Key,
                    zone.Name,
                    zone.Description,
                    zone.FieldTypeKey,
                    string.IsNullOrWhiteSpace(zone.ConfigurationJson)
                        ? null
                        : StructureJson.Read(zone.ConfigurationJson, logger, $"the configuration of zone '{zone.Key}'"),
                    zone.IsRequired,
                    zone.IsInlineEditable,
                    zone.Group,
                    zone.SortOrder))
                .ToList(),
            template.CreatedOn,
            template.ModifiedOn);
}
