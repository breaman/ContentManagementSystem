using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Caching;

/// <inheritdoc cref="ICacheInvalidationQueue" />
/// <param name="context">
/// The caller's database context — the same instance, and therefore the same transaction.
/// </param>
/// <param name="clock">Source of the enqueue timestamp.</param>
public sealed class CacheInvalidationQueue(ApplicationDbContext context, TimeProvider clock)
    : ICacheInvalidationQueue
{
    /// <inheritdoc />
    public Task EnqueuePageAsync(int pageId, CancellationToken cancellationToken = default) =>
        EnqueuePagesAsync([pageId], cancellationToken);

    /// <inheritdoc />
    public async Task EnqueuePagesAsync(
        IEnumerable<int> pageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageIds);

        var ids = pageIds.Where(id => id > 0).Distinct().ToArray();

        if (ids.Length == 0) return;

        var tags = new List<string>(ids.Length + 4);

        foreach (var id in ids)
        {
            tags.Add(CacheTags.Page(id));
        }

        // Publish state is what tree navigation is filtered by, so any of these changes it.
        tags.Add(CacheTags.Navigation(CacheTags.StructuralMenuKey));

        var menuKeys = await context.NavigationItems
            .AsNoTracking()
            .Where(item => item.PageId != null && ids.Contains(item.PageId.Value))
            .Select(item => item.Menu.Key)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var key in menuKeys)
        {
            tags.Add(CacheTags.Navigation(key));
        }

        Enqueue(tags);
    }

    /// <inheritdoc />
    public void EnqueueReusable(int reusableContentId) =>
        Enqueue([CacheTags.Reusable(reusableContentId)]);

    /// <inheritdoc />
    public void EnqueueMedia(int mediaItemId) => Enqueue([CacheTags.Media(mediaItemId)]);

    /// <inheritdoc />
    public void EnqueueTemplate(int templateId) => Enqueue([CacheTags.Template(templateId)]);

    /// <inheritdoc />
    public void Enqueue(IReadOnlyCollection<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var distinct = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinct.Length == 0) return;

        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = CacheInvalidationMessage.MessageType,
            PayloadJson = new CacheInvalidationMessage(distinct).ToJson(),
            CreatedOn = clock.GetUtcNow(),
        });
    }
}
