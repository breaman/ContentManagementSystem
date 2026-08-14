using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Fields.Configuration;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Structure;

/// <inheritdoc cref="IZoneService" />
/// <param name="context">The application database context.</param>
/// <param name="configurations">Checks a configuration against its field type's schema (P1-12).</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="logger">Log for structural changes and unreadable stored JSON.</param>
public sealed class ZoneService(
    ApplicationDbContext context,
    IFieldConfigurationValidator configurations,
    ICmsAuthorization authorization,
    ILogger<ZoneService> logger) : IZoneService
{
    /// <inheritdoc />
    public async Task<StructureResult<IReadOnlyList<ZoneDefinition>>> ListAsync(
        int templateId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return StructureResult<IReadOnlyList<ZoneDefinition>>.Forbidden("Reading templates is not permitted.");
        }

        if (!await context.Templates.AnyAsync(template => template.Id == templateId, cancellationToken))
        {
            return StructureResult<IReadOnlyList<ZoneDefinition>>.NotFound($"No template has id {templateId}.");
        }

        var zones = await context.Zones
            .AsNoTracking()
            .Where(zone => zone.TemplateId == templateId)
            .OrderBy(zone => zone.SortOrder)
            .ThenBy(zone => zone.Key)
            .ToListAsync(cancellationToken);

        return StructureResult<IReadOnlyList<ZoneDefinition>>.Success(
            zones.Select(ToDefinition).ToList());
    }

    /// <inheritdoc />
    public async Task<StructureResult<ZoneDefinition>> GetAsync(
        int templateId,
        int zoneId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return StructureResult<ZoneDefinition>.Forbidden("Reading templates is not permitted.");
        }

        var zone = await context.Zones
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == zoneId && candidate.TemplateId == templateId,
                cancellationToken);

        // A zone id belonging to another template is reported as not found rather than as a
        // mismatch: the pair is the address, and answering "wrong template" would confirm the
        // existence of a row the caller did not ask about.
        return zone is null
            ? StructureResult<ZoneDefinition>.NotFound($"Template {templateId} has no zone with id {zoneId}.")
            : StructureResult<ZoneDefinition>.Success(ToDefinition(zone));
    }

    /// <inheritdoc />
    public async Task<StructureResult<ZoneSaveResult>> CreateAsync(
        int templateId,
        CreateZoneRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return StructureResult<ZoneSaveResult>.Forbidden("Managing templates is not permitted.");
        }

        var template = await LoadForWriteAsync(templateId, cancellationToken);

        if (template is null)
        {
            return StructureResult<ZoneSaveResult>.NotFound($"No template has id {templateId}.");
        }

        var configurationJson = StructureJson.Normalize(request.Configuration);

        var diagnostics = new List<ValidationDiagnostic>();
        diagnostics.AddRange(ContentKeys.Validate(request.Key, SlotRules.KeyPath).Diagnostics);
        diagnostics.AddRange(SlotRules.ValidateMetadata(request.Name, request.Description, request.Group));
        diagnostics.AddRange(SlotRules.ValidateFieldType(configurations, request.FieldTypeKey, configurationJson));

        var checks = ValidationResult.From(diagnostics);

        // Errors block; warnings do not. A configuration setting whose phase has not shipped is
        // stored and reported back with the saved zone (P1-12), so the result has to be judged on
        // severity rather than on whether anything was said at all.
        if (checks.HasErrors) return StructureResult<ZoneSaveResult>.Invalid(checks);

        var key = request.Key!.Trim();

        if (await KeyExistsAsync(templateId, key, cancellationToken))
        {
            return DuplicateKey<ZoneSaveResult>(key, template.Key);
        }

        var zone = new Zone
        {
            Key = key,
            Name = request.Name!.Trim(),
            Description = SlotRules.Clean(request.Description),
            FieldTypeKey = request.FieldTypeKey!.Trim(),
            ConfigurationJson = configurationJson,
            IsRequired = request.IsRequired,
            IsInlineEditable = request.IsInlineEditable,
            Group = SlotRules.Clean(request.Group),
            SortOrder = request.SortOrder,
        };

        // Attached through the template's collection rather than through the set, so that the zone
        // appears in it exactly once. Adding it to the set instead sets the foreign key, and EF's
        // relationship fixup then appends it to the loaded collection as well — a snapshot built by
        // hand from "the collection plus this zone" captures it twice.
        template.Zones.Add(zone);

        var revision = CutRevision(template, template.Zones, $"Zone '{key}' added.");

        if (await SaveAsync(template, revision, cancellationToken) is { } failure)
        {
            return await ExplainAsync<ZoneSaveResult>(failure, template, key, revision, cancellationToken);
        }

        logger.LogInformation(
            "Zone {ZoneKey} added to template {TemplateKey}, now at revision {Revision}.",
            zone.Key,
            template.Key,
            template.CurrentRevision);

        return Saved(zone, template, checks);
    }

    /// <inheritdoc />
    public async Task<StructureResult<ZoneSaveResult>> UpdateAsync(
        int templateId,
        int zoneId,
        UpdateZoneRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return StructureResult<ZoneSaveResult>.Forbidden("Managing templates is not permitted.");
        }

        var template = await LoadForWriteAsync(templateId, cancellationToken);

        if (template is null)
        {
            return StructureResult<ZoneSaveResult>.NotFound($"No template has id {templateId}.");
        }

        if (template.Zones.FirstOrDefault(candidate => candidate.Id == zoneId) is not { } zone)
        {
            return StructureResult<ZoneSaveResult>.NotFound($"Template {templateId} has no zone with id {zoneId}.");
        }

        var configurationJson = StructureJson.Normalize(request.Configuration);

        var diagnostics = SlotRules.ValidateMetadata(request.Name, request.Description, request.Group);

        diagnostics.AddRange(SlotRules.ValidateImmutable(
            request.Key,
            request.FieldTypeKey,
            zone.Key,
            zone.FieldTypeKey,
            "zone",
            $"template '{template.Key}'"));

        // Checked against the field type the zone already has, not the one the request names. The
        // request cannot change it, so validating the incoming key would report a configuration
        // problem against a binding that will never exist.
        diagnostics.AddRange(SlotRules.ValidateFieldType(configurations, zone.FieldTypeKey, configurationJson));

        var checks = ValidationResult.From(diagnostics);

        if (checks.HasErrors) return StructureResult<ZoneSaveResult>.Invalid(checks);

        // Structural means "changes how a stored value is read or judged". Everything else on a
        // zone is a label, and cutting a revision for a corrected typo would bury the changes that
        // revisions exist to record under the ones nobody needs to see (spec section 8.5).
        var isStructural = zone.IsRequired != request.IsRequired ||
            !string.Equals(zone.ConfigurationJson, configurationJson, StringComparison.Ordinal);

        zone.Name = request.Name!.Trim();
        zone.Description = SlotRules.Clean(request.Description);
        zone.ConfigurationJson = configurationJson;
        zone.IsRequired = request.IsRequired;
        zone.IsInlineEditable = request.IsInlineEditable;
        zone.Group = SlotRules.Clean(request.Group);
        zone.SortOrder = request.SortOrder;

        var revision = isStructural
            ? CutRevision(template, template.Zones, $"Zone '{zone.Key}' changed.")
            : null;

        if (await SaveAsync(template, revision, cancellationToken) is { } failure)
        {
            return await ExplainAsync<ZoneSaveResult>(failure, template, zone.Key, revision, cancellationToken);
        }

        return Saved(zone, template, checks);
    }

    /// <inheritdoc />
    public async Task<StructureResult<ZoneRemovalResult>> DeleteAsync(
        int templateId,
        int zoneId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return StructureResult<ZoneRemovalResult>.Forbidden("Managing templates is not permitted.");
        }

        var template = await LoadForWriteAsync(templateId, cancellationToken);

        if (template is null)
        {
            return StructureResult<ZoneRemovalResult>.NotFound($"No template has id {templateId}.");
        }

        if (template.Zones.FirstOrDefault(candidate => candidate.Id == zoneId) is not { } zone)
        {
            return StructureResult<ZoneRemovalResult>.NotFound(
                $"Template {templateId} has no zone with id {zoneId}.");
        }

        // Removed through the set rather than by detaching it from the collection: the relationship
        // is required and configured DeleteBehavior.Restrict, so severing it would ask the database
        // to null a non-nullable foreign key.
        context.Zones.Remove(zone);

        var remaining = template.Zones.Where(candidate => candidate.Id != zoneId).ToList();
        var revision = CutRevision(template, remaining, $"Zone '{zone.Key}' removed.");

        if (await SaveAsync(template, revision, cancellationToken) is { } failure)
        {
            return await ExplainAsync<ZoneRemovalResult>(
                failure, template, zone.Key, revision, cancellationToken);
        }

        logger.LogInformation(
            "Zone {ZoneKey} removed from template {TemplateKey}, now at revision {Revision}. Payload " +
            "values stored under that key are retained as orphaned content.",
            zone.Key,
            template.Key,
            template.CurrentRevision);

        return StructureResult<ZoneRemovalResult>.Success(
            new ZoneRemovalResult(zone.Key, template.CurrentRevision));
    }

    /// <summary>Loads a template with the zones a revision snapshot has to capture.</summary>
    private Task<Template?> LoadForWriteAsync(int templateId, CancellationToken cancellationToken) =>
        context.Templates
            .Include(candidate => candidate.Zones)
            .FirstOrDefaultAsync(candidate => candidate.Id == templateId, cancellationToken);

    /// <summary>Checks the key the way the unique index will, under the database's collation.</summary>
    /// <remarks>
    /// Asked of the database rather than of the loaded collection. The index on
    /// <c>(TemplateId, Key)</c> compares under the server's collation, which is case-insensitive by
    /// default; an ordinal check in memory would admit a case-only variant and then fail on insert.
    /// </remarks>
    private Task<bool> KeyExistsAsync(int templateId, string key, CancellationToken cancellationToken) =>
        context.Zones.AnyAsync(
            zone => zone.TemplateId == templateId && zone.Key == key,
            cancellationToken);

    /// <summary>
    /// Appends a revision capturing the zone set as it will stand once the change is saved.
    /// </summary>
    /// <param name="template">The template being changed.</param>
    /// <param name="zones">The zone set to capture, with the change already applied to it.</param>
    /// <param name="notes">What changed, for the history list.</param>
    /// <returns>
    /// The revision, so a failed save can tell a collision on the revision number from one on the
    /// zone key.
    /// </returns>
    private static TemplateRevision CutRevision(Template template, IEnumerable<Zone> zones, string notes)
    {
        var revision = new TemplateRevision
        {
            TemplateId = template.Id,
            RevisionNumber = template.CurrentRevision + 1,
            ZoneSnapshotJson = ContentSchemaSnapshot.WriteZones(zones),
            Notes = notes,
        };

        template.Revisions.Add(revision);
        template.CurrentRevision = revision.RevisionNumber;

        return revision;
    }

    /// <summary>
    /// Saves the zone change and its revision together.
    /// </summary>
    /// <param name="template">The template being changed, named in the log.</param>
    /// <param name="revision">The revision being cut, if this change cuts one.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>Null on success, or the failure for the caller to explain.</returns>
    /// <remarks>
    /// One <c>SaveChangesAsync</c> for both, which EF runs in a single transaction. A zone whose
    /// revision was not cut would leave content authored a moment later capturing a revision number
    /// whose snapshot does not describe the structure it was written against.
    /// </remarks>
    private async Task<DbUpdateException?> SaveAsync(
        Template template,
        TemplateRevision? revision,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return null;
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(
                exception,
                "A zone change to template {TemplateKey} could not be saved{Revision}.",
                template.Key,
                revision is null ? string.Empty : $" while cutting revision {revision.RevisionNumber}");

            return exception;
        }
    }

    /// <summary>
    /// Turns a failed save into the answer the caller would have got a moment earlier.
    /// </summary>
    /// <typeparam name="T">Type the operation produces on success.</typeparam>
    /// <param name="failure">The failure to explain.</param>
    /// <param name="template">The template being changed.</param>
    /// <param name="key">Key of the zone being written.</param>
    /// <param name="revision">The revision being cut, if this change cuts one.</param>
    /// <param name="cancellationToken">Token observed while re-querying.</param>
    /// <returns>The conflict the failure represents.</returns>
    /// <exception cref="DbUpdateException">The failure was not one of the two collisions below.</exception>
    /// <remarks>
    /// Two unique indexes can be hit here and they mean different things.
    /// <c>(TemplateId, RevisionNumber)</c> means someone else changed the same template's structure
    /// between this request reading it and writing it — a lost update, which has to be retried
    /// rather than merged, because the revision this request cut would not describe their change.
    /// <c>(TemplateId, Key)</c> means someone added the same zone key first; the pre-check cannot
    /// close that window, the index can. Anything else is rethrown: a save that failed for an
    /// unrelated reason must not be reported as a conflict a client will retry forever.
    /// </remarks>
    private async Task<StructureResult<T>> ExplainAsync<T>(
        DbUpdateException failure,
        Template template,
        string key,
        TemplateRevision? revision,
        CancellationToken cancellationToken)
    {
        if (revision is not null &&
            await context.TemplateRevisions
                .AsNoTracking()
                .AnyAsync(
                    candidate => candidate.TemplateId == template.Id &&
                        candidate.RevisionNumber == revision.RevisionNumber,
                    cancellationToken))
        {
            return StructureResult<T>.Conflict(
                StructureCodes.ConcurrentChange,
                $"The structure of template '{template.Key}' was changed by someone else while this " +
                "change was being saved. Reload the template and apply the change again.");
        }

        if (await KeyExistsAsync(template.Id, key, cancellationToken))
        {
            return DuplicateKey<T>(key, template.Key);
        }

        throw failure;
    }

    private static StructureResult<T> DuplicateKey<T>(string key, string templateKey) =>
        StructureResult<T>.Conflict(
            StructureCodes.KeyDuplicate,
            $"Template '{templateKey}' already has a zone with the key '{key}'.",
            SlotRules.KeyPath);

    private StructureResult<ZoneSaveResult> Saved(Zone zone, Template template, ValidationResult checks) =>
        StructureResult<ZoneSaveResult>.Success(
            new ZoneSaveResult(
                ToDefinition(zone),
                template.CurrentRevision,
                ApiDiagnostics.Project(checks, ValidationSeverity.Warning)),
            checks);

    private ZoneDefinition ToDefinition(Zone zone) =>
        new(
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
            zone.SortOrder);
}
