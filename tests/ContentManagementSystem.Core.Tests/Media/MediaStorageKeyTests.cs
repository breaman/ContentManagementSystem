using System.Security.Cryptography;
using System.Text;

using ContentManagementSystem.Core.Media.Stores;

using Microsoft.Extensions.Logging.Abstractions;

namespace ContentManagementSystem.Core.Tests.Media;

/// <summary>
/// Storage keys and the filesystem store's path handling (tasks P5-03 and P5-29,
/// spec section 13.2).
/// </summary>
/// <remarks>
/// The traversal probes are the point of this file. The keys the application generates could never
/// contain a <c>..</c>, so these assert the property the store is entitled to rely on rather than a
/// scenario anybody expects — the day a key reaches the database by some other route, this is what
/// keeps it inside the root.
/// </remarks>
public class MediaStorageKeyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"cms-media-tests-{Guid.NewGuid():N}", "media");

    private static readonly byte[] Hash = SHA256.HashData("a photograph"u8.ToArray());

    public void Dispose()
    {
        var parent = Directory.GetParent(_root)?.FullName;

        if (parent is not null && Directory.Exists(parent)) Directory.Delete(parent, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AnOriginalKeyIsContentAddressedAndFannedOut()
    {
        var key = MediaStorageKeys.ForOriginal(Hash, ".jpg");
        var hex = Convert.ToHexStringLower(Hash);

        key.Should().Be($"originals/{hex[..2]}/{hex[2..4]}/{hex}.jpg");
    }

    [Fact]
    public void IdenticalBytesProduceAnIdenticalKey() =>
        // Deduplication as a property of the naming scheme rather than of a check somebody could
        // skip (spec section 13.1).
        MediaStorageKeys.ForOriginal(Hash, ".jpg")
            .Should().Be(MediaStorageKeys.ForOriginal(SHA256.HashData("a photograph"u8.ToArray()), ".JPG"));

    [Fact]
    public void AQuarantineKeyCarriesNoExtension() =>
        // Nothing should ever hand a quarantined file to a program that opens it by extension.
        Path.GetExtension(MediaStorageKeys.ForQuarantine(Hash)).Should().BeEmpty();

    [Theory]
    [InlineData("originals/ab/cd/abcd.jpg")]
    [InlineData("renditions/812/9c4b.webp")]
    [InlineData("quarantine/ab/abcd")]
    public void AGeneratedKeyIsValid(string key) =>
        MediaStorageKeys.IsValid(key).Should().BeTrue();

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("originals/../../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("originals//abcd.jpg")]
    [InlineData("originals/./abcd.jpg")]
    [InlineData("originals\\..\\abcd.jpg")]
    [InlineData("C:/Windows/System32/config")]
    [InlineData("originals/ab/cd/ABCD.JPG")]
    [InlineData("originals/ab/cd/abcd.jpg/")]
    [InlineData("originals/ab/cd/ab cd.jpg")]
    [InlineData("originals/ab/cd/abcd.jpg\u0000")]
    [InlineData("")]
    public void AKeyThisApplicationCouldNotHaveGeneratedIsRefused(string key) =>
        MediaStorageKeys.IsValid(key).Should().BeFalse();

    [Fact]
    public void AKeyLongerThanTheColumnIsRefused() =>
        MediaStorageKeys.IsValid(new string('a', MediaStorageKeys.MaxLength + 1)).Should().BeFalse();

    [Fact]
    public void TheFileSystemStoreRefusesARootInsideWwwroot()
    {
        var act = () => new FileSystemMediaStore(
            Path.Combine(Path.GetTempPath(), "site", "wwwroot", "media"),
            NullLogger<FileSystemMediaStore>.Instance);

        // Serving uploads as static files would bypass content-type pinning, nosniff, and
        // authorization (spec section 20.7), so this is a startup failure rather than a warning.
        act.Should().Throw<ArgumentException>().WithMessage("*wwwroot*");
    }

    [Fact]
    public async Task TheFileSystemStoreRoundTripsContent()
    {
        var store = new FileSystemMediaStore(_root, NullLogger<FileSystemMediaStore>.Instance);
        var key = MediaStorageKeys.ForOriginal(Hash, ".jpg");

        using var content = new MemoryStream("bytes"u8.ToArray());

        var result = await store.PutAsync(key, content, "image/jpeg", TestContext.Current.CancellationToken);

        result.SizeBytes.Should().Be(5);

        (await store.ExistsAsync(key, TestContext.Current.CancellationToken)).Should().BeTrue();

        await using (var read = await store.GetAsync(key, TestContext.Current.CancellationToken))
        {
            read.Should().NotBeNull();

            using var buffer = new MemoryStream();

            await read!.CopyToAsync(buffer, TestContext.Current.CancellationToken);

            Encoding.UTF8.GetString(buffer.ToArray()).Should().Be("bytes");
        }

        await store.DeleteAsync(key, TestContext.Current.CancellationToken);

        (await store.ExistsAsync(key, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task DeletingSomethingThatIsNotThereSucceeds()
    {
        var store = new FileSystemMediaStore(_root, NullLogger<FileSystemMediaStore>.Instance);

        var act = async () => await store.DeleteAsync(
            MediaStorageKeys.ForOriginal(Hash, ".png"), TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("originals/../../escape.jpg")]
    public async Task TheFileSystemStoreRefusesATraversalProbe(string key)
    {
        var store = new FileSystemMediaStore(_root, NullLogger<FileSystemMediaStore>.Instance);

        var act = async () => await store.ExistsAsync(key, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void TheFileSystemStoreHasNoPublicUrl() =>
        // A file on the application's own disk has no URL a client could fetch, and inventing one
        // would mean exposing the root through static file middleware.
        new FileSystemMediaStore(_root, NullLogger<FileSystemMediaStore>.Instance)
            .GetPublicUrl("originals/ab/cd/abcd.jpg").Should().BeNull();
}
