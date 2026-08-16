using System.Net;
using System.Net.Http.Json;
using System.Text;

using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

using Microsoft.Extensions.DependencyInjection;

using SkiaSharp;

using static ContentManagementSystem.Server.Tests.Api.Cms.PageApiClient;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// <c>/api/cms/v1/media</c> over real HTTP (tasks P5-23, P5-28, P5-31).
/// </summary>
/// <remarks>
/// The Core suite proves the pipeline's decisions in isolation — what the sniffer detects, what the
/// sanitizer removes, what the signer refuses. This suite proves the decisions survive the trip
/// through HTTP: that the refusals arrive as the right status with the right code, that the two body
/// limits are wired to the same options the service reads, and that the write endpoints the image
/// editor depends on move <c>EditsVersion</c>.
/// <para>
/// Uploads go through the real store the host registers, which under the test harness is the local
/// filesystem one rooted outside <c>wwwroot</c> — the same code path a deployment without a storage
/// account takes.
/// </para>
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class MediaApiTests(SqlServerFixture fixture) : IAsyncLifetime
{
    /// <summary>Route of the media collection.</summary>
    private const string Media = $"{CmsApiEndpoints.BasePath}/media";

    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task UploadingAJpegAnswers201WithItsDimensionsAndSize()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var response = await UploadAsync(client, Unique("photo") + ".jpg", Jpeg(800, 600), cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(cancellationToken));

        var upload = (await response.Content.ReadFromJsonAsync<MediaUploadResult>(cancellationToken))!;

        upload.Deduplicated.Should().BeFalse();
        upload.Item.Kind.Should().Be("Image");
        upload.Item.ContentType.Should().Be("image/jpeg");
        upload.Item.Width.Should().Be(800);
        upload.Item.Height.Should().Be(600);
        upload.Item.SizeBytes.Should().BeGreaterThan(0);
        upload.Item.AltText.Should().Be("A test photograph");
        upload.Item.EditsVersion.Should().Be(0);
        upload.Item.Edits.Should().BeNull("an item nobody has edited carries no edit document");

        response.Headers.Location!.ToString().Should().EndWith($"{Media}/{upload.Item.Id}");
    }

    /// <remarks>
    /// Acceptance criterion P5 #1. The stored original is fetched back through its own signed URL
    /// rather than read off the disk, because what matters is what the site would hand a visitor —
    /// a pipeline that stripped the metadata from a copy and stored the upload would pass any
    /// assertion made against the bytes it happened to keep in memory.
    /// </remarks>
    [Fact]
    public async Task APhotographsGpsCoordinatesAreGoneFromTheStoredOriginal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        // Orientation 6 is "rotate 90° clockwise", and the fixture carries GPS alongside it. Both
        // are EXIF, and the pipeline treats them differently on purpose: the orientation is baked
        // into the pixels and the whole block is then dropped.
        var source = TestImages.EncodeWithExif(800, 600, orientation: 6);

        ExifDirectories(source).Should().Contain(directory => directory is GpsDirectory,
            "the fixture has to carry what the test claims is removed");

        var response = await UploadAsync(client, Unique("holiday") + ".jpg", source, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var item = (await response.Content.ReadFromJsonAsync<MediaUploadResult>(cancellationToken))!.Item;

        // The rotation is in the pixels now, so the recorded size is the upright one.
        item.Width.Should().Be(600);
        item.Height.Should().Be(800);

        var signer = _factory.Services.GetRequiredService<IMediaUrlSigner>();

        using var anonymous = _factory.CreateClient();

        using var original = await anonymous.GetAsync(
            signer.BuildOriginalUrl(item.Id, item.EditsVersion, item.OriginalFileName),
            cancellationToken);

        original.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await original.Content.ReadAsByteArrayAsync(cancellationToken);

        ExifDirectories(stored).Should().NotContain(directory => directory is GpsDirectory,
            "a published photograph carrying the coordinates of a private address is a privacy incident");
    }

    /// <remarks>
    /// Acceptance criterion P5 #5, against the shipped default. Q7 is still open, and <c>Reject</c>
    /// is the safe reading of an unanswered question — answering it changes one line of
    /// configuration rather than any code.
    /// </remarks>
    [Fact]
    public async Task AnSvgUploadFollowsTheDeploymentsPolicyWhichDefaultsToRefusingIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var svg = Encoding.UTF8.GetBytes(
            """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""");

        var response = await UploadAsync(client, "logo.svg", svg, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Its own code rather than "extension not allowed", so the message can say the true thing:
        // the file is a valid SVG and this site does not take them, rather than implying a rename
        // would help.
        (await ProblemAsync(response, cancellationToken)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MediaCodes.SvgNotAllowed);
    }

    /// <remarks>Acceptance criterion P5 #6 — the half that says the original is never rewritten.</remarks>
    [Fact]
    public async Task EditingAnItemLeavesItsStoredOriginalByteForByteIdentical()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await UploadItemAsync(client, cancellationToken, seed: 83);

        var before = await OriginalBytesAsync(item, cancellationToken);

        var edited = await client.PutAsJsonAsync(
            $"{Media}/{item.Id}/edits",
            new SetMediaEditsRequest(new MediaEdits(Rotate: 180, Crop: new NormalizedRect(0, 0, 0.5, 0.5))),
            cancellationToken);

        edited.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterEdit = (await edited.Content.ReadFromJsonAsync<MediaDetail>(cancellationToken))!;

        // Fetched at the new version, because the old URL is deliberately retired by the edit.
        var after = await OriginalBytesAsync(afterEdit, cancellationToken);

        after.Should().Equal(before, "edits are a JSON document, never a rewrite of the uploaded file");
    }

    /// <remarks>Task P5-31, acceptance criterion P5 #2.</remarks>
    [Fact]
    public async Task ReuploadingIdenticalBytesReturnsTheExistingItemRatherThanCreatingASecond()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        // Identical pixels, deliberately different file names and folders. The hash is taken over
        // the normalized bytes, so neither difference should produce a second item.
        var bytes = Jpeg(640, 480, seed: 7);

        var first = await UploadAsync(client, Unique("first") + ".jpg", bytes, cancellationToken);
        var second = await UploadAsync(client, Unique("second") + ".jpg", bytes, cancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // 200 rather than 201, because nothing was created — and the flag is what lets the client
        // say "this is the file you already have" instead of silently filing a copy.
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var original = (await first.Content.ReadFromJsonAsync<MediaUploadResult>(cancellationToken))!;
        var duplicate = (await second.Content.ReadFromJsonAsync<MediaUploadResult>(cancellationToken))!;

        duplicate.Deduplicated.Should().BeTrue();
        duplicate.Item.Id.Should().Be(original.Item.Id);

        var listed = await client.GetFromJsonAsync<MediaListResult>(
            $"{Media}?q={original.Item.OriginalFileName}", cancellationToken);

        listed!.Total.Should().Be(1, "the second upload resolved to the first item");
    }

    /// <remarks>Acceptance criterion P5 #3 — the type-confusion refusal, over HTTP.</remarks>
    [Fact]
    public async Task AnHtmlFileRenamedJpgIsRefusedWithATypeMismatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var html = Encoding.UTF8.GetBytes("<html><body><script>alert(1)</script></body></html>");

        var response = await UploadAsync(client, "not-really.jpg", html, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await ProblemAsync(response, cancellationToken);

        problem.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MediaCodes.TypeMismatch);
    }

    /// <remarks>
    /// Task P5-28, acceptance criterion P5 #4. The guard reads the PNG header and refuses before a
    /// pixel is decoded, which is why a file that would allocate gigabytes costs a few kilobytes to
    /// reject.
    /// </remarks>
    [Fact]
    public async Task ADecodeBombIsRefusedFromItsHeaderRatherThanDecoded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        // 40,000 × 40,000 is 1.6 gigapixels — six gigabytes decoded, and a few hundred bytes on the
        // wire. Only the header is real; nothing downstream of the guard ever looks at the rest.
        var bomb = PngWithDeclaredSize(40_000, 40_000);

        bomb.Length.Should().BeLessThan(4096, "the whole point is that the file is small");

        var response = await UploadAsync(client, "bomb.png", bomb, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await ProblemAsync(response, cancellationToken);

        problem.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MediaCodes.DimensionsTooLarge);
    }

    [Fact]
    public async Task AnImageWithNeitherAltTextNorADecorativeFlagIsRefusedAtUpload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var response = await UploadAsync(
            client,
            Unique("undescribed") + ".jpg",
            Jpeg(320, 240, seed: 11),
            cancellationToken,
            altText: null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await ProblemAsync(response, cancellationToken);

        problem.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MediaCodes.AltTextRequired);
    }

    /// <remarks>
    /// Acceptance criterion P5 #13. The counter is what makes a library edit reach browsers and CDNs
    /// with no purge to run: it is folded into every rendition signature, so a page's image URLs
    /// after the edit are different strings from the ones already cached (ADR 0007).
    /// </remarks>
    [Fact]
    public async Task ALibraryEditBumpsEditsVersionAndRevertingBumpsItAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await UploadItemAsync(client, cancellationToken);

        var edited = await client.PutAsJsonAsync(
            $"{Media}/{item.Id}/edits",
            new SetMediaEditsRequest(new MediaEdits(
                Rotate: 90,
                Flip: FlipDirection.Horizontal,
                Crop: new NormalizedRect(0.1, 0.1, 0.5, 0.5),
                FocalPoint: new NormalizedPoint(0.25, 0.75))),
            cancellationToken);

        edited.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await edited.Content.ReadAsStringAsync(cancellationToken));

        var afterEdit = (await edited.Content.ReadFromJsonAsync<MediaDetail>(cancellationToken))!;

        afterEdit.EditsVersion.Should().Be(item.EditsVersion + 1);
        afterEdit.Edits!.Rotate.Should().Be(90);
        afterEdit.Edits.Flip.Should().Be(FlipDirection.Horizontal);
        afterEdit.Edits.Crop.Should().Be(new NormalizedRect(0.1, 0.1, 0.5, 0.5));

        // The focal point is mirrored into its own columns so the picker can sort and filter without
        // parsing JSON, while the document stays the single source of truth for the pixels.
        afterEdit.FocalPointX.Should().Be(0.25);
        afterEdit.FocalPointY.Should().Be(0.75);

        var reverted = await client.PostAsync(
            $"{Media}/{item.Id}/revert", content: null, cancellationToken);

        reverted.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterRevert = (await reverted.Content.ReadFromJsonAsync<MediaDetail>(cancellationToken))!;

        afterRevert.Edits.Should().BeNull();
        afterRevert.FocalPointX.Should().BeNull();

        // A revert has to move the counter too. Leaving it where the edit put it would let CDNs keep
        // serving the cropped version under URLs the site is still emitting.
        afterRevert.EditsVersion.Should().Be(afterEdit.EditsVersion + 1);
    }

    [Fact]
    public async Task AnEditDocumentWithAnImpossibleRotationIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await UploadItemAsync(client, cancellationToken);

        var response = await client.PutAsJsonAsync(
            $"{Media}/{item.Id}/edits",
            new SetMediaEditsRequest(new MediaEdits(Rotate: 45)),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await ProblemAsync(response, cancellationToken);

        problem.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MediaCodes.EditsInvalid);
    }

    /// <remarks>
    /// The replacement keeps the id, which is the whole point — every page pointing at the item goes
    /// on pointing at it — and moves the counter, which is what makes the new picture visible
    /// through caches.
    /// </remarks>
    [Fact]
    public async Task ReplacingAnItemKeepsItsIdAndChangesItsBytes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await UploadItemAsync(client, cancellationToken);

        using var body = MultipartOf(Unique("replacement") + ".jpg", Jpeg(1024, 768, seed: 23));

        var response = await client.PostAsync($"{Media}/{item.Id}/replace", body, cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(cancellationToken));

        var replaced = (await response.Content.ReadFromJsonAsync<MediaUploadResult>(cancellationToken))!;

        replaced.Item.Id.Should().Be(item.Id);
        replaced.Item.Width.Should().Be(1024);
        replaced.Item.Height.Should().Be(768);
        replaced.Item.EditsVersion.Should().Be(item.EditsVersion + 1);
        replaced.Item.AltText.Should().Be(item.AltText, "a replacement does not blank the description");
    }

    [Fact]
    public async Task ReplacingAnItemWithBytesAlreadyInTheLibraryIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var target = await UploadItemAsync(client, cancellationToken, seed: 31);
        var otherBytes = Jpeg(500, 400, seed: 37);

        (await UploadAsync(client, Unique("other") + ".jpg", otherBytes, cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        using var body = MultipartOf(Unique("copy") + ".jpg", otherBytes);

        var response = await client.PostAsync($"{Media}/{target.Id}/replace", body, cancellationToken);

        // Merging the two identities would silently redirect every page pointing at the loser.
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await ProblemAsync(response, cancellationToken);

        problem.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MediaCodes.Duplicate);
    }

    [Fact]
    public async Task DeletingMovesTheItemToTheBinAndRestoringBringsItBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await UploadItemAsync(client, cancellationToken, seed: 41);

        var deleted = await client.DeleteAsync($"{Media}/{item.Id}", cancellationToken);

        deleted.StatusCode.Should().Be(HttpStatusCode.OK);

        var live = await client.GetFromJsonAsync<MediaListResult>(Media, cancellationToken);

        live!.Items.Should().NotContain(candidate => candidate.Id == item.Id);

        var binned = await client.GetFromJsonAsync<MediaListResult>(
            $"{Media}?deletedOnly=true", cancellationToken);

        binned!.Items.Should().Contain(candidate => candidate.Id == item.Id);

        var restored = await client.PostAsync(
            $"{Media}/{item.Id}/restore", content: null, cancellationToken);

        restored.StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetFromJsonAsync<MediaListResult>(Media, cancellationToken))!
            .Items.Should().Contain(candidate => candidate.Id == item.Id);
    }

    /// <remarks>Task P5-24 — permanent deletion is never the first thing that happens to a file.</remarks>
    [Fact]
    public async Task PermanentDeletionOfAnItemThatIsNotInTheBinIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await UploadItemAsync(client, cancellationToken, seed: 43);

        var response = await client.DeleteAsync($"{Media}/{item.Id}/permanent", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await ProblemAsync(response, cancellationToken);

        problem.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MediaCodes.NotDeleted);
    }

    [Fact]
    public async Task PermanentDeletionRemovesAnUnreferencedItemFromTheBin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await UploadItemAsync(client, cancellationToken, seed: 47);

        (await client.DeleteAsync($"{Media}/{item.Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.DeleteAsync($"{Media}/{item.Id}/permanent", cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(cancellationToken));

        var purged = (await response.Content.ReadFromJsonAsync<MediaDeleteResult>(cancellationToken))!;

        purged.WasPermanent.Should().BeTrue();

        (await client.GetAsync($"{Media}/{item.Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <remarks>
    /// The permanent delete sits with <c>Media.Delete</c>, which only Administrators and media
    /// managers hold. An Author may upload and describe files and may put one in the bin; taking it
    /// out of the database for good is a different decision.
    /// </remarks>
    [Fact]
    public async Task AnAuthorMayUploadAndPatchButNotPermanentlyDelete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var author = await ClientAsync(_factory, cancellationToken, CmsRoles.Author);

        var uploaded = await UploadAsync(
            author, Unique("author") + ".jpg", Jpeg(320, 200, seed: 53), cancellationToken);

        uploaded.StatusCode.Should().Be(HttpStatusCode.Created);

        var item = (await uploaded.Content.ReadFromJsonAsync<MediaUploadResult>(cancellationToken))!.Item;

        (await author.DeleteAsync($"{Media}/{item.Id}", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await author.DeleteAsync($"{Media}/{item.Id}/permanent", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AViewerMayBrowseTheLibraryButNotWriteToIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var viewer = await ClientAsync(_factory, cancellationToken, CmsRoles.Viewer);

        (await viewer.GetAsync(Media, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var upload = await UploadAsync(
            viewer, Unique("viewer") + ".jpg", Jpeg(200, 200, seed: 59), cancellationToken);

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PatchingMetadataLeavesOmittedMembersAloneAndStampsAnETag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await UploadItemAsync(client, cancellationToken, seed: 61);

        var titled = await client.PatchAsJsonAsync(
            $"{Media}/{item.Id}",
            new PatchMediaRequest { Title = "Cover photograph", Credit = "A photographer" },
            cancellationToken);

        titled.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterTitle = (await titled.Content.ReadFromJsonAsync<MediaDetail>(cancellationToken))!;

        afterTitle.Title.Should().Be("Cover photograph");
        afterTitle.AltText.Should().Be(item.AltText, "the patch never mentioned the alt text");

        titled.Headers.ETag.Should().NotBeNull("a later write echoes this back as If-Match");

        var captioned = await client.PatchAsJsonAsync(
            $"{Media}/{item.Id}",
            new PatchMediaRequest { Caption = "Taken at dawn" },
            cancellationToken);

        var afterCaption = (await captioned.Content.ReadFromJsonAsync<MediaDetail>(cancellationToken))!;

        afterCaption.Caption.Should().Be("Taken at dawn");
        afterCaption.Title.Should().Be("Cover photograph", "the second patch did not mention it");
        afterCaption.Credit.Should().Be("A photographer");
    }

    [Fact]
    public async Task ClearingTheAltTextOfANonDecorativeImageIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await UploadItemAsync(client, cancellationToken, seed: 67);

        var response = await client.PatchAsJsonAsync(
            $"{Media}/{item.Id}",
            new PatchMediaRequest { AltText = new(null) },
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await ProblemAsync(response, cancellationToken);

        problem.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MediaCodes.AltTextRequired);
    }

    [Fact]
    public async Task FoldersNestFilterTheBrowserAndRefuseToBeDeletedWhileTheyHoldAnything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var created = await client.PostAsJsonAsync(
            $"{Media}/folders", new CreateMediaFolderRequest("Campaigns"), cancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var parent = (await created.Content.ReadFromJsonAsync<MediaFolderNode>(cancellationToken))!;

        var childResponse = await client.PostAsJsonAsync(
            $"{Media}/folders", new CreateMediaFolderRequest("Spring", parent.Id), cancellationToken);

        var child = (await childResponse.Content.ReadFromJsonAsync<MediaFolderNode>(cancellationToken))!;

        var filed = await UploadAsync(
            client,
            Unique("filed") + ".jpg",
            Jpeg(300, 300, seed: 71),
            cancellationToken,
            folderId: child.Id);

        filed.StatusCode.Should().Be(HttpStatusCode.Created);

        var tree = await client.GetFromJsonAsync<List<MediaFolderNode>>($"{Media}/folders", cancellationToken);

        tree!.Should().ContainSingle(folder => folder.Id == parent.Id)
            .Which.Children.Should().ContainSingle(folder => folder.Id == child.Id)
            .Which.ItemCount.Should().Be(1);

        // The folder itself holds nothing; its child does. Descendant search is the prefix match the
        // materialized path exists for.
        (await client.GetFromJsonAsync<MediaListResult>(
            $"{Media}?folderId={parent.Id}", cancellationToken))!.Total.Should().Be(0);

        (await client.GetFromJsonAsync<MediaListResult>(
            $"{Media}?folderId={parent.Id}&includeDescendants=true", cancellationToken))!
            .Total.Should().Be(1);

        var refused = await client.DeleteAsync($"{Media}/folders/{child.Id}", cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await ProblemAsync(refused, cancellationToken)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MediaCodes.FolderNotEmpty);
    }

    [Fact]
    public async Task AFolderCannotBeMovedInsideItself()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var parentResponse = await client.PostAsJsonAsync(
            $"{Media}/folders", new CreateMediaFolderRequest("Brand"), cancellationToken);

        var parent = (await parentResponse.Content.ReadFromJsonAsync<MediaFolderNode>(cancellationToken))!;

        var childResponse = await client.PostAsJsonAsync(
            $"{Media}/folders", new CreateMediaFolderRequest("Logos", parent.Id), cancellationToken);

        var child = (await childResponse.Content.ReadFromJsonAsync<MediaFolderNode>(cancellationToken))!;

        var response = await client.PatchAsJsonAsync(
            $"{Media}/folders/{parent.Id}",
            new PatchMediaFolderRequest { ParentId = child.Id },
            cancellationToken);

        // The branch would still be present, and every foreign key satisfied — it would simply be
        // unreachable from the root, which no query would report as missing.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await ProblemAsync(response, cancellationToken)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MediaCodes.FolderInvalidParent);
    }

    [Fact]
    public async Task AnUnusedItemAppearsInTheUnusedFilterAndHasAnEmptyWhereUsedList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var item = await UploadItemAsync(client, cancellationToken, seed: 73);

        (await client.GetFromJsonAsync<MediaListResult>($"{Media}?unusedOnly=true", cancellationToken))!
            .Items.Should().Contain(candidate => candidate.Id == item.Id);

        var references = await client.GetAsync($"{Media}/{item.Id}/references", cancellationToken);

        references.StatusCode.Should().Be(HttpStatusCode.OK);

        var impact = (await references.Content
            .ReadFromJsonAsync<ContentManagementSystem.Shared.Contracts.Content.ReferenceImpact>(
                cancellationToken))!;

        impact.IsReferenced.Should().BeFalse();
    }

    [Fact]
    public async Task AWriteWithoutAnAntiforgeryTokenIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Deliberately not through ClientAsync, which fetches the token pair first.
        using var client = _factory.CreateClientAs(CmsRoles.Administrator);

        using var body = MultipartOf("forged.jpg", Jpeg(200, 200, seed: 79));

        var response = await client.PostAsync(Media, body, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Fetches an item's stored original through its signed URL.</summary>
    private async Task<byte[]> OriginalBytesAsync(MediaDetail item, CancellationToken cancellationToken)
    {
        var url = _factory.Services.GetRequiredService<IMediaUrlSigner>()
            .BuildOriginalUrl(item.Id, item.EditsVersion, item.OriginalFileName);

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(url, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>Reads whatever metadata directories a JPEG carries.</summary>
    private static IReadOnlyList<MetadataExtractor.Directory> ExifDirectories(byte[] jpeg)
    {
        using var content = new MemoryStream(jpeg, writable: false);

        return [.. ImageMetadataReader.ReadMetadata(content)];
    }

    /// <summary>Uploads an image and returns the stored item, failing loudly if the API refused it.</summary>
    private static async Task<MediaDetail> UploadItemAsync(
        HttpClient client,
        CancellationToken cancellationToken,
        int seed = 3)
    {
        var response = await UploadAsync(
            client, Unique("fixture") + ".jpg", Jpeg(800, 600, seed), cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(cancellationToken));

        return (await response.Content.ReadFromJsonAsync<MediaUploadResult>(cancellationToken))!.Item;
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string fileName,
        byte[] bytes,
        CancellationToken cancellationToken,
        string? altText = "A test photograph",
        int? folderId = null)
    {
        using var body = MultipartOf(fileName, bytes, altText, folderId);

        return await client.PostAsync(Media, body, cancellationToken);
    }

    private static MultipartFormDataContent MultipartOf(
        string fileName,
        byte[] bytes,
        string? altText = "A test photograph",
        int? folderId = null)
    {
        var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(bytes), "file", fileName },
        };

        if (altText is not null) content.Add(new StringContent(altText), "altText");

        if (folderId is { } id)
        {
            content.Add(new StringContent(id.ToString(System.Globalization.CultureInfo.InvariantCulture)), "folderId");
        }

        return content;
    }

    /// <summary>A JPEG whose bytes differ per seed, so two fixtures do not deduplicate onto each other.</summary>
    private static byte[] Jpeg(int width, int height, int seed = 3)
    {
        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor((byte)(seed * 7 % 256), (byte)(seed * 13 % 256), (byte)(seed * 29 % 256)));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);

        return data.ToArray();
    }

    /// <summary>
    /// A real, tiny PNG whose header has been rewritten to declare an enormous image.
    /// </summary>
    /// <param name="width">Width to declare.</param>
    /// <param name="height">Height to declare.</param>
    /// <returns>The doctored file.</returns>
    /// <remarks>
    /// This is what a decode bomb actually looks like: a few hundred bytes on the wire that a
    /// decoder would turn into gigabytes of allocation. Built by patching a genuine file rather than
    /// by hand-assembling one, so the decoder reads the header exactly as it would read a hostile
    /// upload — a synthetic header the codec refuses outright would prove the wrong refusal.
    /// <para>
    /// <c>IHDR</c> is always the first chunk and always at offset 8, and its four-byte width and
    /// height are the first two fields of its payload. The chunk's CRC is recomputed so the file
    /// stays well formed up to the point where the guard stops it (spec section 13.3 step 4).
    /// </para>
    /// </remarks>
    private static byte[] PngWithDeclaredSize(int width, int height)
    {
        using var bitmap = new SKBitmap(2, 2);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var png = data.ToArray();

        const int headerChunkStart = 8;                 // straight after the eight-byte signature
        const int headerPayloadStart = headerChunkStart + 8;   // past the length and the "IHDR" type
        const int headerPayloadLength = 13;

        BigEndian(width).CopyTo(png, headerPayloadStart);
        BigEndian(height).CopyTo(png, headerPayloadStart + 4);

        var chunk = png[(headerChunkStart + 4)..(headerPayloadStart + headerPayloadLength)];

        BigEndian(unchecked((int)Crc32(chunk))).CopyTo(png, headerPayloadStart + headerPayloadLength);

        return png;
    }

    private static byte[] BigEndian(int value) =>
    [
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value,
    ];

    /// <summary>The CRC-32 a PNG chunk carries, so the header parses as a real one.</summary>
    private static uint Crc32(IReadOnlyList<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in bytes)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>A name unique to this run, so a shared media store cannot leak state between tests.</summary>
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
