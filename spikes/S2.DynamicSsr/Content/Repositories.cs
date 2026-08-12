namespace S2.DynamicSsr.Content;

public sealed record MediaItem(long Id, string Url, string AltText, int Width, int Height);

/// <summary>Stands in for the media library. Id 404 is deliberately absent.</summary>
public sealed class MediaRepository
{
    private readonly Dictionary<long, MediaItem> _items = new()
    {
        [812] = new MediaItem(812, "/media/812/1280x720/cover/hero.webp", "A team at a whiteboard", 1280, 720),
        [913] = new MediaItem(913, "/media/913/320x320/crop/portrait.webp", "Portrait of the speaker", 320, 320),
    };

    public bool TryGet(long id, out MediaItem item) => _items.TryGetValue(id, out item!);
}

/// <summary>Stands in for <c>ReusableContentResolver</c> (P4-06). Id 9 exists but is unpublished.</summary>
public sealed class ReusableContentRepository
{
    private readonly Dictionary<long, string?> _published = new()
    {
        [3] = "Shared footer — © Contoso",
        [9] = null,
    };

    public async Task<string?> GetPublishedAsync(long id)
    {
        // A real resolver awaits the database; the await is the point of this method.
        await Task.Yield();

        return _published.TryGetValue(id, out var body) ? body : null;
    }
}
