using System.Text.Json;

namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// The one message the outbox carries in v1: evict these cache tags (spec section 16.3).
/// </summary>
/// <param name="Tags">The tags to evict, as <c>CacheTags</c> spells them.</param>
/// <remarks>
/// A message rather than a direct call, because the moment it has to survive is the one where the
/// process does not: enqueued inside the publish's own transaction, it commits with the publish or
/// not at all, and any instance can dispatch it afterwards (task P8-09).
/// <para>
/// Tags rather than entity ids. What the render actually depended on was recorded as tags while it
/// rendered, so the eviction side needs no model of what a page contains — which is what keeps the
/// two halves from disagreeing about, say, whether a page that embeds a reusable item depends on it
/// (spec section 16.2).
/// </para>
/// </remarks>
public sealed record CacheInvalidationMessage(IReadOnlyList<string> Tags)
{
    /// <summary>The <c>OutboxMessage.Type</c> value these are stored under.</summary>
    public const string MessageType = "cms.cache.invalidate";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes the message for storage.</summary>
    /// <returns>The payload JSON.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Reads a stored payload back, or returns null when it cannot be read.
    /// </summary>
    /// <param name="json">The stored payload.</param>
    /// <returns>The message, or null.</returns>
    /// <remarks>
    /// Null rather than an exception. A malformed row is a row that will never dispatch, and
    /// throwing here would stop every message behind it — turning one bad payload into a site that
    /// never invalidates anything again.
    /// </remarks>
    public static CacheInvalidationMessage? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<CacheInvalidationMessage>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
