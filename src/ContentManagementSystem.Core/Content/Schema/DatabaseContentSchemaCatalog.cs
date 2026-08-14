using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Content.Schema;

/// <summary>
/// The <see cref="IContentSchemaCatalog"/> a deployment actually runs on: revision snapshots read
/// from the database and cached for the life of the process.
/// </summary>
/// <param name="context">The application database context.</param>
/// <param name="cache">Process-wide cache of the revisions already read.</param>
/// <param name="logger">Log for revisions that cannot be found or cannot be parsed.</param>
/// <remarks>
/// This is the implementation task <c>P1-30</c> was waiting for. <c>AddCmsContent()</c> could not be
/// called until it existed, because registering an empty catalog would let a deployment start up
/// validating every payload against nothing and reporting success.
/// <para>
/// <strong>A cache miss reads synchronously.</strong> The interface is deliberately synchronous —
/// <c>ContentSchemaValidator</c> resolves a block type revision in the middle of a walk that is
/// itself on a hot path, and threading a task through every step of it would buy nothing, since
/// after the first request the answer is always in memory. The blocking call therefore happens at
/// most once per revision per process. Making the interface async instead would put an await on the
/// inner loop of every payload validation to serve a cache that hits essentially always.
/// </para>
/// <para>
/// Nothing here throws. A revision that cannot be found or cannot be parsed is reported as unknown,
/// which the walk turns into a diagnostic (an error for a template, a warning for a block type) and
/// delivery turns into a degraded render (spec section 15.3). A page whose structure has gone
/// missing has to be openable, or an editor has no way to repair it.
/// </para>
/// </remarks>
public sealed class DatabaseContentSchemaCatalog(
    ApplicationDbContext context,
    ContentSchemaCache cache,
    ILogger<DatabaseContentSchemaCatalog> logger) : IContentSchemaCatalog
{
    /// <inheritdoc />
    public bool TryGetTemplate(
        string templateKey,
        int revisionNumber,
        [NotNullWhen(true)] out ContentSchema? schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        schema = cache.FindTemplate(templateKey, revisionNumber);

        if (schema is not null) return true;

        var snapshot = context.TemplateRevisions
            .AsNoTracking()
            .Where(revision => revision.Template.Key == templateKey &&
                revision.RevisionNumber == revisionNumber)
            .Select(revision => revision.ZoneSnapshotJson)
            .FirstOrDefault();

        if (snapshot is null)
        {
            logger.LogWarning(
                "Template '{TemplateKey}' has no revision {Revision} in this deployment.",
                templateKey,
                revisionNumber);

            return false;
        }

        try
        {
            schema = ContentSchemaSnapshot.ReadTemplate(templateKey, revisionNumber, snapshot);
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception,
                "Revision {Revision} of template '{TemplateKey}' has an unreadable zone snapshot.",
                revisionNumber,
                templateKey);

            return false;
        }

        cache.Add(schema);

        return true;
    }

    /// <inheritdoc />
    public bool TryGetBlockType(
        string blockTypeKey,
        int? revisionNumber,
        [NotNullWhen(true)] out BlockTypeSchema? schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockTypeKey);

        schema = null;

        // A block carrying no revision was written before revisions were captured, and falls back to
        // whatever the block type looks like now rather than being treated as unknown — the same
        // rule the in-memory catalog follows, asked of the current row instead of the loaded set.
        var revision = revisionNumber ?? context.BlockTypes
            .AsNoTracking()
            .Where(blockType => blockType.Key == blockTypeKey)
            .Select(blockType => (int?)blockType.CurrentRevision)
            .FirstOrDefault();

        if (revision is null) return false;

        schema = cache.FindBlockType(blockTypeKey, revision.Value);

        if (schema is not null) return true;

        var snapshot = context.BlockTypeRevisions
            .AsNoTracking()
            .Where(candidate => candidate.BlockType.Key == blockTypeKey &&
                candidate.RevisionNumber == revision.Value)
            .Select(candidate => candidate.PropertySnapshotJson)
            .FirstOrDefault();

        if (snapshot is null)
        {
            logger.LogWarning(
                "Block type '{BlockTypeKey}' has no revision {Revision} in this deployment.",
                blockTypeKey,
                revision.Value);

            return false;
        }

        try
        {
            schema = ContentSchemaSnapshot.ReadBlockType(blockTypeKey, revision.Value, snapshot);
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception,
                "Revision {Revision} of block type '{BlockTypeKey}' has an unreadable property snapshot.",
                revision.Value,
                blockTypeKey);

            return false;
        }

        cache.Add(schema);

        return true;
    }

    /// <summary>
    /// Loads the schema of a template's current revision, for adopting a newer one.
    /// </summary>
    /// <param name="templateId">Identity of the template.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The schema, or null when the template is gone or its snapshot is unreadable.</returns>
    /// <remarks>
    /// The one lookup that is by identity and current revision rather than by the pair a payload
    /// names: publishing checks a draft against the structure as it stands <em>now</em>, so that a
    /// zone made required since the draft was written blocks the publish instead of reaching the
    /// public site empty (spec section 8.5).
    /// </remarks>
    public async Task<ContentSchema?> GetCurrentAsync(int templateId, CancellationToken cancellationToken)
    {
        var current = await context.Templates
            .AsNoTracking()
            .Where(template => template.Id == templateId)
            .Select(template => new { template.Key, template.CurrentRevision })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null) return null;

        return TryGetTemplate(current.Key, current.CurrentRevision, out var schema) ? schema : null;
    }
}
