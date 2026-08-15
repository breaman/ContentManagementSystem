using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// The result of one reconciliation pass, for logging and for the CLI to print.
/// </summary>
/// <param name="TemplatesCreated">Keys of templates that existed in code but not in the database.</param>
/// <param name="TemplatesAdopted">Keys of templates a deployment gave a component back to.</param>
/// <param name="TemplatesOrphaned">Keys of templates newly left without a component.</param>
/// <param name="BlockTypesCreated">Keys of block types created from code.</param>
/// <param name="BlockTypesAdopted">Keys of block types a deployment gave a component back to.</param>
/// <param name="BlockTypesOrphaned">Keys of block types newly left without a component.</param>
public sealed record ReconciliationReport(
    IReadOnlyList<string> TemplatesCreated,
    IReadOnlyList<string> TemplatesAdopted,
    IReadOnlyList<string> TemplatesOrphaned,
    IReadOnlyList<string> BlockTypesCreated,
    IReadOnlyList<string> BlockTypesAdopted,
    IReadOnlyList<string> BlockTypesOrphaned)
{
    /// <summary>Whether the pass changed anything at all.</summary>
    public bool HasChanges =>
        TemplatesCreated.Count > 0 || TemplatesAdopted.Count > 0 || TemplatesOrphaned.Count > 0 ||
        BlockTypesCreated.Count > 0 || BlockTypesAdopted.Count > 0 || BlockTypesOrphaned.Count > 0;
}

/// <summary>
/// Reconciles the templates and block types declared in code with the rows in the database
/// (task P1-25, spec section 8.4).
/// </summary>
public interface ITemplateReconciler
{
    /// <summary>
    /// Runs one reconciliation pass.
    /// </summary>
    /// <param name="cancellationToken">Token observed while querying and saving.</param>
    /// <returns>What changed.</returns>
    Task<ReconciliationReport> ReconcileAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <param name="context">The application database context.</param>
/// <param name="scanner">Reads what the deployed assemblies declare.</param>
/// <param name="logger">Log for the startup diff.</param>
/// <remarks>
/// The four rules of spec section 8.4, and the reasoning behind the one that looks like a bug:
/// <list type="number">
/// <item>A template in code with no row is <b>created</b>, so a new deployment works without anyone
/// touching the backoffice.</item>
/// <item>A row whose key no code declares is marked <b>orphaned</b>, which stops it being assigned
/// to new pages and degrades the <c>cms-templates</c> health check.</item>
/// <item>Nothing is ever <b>deleted</b>. A component removed from a branch, a bad merge, or a
/// half-deployed rollout would otherwise take zone definitions — and with them the ability to read
/// stored payloads — with it.</item>
/// <item>Name and description are applied on <b>creation only</b>. They are editable in the
/// backoffice, and rewriting them from the attribute on every startup would silently undo an
/// editor's rename after each deploy.</item>
/// </list>
/// <para>
/// The pass is idempotent: run twice against an unchanged deployment, the second run reports no
/// changes and writes nothing.
/// </para>
/// </remarks>
public sealed class TemplateReconciler(
    ApplicationDbContext context,
    CmsComponentScanner scanner,
    ILogger<TemplateReconciler> logger) : ITemplateReconciler
{
    /// <inheritdoc />
    public async Task<ReconciliationReport> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        // The same scan the rendering pipeline resolves components through, so a key the render path
        // would refuse to resolve is a key this refuses to reconcile.
        var declarations = scanner.Scan();

        var templates = await ReconcileTemplatesAsync(declarations.Templates, cancellationToken);
        var blockTypes = await ReconcileBlockTypesAsync(declarations.BlockTypes, cancellationToken);

        var report = new ReconciliationReport(
            templates.Created, templates.Adopted, templates.Orphaned,
            blockTypes.Created, blockTypes.Adopted, blockTypes.Orphaned);

        if (report.HasChanges)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        Report(report);

        return report;
    }

    private async Task<(List<string> Created, List<string> Adopted, List<string> Orphaned)>
        ReconcileTemplatesAsync(
            IReadOnlyDictionary<string, CmsTemplateDeclaration> declared,
            CancellationToken cancellationToken)
    {
        var stored = await context.Templates.ToListAsync(cancellationToken);
        var byKey = stored.ToDictionary(template => template.Key, StringComparer.Ordinal);

        List<string> created = [], adopted = [], orphaned = [];

        foreach (var (key, declaration) in declared)
        {
            if (byKey.TryGetValue(key, out var template))
            {
                if (template.IsOrphaned) adopted.Add(key);

                template.IsOrphaned = false;
                template.ComponentTypeName = declaration.ComponentTypeName;

                continue;
            }

            var fresh = new Template
            {
                Key = key,
                Name = declaration.Attribute.Name,
                Description = declaration.Attribute.Description,
                ComponentTypeName = declaration.ComponentTypeName,
                SortOrder = declaration.Attribute.SortOrder,
                IsEnabled = true,
                IsOrphaned = false,
                CurrentRevision = 1,
            };

            // Revision 1 with no zones, exactly as a backoffice-created template gets: the zone
            // definitions arrive from the schema sync (P1-26) or from a developer in the admin
            // screens, and content created in between must still have a revision to capture.
            fresh.Revisions.Add(new TemplateRevision
            {
                RevisionNumber = 1,
                ZoneSnapshotJson = ContentSchemaSnapshot.WriteZones([]),
                Notes = $"Created from code by {declaration.ComponentTypeName}.",
            });

            context.Templates.Add(fresh);
            created.Add(key);
        }

        foreach (var template in stored)
        {
            if (declared.ContainsKey(template.Key) || template.IsOrphaned) continue;

            template.IsOrphaned = true;
            orphaned.Add(template.Key);
        }

        return (created, adopted, orphaned);
    }

    private async Task<(List<string> Created, List<string> Adopted, List<string> Orphaned)>
        ReconcileBlockTypesAsync(
            IReadOnlyDictionary<string, CmsBlockTypeDeclaration> declared,
            CancellationToken cancellationToken)
    {
        var stored = await context.BlockTypes.ToListAsync(cancellationToken);
        var byKey = stored.ToDictionary(blockType => blockType.Key, StringComparer.Ordinal);

        List<string> created = [], adopted = [], orphaned = [];

        foreach (var (key, declaration) in declared)
        {
            if (byKey.TryGetValue(key, out var blockType))
            {
                if (blockType.IsOrphaned) adopted.Add(key);

                blockType.IsOrphaned = false;
                blockType.ComponentTypeName = declaration.ComponentTypeName;

                continue;
            }

            var fresh = new BlockType
            {
                Key = key,
                Name = declaration.Attribute.Name,
                Description = declaration.Attribute.Description,
                IconKey = declaration.Attribute.IconKey,
                SummaryTemplate = declaration.Attribute.SummaryTemplate,
                ComponentTypeName = declaration.ComponentTypeName,
                IsOrphaned = false,
                IsBuiltIn = false,
                CurrentRevision = 1,
            };

            fresh.Revisions.Add(new BlockTypeRevision
            {
                RevisionNumber = 1,
                PropertySnapshotJson = ContentSchemaSnapshot.WriteSlots([]),
                Notes = $"Created from code by {declaration.ComponentTypeName}.",
            });

            context.BlockTypes.Add(fresh);
            created.Add(key);
        }

        foreach (var blockType in stored)
        {
            // A built-in is declared by the system, not by a scanned attribute. Marking it orphaned
            // because no component carries the attribute would degrade the health check on a fresh
            // install, which is the opposite of what the flag is for.
            if (blockType.IsBuiltIn || declared.ContainsKey(blockType.Key) || blockType.IsOrphaned) continue;

            blockType.IsOrphaned = true;
            orphaned.Add(blockType.Key);
        }

        return (created, adopted, orphaned);
    }

    /// <summary>
    /// Logs what the pass did.
    /// </summary>
    /// <remarks>
    /// Orphans are a warning at any level, because they are how a bad deployment becomes visible
    /// (spec section 8.4) and the health check reads the same state. Everything else is information
    /// and is written once, so a startup that changed nothing produces one quiet line rather than a
    /// diff nobody reads.
    /// </remarks>
    private void Report(ReconciliationReport report)
    {
        if (!report.HasChanges)
        {
            logger.LogInformation("Structure reconciliation: code and database already agree.");

            return;
        }

        logger.LogInformation(
            "Structure reconciliation: templates created [{TemplatesCreated}], adopted " +
            "[{TemplatesAdopted}]; block types created [{BlockTypesCreated}], adopted " +
            "[{BlockTypesAdopted}].",
            string.Join(", ", report.TemplatesCreated),
            string.Join(", ", report.TemplatesAdopted),
            string.Join(", ", report.BlockTypesCreated),
            string.Join(", ", report.BlockTypesAdopted));

        if (report.TemplatesOrphaned.Count > 0 || report.BlockTypesOrphaned.Count > 0)
        {
            logger.LogWarning(
                "Structure reconciliation orphaned templates [{TemplatesOrphaned}] and block types " +
                "[{BlockTypesOrphaned}]: the database holds them but no deployed component declares " +
                "them. Nothing was deleted, and existing content renders a logged fallback.",
                string.Join(", ", report.TemplatesOrphaned),
                string.Join(", ", report.BlockTypesOrphaned));
        }
    }
}

/// <summary>Formats the component name stored against a template or block type.</summary>
internal static class ComponentTypeNames
{
    /// <summary>
    /// Names a component type in the form the <c>ComponentTypeName</c> column stores.
    /// </summary>
    /// <param name="type">The component type.</param>
    /// <returns><c>Namespace.Type, Assembly</c>.</returns>
    /// <remarks>
    /// Deliberately not the full assembly-qualified name: that carries a version, culture, and
    /// public key token, so every rebuild with a bumped version would rewrite the column on every
    /// row and make an audit log of structural changes unreadable.
    /// </remarks>
    public static string Of(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";
}
