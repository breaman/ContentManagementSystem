using System.Text.Json;
using System.Text.Json.Serialization;

using ContentManagementSystem.Data.Models.Cms;

namespace ContentManagementSystem.Core.Search;

/// <summary>
/// The outbox's second message: rebuild the index entries for these things (task P8-18).
/// </summary>
/// <param name="Kind">What sort of thing the ids name.</param>
/// <param name="EntityIds">The things, in any order.</param>
/// <remarks>
/// Ids rather than the text to index, which is the decision that makes the message safe to apply
/// late. A payload carrying the extracted text would index what the page said when it was saved; an
/// id indexes what it says when the indexer runs, so a message dispatched after three more saves
/// produces one correct document rather than three stale ones.
/// <para>
/// Several ids per message because a move or a bulk operation touches a subtree at once, and one
/// row per page would put thousands of rows through the poller for one drag.
/// </para>
/// </remarks>
public sealed record SearchIndexMessage(SearchEntityKind Kind, IReadOnlyList<int> EntityIds)
{
    /// <summary>The <c>OutboxMessage.Type</c> value these are stored under.</summary>
    public const string MessageType = "cms.search.index";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // The kind is written by name. These rows are read by a person asking why something is not
        // findable, and "Page" answers that question where "0" starts another one.
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes the message for storage.</summary>
    /// <returns>The payload JSON.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Reads a stored payload back, or returns null when it cannot be read.
    /// </summary>
    /// <param name="json">The stored payload.</param>
    /// <returns>The message, or null.</returns>
    /// <remarks>
    /// Null rather than an exception, for the reason the invalidation message gives: a malformed row
    /// that throws stops every message behind it.
    /// </remarks>
    public static SearchIndexMessage? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<SearchIndexMessage>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
