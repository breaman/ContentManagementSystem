using System.Security.Cryptography;

using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.TestSupport;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Data.Tests.Cms;

/// <summary>
/// The media schema's constraints (tasks P5-01 and P5-02, spec section 23.3).
/// </summary>
/// <remarks>
/// Against a real SQL Server, because every property asserted here is one the in-memory provider
/// does not have: a filtered unique index, a cascade, and a global query filter interacting with a
/// soft delete.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class MediaSchemaTests(SqlServerFixture fixture)
{
    private static MediaItem Image(byte[] hash, string name = "photo.jpg") => new()
    {
        FileName = name,
        OriginalFileName = name,
        ContentType = "image/jpeg",
        SizeBytes = 1024,
        Sha256 = hash,
        StorageKey = $"originals/ab/cd/{Convert.ToHexStringLower(hash)}.jpg",
        MediaKind = MediaKind.Image,
        Width = 800,
        Height = 600,
        AltText = "A photograph",
    };

    private static byte[] Hash(string content) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));

    [Test]
    public async Task IdenticalBytesCannotProduceASecondLiveItem()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var hash = Hash("the same photograph");

        context.MediaItems.Add(Image(hash, "first.jpg"));
        await context.SaveChangesAsync(cancellationToken);

        context.MediaItems.Add(Image(hash, "second.jpg"));

        // Deduplication as a constraint rather than as a check the upload pipeline could skip or
        // lose a race against (spec section 13.3 step 7).
        var save = async () => await context.SaveChangesAsync(cancellationToken);

        await save.Should().ThrowAsync<DbUpdateException>()
            .WithInnerException<DbUpdateException, SqlException>()
            .Where(exception => exception.Message.Contains("IX_MediaItems_Sha256_Live"));
    }

    [Test]
    public async Task AnItemInTheRecycleBinDoesNotBlockReUploadingTheSameFile()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var hash = Hash("a deleted photograph");
        var first = Image(hash, "first.jpg");

        context.MediaItems.Add(first);
        await context.SaveChangesAsync(cancellationToken);

        first.IsDeleted = true;
        first.DeletedOn = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        context.MediaItems.Add(Image(hash, "again.jpg"));

        // The index is filtered on IsDeleted for exactly this: a plain unique index would let one
        // recycled item permanently block re-uploading its own file, which is the standard trap in
        // this schema (spec section 23.3).
        var save = async () => await context.SaveChangesAsync(cancellationToken);

        await save.Should().NotThrowAsync();
    }

    [Test]
    public async Task ASoftDeletedItemIsInvisibleToOrdinaryQueries()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var item = Image(Hash("hidden"));

        context.MediaItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);

        item.IsDeleted = true;
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        // What makes "deleting an image stops it being served" true without the delivery endpoint
        // having to remember to ask (spec section 23.5).
        (await context.MediaItems.AnyAsync(cancellationToken)).Should().BeFalse();
        (await context.MediaItems.IgnoreQueryFilters().AnyAsync(cancellationToken)).Should().BeTrue();
    }

    [Test]
    public async Task RenditionsGoWithTheItemTheyDeriveFrom()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var item = Image(Hash("with renditions"));

        context.MediaItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);

        context.MediaRenditions.Add(new MediaRendition
        {
            MediaItemId = item.Id,
            SpecHash = Hash("spec"),
            Spec = "812|1280x720|crop|webp|82|v0||",
            Width = 1280,
            Height = 720,
            Format = "webp",
            Quality = 82,
            SizeBytes = 42,
            StorageKey = "renditions/1/abc.webp",
            EditsVersion = 0,
            GeneratedOn = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        // Deleted with SQL rather than with Remove(), because Remove() on a soft-deletable entity is
        // rewritten into a flag update by SoftDeleteInterceptor — which is the safety net working, and
        // which means the cascade this test is about would never fire through the tracker. Permanent
        // deletion is its own operation, and it is the one this constraint backs (spec section 13.8).
        await context.Database.ExecuteSqlAsync(
            $"DELETE FROM MediaItems WHERE Id = {item.Id}", cancellationToken);

        // Cascade, unlike everywhere else in this schema: renditions are derived data with no
        // independent meaning, and permanent deletion only happens once nothing references the item.
        (await context.MediaRenditions.AnyAsync(cancellationToken)).Should().BeFalse();
    }

    [Test]
    public async Task OneSpecCannotBeRecordedTwiceForAnItem()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);
        var context = database.Context;

        var item = Image(Hash("one spec"));

        context.MediaItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);

        MediaRendition Rendition() => new()
        {
            MediaItemId = item.Id,
            SpecHash = Hash("the same spec"),
            Spec = "812|1280x720|crop|webp|82|v0||",
            Width = 1280,
            Height = 720,
            Format = "webp",
            Quality = 82,
            SizeBytes = 42,
            StorageKey = "renditions/1/abc.webp",
            EditsVersion = 0,
            GeneratedOn = DateTimeOffset.UtcNow,
        };

        context.MediaRenditions.Add(Rendition());
        await context.SaveChangesAsync(cancellationToken);

        context.MediaRenditions.Add(Rendition());

        // The backstop behind the per-key semaphore: if two instances raced past it, the second
        // insert fails rather than leaving two rows pointing at two copies of identical bytes.
        var save = async () => await context.SaveChangesAsync(cancellationToken);

        await save.Should().ThrowAsync<DbUpdateException>();
    }
}
