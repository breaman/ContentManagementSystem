using ContentManagementSystem.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ContentManagementSystem.Server.HealthChecks;

/// <summary>
/// The <c>cms-templates</c> health check (task P1-27, spec section 24.2).
/// </summary>
/// <remarks>
/// Reports <see cref="HealthStatus.Degraded"/> — never unhealthy — when the database holds a
/// template or block type that no deployed component declares. That is the whole point of the check
/// per spec section 8.4: a bad deployment must be <em>visible</em> without taking the site down,
/// because pages on an orphaned template still render a logged fallback and everything else on the
/// site is fine.
/// <para>
/// <b>What this check will tighten in Phase 2.</b> Spec section 24.2 words the condition as "any
/// <c>IsOrphaned</c> template has non-deleted pages", and that is the condition to end at — an
/// orphan nobody uses is a housekeeping matter, not an operational one. There is no page table to
/// ask until <c>P2-01</c>, so today the check fires on orphan existence alone. It is the broader
/// condition, and it is the honest one meanwhile: in this phase nothing can tell you whether anyone
/// depends on the orphan.
/// </para>
/// <para>
/// Note the interaction with <c>P1-21</c>: a template created in the backoffice is orphaned by
/// design until code claims its key, so a developer building a content model ahead of its markup
/// will see this degrade. That is not a false positive. Such a template cannot render, and the
/// check saying so is exactly the signal spec section 8.4 asks for.
/// </para>
/// </remarks>
/// <param name="context">The application database context.</param>
public sealed class CmsTemplatesHealthCheck(ApplicationDbContext context) : IHealthCheck
{
    /// <summary>The name this check is registered under.</summary>
    public const string Name = "cms-templates";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        var templates = await context.Templates
            .AsNoTracking()
            .Where(template => template.IsOrphaned)
            .Select(template => template.Key)
            .OrderBy(key => key)
            .ToListAsync(cancellationToken);

        // Built-ins are excluded for the reason the reconciler does not orphan them: they are
        // declared by the system rather than by a scanned attribute, so their having no component
        // attribute says nothing about the deployment.
        var blockTypes = await context.BlockTypes
            .AsNoTracking()
            .Where(blockType => blockType.IsOrphaned && !blockType.IsBuiltIn)
            .Select(blockType => blockType.Key)
            .OrderBy(key => key)
            .ToListAsync(cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["orphanedTemplates"] = templates,
            ["orphanedBlockTypes"] = blockTypes,
        };

        if (templates.Count == 0 && blockTypes.Count == 0)
        {
            return HealthCheckResult.Healthy(
                "Every template and block type in the database has a deployed component.",
                data);
        }

        return HealthCheckResult.Degraded(
            $"No deployed component declares {Describe(templates, "template")}" +
            $"{(templates.Count > 0 && blockTypes.Count > 0 ? " and " : string.Empty)}" +
            $"{Describe(blockTypes, "block type")}. Existing content renders a logged fallback; " +
            "nothing was deleted.",
            exception: null,
            data);
    }

    /// <summary>Names the offenders, since a count alone tells an operator nothing actionable.</summary>
    private static string Describe(IReadOnlyList<string> keys, string noun) =>
        keys.Count switch
        {
            0 => string.Empty,
            1 => $"{noun} '{keys[0]}'",
            _ => $"{noun}s {string.Join(", ", keys.Select(key => $"'{key}'"))}",
        };
}
