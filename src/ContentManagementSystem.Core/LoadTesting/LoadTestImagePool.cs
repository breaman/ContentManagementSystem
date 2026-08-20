using System.Security.Cryptography;

using ContentManagementSystem.Core.Media.Stores;

using SkiaSharp;

namespace ContentManagementSystem.Core.LoadTesting;

/// <summary>One generated image, and everything a media row needs to describe it.</summary>
/// <param name="StorageKey">Where the bytes are in the media store.</param>
/// <param name="Sha256">Hash of the bytes actually written.</param>
/// <param name="Width">Pixel width.</param>
/// <param name="Height">Pixel height.</param>
/// <param name="SizeBytes">Encoded size.</param>
/// <param name="ContentType">Always <c>image/jpeg</c> today.</param>
/// <param name="Extension">File extension including the dot.</param>
public sealed record LoadTestImage(
    string StorageKey,
    byte[] Sha256,
    int Width,
    int Height,
    long SizeBytes,
    string ContentType,
    string Extension);

/// <summary>
/// Generates the handful of real images the seeded media rows point at.
/// </summary>
/// <remarks>
/// Sizes vary from a phone-camera original down to a thumbnail-sized logo, because rendition cost
/// is a function of the source: a load test whose every image is 800 px wide would never exercise
/// the resize path NFR-8 is about. The content is drawn rather than photographed so the repository
/// carries no binary fixtures, and each image is drawn differently so that no two share a hash —
/// identical bytes would collide on the media store's content-addressed key.
/// </remarks>
internal static class LoadTestImagePool
{
    /// <summary>The source sizes, cycled through as images are generated.</summary>
    private static readonly (int Width, int Height)[] Sizes =
    [
        (4000, 3000),
        (3000, 2000),
        (2400, 1600),
        (1920, 1080),
        (1600, 1200),
        (1200, 800),
        (960, 640),
        (800, 600),
    ];

    /// <summary>
    /// Draws the pool and writes any blob the store does not already hold.
    /// </summary>
    /// <param name="store">Where the bytes go.</param>
    /// <param name="count">How many distinct images to produce.</param>
    /// <param name="cancellationToken">Token observed while writing.</param>
    /// <returns>The pool, in generation order.</returns>
    public static async Task<IReadOnlyList<LoadTestImage>> CreateAsync(
        IMediaStore store,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var pool = new List<LoadTestImage>(count);

        for (var index = 0; index < count; index++)
        {
            var (width, height) = Sizes[index % Sizes.Length];
            var bytes = Draw(width, height, index);
            var hash = SHA256.HashData(bytes);
            var key = MediaStorageKeys.ForOriginal(hash, ".jpg");

            // Re-running the seeder against a store that already holds the pool rewrites nothing.
            // The key is the hash, so a blob that is there is by definition the right blob.
            if (!await store.ExistsAsync(key, cancellationToken))
            {
                using var content = new MemoryStream(bytes, writable: false);

                await store.PutAsync(key, content, "image/jpeg", cancellationToken);
            }

            pool.Add(new LoadTestImage(key, hash, width, height, bytes.Length, "image/jpeg", ".jpg"));
        }

        return pool;
    }

    /// <summary>
    /// Draws one image. Deterministic in <paramref name="index"/>, so the pool is reproducible.
    /// </summary>
    private static byte[] Draw(int width, int height, int index)
    {
        var random = new Random(index * 7919);

        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            using var background = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(width, height),
                    [Colour(random), Colour(random)],
                    null,
                    SKShaderTileMode.Clamp),
            };

            canvas.DrawRect(0, 0, width, height, background);

            // Detail, not decoration: a flat gradient compresses to almost nothing, and a JPEG of a
            // few kilobytes would make every measurement of transfer and re-encode cost meaningless.
            for (var shape = 0; shape < 60; shape++)
            {
                using var paint = new SKPaint { Color = Colour(random), IsAntialias = true };

                var radius = random.Next(width / 40, Math.Max(width / 8, width / 40 + 1));

                canvas.DrawCircle(random.Next(width), random.Next(height), radius, paint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);

        return data.ToArray();
    }

    private static SKColor Colour(Random random) =>
        new((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 220);
}
