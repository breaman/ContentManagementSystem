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
/// <b>Templates are judged on use, block types on existence.</b> An orphaned template degrades only
/// once a non-deleted page is built on it, which is how spec section 24.2 words it and which is the
/// condition this narrowed to when <c>P2-01</c> supplied a page table to ask. An orphan nobody has
/// used is a housekeeping matter — and a template created in the backoffice ahead of its markup is
/// orphaned by design (<c>P1-21</c>), so degrading on that would train an operator to ignore the
/// check. A block type has no equivalent question available: nothing references one relationally,
/// because block instances name it from inside a payload, so its existence is the only signal there
/// is until the reference index can answer for it.
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
        // The page query filter excludes soft-deleted rows, so "has non-deleted pages" needs no
        // clause of its own here — a template whose only pages are in the recycle bin is not an
        // operational problem, and if they are restored this starts reporting again.
        var templates = await context.Templates
            .AsNoTracking()
            .Where(template => template.IsOrphaned && context.Pages.Any(page => page.TemplateId == template.Id))
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
