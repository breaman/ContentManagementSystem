using System.Text.Json;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Fields.Configuration;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Structure;

/// <inheritdoc cref="IBlockTypeService" />
/// <param name="context">The application database context.</param>
/// <param name="configurations">Checks a configuration against its field type's schema (P1-12).</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="logger">Log for structural changes and unreadable stored JSON.</param>
public sealed class BlockTypeService(
    ApplicationDbContext context,
    IFieldConfigurationValidator configurations,
    ICmsAuthorization authorization,
    ILogger<BlockTypeService> logger) : IBlockTypeService
{
    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<BlockTypeSummary>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<BlockTypeSummary>>.Forbidden(
                "Reading block types is not permitted.");
        }

        var blockTypes = await Query()
            .AsNoTracking()
            .OrderBy(blockType => blockType.Name)
            .ToListAsync(cancellationToken);

        return CmsResult<IReadOnlyList<BlockTypeSummary>>.Success(
            blockTypes.Select(ToSummary).ToList());
    }

    /// <inheritdoc />
    public async Task<CmsResult<BlockTypeDetail>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<BlockTypeDetail>.Forbidden("Reading block types is not permitted.");
        }

        var blockType = await Query()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return blockType is null
            ? CmsResult<BlockTypeDetail>.NotFound($"No block type has id {id}.")
            : CmsResult<BlockTypeDetail>.Success(ToDetail(blockType));
    }

    /// <inheritdoc />
    public async Task<CmsResult<BlockTypeDetail>> CreateAsync(
        CreateBlockTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<BlockTypeDetail>.Forbidden("Managing block types is not permitted.");
        }

        var diagnostics = new List<ValidationDiagnostic>();
        diagnostics.AddRange(ContentKeys.Validate(request.Key, SlotRules.KeyPath).Diagnostics);
        diagnostics.AddRange(SlotRules.ValidateMetadata(request.Name, request.Description, group: null));
        diagnostics.AddRange(ValidatePresentation(request.IconKey, request.SummaryTemplate));

        if (diagnostics.Count > 0)
        {
            return CmsResult<BlockTypeDetail>.Invalid(ValidationResult.From(diagnostics));
        }

        var key = request.Key!.Trim();

        if (await context.BlockTypes.AnyAsync(candidate => candidate.Key == key, cancellationToken))
        {
            return DuplicateBlockTypeKey<BlockTypeDetail>(key);
        }

        var blockType = new BlockType
        {
            Key = key,
            Name = request.Name!.Trim(),
            Description = SlotRules.Clean(request.Description),
            IconKey = SlotRules.Clean(request.IconKey),
            SummaryTemplate = SlotRules.Clean(request.SummaryTemplate),
            // No deployed component claims this key yet, which is exactly what the flag means. The
            // startup reconciler clears it when one does (spec section 8.4).
            IsOrphaned = true,
            IsBuiltIn = false,
            CurrentRevision = 1,
        };

        // Revision 1 from the first moment, empty, for the reason a template gets one: a block
        // instance captures a revision number, and one that was never cut is unrenderable.
        blockType.Revisions.Add(new BlockTypeRevision
        {
            RevisionNumber = 1,
            PropertySnapshotJson = ContentSchemaSnapshot.WriteSlots([]),
            Notes = "Block type created.",
        });

        context.BlockTypes.Add(blockType);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The pre-check cannot close the window against a concurrent create; the unique index
            // does. Re-asking turns that into the answer the caller would have had a moment earlier.
            if (await context.BlockTypes.AnyAsync(candidate => candidate.Key == key, cancellationToken))
            {
                return DuplicateBlockTypeKey<BlockTypeDetail>(key);
            }

            throw;
        }

        logger.LogInformation(
            "Block type {BlockTypeKey} created with id {BlockTypeId} at revision 1.",
            blockType.Key,
            blockType.Id);

        return CmsResult<BlockTypeDetail>.Success(ToDetail(blockType));
    }

    /// <inheritdoc />
    public async Task<CmsResult<BlockTypeDetail>> UpdateAsync(
        int id,
        UpdateBlockTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<BlockTypeDetail>.Forbidden("Managing block types is not permitted.");
        }

        var blockType = await LoadAsync(id, cancellationToken);

        if (blockType is null)
        {
            return CmsResult<BlockTypeDetail>.NotFound($"No block type has id {id}.");
        }

        var diagnostics = SlotRules.ValidateMetadata(request.Name, request.Description, group: null);
        diagnostics.AddRange(ValidatePresentation(request.IconKey, request.SummaryTemplate));

        if (!string.IsNullOrWhiteSpace(request.Key) &&
            !string.Equals(request.Key.Trim(), blockType.Key, StringComparison.Ordinal))
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.KeyImmutable,
                $"A block type key cannot be changed. This one is '{blockType.Key}', and every block " +
                "instance authored against it names that key. Create a new block type instead.",
                ValidationSeverity.Error,
                SlotRules.KeyPath));
        }

        if (diagnostics.Count > 0)
        {
            return CmsResult<BlockTypeDetail>.Invalid(ValidationResult.From(diagnostics));
        }

        // Metadata only, so a built-in is fair game here: renaming "Raw HTML" or changing its icon
        // changes nothing about the shape its renderer expects.
        blockType.Name = request.Name!.Trim();
        blockType.Description = SlotRules.Clean(request.Description);
        blockType.IconKey = SlotRules.Clean(request.IconKey);
        blockType.SummaryTemplate = SlotRules.Clean(request.SummaryTemplate);

        await context.SaveChangesAsync(cancellationToken);

        return CmsResult<BlockTypeDetail>.Success(ToDetail(blockType));
    }

    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<BlockTypeRevisionSummary>>> ListRevisionsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<BlockTypeRevisionSummary>>.Forbidden(
                "Reading block types is not permitted.");
        }

        var blockType = await context.BlockTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (blockType is null)
        {
            return CmsResult<IReadOnlyList<BlockTypeRevisionSummary>>.NotFound(
                $"No block type has id {id}.");
        }

        var revisions = await context.BlockTypeRevisions
            .AsNoTracking()
            .Where(revision => revision.BlockTypeId == id)
            .OrderByDescending(revision => revision.RevisionNumber)
            .ToListAsync(cancellationToken);

        return CmsResult<IReadOnlyList<BlockTypeRevisionSummary>>.Success(
            revisions.Select(revision => ToSummary(revision, blockType)).ToList());
    }

    /// <inheritdoc />
    public async Task<CmsResult<BlockTypeRevisionDetail>> GetRevisionAsync(
        int id,
        int revisionNumber,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<BlockTypeRevisionDetail>.Forbidden("Reading block types is not permitted.");
        }

        var blockType = await context.BlockTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (blockType is null)
        {
            return CmsResult<BlockTypeRevisionDetail>.NotFound($"No block type has id {id}.");
        }

        var revision = await context.BlockTypeRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.BlockTypeId == id && candidate.RevisionNumber == revisionNumber,
                cancellationToken);

        if (revision is null)
        {
            return CmsResult<BlockTypeRevisionDetail>.NotFound(
                $"Block type '{blockType.Key}' has no revision {revisionNumber}.");
        }

        return CmsResult<BlockTypeRevisionDetail>.Success(new BlockTypeRevisionDetail(
            ToSummary(revision, blockType),
            blockType.Key,
            StructureJson.Read(
                revision.PropertySnapshotJson,
                logger,
                $"revision {revisionNumber} of block type '{blockType.Key}'")));
    }

    /// <inheritdoc />
    public async Task<CmsResult<PropertySaveResult>> CreatePropertyAsync(
        int blockTypeId,
        CreatePropertyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<PropertySaveResult>.Forbidden("Managing block types is not permitted.");
        }

        var blockType = await LoadAsync(blockTypeId, cancellationToken);

        if (blockType is null)
        {
            return CmsResult<PropertySaveResult>.NotFound($"No block type has id {blockTypeId}.");
        }

        if (BuiltIn<PropertySaveResult>(blockType) is { } refusal) return refusal;

        var configurationJson = StructureJson.Normalize(request.Configuration);

        var diagnostics = new List<ValidationDiagnostic>();
        diagnostics.AddRange(ContentKeys.Validate(request.Key, SlotRules.KeyPath).Diagnostics);
        diagnostics.AddRange(SlotRules.ValidateMetadata(request.Name, request.Description, request.Group));
        diagnostics.AddRange(SlotRules.ValidateFieldType(configurations, request.FieldTypeKey, configurationJson));

        var checks = ValidationResult.From(diagnostics);

        if (checks.HasErrors) return CmsResult<PropertySaveResult>.Invalid(checks);

        var key = request.Key!.Trim();

        if (ComposedKey(blockType, key) is { } composition)
        {
            return CmsResult<PropertySaveResult>.Invalid(
                StructureCodes.CompositionCollision,
                $"The composition '{composition}' already contributes a property called '{key}' to " +
                "this block type, and both would land in the same block instance. Rename this " +
                "property or detach the composition.",
                SlotRules.KeyPath);
        }

        if (await PropertyKeyExistsAsync(blockTypeId, key, cancellationToken))
        {
            return DuplicatePropertyKey<PropertySaveResult>(key, blockType.Key);
        }

        var property = new BlockTypeProperty
        {
            Key = key,
            Name = request.Name!.Trim(),
            Description = SlotRules.Clean(request.Description),
            FieldTypeKey = request.FieldTypeKey!.Trim(),
            ConfigurationJson = configurationJson,
            IsRequired = request.IsRequired,
            Group = SlotRules.Clean(request.Group),
            SortOrder = request.SortOrder,
        };

        // Attached through the collection rather than the set, so relationship fixup cannot make
        // the flattened snapshot count it twice.
        blockType.Properties.Add(property);

        var revision = BlockTypeSchemaWriter.Cut(blockType, $"Property '{key}' added.");

        if (await SaveAsync(blockType, revision, cancellationToken) is { } failure)
        {
            return await ExplainAsync<PropertySaveResult>(failure, blockType, key, revision, cancellationToken);
        }

        logger.LogInformation(
            "Property {PropertyKey} added to block type {BlockTypeKey}, now at revision {Revision}.",
            property.Key,
            blockType.Key,
            blockType.CurrentRevision);

        return Saved(property, blockType, checks);
    }

    /// <inheritdoc />
    public async Task<CmsResult<PropertySaveResult>> UpdatePropertyAsync(
        int blockTypeId,
        int propertyId,
        UpdatePropertyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<PropertySaveResult>.Forbidden("Managing block types is not permitted.");
        }

        var blockType = await LoadAsync(blockTypeId, cancellationToken);

        if (blockType is null)
        {
            return CmsResult<PropertySaveResult>.NotFound($"No block type has id {blockTypeId}.");
        }

        if (BuiltIn<PropertySaveResult>(blockType) is { } refusal) return refusal;

        if (blockType.Properties.FirstOrDefault(candidate => candidate.Id == propertyId) is not { } property)
        {
            return CmsResult<PropertySaveResult>.NotFound(
                $"Block type {blockTypeId} has no property with id {propertyId}.");
        }

        var configurationJson = StructureJson.Normalize(request.Configuration);

        var diagnostics = SlotRules.ValidateMetadata(request.Name, request.Description, request.Group);

        diagnostics.AddRange(SlotRules.ValidateImmutable(
            request.Key,
            request.FieldTypeKey,
            property.Key,
            property.FieldTypeKey,
            "property",
            $"block type '{blockType.Key}'"));

        diagnostics.AddRange(SlotRules.ValidateFieldType(configurations, property.FieldTypeKey, configurationJson));

        var checks = ValidationResult.From(diagnostics);

        if (checks.HasErrors) return CmsResult<PropertySaveResult>.Invalid(checks);

        var isStructural = property.IsRequired != request.IsRequired ||
            !string.Equals(property.ConfigurationJson, configurationJson, StringComparison.Ordinal);

        property.Name = request.Name!.Trim();
        property.Description = SlotRules.Clean(request.Description);
        property.ConfigurationJson = configurationJson;
        property.IsRequired = request.IsRequired;
        property.Group = SlotRules.Clean(request.Group);
        property.SortOrder = request.SortOrder;

        var revision = isStructural
            ? BlockTypeSchemaWriter.Cut(blockType, $"Property '{property.Key}' changed.")
            : null;

        if (await SaveAsync(blockType, revision, cancellationToken) is { } failure)
        {
            return await ExplainAsync<PropertySaveResult>(
                failure, blockType, property.Key, revision, cancellationToken);
        }

        return Saved(property, blockType, checks);
    }

    /// <inheritdoc />
    public async Task<CmsResult<PropertyRemovalResult>> DeletePropertyAsync(
        int blockTypeId,
        int propertyId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<PropertyRemovalResult>.Forbidden("Managing block types is not permitted.");
        }

        var blockType = await LoadAsync(blockTypeId, cancellationToken);

        if (blockType is null)
        {
            return CmsResult<PropertyRemovalResult>.NotFound($"No block type has id {blockTypeId}.");
        }

        if (BuiltIn<PropertyRemovalResult>(blockType) is { } refusal) return refusal;

        if (blockType.Properties.FirstOrDefault(candidate => candidate.Id == propertyId) is not { } property)
        {
            return CmsResult<PropertyRemovalResult>.NotFound(
                $"Block type {blockTypeId} has no property with id {propertyId}.");
        }

        // Through the set, because the relationship is required and Restrict: severing it would ask
        // the database to null a non-nullable foreign key.
        context.BlockTypeProperties.Remove(property);
        blockType.Properties.Remove(property);

        var revision = BlockTypeSchemaWriter.Cut(blockType, $"Property '{property.Key}' removed.");

        if (await SaveAsync(blockType, revision, cancellationToken) is { } failure)
        {
            return await ExplainAsync<PropertyRemovalResult>(
                failure, blockType, property.Key, revision, cancellationToken);
        }

        logger.LogInformation(
            "Property {PropertyKey} removed from block type {BlockTypeKey}, now at revision {Revision}. " +
            "Values stored under that key are retained as orphaned content.",
            property.Key,
            blockType.Key,
            blockType.CurrentRevision);

        return CmsResult<PropertyRemovalResult>.Success(
            new PropertyRemovalResult(property.Key, blockType.CurrentRevision));
    }

    /// <inheritdoc />
    public async Task<CmsResult<BlockTypeDetail>> AttachCompositionAsync(
        int blockTypeId,
        AttachCompositionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<BlockTypeDetail>.Forbidden("Managing block types is not permitted.");
        }

        var blockType = await LoadAsync(blockTypeId, cancellationToken);

        if (blockType is null)
        {
            return CmsResult<BlockTypeDetail>.NotFound($"No block type has id {blockTypeId}.");
        }

        if (BuiltIn<BlockTypeDetail>(blockType) is { } refusal) return refusal;

        var composition = await context.Compositions
            .Include(candidate => candidate.Properties)
            .FirstOrDefaultAsync(candidate => candidate.Id == request.CompositionId, cancellationToken);

        if (composition is null)
        {
            return CmsResult<BlockTypeDetail>.NotFound(
                $"No composition has id {request.CompositionId}.");
        }

        if (blockType.Compositions.Any(binding => binding.CompositionId == composition.Id))
        {
            return CmsResult<BlockTypeDetail>.Conflict(
                StructureCodes.CompositionDuplicate,
                $"Block type '{blockType.Key}' already composes '{composition.Key}'.",
                nameof(AttachCompositionRequest.CompositionId));
        }

        // Every key the group brings has to be free, against the host's own properties and against
        // every group already composed. Reported together rather than one at a time: a developer
        // resolving a collision wants the whole overlap, not the alphabetically first member of it.
        var taken = new HashSet<string>(
            blockType.Properties.Select(property => property.Key),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (_, property) in BlockTypeSchemaWriter.Composed(blockType))
        {
            taken.Add(property.Key);
        }

        var collisions = composition.Properties
            .Where(property => taken.Contains(property.Key))
            .Select(property => property.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (collisions.Count > 0)
        {
            return CmsResult<BlockTypeDetail>.Invalid(
                StructureCodes.CompositionCollision,
                $"Composing '{composition.Key}' into '{blockType.Key}' would define " +
                $"{string.Join(", ", collisions.Select(key => $"'{key}'"))} twice in one block " +
                "instance. Rename the block type's own property, or compose a different group.",
                nameof(AttachCompositionRequest.CompositionId));
        }

        blockType.Compositions.Add(new BlockTypeComposition
        {
            Composition = composition,
            SortOrder = request.SortOrder,
        });

        var revision = BlockTypeSchemaWriter.Cut(blockType, $"Composition '{composition.Key}' composed in.");

        if (await SaveAsync(blockType, revision, cancellationToken) is { } failure)
        {
            return await ExplainAsync<BlockTypeDetail>(
                failure, blockType, composition.Key, revision, cancellationToken);
        }

        return CmsResult<BlockTypeDetail>.Success(ToDetail(blockType));
    }

    /// <inheritdoc />
    public async Task<CmsResult<BlockTypeDetail>> DetachCompositionAsync(
        int blockTypeId,
        int compositionId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<BlockTypeDetail>.Forbidden("Managing block types is not permitted.");
        }

        var blockType = await LoadAsync(blockTypeId, cancellationToken);

        if (blockType is null)
        {
            return CmsResult<BlockTypeDetail>.NotFound($"No block type has id {blockTypeId}.");
        }

        if (BuiltIn<BlockTypeDetail>(blockType) is { } refusal) return refusal;

        if (blockType.Compositions.FirstOrDefault(candidate => candidate.CompositionId == compositionId)
            is not { } binding)
        {
            return CmsResult<BlockTypeDetail>.NotFound(
                $"Block type {blockTypeId} does not compose composition {compositionId}.");
        }

        var key = binding.Composition?.Key ?? compositionId.ToString();

        context.BlockTypeCompositions.Remove(binding);
        blockType.Compositions.Remove(binding);

        var revision = BlockTypeSchemaWriter.Cut(blockType, $"Composition '{key}' detached.");

        if (await SaveAsync(blockType, revision, cancellationToken) is { } failure)
        {
            return await ExplainAsync<BlockTypeDetail>(failure, blockType, key, revision, cancellationToken);
        }

        return CmsResult<BlockTypeDetail>.Success(ToDetail(blockType));
    }

    /// <summary>Everything a block type's flattened property set needs, in one query.</summary>
    private IQueryable<BlockType> Query() =>
        context.BlockTypes
            .Include(blockType => blockType.Properties)
            .Include(blockType => blockType.Compositions)
                .ThenInclude(binding => binding.Composition)
                    .ThenInclude(composition => composition.Properties);

    private Task<BlockType?> LoadAsync(int id, CancellationToken cancellationToken) =>
        Query().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

    /// <summary>Refuses a structural change to a block type the system itself depends on.</summary>
    /// <returns>The refusal, or null when the block type is an ordinary one.</returns>
    private static CmsResult<T>? BuiltIn<T>(BlockType blockType) =>
        blockType.IsBuiltIn
            ? CmsResult<T>.Invalid(
                StructureCodes.BuiltInImmutable,
                $"'{blockType.Key}' is a built-in block type and its property set is fixed. The code " +
                "that renders it expects exactly these properties, so a change here would break a " +
                "renderer no editor can repair. Create your own block type instead.")
            : null;

    /// <summary>Finds the composition contributing a key, if one does.</summary>
    private static string? ComposedKey(BlockType blockType, string key) =>
        BlockTypeSchemaWriter.Composed(blockType)
            .Where(composed => string.Equals(composed.Property.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(composed => composed.Composition.Key)
            .FirstOrDefault();

    /// <summary>Checks the key the way the unique index will, under the database's collation.</summary>
    private Task<bool> PropertyKeyExistsAsync(int blockTypeId, string key, CancellationToken cancellationToken) =>
        context.BlockTypeProperties.AnyAsync(
            property => property.BlockTypeId == blockTypeId && property.Key == key,
            cancellationToken);

    /// <summary>Saves the change and its revision together, in one transaction.</summary>
    /// <returns>Null on success, or the failure for the caller to explain.</returns>
    private async Task<DbUpdateException?> SaveAsync(
        BlockType blockType,
        BlockTypeRevision? revision,
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
                "A change to block type {BlockTypeKey} could not be saved{Revision}.",
                blockType.Key,
                revision is null ? string.Empty : $" while cutting revision {revision.RevisionNumber}");

            return exception;
        }
    }

    /// <summary>
    /// Turns a failed save into the answer the caller would have got a moment earlier.
    /// </summary>
    /// <remarks>
    /// The same two collisions <c>ZoneService</c> explains, against this table's indexes:
    /// <c>(BlockTypeId, RevisionNumber)</c> is a concurrent structural change and is retryable,
    /// <c>(BlockTypeId, Key)</c> is a duplicate property key. Anything else is rethrown.
    /// </remarks>
    private async Task<CmsResult<T>> ExplainAsync<T>(
        DbUpdateException failure,
        BlockType blockType,
        string key,
        BlockTypeRevision? revision,
        CancellationToken cancellationToken)
    {
        if (revision is not null &&
            await context.BlockTypeRevisions
                .AsNoTracking()
                .AnyAsync(
                    candidate => candidate.BlockTypeId == blockType.Id &&
                        candidate.RevisionNumber == revision.RevisionNumber,
                    cancellationToken))
        {
            return CmsResult<T>.Conflict(
                StructureCodes.ConcurrentChange,
                $"The structure of block type '{blockType.Key}' was changed by someone else while " +
                "this change was being saved. Reload it and apply the change again.");
        }

        if (await PropertyKeyExistsAsync(blockType.Id, key, cancellationToken))
        {
            return DuplicatePropertyKey<T>(key, blockType.Key);
        }

        throw failure;
    }

    private static CmsResult<T> DuplicateBlockTypeKey<T>(string key) =>
        CmsResult<T>.Conflict(
            StructureCodes.KeyDuplicate,
            $"A block type already uses the key '{key}'.",
            SlotRules.KeyPath);

    private static CmsResult<T> DuplicatePropertyKey<T>(string key, string blockTypeKey) =>
        CmsResult<T>.Conflict(
            StructureCodes.KeyDuplicate,
            $"Block type '{blockTypeKey}' already has a property with the key '{key}'.",
            SlotRules.KeyPath);

    private CmsResult<PropertySaveResult> Saved(
        BlockTypeProperty property,
        BlockType blockType,
        ValidationResult checks) =>
        CmsResult<PropertySaveResult>.Success(
            new PropertySaveResult(
                ToDefinition(property),
                blockType.CurrentRevision,
                ApiDiagnostics.Project(checks, ValidationSeverity.Warning)),
            checks);

    /// <summary>Checks the two presentation strings a block type carries beyond the shared ones.</summary>
    private static List<ValidationDiagnostic> ValidatePresentation(string? iconKey, string? summaryTemplate)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        if (iconKey?.Trim().Length > FieldLengths.IconKey)
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.TooLong,
                $"An icon key may be at most {FieldLengths.IconKey} characters.",
                ValidationSeverity.Error,
                nameof(CreateBlockTypeRequest.IconKey)));
        }

        if (summaryTemplate?.Trim().Length > FieldLengths.SummaryTemplate)
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.TooLong,
                $"A summary template may be at most {FieldLengths.SummaryTemplate} characters.",
                ValidationSeverity.Error,
                nameof(CreateBlockTypeRequest.SummaryTemplate)));
        }

        return diagnostics;
    }

    private static BlockTypeRevisionSummary ToSummary(BlockTypeRevision revision, BlockType blockType) =>
        new(
            revision.RevisionNumber,
            revision.RevisionNumber == blockType.CurrentRevision,
            StructureJson.CountSlots(revision.PropertySnapshotJson),
            revision.CreatedOn,
            revision.CreatedBy,
            revision.Notes);

    private static BlockTypeSummary ToSummary(BlockType blockType) =>
        new(
            blockType.Id,
            blockType.Key,
            blockType.Name,
            blockType.Description,
            blockType.ComponentTypeName,
            blockType.IconKey,
            blockType.SummaryTemplate,
            blockType.IsOrphaned,
            blockType.IsBuiltIn,
            blockType.CurrentRevision,
            blockType.Properties.Count + BlockTypeSchemaWriter.Composed(blockType).Count());

    private BlockTypeDetail ToDetail(BlockType blockType)
    {
        var own = blockType.Properties
            .OrderBy(property => property.SortOrder)
            .ThenBy(property => property.Key, StringComparer.Ordinal)
            .Select(ToDefinition)
            .ToList();

        var composed = BlockTypeSchemaWriter.Composed(blockType)
            .Select(entry => ToDefinition(entry.Property, entry.Composition.Key))
            .ToList();

        return new BlockTypeDetail(
            ToSummary(blockType),
            own,
            blockType.Compositions
                .OrderBy(binding => binding.SortOrder)
                .ThenBy(binding => binding.CompositionId)
                .Where(binding => binding.Composition is not null)
                .Select(binding => new CompositionBinding(
                    binding.CompositionId,
                    binding.Composition.Key,
                    binding.Composition.Name,
                    binding.SortOrder,
                    binding.Composition.Properties.Count))
                .ToList(),
            // Effective order is own first, then each composed group — the same order the revision
            // snapshot records, so what the screen shows and what content validates against agree.
            [.. own, .. composed],
            blockType.CreatedOn,
            blockType.ModifiedOn);
    }

    private PropertyDefinition ToDefinition(BlockTypeProperty property) =>
        new(
            property.Id,
            property.Key,
            property.Name,
            property.Description,
            property.FieldTypeKey,
            ReadConfiguration(property.ConfigurationJson, property.Key),
            property.IsRequired,
            property.Group,
            property.SortOrder);

    private PropertyDefinition ToDefinition(CompositionProperty property, string compositionKey) =>
        new(
            property.Id,
            property.Key,
            property.Name,
            property.Description,
            property.FieldTypeKey,
            ReadConfiguration(property.ConfigurationJson, property.Key),
            property.IsRequired,
            property.Group,
            property.SortOrder,
            compositionKey);

    private JsonElement? ReadConfiguration(string? configurationJson, string key) =>
        string.IsNullOrWhiteSpace(configurationJson)
            ? null
            : StructureJson.Read(configurationJson, logger, $"the configuration of property '{key}'");
}
