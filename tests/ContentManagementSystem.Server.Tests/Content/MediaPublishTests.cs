using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Media.Library;
using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

using static ContentManagementSystem.Server.Tests.Content.PageWorkbench;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// What a publish does about the pictures a page places (tasks P5-19, P5-21 and P5-24,
/// acceptance criteria P5 #6, #7, #11 and #12).
/// </summary>
/// <remarks>
/// These are the rules a field type structurally cannot enforce, so this is the level they have to
/// be proved at: every one of them needs the media row, the page payload, and the captured slot
/// configuration in the same place, which happens exactly once — on the publish path
/// (spec section 7).
/// <para>
/// Through the real services against a real database, and with the pictures put into the library by
/// the real upload pipeline. A fixture that inserted a <c>MediaItem</c> row directly would let a
/// test pass with dimensions no decoder ever agreed to.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class MediaPublishTests(SqlServerFixture fixture)
{
    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    /// <remarks>Acceptance criterion P5 #11.</remarks>
    [Test]
    public async Task PublishingAPageWhoseImageHasNeitherAltTextNorADecorativeFlagFailsValidation()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PageWithImageZoneAsync(cancellationToken);

        // Uploaded with the upload-time check relaxed, which is the state a migrated library is in:
        // the picture is already there and nobody described it. The publish check is the one that
        // cannot be skipped (spec section 13.7).
        var image = await _bench.AddImageAsync(1200, 800, cancellationToken, altText: null);

        await _bench.PlaceMediaAsync(page, "hero", image.Id, cancellationToken);

        var published = await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        published.Outcome.Should().Be(CmsOutcome.Invalid);
        published.Diagnostics.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == MediaCodes.AltTextRequired &&
            diagnostic.Severity == ValidationSeverity.Error);
    }

    [Test]
    public async Task ThePlacementsOwnDescriptionSatisfiesTheRuleWithoutChangingTheLibrary()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PageWithImageZoneAsync(cancellationToken);
        var image = await _bench.AddImageAsync(1200, 800, cancellationToken, altText: null);

        // The case the override exists for: a library description that is wrong for this page's
        // context. A rule that ignored it would force a choice between an accurate library and a
        // publishable page.
        await _bench.PlaceMediaAsync(
            page,
            "hero",
            image.Id,
            cancellationToken,
            altOverride: "The team assembling a prototype");

        var published = await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            cancellationToken: cancellationToken);

        published.IsSuccess.Should().BeTrue(Because(published));
    }

    [Test]
    public async Task ADecorativeImageIsPublishableWithNothingWrittenAboutItAnywhere()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PageWithImageZoneAsync(cancellationToken);

        var divider = await _bench.AddImageAsync(
            600,
            40,
            cancellationToken,
            altText: null,
            isDecorative: true);

        await _bench.PlaceMediaAsync(page, "hero", divider.Id, cancellationToken);

        var published = await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            cancellationToken: cancellationToken);

        published.IsSuccess.Should().BeTrue(Because(published));
    }

    /// <remarks>
    /// The migration escape hatch of spec section 13.7. Importing a legacy site produces thousands
    /// of undescribed pictures at once, and a rule that made every one of those pages unpublishable
    /// would be turned off wholesale rather than worked through.
    /// </remarks>
    [Test]
    public async Task ADeploymentMayDowngradeTheAltTextRuleToAWarningAndStillPublish()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        _bench.Resolve<MediaValidationOptions>().MissingAltTextSeverity = ValidationSeverity.Warning;

        var page = await PageWithImageZoneAsync(cancellationToken);
        var image = await _bench.AddImageAsync(1200, 800, cancellationToken, altText: null);

        await _bench.PlaceMediaAsync(page, "hero", image.Id, cancellationToken);

        var published = await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        published.IsSuccess.Should().BeTrue(Because(published));
        published.Value!.Warnings.Should().Contain(warning => warning.Code == MediaCodes.AltTextRequired);
    }

    [Test]
    public async Task APlacementOfAnImageInTheRecycleBinIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PageWithImageZoneAsync(cancellationToken);
        var image = await _bench.AddImageAsync(1200, 800, cancellationToken);

        await _bench.PlaceMediaAsync(page, "hero", image.Id, cancellationToken);

        (await _bench.Resolve<IMediaLibraryService>().DeleteAsync(image.Id, cancellationToken))
            .IsSuccess.Should().BeTrue();

        var published = await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            acknowledgeWarnings: true,
            cancellationToken);

        // Publishing it would put the section 15.3 placeholder on the public site, which is the one
        // outcome nobody chose.
        published.Outcome.Should().Be(CmsOutcome.Invalid);
        published.Diagnostics.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == MediaCodes.NotFound);
    }

    /// <remarks>
    /// The <c>minWidth</c> half of task P5-19, and the reason the check measures the picture as
    /// placed: the same photograph passes and fails depending on how much of it this page uses.
    /// </remarks>
    [Test]
    public async Task AMinimumWidthIsJudgedAgainstThePictureAfterThePlacementsOwnCrop()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PageWithImageZoneAsync(cancellationToken, """{"minWidth":1000}""");
        var image = await _bench.AddImageAsync(1200, 800, cancellationToken);

        await _bench.PlaceMediaAsync(page, "hero", image.Id, cancellationToken);

        var whole = await _bench.Resolve<IPublishingService>().ValidateAsync(
            page.Summary.Id,
            cancellationToken);

        whole.Value!.CanPublish.Should().BeTrue(
            "1200 px is wider than the 1000 px the slot asks for");

        // Half the width kept, which is 600 px — under the floor, though the file on disk is not.
        var reloaded = (await _bench.Resolve<IPageService>().GetAsync(page.Summary.Id, cancellationToken)).Value!;

        await _bench.PlaceMediaAsync(
            reloaded,
            "hero",
            image.Id,
            cancellationToken,
            crop: """{"x":0,"y":0,"w":0.5,"h":1}""");

        var cropped = await _bench.Resolve<IPublishingService>().ValidateAsync(
            page.Summary.Id,
            cancellationToken);

        cropped.Value!.CanPublish.Should().BeFalse();
        cropped.Value.Errors.Should().Contain(error => error.Message.Contains("600 px wide"));
    }

    /// <remarks>
    /// The <c>aspectRatio</c> half, whose syntax this phase settles: <c>W:H</c>, checked against the
    /// picture the page will show. Cropping to fit is how an editor satisfies it, which is what makes
    /// the setting usable rather than a demand to re-upload.
    /// </remarks>
    [Test]
    public async Task AnAspectRatioIsSatisfiedByCroppingRatherThanByReUploading()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PageWithImageZoneAsync(cancellationToken, """{"aspectRatio":"16:9"}""");

        // 4:3, which is not 16:9 by any tolerance.
        var image = await _bench.AddImageAsync(1600, 1200, cancellationToken);

        await _bench.PlaceMediaAsync(page, "hero", image.Id, cancellationToken);

        var uncropped = await _bench.Resolve<IPublishingService>().ValidateAsync(
            page.Summary.Id,
            cancellationToken);

        uncropped.Value!.CanPublish.Should().BeFalse();
        uncropped.Value.Errors.Should().Contain(error => error.Message.Contains("16:9"));

        // 1600 × 900 is 16:9, and 900 of 1200 is three quarters of the height.
        var reloaded = (await _bench.Resolve<IPageService>().GetAsync(page.Summary.Id, cancellationToken)).Value!;

        await _bench.PlaceMediaAsync(
            reloaded,
            "hero",
            image.Id,
            cancellationToken,
            crop: """{"x":0,"y":0.125,"w":1,"h":0.75}""");

        var cropped = await _bench.Resolve<IPublishingService>().ValidateAsync(
            page.Summary.Id,
            cancellationToken);

        cropped.Value!.CanPublish.Should().BeTrue(
            string.Join("; ", cropped.Value.Errors.Select(error => error.Message)));
    }

    /// <remarks>
    /// The <c>allowedTypes</c> half. An error rather than a warning, for the reason the reusable
    /// rule it is modelled on gives: a slot written for a picture and filled with a PDF renders
    /// through markup that was designed for the other thing, and the failure surfaces on the public
    /// site rather than here.
    /// </remarks>
    [Test]
    public async Task ASlotRestrictedToImagesTakesAPhotographAndRefusesADocument()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PageWithImageZoneAsync(cancellationToken, """{"allowedTypes":["Image"]}""");
        var image = await _bench.AddImageAsync(800, 600, cancellationToken);

        await _bench.PlaceMediaAsync(page, "hero", image.Id, cancellationToken);

        var publishing = _bench.Resolve<IPublishingService>();

        (await publishing.ValidateAsync(page.Summary.Id, cancellationToken))
            .Value!.CanPublish.Should().BeTrue();

        var document = await _bench.AddDocumentAsync(cancellationToken);
        var reloaded = (await _bench.Resolve<IPageService>().GetAsync(page.Summary.Id, cancellationToken)).Value!;

        await _bench.PlaceMediaAsync(reloaded, "hero", document.Id, cancellationToken, altOverride: "The brochure");

        var refused = await publishing.ValidateAsync(page.Summary.Id, cancellationToken);

        refused.Value!.CanPublish.Should().BeFalse();
        refused.Value.Errors.Should().Contain(error => error.Code == FieldValidationCodes.NotAllowed);
    }

    /// <remarks>
    /// Acceptance criterion P5 #7. Two pages showing one picture, cropped differently — the proof
    /// that a usage-scope edit is stored on the placement and not on the item (spec section 13.4).
    /// </remarks>
    [Test]
    public async Task AUsageLevelCropAffectsOnlyThatPageAndLeavesTheOtherUsageUntouched()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("gallery", cancellationToken, MediaZone("hero"));
        var cropped = await _bench.AddPageAsync(template, "Cropped", cancellationToken);
        var whole = await _bench.AddPageAsync(template, "Whole", cancellationToken);
        var image = await _bench.AddImageAsync(1600, 1200, cancellationToken);

        await _bench.PlaceMediaAsync(
            cropped,
            "hero",
            image.Id,
            cancellationToken,
            crop: """{"x":0,"y":0,"w":1,"h":0.5}""");

        await _bench.PlaceMediaAsync(whole, "hero", image.Id, cancellationToken);

        var publishing = _bench.Resolve<IPublishingService>();

        (await publishing.PublishAsync(cropped.Summary.Id, cancellationToken: cancellationToken))
            .IsSuccess.Should().BeTrue();
        (await publishing.PublishAsync(whole.Summary.Id, cancellationToken: cancellationToken))
            .IsSuccess.Should().BeTrue();

        // The item itself is untouched by either placement — no edits, and the generation counter
        // has not moved. A usage crop that reached the library would have bumped it.
        var stored = await _bench.Context.MediaItems
            .AsNoTracking()
            .SingleAsync(item => item.Id == image.Id, cancellationToken);

        stored.EditsJson.Should().BeNull();
        stored.EditsVersion.Should().Be(0);

        // And the two pages resolve to different pictures: one is half the height of the other.
        var resolved = (await _bench.Resolve<IMediaResolver>()
            .ResolveAsync([image.Id], cancellationToken))[image.Id];

        MediaGeometry.Effective(
                resolved.Width,
                resolved.Height,
                resolved.Edits,
                new NormalizedRect(0, 0, 1, 0.5))
            .Should().Be(new PixelSize(1600, 600));

        MediaGeometry.Effective(resolved.Width, resolved.Height, resolved.Edits)
            .Should().Be(new PixelSize(1600, 1200));
    }

    /// <remarks>
    /// Acceptance criterion P5 #6, the half the delivery suite cannot reach: a library rotation is
    /// visible to <em>every</em> page showing the item, because none of them stored anything about
    /// its geometry in the first place.
    /// </remarks>
    [Test]
    public async Task RotatingAnImageInTheLibraryChangesWhatEveryPageShowingItResolvesTo()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("gallery", cancellationToken, MediaZone("hero"));
        var first = await _bench.AddPageAsync(template, "First", cancellationToken);
        var second = await _bench.AddPageAsync(template, "Second", cancellationToken);
        var image = await _bench.AddImageAsync(1600, 1200, cancellationToken);

        await _bench.PlaceMediaAsync(first, "hero", image.Id, cancellationToken);
        await _bench.PlaceMediaAsync(second, "hero", image.Id, cancellationToken);

        var publishing = _bench.Resolve<IPublishingService>();

        await publishing.PublishAsync(first.Summary.Id, cancellationToken: cancellationToken);
        await publishing.PublishAsync(second.Summary.Id, cancellationToken: cancellationToken);

        var edited = await _bench.Resolve<IMediaLibraryService>().SetEditsAsync(
            image.Id,
            new SetMediaEditsRequest(new MediaEdits(Rotate: 90)),
            cancellationToken);

        edited.IsSuccess.Should().BeTrue(Because(edited));

        _bench.Context.ChangeTracker.Clear();

        var resolved = (await _bench.Resolve<IMediaResolver>()
            .ResolveAsync([image.Id], cancellationToken))[image.Id];

        // Neither page's payload changed and neither was republished. The rotation reaches both
        // because a placement stores an id and nothing else about the picture (spec section 13.1).
        resolved.EditsVersion.Should().Be(1);
        MediaGeometry.Effective(resolved.Width, resolved.Height, resolved.Edits)
            .Should().Be(new PixelSize(1200, 1600));

        var payloads = await _bench.Context.PageVersions
            .AsNoTracking()
            .Where(version => version.PageId == first.Summary.Id || version.PageId == second.Summary.Id)
            .Select(version => version.ContentJson)
            .ToListAsync(cancellationToken);

        payloads.Should().OnlyContain(json => !json.Contains("rotate"));
    }

    /// <remarks>
    /// Acceptance criterion P5 #12, end to end at last: a published page placing the picture is
    /// exactly the <c>ContentReference</c> row the purge guard reads, and the where-used endpoint
    /// names the page that caused the refusal (spec section 13.8).
    /// </remarks>
    [Test]
    public async Task PermanentDeletionOfAPlacedImageIsRefusedAndTheWhereUsedListNamesThePage()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PageWithImageZoneAsync(cancellationToken);
        var image = await _bench.AddImageAsync(1200, 800, cancellationToken);

        await _bench.PlaceMediaAsync(page, "hero", image.Id, cancellationToken);

        (await _bench.Resolve<IPublishingService>().PublishAsync(
            page.Summary.Id,
            cancellationToken: cancellationToken)).IsSuccess.Should().BeTrue();

        var library = _bench.Resolve<IMediaLibraryService>();

        (await library.DeleteAsync(image.Id, cancellationToken)).IsSuccess.Should().BeTrue(
            "a soft delete is reversible, so it is deliberately not reference-guarded");

        var purge = await library.PurgeAsync(image.Id, cancellationToken);

        purge.Outcome.Should().Be(CmsOutcome.Conflict);
        purge.Diagnostics.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == MediaCodes.StillReferenced);

        var whereUsed = await library.WhereUsedAsync(image.Id, cancellationToken);

        whereUsed.IsSuccess.Should().BeTrue(Because(whereUsed));
        whereUsed.Value!.AffectedPages.Should().Contain(usage => usage.Id == page.Summary.Id);
    }

    /// <summary>Builds a page whose only zone holds one picture.</summary>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <param name="configurationJson">The slot's picker settings, or null for no restriction.</param>
    private async Task<PageDetail> PageWithImageZoneAsync(
        CancellationToken cancellationToken,
        string? configurationJson = null)
    {
        var template = await _bench.AddTemplateAsync(
            "gallery",
            cancellationToken,
            MediaZone("hero", configurationJson));

        return await _bench.AddPageAsync(template, "Gallery", cancellationToken);
    }
}
