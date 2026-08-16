using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Content;

namespace ContentManagementSystem.Client.Components.Admin.Pickers;

/// <summary>Member names of a stored link that are not its destination.</summary>
public static class StoredLinkMembers
{
    /// <summary>The field type discriminator every stored value carries.</summary>
    public const string Type = ContentPayloadMembers.Type;
}

/// <summary>
/// A stored <c>link</c> value, read into the fields a picker fills in (spec section 7.1).
/// </summary>
/// <param name="Kind">Which destination member applies.</param>
/// <param name="PageId">The page a <c>page</c> link points at.</param>
/// <param name="MediaId">The item a <c>media</c> link points at.</param>
/// <param name="Url">The address an <c>external</c> link points at.</param>
/// <param name="Email">The address an <c>email</c> link opens.</param>
/// <param name="Anchor">The fragment an <c>anchor</c> link jumps to.</param>
/// <param name="Text">What the link reads as.</param>
/// <param name="Target">The browsing context it opens in.</param>
/// <remarks>
/// A read-only projection, deliberately not a round-trip model. Writing goes back through
/// <see cref="JsonObject"/> so that members this build does not recognise survive being edited —
/// the same rule every field editor follows. Reading is where a record is worth having, because the
/// alternative is six null checks at every use site.
/// </remarks>
public sealed record StoredLink(
    string? Kind,
    int? PageId,
    int? MediaId,
    string? Url,
    string? Email,
    string? Anchor,
    string? Text,
    string? Target)
{
    /// <summary>Reads a stored link, tolerating anything unreadable.</summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <returns>The link, or null when there is nothing readable.</returns>
    public static StoredLink? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        JsonObject? stored;

        try
        {
            stored = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (stored is null) return null;

        return new StoredLink(
            TextMember(stored, LinkKinds.KindMember),
            Number(stored, LinkKinds.PageIdMember),
            Number(stored, LinkKinds.MediaIdMember),
            TextMember(stored, LinkKinds.UrlMember),
            TextMember(stored, LinkKinds.EmailMember),
            TextMember(stored, LinkKinds.AnchorMember),
            TextMember(stored, LinkKinds.TextMember),
            TextMember(stored, LinkKinds.TargetMember));
    }

    /// <summary>
    /// A one-line description of where the link goes, for a control that is not the picker.
    /// </summary>
    /// <param name="pageTitle">Title of the target page, when the caller has resolved one.</param>
    /// <param name="mediaName">Name of the target file, when the caller has resolved one.</param>
    /// <returns>Text an author can recognise the destination by.</returns>
    /// <remarks>
    /// An internal link's destination is an id, which means nothing on its own; resolving it is a
    /// request, so the caller does it and passes the answer in rather than this type reaching for a
    /// client. Falling back to the id is still better than showing nothing — it is what an editor
    /// would quote in a ticket.
    /// </remarks>
    public string Describe(string? pageTitle = null, string? mediaName = null) => Kind switch
    {
        LinkKinds.Page => pageTitle is { Length: > 0 } ? pageTitle : $"Page {PageId}",
        LinkKinds.Media => mediaName is { Length: > 0 } ? mediaName : $"File {MediaId}",
        LinkKinds.External => Url ?? "an address that is not set",
        LinkKinds.Email => Email ?? "an address that is not set",
        LinkKinds.Anchor => Anchor is { Length: > 0 } anchor ? $"#{anchor}" : "an anchor that is not set",
        _ => "somewhere this build does not recognise",
    };

    private static string? TextMember(JsonObject stored, string member) =>
        stored[member]?.GetValueKind() is JsonValueKind.String ? stored[member]!.GetValue<string>() : null;

    private static int? Number(JsonObject stored, string member) =>
        stored[member]?.GetValueKind() is JsonValueKind.Number ? stored[member]!.GetValue<int>() : null;
}
