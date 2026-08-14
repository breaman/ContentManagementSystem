using System.Text.Json;

using ContentManagementSystem.Core.Fields.Configuration;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Structure;

/// <summary>Where the schema files live and whether the sync runs at startup.</summary>
public sealed class SchemaSyncOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Cms:SchemaSync";

    /// <summary>Directory holding the <c>*.json</c> files, relative to the content root.</summary>
    public string Directory { get; set; } = "CmsSchema";

    /// <summary>Whether the sync applies at startup. Diff and export never depend on this.</summary>
    public bool ApplyAtStartup { get; set; } = true;
}

/// <summary>
/// Applies the versioned zone and property definitions under <c>Server/CmsSchema/</c>
/// (task P1-26, spec section 27.1).
/// </summary>
public interface ISchemaSyncService
{
    /// <summary>
    /// Works out what the files would change, without writing anything.
    /// </summary>
    /// <param name="directory">Directory holding the files.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The plan, which is also what the drift check in CI reads.</returns>
    Task<SchemaSyncReport> DiffAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the files.
    /// </summary>
    /// <param name="directory">Directory holding the files.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>What was done.</returns>
    /// <remarks>
    /// Idempotent: running it twice against unchanged files reports no pending work the second time
    /// and writes nothing.
    /// </remarks>
    Task<SchemaSyncReport> ApplyAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the current database structure out as files.
    /// </summary>
    /// <param name="directory">Directory to write into. Created if missing.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The paths written.</returns>
    Task<IReadOnlyList<string>> ExportAsync(string directory, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <param name="context">The application database context.</param>
/// <param name="configurations">Checks a configuration against its field type's schema (P1-12).</param>
/// <param name="logger">Log for what a startup pass changed.</param>
/// <remarks>
/// <b>Additive and non-destructive, which is narrower than "additive".</b> The pass creates records
/// that are missing, adds slots that are missing, and updates an existing slot's labels, grouping,
/// ordering, required flag, and configuration. It never removes a slot the files do not mention, and
/// it never changes the field type of an existing one — those are the two changes spec section 8.5
/// calls destructive, and they are reported as refusals instead of applied. A structure promotion
/// that silently retyped a zone would make every stored value under that key unreadable, in an
/// environment nobody was watching.
/// <para>
/// The whole pass is one transaction. A promotion that applied four files out of six would leave a
/// content model that matches no commit.
/// </para>
/// </remarks>
public sealed class SchemaSyncService(
    ApplicationDbContext context,
    IFieldConfigurationValidator configurations,
    ILogger<SchemaSyncService> logger) : ISchemaSyncService
{
    /// <summary>How the files are written and read.</summary>
    /// <remarks>
    /// Indented and camel-cased: these files are reviewed in pull requests, which is the only reason
    /// they exist as files at all.
    /// </remarks>
    private static readonly JsonSerializerOptions FileFormat = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <inheritdoc />
    public Task<SchemaSyncReport> DiffAsync(string directory, CancellationToken cancellationToken = default) =>
        RunAsync(directory, apply: false, cancellationToken);

    /// <inheritdoc />
    public Task<SchemaSyncReport> ApplyAsync(string directory, CancellationToken cancellationToken = default) =>
        RunAsync(directory, apply: true, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ExportAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        System.IO.Directory.CreateDirectory(directory);

        var written = new List<string>();

        foreach (var template in await context.Templates
            .AsNoTracking()
            .Include(template => template.Zones)
            .OrderBy(template => template.Key)
            .ToListAsync(cancellationToken))
        {
            written.Add(await WriteAsync(
                directory,
                new SchemaDocument(
                    SchemaKind.Template,
                    template.Key,
                    template.Name,
                    template.Description,
                    template.Zones
                        .OrderBy(zone => zone.SortOrder)
                        .ThenBy(zone => zone.Key, StringComparer.Ordinal)
                        .Select(zone => new SchemaSlot(
                            zone.Key,
                            zone.Name,
                            zone.FieldTypeKey,
                            zone.Description,
                            Read(zone.ConfigurationJson),
                            zone.IsRequired,
                            zone.IsInlineEditable,
                            zone.Group,
                            zone.SortOrder))
                        .ToList(),
                    SortOrder: template.SortOrder),
                cancellationToken));
        }

        // Built-ins are deliberately not exported. Their property set ships with the code and the
        // sync refuses to reshape it, so a file describing one could only ever be a refusal on every
        // future run — permanent drift in the CI check, from a record nobody can change anyway.
        foreach (var blockType in await context.BlockTypes
            .AsNoTracking()
            .Where(blockType => !blockType.IsBuiltIn)
            .Include(blockType => blockType.Properties)
            .Include(blockType => blockType.Compositions)
                .ThenInclude(binding => binding.Composition)
            .OrderBy(blockType => blockType.Key)
            .ToListAsync(cancellationToken))
        {
            written.Add(await WriteAsync(
                directory,
                new SchemaDocument(
                    SchemaKind.BlockType,
                    blockType.Key,
                    blockType.Name,
                    blockType.Description,
                    blockType.Properties
                        .OrderBy(property => property.SortOrder)
                        .ThenBy(property => property.Key, StringComparer.Ordinal)
                        .Select(ToSlot)
                        .ToList(),
                    blockType.IconKey,
                    blockType.SummaryTemplate,
                    Compositions: blockType.Compositions
                        .OrderBy(binding => binding.SortOrder)
                        .Where(binding => binding.Composition is not null)
                        .Select(binding => binding.Composition.Key)
                        .ToList()),
                cancellationToken));
        }

        foreach (var composition in await context.Compositions
            .AsNoTracking()
            .Include(composition => composition.Properties)
            .OrderBy(composition => composition.Key)
            .ToListAsync(cancellationToken))
        {
            written.Add(await WriteAsync(
                directory,
                new SchemaDocument(
                    SchemaKind.Composition,
                    composition.Key,
                    composition.Name,
                    composition.Description,
                    composition.Properties
                        .OrderBy(property => property.SortOrder)
                        .ThenBy(property => property.Key, StringComparer.Ordinal)
                        .Select(ToSlot)
                        .ToList()),
                cancellationToken));
        }

        return written;
    }

    /// <summary>Computes the plan and, when asked, saves it.</summary>
    private async Task<SchemaSyncReport> RunAsync(
        string directory,
        bool apply,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!System.IO.Directory.Exists(directory))
        {
            // Not an error. A deployment that models its content entirely in the backoffice has no
            // files, and failing to start over an absent optional directory would be absurd.
            logger.LogDebug("No schema directory at {Directory}; nothing to sync.", directory);

            return SchemaSyncReport.Empty;
        }

        var changes = new List<SchemaChange>();
        var errors = new List<string>();
        var files = System.IO.Directory.GetFiles(directory, "*.json").OrderBy(path => path).ToList();

        // Compositions first, then block types, then templates. A block type file may name a group
        // that the same promotion introduces, and applying in dependency order is what lets one
        // commit add both.
        var documents = new List<SchemaDocument>();

        foreach (var path in files)
        {
            if (await ReadAsync(path, errors, cancellationToken) is { } document)
            {
                documents.Add(document);
            }
        }

        foreach (var document in documents.Where(d => d.Kind is SchemaKind.Composition))
        {
            await SyncCompositionAsync(document, changes, cancellationToken);
        }

        foreach (var document in documents.Where(d => d.Kind is SchemaKind.BlockType))
        {
            await SyncBlockTypeAsync(document, changes, cancellationToken);
        }

        foreach (var document in documents.Where(d => d.Kind is SchemaKind.Template))
        {
            await SyncTemplateAsync(document, changes, cancellationToken);
        }

        var report = new SchemaSyncReport(changes, files.Count, errors);

        if (apply && report.HasPendingWork)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // A diff must leave the context exactly as it found it, or the caller's next save would
            // apply a plan it only asked to see.
            context.ChangeTracker.Clear();
        }

        return report;
    }

    private async Task<SchemaDocument?> ReadAsync(
        string path,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);

            var document = await JsonSerializer.DeserializeAsync<SchemaDocument>(
                stream,
                FileFormat,
                cancellationToken);

            if (document is null || string.IsNullOrWhiteSpace(document.Key))
            {
                errors.Add($"{Path.GetFileName(path)}: no 'key', so it describes nothing.");

                return null;
            }

            return document;
        }
        catch (JsonException exception)
        {
            errors.Add($"{Path.GetFileName(path)}: {exception.Message}");

            return null;
        }
    }

    private async Task SyncTemplateAsync(
        SchemaDocument document,
        List<SchemaChange> changes,
        CancellationToken cancellationToken)
    {
        var template = await context.Templates
            .Include(candidate => candidate.Zones)
            .FirstOrDefaultAsync(candidate => candidate.Key == document.Key, cancellationToken);

        if (template is null)
        {
            template = new Template
            {
                Key = document.Key,
                Name = document.Name ?? document.Key,
                Description = document.Description,
                SortOrder = document.SortOrder,
                IsEnabled = true,
                // Created from a file, not from a component: no code claims the key until the
                // reconciler says otherwise, which is exactly what the flag means.
                IsOrphaned = true,
                CurrentRevision = 0,
            };

            context.Templates.Add(template);
            changes.Add(new SchemaChange(
                SchemaKind.Template, document.Key, SchemaChangeKind.Created, "Template created."));
        }

        var structural = false;

        foreach (var slot in document.Definitions)
        {
            var zone = template.Zones.FirstOrDefault(
                candidate => string.Equals(candidate.Key, slot.Key, StringComparison.OrdinalIgnoreCase));

            if (Refuse(document, slot, zone?.FieldTypeKey, changes)) continue;

            var configurationJson = StructureJson.Normalize(slot.Configuration);

            if (zone is null)
            {
                template.Zones.Add(new Zone
                {
                    Key = slot.Key,
                    Name = slot.Name,
                    Description = slot.Description,
                    FieldTypeKey = slot.FieldTypeKey,
                    ConfigurationJson = configurationJson,
                    IsRequired = slot.IsRequired,
                    IsInlineEditable = slot.IsInlineEditable,
                    Group = slot.Group,
                    SortOrder = slot.SortOrder,
                });

                structural = true;
                changes.Add(new SchemaChange(
                    SchemaKind.Template, document.Key, SchemaChangeKind.SlotAdded, $"Zone '{slot.Key}' added."));

                continue;
            }

            if (Apply(zone, slot, configurationJson) is { } applied)
            {
                structural |= applied.Structural;
                changes.Add(new SchemaChange(
                    SchemaKind.Template,
                    document.Key,
                    SchemaChangeKind.SlotUpdated,
                    $"Zone '{slot.Key}': {applied.Detail}."));
            }
        }

        ReportUnlisted(
            SchemaKind.Template,
            document,
            template.Zones.Select(zone => zone.Key),
            "Zone",
            changes);

        if (structural)
        {
            template.Revisions.Add(new TemplateRevision
            {
                RevisionNumber = template.CurrentRevision + 1,
                ZoneSnapshotJson = Content.Schema.ContentSchemaSnapshot.WriteZones(template.Zones),
                Notes = "Applied from CmsSchema.",
            });

            template.CurrentRevision += 1;
        }
    }

    private async Task SyncBlockTypeAsync(
        SchemaDocument document,
        List<SchemaChange> changes,
        CancellationToken cancellationToken)
    {
        var blockType = await context.BlockTypes
            .Include(candidate => candidate.Properties)
            .Include(candidate => candidate.Compositions)
                .ThenInclude(binding => binding.Composition)
                    .ThenInclude(composition => composition.Properties)
            .FirstOrDefaultAsync(candidate => candidate.Key == document.Key, cancellationToken);

        if (blockType is null)
        {
            blockType = new BlockType
            {
                Key = document.Key,
                Name = document.Name ?? document.Key,
                Description = document.Description,
                IconKey = document.IconKey,
                SummaryTemplate = document.SummaryTemplate,
                IsOrphaned = true,
                IsBuiltIn = false,
                CurrentRevision = 0,
            };

            context.BlockTypes.Add(blockType);
            changes.Add(new SchemaChange(
                SchemaKind.BlockType, document.Key, SchemaChangeKind.Created, "Block type created."));
        }
        else if (blockType.IsBuiltIn)
        {
            // The one record a file may not reshape, for the reason the API refuses it: its renderer
            // expects exactly the properties it ships with.
            changes.Add(new SchemaChange(
                SchemaKind.BlockType,
                document.Key,
                SchemaChangeKind.Refused,
                "Built-in block type; its property set is fixed and the file was ignored."));

            return;
        }

        var structural = false;

        foreach (var slot in document.Definitions)
        {
            var property = blockType.Properties.FirstOrDefault(
                candidate => string.Equals(candidate.Key, slot.Key, StringComparison.OrdinalIgnoreCase));

            if (Refuse(document, slot, property?.FieldTypeKey, changes)) continue;

            var configurationJson = StructureJson.Normalize(slot.Configuration);

            if (property is null)
            {
                blockType.Properties.Add(new BlockTypeProperty
                {
                    Key = slot.Key,
                    Name = slot.Name,
                    Description = slot.Description,
                    FieldTypeKey = slot.FieldTypeKey,
                    ConfigurationJson = configurationJson,
                    IsRequired = slot.IsRequired,
                    Group = slot.Group,
                    SortOrder = slot.SortOrder,
                });

                structural = true;
                changes.Add(new SchemaChange(
                    SchemaKind.BlockType,
                    document.Key,
                    SchemaChangeKind.SlotAdded,
                    $"Property '{slot.Key}' added."));

                continue;
            }

            if (Apply(property, slot, configurationJson) is { } applied)
            {
                structural |= applied.Structural;
                changes.Add(new SchemaChange(
                    SchemaKind.BlockType,
                    document.Key,
                    SchemaChangeKind.SlotUpdated,
                    $"Property '{slot.Key}': {applied.Detail}."));
            }
        }

        structural |= await ComposeAsync(blockType, document, changes, cancellationToken);

        ReportUnlisted(
            SchemaKind.BlockType,
            document,
            blockType.Properties.Select(property => property.Key),
            "Property",
            changes);

        if (structural)
        {
            BlockTypeSchemaWriter.Cut(blockType, "Applied from CmsSchema.");
        }
    }

    /// <summary>Composes any group the file names that is not composed yet.</summary>
    /// <returns>Whether anything was composed.</returns>
    /// <remarks>
    /// Additive here too: a group the file omits is left composed rather than detached, because
    /// detaching takes properties out of block instances that are using them.
    /// </remarks>
    private async Task<bool> ComposeAsync(
        BlockType blockType,
        SchemaDocument document,
        List<SchemaChange> changes,
        CancellationToken cancellationToken)
    {
        var composed = false;
        var order = 0;

        foreach (var key in document.ComposedKeys)
        {
            order++;

            if (blockType.Compositions.Any(binding =>
                string.Equals(binding.Composition?.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // The tracker first, then the database. A composition file earlier in the same pass has
            // been added but not saved, and a query would go to the server and not find it — which
            // would refuse the very case applying in dependency order exists to support: one commit
            // introducing a group and the block type that composes it.
            var composition =
                context.Compositions.Local.FirstOrDefault(candidate =>
                    string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase)) ??
                await context.Compositions
                    .Include(candidate => candidate.Properties)
                    .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

            if (composition is null)
            {
                changes.Add(new SchemaChange(
                    SchemaKind.BlockType,
                    document.Key,
                    SchemaChangeKind.Refused,
                    $"Composition '{key}' does not exist, so it was not composed in."));

                continue;
            }

            var taken = new HashSet<string>(
                blockType.Properties.Select(property => property.Key),
                StringComparer.OrdinalIgnoreCase);

            foreach (var (_, property) in BlockTypeSchemaWriter.Composed(blockType))
            {
                taken.Add(property.Key);
            }

            if (composition.Properties.FirstOrDefault(property => taken.Contains(property.Key)) is { } collision)
            {
                changes.Add(new SchemaChange(
                    SchemaKind.BlockType,
                    document.Key,
                    SchemaChangeKind.Refused,
                    $"Composing '{key}' would define '{collision.Key}' twice in one block instance."));

                continue;
            }

            blockType.Compositions.Add(new BlockTypeComposition
            {
                Composition = composition,
                SortOrder = order,
            });

            composed = true;
            changes.Add(new SchemaChange(
                SchemaKind.BlockType,
                document.Key,
                SchemaChangeKind.SlotAdded,
                $"Composition '{key}' composed in."));
        }

        return composed;
    }

    private async Task SyncCompositionAsync(
        SchemaDocument document,
        List<SchemaChange> changes,
        CancellationToken cancellationToken)
    {
        var composition = await context.Compositions
            .Include(candidate => candidate.Properties)
            .FirstOrDefaultAsync(candidate => candidate.Key == document.Key, cancellationToken);

        if (composition is null)
        {
            composition = new Composition
            {
                Key = document.Key,
                Name = document.Name ?? document.Key,
                Description = document.Description,
            };

            context.Compositions.Add(composition);
            changes.Add(new SchemaChange(
                SchemaKind.Composition, document.Key, SchemaChangeKind.Created, "Composition created."));
        }

        foreach (var slot in document.Definitions)
        {
            var property = composition.Properties.FirstOrDefault(
                candidate => string.Equals(candidate.Key, slot.Key, StringComparison.OrdinalIgnoreCase));

            if (Refuse(document, slot, property?.FieldTypeKey, changes)) continue;

            var configurationJson = StructureJson.Normalize(slot.Configuration);

            if (property is null)
            {
                composition.Properties.Add(new CompositionProperty
                {
                    Key = slot.Key,
                    Name = slot.Name,
                    Description = slot.Description,
                    FieldTypeKey = slot.FieldTypeKey,
                    ConfigurationJson = configurationJson,
                    IsRequired = slot.IsRequired,
                    Group = slot.Group,
                    SortOrder = slot.SortOrder,
                });

                changes.Add(new SchemaChange(
                    SchemaKind.Composition,
                    document.Key,
                    SchemaChangeKind.SlotAdded,
                    $"Property '{slot.Key}' added."));

                continue;
            }

            if (Apply(property, slot, configurationJson) is { } applied)
            {
                changes.Add(new SchemaChange(
                    SchemaKind.Composition,
                    document.Key,
                    SchemaChangeKind.SlotUpdated,
                    $"Property '{slot.Key}': {applied.Detail}."));
            }
        }

        ReportUnlisted(
            SchemaKind.Composition,
            document,
            composition.Properties.Select(property => property.Key),
            "Property",
            changes);
    }

    /// <summary>
    /// Decides whether a slot in a file must be refused rather than applied.
    /// </summary>
    /// <param name="document">The file being applied.</param>
    /// <param name="slot">The slot in question.</param>
    /// <param name="storedFieldTypeKey">The field type stored under this key, or null if new.</param>
    /// <param name="changes">Collected changes, appended to on a refusal.</param>
    /// <returns>Whether the slot was refused.</returns>
    private bool Refuse(
        SchemaDocument document,
        SchemaSlot slot,
        string? storedFieldTypeKey,
        List<SchemaChange> changes)
    {
        if (string.IsNullOrWhiteSpace(slot.Key) || string.IsNullOrWhiteSpace(slot.FieldTypeKey))
        {
            changes.Add(new SchemaChange(
                document.Kind,
                document.Key,
                SchemaChangeKind.Refused,
                $"A slot is missing its key or field type: '{slot.Key}'."));

            return true;
        }

        if (storedFieldTypeKey is not null &&
            !string.Equals(storedFieldTypeKey, slot.FieldTypeKey, StringComparison.Ordinal))
        {
            changes.Add(new SchemaChange(
                document.Kind,
                document.Key,
                SchemaChangeKind.Refused,
                $"'{slot.Key}' is stored as '{storedFieldTypeKey}' and the file says " +
                $"'{slot.FieldTypeKey}'. Retyping a slot needs a converter for the values already " +
                "stored under it, so it is never done by a promotion."));

            return true;
        }

        var configuration = configurations.Validate(
            slot.FieldTypeKey,
            StructureJson.Normalize(slot.Configuration));

        if (configuration.HasErrors)
        {
            changes.Add(new SchemaChange(
                document.Kind,
                document.Key,
                SchemaChangeKind.Refused,
                $"'{slot.Key}': " + string.Join(
                    " ",
                    configuration.Diagnostics
                        .Where(diagnostic => diagnostic.Severity is ValidationSeverity.Error)
                        .Select(diagnostic => diagnostic.Message))));

            return true;
        }

        return false;
    }

    /// <summary>Applies the non-destructive differences to a zone.</summary>
    /// <returns>What changed, or null when the zone already matches.</returns>
    private static (bool Structural, string Detail)? Apply(Zone zone, SchemaSlot slot, string? configurationJson)
    {
        var structural = zone.IsRequired != slot.IsRequired ||
            !string.Equals(zone.ConfigurationJson, configurationJson, StringComparison.Ordinal);

        var changed = structural ||
            zone.Name != slot.Name ||
            zone.Description != slot.Description ||
            zone.Group != slot.Group ||
            zone.SortOrder != slot.SortOrder ||
            zone.IsInlineEditable != slot.IsInlineEditable;

        if (!changed) return null;

        var detail = structural ? "validation settings updated" : "labels updated";

        zone.Name = slot.Name;
        zone.Description = slot.Description;
        zone.ConfigurationJson = configurationJson;
        zone.IsRequired = slot.IsRequired;
        zone.IsInlineEditable = slot.IsInlineEditable;
        zone.Group = slot.Group;
        zone.SortOrder = slot.SortOrder;

        return (structural, detail);
    }

    /// <summary>Applies the non-destructive differences to a block-type property.</summary>
    private static (bool Structural, string Detail)? Apply(
        BlockTypeProperty property,
        SchemaSlot slot,
        string? configurationJson)
    {
        var structural = property.IsRequired != slot.IsRequired ||
            !string.Equals(property.ConfigurationJson, configurationJson, StringComparison.Ordinal);

        var changed = structural ||
            property.Name != slot.Name ||
            property.Description != slot.Description ||
            property.Group != slot.Group ||
            property.SortOrder != slot.SortOrder;

        if (!changed) return null;

        var detail = structural ? "validation settings updated" : "labels updated";

        property.Name = slot.Name;
        property.Description = slot.Description;
        property.ConfigurationJson = configurationJson;
        property.IsRequired = slot.IsRequired;
        property.Group = slot.Group;
        property.SortOrder = slot.SortOrder;

        return (structural, detail);
    }

    /// <summary>Applies the non-destructive differences to a composition property.</summary>
    private static (bool Structural, string Detail)? Apply(
        CompositionProperty property,
        SchemaSlot slot,
        string? configurationJson)
    {
        var structural = property.IsRequired != slot.IsRequired ||
            !string.Equals(property.ConfigurationJson, configurationJson, StringComparison.Ordinal);

        var changed = structural ||
            property.Name != slot.Name ||
            property.Description != slot.Description ||
            property.Group != slot.Group ||
            property.SortOrder != slot.SortOrder;

        if (!changed) return null;

        var detail = structural ? "validation settings updated" : "labels updated";

        property.Name = slot.Name;
        property.Description = slot.Description;
        property.ConfigurationJson = configurationJson;
        property.IsRequired = slot.IsRequired;
        property.Group = slot.Group;
        property.SortOrder = slot.SortOrder;

        return (structural, detail);
    }

    /// <summary>Reports stored slots the file does not mention, which are kept rather than removed.</summary>
    private static void ReportUnlisted(
        SchemaKind kind,
        SchemaDocument document,
        IEnumerable<string> storedKeys,
        string noun,
        List<SchemaChange> changes)
    {
        var listed = new HashSet<string>(
            document.Definitions.Select(slot => slot.Key),
            StringComparer.OrdinalIgnoreCase);

        foreach (var key in storedKeys.Where(key => !listed.Contains(key)))
        {
            changes.Add(new SchemaChange(
                kind,
                document.Key,
                SchemaChangeKind.KeptUnlisted,
                $"{noun} '{key}' is in the database but not in the file; it was kept."));
        }
    }

    private async Task<string> WriteAsync(
        string directory,
        SchemaDocument document,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, FileName(document));

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(document, FileFormat),
            cancellationToken);

        return path;
    }

    /// <summary>Names a file after what it describes, so a directory listing is readable.</summary>
    private static string FileName(SchemaDocument document) => document.Kind switch
    {
        SchemaKind.Template => $"template.{document.Key}.json",
        SchemaKind.BlockType => $"block-type.{document.Key}.json",
        _ => $"composition.{document.Key}.json",
    };

    private static SchemaSlot ToSlot(BlockTypeProperty property) =>
        new(
            property.Key,
            property.Name,
            property.FieldTypeKey,
            property.Description,
            Read(property.ConfigurationJson),
            property.IsRequired,
            IsInlineEditable: false,
            property.Group,
            property.SortOrder);

    private static SchemaSlot ToSlot(CompositionProperty property) =>
        new(
            property.Key,
            property.Name,
            property.FieldTypeKey,
            property.Description,
            Read(property.ConfigurationJson),
            property.IsRequired,
            IsInlineEditable: false,
            property.Group,
            property.SortOrder);

    /// <summary>Embeds a stored configuration as an object so the file stays reviewable.</summary>
    private static JsonElement? Read(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson)) return null;

        using var document = JsonDocument.Parse(configurationJson);

        return document.RootElement.Clone();
    }
}
