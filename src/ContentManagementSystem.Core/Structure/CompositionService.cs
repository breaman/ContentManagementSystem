using System.Text.Json;

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

/// <inheritdoc cref="ICompositionService" />
/// <param name="context">The application database context.</param>
/// <param name="configurations">Checks a configuration against its field type's schema (P1-12).</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="logger">Log for structural changes and unreadable stored JSON.</param>
public sealed class CompositionService(
    ApplicationDbContext context,
    IFieldConfigurationValidator configurations,
    ICmsAuthorization authorization,
    ILogger<CompositionService> logger) : ICompositionService
{
    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<CompositionSummary>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<CompositionSummary>>.Forbidden(
                "Reading compositions is not permitted.");
        }

        var compositions = await context.Compositions
            .AsNoTracking()
            .OrderBy(composition => composition.Name)
            .Select(composition => new CompositionSummary(
                composition.Id,
                composition.Key,
                composition.Name,
                composition.Description,
                composition.Properties.Count,
                composition.BlockTypes.Count))
            .ToListAsync(cancellationToken);

        return CmsResult<IReadOnlyList<CompositionSummary>>.Success(compositions);
    }

    /// <inheritdoc />
    public async Task<CmsResult<CompositionDetail>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<CompositionDetail>.Forbidden("Reading compositions is not permitted.");
        }

        var composition = await Query()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return composition is null
            ? CmsResult<CompositionDetail>.NotFound($"No composition has id {id}.")
            : CmsResult<CompositionDetail>.Success(ToDetail(composition));
    }

    /// <inheritdoc />
    public async Task<CmsResult<CompositionDetail>> CreateAsync(
        CreateCompositionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<CompositionDetail>.Forbidden("Managing compositions is not permitted.");
        }

        var diagnostics = new List<ValidationDiagnostic>();
        diagnostics.AddRange(ContentKeys.Validate(request.Key, SlotRules.KeyPath).Diagnostics);
        diagnostics.AddRange(SlotRules.ValidateMetadata(request.Name, request.Description, group: null));

        if (diagnostics.Count > 0)
        {
            return CmsResult<CompositionDetail>.Invalid(ValidationResult.From(diagnostics));
        }

        var key = request.Key!.Trim();

        if (await KeyExistsAsync(key, cancellationToken))
        {
            return DuplicateKey<CompositionDetail>(key);
        }

        var composition = new Composition
        {
            Key = key,
            Name = request.Name!.Trim(),
            Description = SlotRules.Clean(request.Description),
        };

        context.Compositions.Add(composition);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await KeyExistsAsync(key, cancellationToken)) return DuplicateKey<CompositionDetail>(key);

            throw;
        }

        return CmsResult<CompositionDetail>.Success(ToDetail(composition));
    }

    /// <inheritdoc />
    public async Task<CmsResult<CompositionDetail>> UpdateAsync(
        int id,
        UpdateCompositionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<CompositionDetail>.Forbidden("Managing compositions is not permitted.");
        }

        var composition = await Query().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (composition is null)
        {
            return CmsResult<CompositionDetail>.NotFound($"No composition has id {id}.");
        }

        var diagnostics = SlotRules.ValidateMetadata(request.Name, request.Description, group: null);

        if (!string.IsNullOrWhiteSpace(request.Key) &&
            !string.Equals(request.Key.Trim(), composition.Key, StringComparison.Ordinal))
        {
            diagnostics.Add(new ValidationDiagnostic(
                StructureCodes.KeyImmutable,
                $"A composition key cannot be changed. This one is '{composition.Key}'. Create a new " +
                "composition instead.",
                ValidationSeverity.Error,
                SlotRules.KeyPath));
        }

        if (diagnostics.Count > 0)
        {
            return CmsResult<CompositionDetail>.Invalid(ValidationResult.From(diagnostics));
        }

        composition.Name = request.Name!.Trim();
        composition.Description = SlotRules.Clean(request.Description);

        await context.SaveChangesAsync(cancellationToken);

        return CmsResult<CompositionDetail>.Success(ToDetail(composition));
    }

    /// <inheritdoc />
    public async Task<CmsResult<bool>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<bool>.Forbidden("Managing compositions is not permitted.");
        }

        var composition = await Query().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (composition is null)
        {
            return CmsResult<bool>.NotFound($"No composition has id {id}.");
        }

        var composedInto = composition.BlockTypes
            .Where(binding => binding.BlockType is not null)
            .Select(binding => binding.BlockType.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (composedInto.Count > 0)
        {
            return CmsResult<bool>.Conflict(
                StructureCodes.InUse,
                $"'{composition.Key}' is composed into {string.Join(", ", composedInto.Select(k => $"'{k}'"))}. " +
                "Detach it from each of them first — deleting it here would take those properties out " +
                "of block types whose content is using them.");
        }

        context.CompositionProperties.RemoveRange(composition.Properties);
        context.Compositions.Remove(composition);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Composition {CompositionKey} deleted.", composition.Key);

        return CmsResult<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<CmsResult<CompositionPropertySaveResult>> CreatePropertyAsync(
        int compositionId,
        CreatePropertyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<CompositionPropertySaveResult>.Forbidden(
                "Managing compositions is not permitted.");
        }

        var composition = await Query().FirstOrDefaultAsync(
            candidate => candidate.Id == compositionId,
            cancellationToken);

        if (composition is null)
        {
            return CmsResult<CompositionPropertySaveResult>.NotFound(
                $"No composition has id {compositionId}.");
        }

        var configurationJson = StructureJson.Normalize(request.Configuration);

        var diagnostics = new List<ValidationDiagnostic>();
        diagnostics.AddRange(ContentKeys.Validate(request.Key, SlotRules.KeyPath).Diagnostics);
        diagnostics.AddRange(SlotRules.ValidateMetadata(request.Name, request.Description, request.Group));
        diagnostics.AddRange(SlotRules.ValidateFieldType(configurations, request.FieldTypeKey, configurationJson));

        var checks = ValidationResult.From(diagnostics);

        if (checks.HasErrors) return CmsResult<CompositionPropertySaveResult>.Invalid(checks);

        var key = request.Key!.Trim();

        if (composition.Properties.Any(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            return CmsResult<CompositionPropertySaveResult>.Conflict(
                StructureCodes.KeyDuplicate,
                $"Composition '{composition.Key}' already has a property with the key '{key}'.",
                SlotRules.KeyPath);
        }

        // The collision that matters is not inside the group — it is where the group lands. A key
        // that is free here but taken on a host block type would define one key twice in one block
        // instance, and the failure would surface as a broken editor on a block type nobody was
        // looking at. Checked before the write rather than after (spec section 6.3).
        var hosts = await HostsAsync(compositionId, cancellationToken);

        if (Collisions(hosts, [key]) is { Count: > 0 } collisions)
        {
            return CmsResult<CompositionPropertySaveResult>.Invalid(
                StructureCodes.CompositionCollision,
                $"'{key}' is already a property of {Describe(collisions)}, which compose " +
                $"'{composition.Key}'. One key cannot have two definitions in one block instance.",
                SlotRules.KeyPath);
        }

        var property = new CompositionProperty
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

        composition.Properties.Add(property);

        var affected = Recut(hosts, $"Composition '{composition.Key}' gained property '{key}'.");

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Property {PropertyKey} added to composition {CompositionKey}, recutting {Count} block types.",
            key,
            composition.Key,
            affected.Count);

        return CmsResult<CompositionPropertySaveResult>.Success(
            new CompositionPropertySaveResult(
                ToDefinition(property, composition.Key),
                affected,
                ApiDiagnostics.Project(checks, ValidationSeverity.Warning)),
            checks);
    }

    /// <inheritdoc />
    public async Task<CmsResult<CompositionPropertySaveResult>> UpdatePropertyAsync(
        int compositionId,
        int propertyId,
        UpdatePropertyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<CompositionPropertySaveResult>.Forbidden(
                "Managing compositions is not permitted.");
        }

        var composition = await Query().FirstOrDefaultAsync(
            candidate => candidate.Id == compositionId,
            cancellationToken);

        if (composition is null)
        {
            return CmsResult<CompositionPropertySaveResult>.NotFound(
                $"No composition has id {compositionId}.");
        }

        if (composition.Properties.FirstOrDefault(candidate => candidate.Id == propertyId) is not { } property)
        {
            return CmsResult<CompositionPropertySaveResult>.NotFound(
                $"Composition {compositionId} has no property with id {propertyId}.");
        }

        var configurationJson = StructureJson.Normalize(request.Configuration);

        var diagnostics = SlotRules.ValidateMetadata(request.Name, request.Description, request.Group);

        diagnostics.AddRange(SlotRules.ValidateImmutable(
            request.Key,
            request.FieldTypeKey,
            property.Key,
            property.FieldTypeKey,
            "property",
            $"composition '{composition.Key}'"));

        diagnostics.AddRange(SlotRules.ValidateFieldType(configurations, property.FieldTypeKey, configurationJson));

        var checks = ValidationResult.From(diagnostics);

        if (checks.HasErrors) return CmsResult<CompositionPropertySaveResult>.Invalid(checks);

        var isStructural = property.IsRequired != request.IsRequired ||
            !string.Equals(property.ConfigurationJson, configurationJson, StringComparison.Ordinal);

        property.Name = request.Name!.Trim();
        property.Description = SlotRules.Clean(request.Description);
        property.ConfigurationJson = configurationJson;
        property.IsRequired = request.IsRequired;
        property.Group = SlotRules.Clean(request.Group);
        property.SortOrder = request.SortOrder;

        var affected = isStructural
            ? Recut(
                await HostsAsync(compositionId, cancellationToken),
                $"Composition '{composition.Key}' changed property '{property.Key}'.")
            : [];

        await context.SaveChangesAsync(cancellationToken);

        return CmsResult<CompositionPropertySaveResult>.Success(
            new CompositionPropertySaveResult(
                ToDefinition(property, composition.Key),
                affected,
                ApiDiagnostics.Project(checks, ValidationSeverity.Warning)),
            checks);
    }

    /// <inheritdoc />
    public async Task<CmsResult<CompositionPropertyRemovalResult>> DeletePropertyAsync(
        int compositionId,
        int propertyId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return CmsResult<CompositionPropertyRemovalResult>.Forbidden(
                "Managing compositions is not permitted.");
        }

        var composition = await Query().FirstOrDefaultAsync(
            candidate => candidate.Id == compositionId,
            cancellationToken);

        if (composition is null)
        {
            return CmsResult<CompositionPropertyRemovalResult>.NotFound(
                $"No composition has id {compositionId}.");
        }

        if (composition.Properties.FirstOrDefault(candidate => candidate.Id == propertyId) is not { } property)
        {
            return CmsResult<CompositionPropertyRemovalResult>.NotFound(
                $"Composition {compositionId} has no property with id {propertyId}.");
        }

        context.CompositionProperties.Remove(property);
        composition.Properties.Remove(property);

        var affected = Recut(
            await HostsAsync(compositionId, cancellationToken),
            $"Composition '{composition.Key}' lost property '{property.Key}'.");

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Property {PropertyKey} removed from composition {CompositionKey}, recutting {Count} block " +
            "types. Values stored under that key are retained as orphaned content.",
            property.Key,
            composition.Key,
            affected.Count);

        return CmsResult<CompositionPropertyRemovalResult>.Success(
            new CompositionPropertyRemovalResult(property.Key, affected));
    }

    /// <summary>Everything a composition's detail and its guards need, in one query.</summary>
    private IQueryable<Composition> Query() =>
        context.Compositions
            .Include(composition => composition.Properties)
            .Include(composition => composition.BlockTypes)
                .ThenInclude(binding => binding.BlockType);

    /// <summary>
    /// Loads every block type composing this group, with everything its snapshot needs.
    /// </summary>
    /// <remarks>
    /// Loaded whole rather than counted, because recutting is not a count: each host's new snapshot
    /// is its own properties plus <em>all</em> of its composed groups, not just this one.
    /// </remarks>
    private Task<List<BlockType>> HostsAsync(int compositionId, CancellationToken cancellationToken) =>
        context.BlockTypes
            .Include(blockType => blockType.Properties)
            .Include(blockType => blockType.Compositions)
                .ThenInclude(binding => binding.Composition)
                    .ThenInclude(composition => composition.Properties)
            .Where(blockType => blockType.Compositions.Any(binding => binding.CompositionId == compositionId))
            .ToListAsync(cancellationToken);

    /// <summary>Cuts a revision on each host, in the same transaction as the edit itself.</summary>
    /// <returns>The keys of the block types recut, for the caller to report.</returns>
    private static List<string> Recut(List<BlockType> hosts, string notes)
    {
        foreach (var host in hosts)
        {
            BlockTypeSchemaWriter.Cut(host, notes);
        }

        return hosts
            .Select(host => host.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Finds hosts that already define any of the given keys directly.</summary>
    private static List<string> Collisions(List<BlockType> hosts, IReadOnlyCollection<string> keys) =>
        hosts
            .Where(host => host.Properties.Any(property => keys.Contains(property.Key, StringComparer.OrdinalIgnoreCase)))
            .Select(host => host.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

    private static string Describe(IEnumerable<string> keys) =>
        string.Join(", ", keys.Select(key => $"'{key}'"));

    private Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken) =>
        context.Compositions.AnyAsync(composition => composition.Key == key, cancellationToken);

    private static CmsResult<T> DuplicateKey<T>(string key) =>
        CmsResult<T>.Conflict(
            StructureCodes.KeyDuplicate,
            $"A composition already uses the key '{key}'.",
            SlotRules.KeyPath);

    private CompositionDetail ToDetail(Composition composition) =>
        new(
            new CompositionSummary(
                composition.Id,
                composition.Key,
                composition.Name,
                composition.Description,
                composition.Properties.Count,
                composition.BlockTypes.Count),
            composition.Properties
                .OrderBy(property => property.SortOrder)
                .ThenBy(property => property.Key, StringComparer.Ordinal)
                .Select(property => ToDefinition(property, composition.Key))
                .ToList(),
            composition.BlockTypes
                .Where(binding => binding.BlockType is not null)
                .Select(binding => binding.BlockType.Key)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList(),
            composition.CreatedOn,
            composition.ModifiedOn);

    private PropertyDefinition ToDefinition(CompositionProperty property, string compositionKey) =>
        new(
            property.Id,
            property.Key,
            property.Name,
            property.Description,
            property.FieldTypeKey,
            string.IsNullOrWhiteSpace(property.ConfigurationJson)
                ? null
                : StructureJson.Read(
                    property.ConfigurationJson,
                    logger,
                    $"the configuration of property '{property.Key}'"),
            property.IsRequired,
            property.Group,
            property.SortOrder,
            compositionKey);
}
