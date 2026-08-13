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
    /// <summary>Stands in for a snapshot or configuration that cannot be read.</summary>
    private static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement.Clone();

    /// <inheritdoc />
    public async Task<StructureResult<IReadOnlyList<TemplateSummary>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return StructureResult<IReadOnlyList<TemplateSummary>>.Forbidden("Reading templates is not permitted.");
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

        return StructureResult<IReadOnlyList<TemplateSummary>>.Success(templates);
    }

    /// <inheritdoc />
    public async Task<StructureResult<TemplateDetail>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return StructureResult<TemplateDetail>.Forbidden("Reading templates is not permitted.");
        }

        var template = await context.Templates
            .AsNoTracking()
            .Include(candidate => candidate.Zones)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return template is null
            ? StructureResult<TemplateDetail>.NotFound($"No template has id {id}.")
            : StructureResult<TemplateDetail>.Success(ToDetail(template));
    }

    /// <inheritdoc />
    public async Task<StructureResult<TemplateDetail>> CreateAsync(
        CreateTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return StructureResult<TemplateDetail>.Forbidden("Managing templates is not permitted.");
        }

        var diagnostics = new List<ValidationDiagnostic>();
        diagnostics.AddRange(ContentKeys.Validate(request.Key).Diagnostics);
        diagnostics.AddRange(ValidateMetadata(request.Name, request.Description));

        if (diagnostics.Count > 0)
        {
            return StructureResult<TemplateDetail>.Invalid(ValidationResult.From(diagnostics));
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
            Description = Clean(request.Description),
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

        return StructureResult<TemplateDetail>.Success(ToDetail(template));
    }

    /// <inheritdoc />
    public async Task<StructureResult<TemplateDetail>> UpdateAsync(
        int id,
        UpdateTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return StructureResult<TemplateDetail>.Forbidden("Managing templates is not permitted.");
        }

        var template = await context.Templates
            .Include(candidate => candidate.Zones)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (template is null)
        {
            return StructureResult<TemplateDetail>.NotFound($"No template has id {id}.");
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
            return StructureResult<TemplateDetail>.Invalid(ValidationResult.From(diagnostics));
        }

        template.Name = request.Name!.Trim();
        template.Description = Clean(request.Description);
        template.IsEnabled = request.IsEnabled;
        template.SortOrder = request.SortOrder;

        await context.SaveChangesAsync(cancellationToken);

        return StructureResult<TemplateDetail>.Success(ToDetail(template));
    }

    /// <inheritdoc />
    public async Task<StructureResult<IReadOnlyList<TemplateRevisionSummary>>> ListRevisionsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return StructureResult<IReadOnlyList<TemplateRevisionSummary>>.Forbidden(
                "Reading templates is not permitted.");
        }

        var template = await context.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (template is null)
        {
            return StructureResult<IReadOnlyList<TemplateRevisionSummary>>.NotFound($"No template has id {id}.");
        }

        var revisions = await context.TemplateRevisions
            .AsNoTracking()
            .Where(revision => revision.TemplateId == id)
            .OrderByDescending(revision => revision.RevisionNumber)
            .ToListAsync(cancellationToken);

        var summaries = revisions
            .Select(revision => ToSummary(revision, template))
            .ToList();

        return StructureResult<IReadOnlyList<TemplateRevisionSummary>>.Success(summaries);
    }

    /// <inheritdoc />
    public async Task<StructureResult<TemplateRevisionDetail>> GetRevisionAsync(
        int id,
        int revisionNumber,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return StructureResult<TemplateRevisionDetail>.Forbidden("Reading templates is not permitted.");
        }

        var template = await context.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (template is null)
        {
            return StructureResult<TemplateRevisionDetail>.NotFound($"No template has id {id}.");
        }

        var revision = await context.TemplateRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.TemplateId == id && candidate.RevisionNumber == revisionNumber,
                cancellationToken);

        if (revision is null)
        {
            return StructureResult<TemplateRevisionDetail>.NotFound(
                $"Template '{template.Key}' has no revision {revisionNumber}.");
        }

        return StructureResult<TemplateRevisionDetail>.Success(new TemplateRevisionDetail(
            ToSummary(revision, template),
            template.Key,
            ReadJson(revision.ZoneSnapshotJson, $"revision {revisionNumber} of template '{template.Key}'")));
    }

    /// <summary>Checks the key the way the unique index will, under the database's collation.</summary>
    private Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken) =>
        context.Templates.AnyAsync(template => template.Key == key, cancellationToken);

    private static StructureResult<TemplateDetail> DuplicateKey(string key) =>
        StructureResult<TemplateDetail>.Conflict(
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

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TemplateRevisionSummary ToSummary(TemplateRevision revision, Template template) =>
        new(
            revision.RevisionNumber,
            revision.RevisionNumber == template.CurrentRevision,
            CountSlots(revision.ZoneSnapshotJson),
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
                        : ReadJson(zone.ConfigurationJson, $"the configuration of zone '{zone.Key}'"),
                    zone.IsRequired,
                    zone.IsInlineEditable,
                    zone.Group,
                    zone.SortOrder))
                .ToList(),
            template.CreatedOn,
            template.ModifiedOn);

    /// <summary>
    /// Reads stored JSON for pass-through to the client.
    /// </summary>
    /// <remarks>
    /// Unreadable JSON is logged and reported as an empty array rather than thrown. These columns
    /// are written only by this application and validated before they are stored, so a failure here
    /// means corruption — and a corrupt configuration on one zone should not take out the structure
    /// screen an operator would use to find it.
    /// </remarks>
    private JsonElement ReadJson(string? json, string description)
    {
        if (string.IsNullOrWhiteSpace(json)) return EmptyArray;

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Stored JSON for {Subject} could not be read.", description);

            return EmptyArray;
        }
    }

    /// <summary>Counts the slots in a snapshot without validating them.</summary>
    /// <remarks>
    /// A count is wanted for a history list, where one corrupt row must not fail the request; the
    /// revision detail endpoint hands back the snapshot itself for anyone who needs to see it.
    /// </remarks>
    private static int CountSlots(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson)) return 0;

        try
        {
            using var document = JsonDocument.Parse(snapshotJson);

            return document.RootElement.ValueKind is JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
