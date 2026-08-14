using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Shared.Content;

using Microsoft.EntityFrameworkCore;

using Entity = ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Rewrites a version's <c>ContentReference</c> rows from its payload.
/// </summary>
/// <remarks>
/// The relational half of the storage decision (spec section 6.2) applied at the point of a write.
/// The payload answers "what does this page contain"; these rows answer "which pages contain this",
/// and every guard built on them — where-used, the permanent-delete refusal, cache-tag derivation —
/// is only as good as the last time they were rebuilt.
/// </remarks>
public interface IContentReferenceProjector
{
    /// <summary>
    /// Replaces every reference row belonging to a version with the ones its payload now holds.
    /// </summary>
    /// <param name="sourceType">Kind of version being projected.</param>
    /// <param name="versionId">Identity of the version.</param>
    /// <param name="payload">The version's payload.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The number of rows written.</returns>
    /// <remarks>
    /// Delete-then-insert rather than a diff, deliberately. A reference set is small, it is
    /// rewritten inside the caller's transaction either way, and a diff has an error mode the
    /// wholesale replacement does not: a row that should have been removed and was not leaves a
    /// delete guard refusing forever, with nothing pointing at why.
    /// <para>
    /// Nothing here calls <c>SaveChanges</c>. Publishing writes these rows in the same transaction
    /// as the version they describe, and a reference index that committed while the publish beside
    /// it rolled back would describe content that does not exist (spec section 5.5).
    /// </para>
    /// </remarks>
    Task<int> ProjectAsync(
        Entity.ContentSourceType sourceType,
        int versionId,
        ContentPayload payload,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IContentReferenceProjector" />
/// <param name="context">The application database context.</param>
/// <param name="indexer">Walks the payload and reports every entity it points at.</param>
public sealed class ContentReferenceProjector(
    ApplicationDbContext context,
    IReferenceIndexer indexer) : IContentReferenceProjector
{
    /// <inheritdoc />
    public async Task<int> ProjectAsync(
        Entity.ContentSourceType sourceType,
        int versionId,
        ContentPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var existing = await context.ContentReferences
            .Where(row => row.SourceType == sourceType && row.SourceVersionId == versionId)
            .ToListAsync(cancellationToken);

        context.ContentReferences.RemoveRange(existing);

        var references = indexer.Extract(payload);
        var rows = new List<Entity.ContentReference>(references.Count);

        foreach (var reference in references)
        {
            var location = ReferencePath.Parse(reference.Path, payload);

            rows.Add(new Entity.ContentReference
            {
                SourceType = sourceType,
                SourceVersionId = versionId,
                TargetType = reference.TargetType,
                TargetId = reference.TargetId,
                ZoneKey = location.ZoneKey,
                BlockId = location.BlockId,
                PropertyKey = location.PropertyKey,
            });
        }

        context.ContentReferences.AddRange(rows);

        return rows.Count;
    }
}
