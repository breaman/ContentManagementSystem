using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Search;

/// <inheritdoc cref="ISearchIndexQueue" />
/// <param name="context">
/// The caller's database context — the same instance, and therefore the same transaction.
/// </param>
/// <param name="clock">Source of the enqueue timestamp.</param>
public sealed class SearchIndexQueue(ApplicationDbContext context, TimeProvider clock) : ISearchIndexQueue
{
    /// <inheritdoc />
    public void EnqueuePage(int pageId) => Enqueue(SearchEntityKind.Page, [pageId]);

    /// <inheritdoc />
    public void EnqueuePages(IEnumerable<int> pageIds) => Enqueue(SearchEntityKind.Page, pageIds);

    /// <inheritdoc />
    public void EnqueueMedia(int mediaItemId) => Enqueue(SearchEntityKind.Media, [mediaItemId]);

    /// <inheritdoc />
    public void EnqueueReusable(int reusableContentId) =>
        Enqueue(SearchEntityKind.Reusable, [reusableContentId]);

    /// <inheritdoc />
    public void Enqueue(SearchEntityKind kind, IEnumerable<int> entityIds)
    {
        ArgumentNullException.ThrowIfNull(entityIds);

        var ids = entityIds.Where(id => id > 0).Distinct().ToArray();

        if (ids.Length == 0) return;

        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = SearchIndexMessage.MessageType,
            PayloadJson = new SearchIndexMessage(kind, ids).ToJson(),
            CreatedOn = clock.GetUtcNow(),
        });
    }
}
