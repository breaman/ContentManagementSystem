using Bunit;

using ContentManagementSystem.Client.Components.Admin.RecycleBin;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ContentManagementSystem.Client.Tests.RecycleBin;

/// <summary>
/// The recycle bin screen (task P6-28, spec section 14.10, acceptance criterion P6 #9).
/// </summary>
/// <remarks>
/// The two behaviours worth pinning are the two that are unpleasant to get wrong: a page swept up by
/// its parent's delete must not be offered as a restore of its own, and the permanent delete must
/// not be reachable without typing the name.
/// </remarks>
public class RecycleBinScreenTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    private readonly BinPageClient _client = new();

    public RecycleBinScreenTests()
    {
        _bunit.JSInterop.Mode = JSRuntimeMode.Loose;

        _bunit.Services.AddSingleton<IPageClient>(_client);
        _bunit.Services.AddSingleton<TimeProvider>(
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero)));

        // Signed in as an Administrator, which is the only role the permanent delete is offered to.
        // A suite run as anybody else would be inspecting a screen with one button on it.
        _bunit.AddAuthorization().SetAuthorized("Elena").SetRoles(CmsRoles.Administrator);
    }

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void OnlyTheSubtreeRootsAreListedAndTheirDescendantsAreCounted()
    {
        var screen = _bunit.Render<RecycleBinScreen>();

        var rows = screen.FindAll("tbody tr");

        rows.Should().HaveCount(
            2,
            "a section's descendants came out of the tree with it; listing them separately would " +
            "ask an editor to restore one delete twelve times, in an order that matters");

        rows[0].TextContent.Should().Contain("Autumn campaign").And.Contain("2 page(s) beneath it");
        screen.Markup.Should().NotContain(
            "Autumn offers",
            "it is restored by restoring the section it was deleted with");
    }

    [Test]
    public void RestoringASectionSaysHowMuchCameBackAndAsWhat()
    {
        var screen = _bunit.Render<RecycleBinScreen>();

        screen.FindAll("tbody tr")[0].QuerySelector(".btn-outline-primary")!.Click();

        _client.Restored.Should().Equal(51);

        screen.WaitForAssertion(() =>
            screen.Find("[aria-live]").TextContent.Should()
                .Contain("2 page(s) beneath it were restored as drafts"));
    }

    [Test]
    public void ARestoreThatCameBackAtTheRootSaysSo()
    {
        _client.RestoreWarning = new SubtreeResult(
            51,
            [51],
            0,
            [new Shared.Contracts.Api.ApiDiagnostic(
                PageCodes.ParentStillDeleted,
                "The former parent of this page is still in the recycle bin.")]);

        var screen = _bunit.Render<RecycleBinScreen>();

        screen.FindAll("tbody tr")[0].QuerySelector(".btn-outline-primary")!.Click();

        screen.WaitForAssertion(() =>
            screen.Markup.Should().Contain("still in the recycle bin"));
    }

    [Test]
    public void PermanentDeletionIsRefusedUntilTheNameIsTyped()
    {
        var screen = _bunit.Render<RecycleBinScreen>();

        screen.FindAll("tbody tr")[0].QuerySelector(".btn-outline-danger")!.Click();

        var confirm = screen.Find("[role=dialog] .btn-danger");

        confirm.HasAttribute("disabled").Should().BeTrue(
            "this is the one operation with no undo, so the ceremony is the feature");

        screen.Find("[role=dialog] input").Input("not the name");
        screen.Find("[role=dialog] .btn-danger").HasAttribute("disabled").Should().BeTrue();

        screen.Find("[role=dialog] input").Input("autumn campaign");

        // Case-insensitive on purpose: the point is that somebody read the name and typed it, not
        // that they reproduced its capitalisation.
        screen.Find("[role=dialog] .btn-danger").HasAttribute("disabled").Should().BeFalse();

        _client.Purged.Should().BeEmpty("nothing is destroyed by opening the dialog");

        screen.Find("[role=dialog] .btn-danger").Click();

        screen.WaitForAssertion(() => _client.Purged.Should().Equal(51));
    }

    [Test]
    public void TheFilterNarrowsTheListByTitleSlugOrId()
    {
        var screen = _bunit.Render<RecycleBinScreen>();

        screen.Find("input[type=search]").Input("old");

        screen.FindAll("tbody tr").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Old pricing");

        // An editor arriving from a log line or a ticket is holding a number, not a title.
        screen.Find("input[type=search]").Input("51");

        screen.FindAll("tbody tr").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Autumn campaign");
    }

    /// <summary>A bin with one deleted section, one of its descendants, and one lone page.</summary>
    private sealed class BinPageClient : StubPageClient
    {
        /// <summary>Every page a restore was sent for.</summary>
        public List<int> Restored { get; } = [];

        /// <summary>Every page a permanent delete was sent for.</summary>
        public List<int> Purged { get; } = [];

        /// <summary>What the next restore answers with, when a test needs the root-warning branch.</summary>
        public SubtreeResult? RestoreWarning { get; set; }

        /// <inheritdoc />
        public override Task<IReadOnlyList<RecycleBinEntry>> GetRecycleBinAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecycleBinEntry>>(
            [
                new RecycleBinEntry(51, "Autumn campaign", "autumn", null, IsSubtreeRoot: true, 2,
                    WasPublished: true, DateTimeOffset.UtcNow.AddDays(-2), 1),
                new RecycleBinEntry(52, "Autumn offers", "offers", 51, IsSubtreeRoot: false, 0,
                    WasPublished: true, DateTimeOffset.UtcNow.AddDays(-2), 1),
                new RecycleBinEntry(53, "Old pricing", "old-pricing", null, IsSubtreeRoot: true, 0,
                    WasPublished: false, DateTimeOffset.UtcNow.AddHours(-3), 1),
            ]);

        /// <inheritdoc />
        public override Task<StructureClientResult<SubtreeResult>> RestoreAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            Restored.Add(id);

            return Task.FromResult(StructureClientResult<SubtreeResult>.Success(
                RestoreWarning ?? new SubtreeResult(id, [id, 52, 54], UnpublishedCount: 0, [])));
        }

        /// <inheritdoc />
        public override Task<StructureClientResult<PurgeResult>> PurgeAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            Purged.Add(id);

            return Task.FromResult(StructureClientResult<PurgeResult>.Success(new PurgeResult(id, 4)));
        }
    }
}
