using Bunit;

using ContentManagementSystem.Client.Components.Admin.Saving;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Client.Tests.Saving;

/// <summary>
/// The save-conflict dialog (task P6-19, acceptance criterion P6 #6).
/// </summary>
/// <remarks>
/// The criterion has two halves and the second is the one a test can lose sight of: all three
/// resolutions are offered, <strong>and no path silently discards work</strong>. So these assert
/// what the buttons do as much as that they exist — in particular that the only irreversible one
/// asks twice, and that closing the dialog decides nothing.
/// </remarks>
public class ConflictDialogTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    private readonly DiffingPageClient _client = new();

    public ConflictDialogTests()
    {
        _bunit.Services.AddSingleton<IPageClient>(_client);
        _bunit.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AllThreeResolutionsAreOffered()
    {
        var dialog = Render();

        var labels = dialog.FindAll("button").Select(button => button.TextContent.Trim()).ToList();

        labels.Should().Contain(label => label.Contains("Keep mine"));
        labels.Should().Contain(label => label.Contains("Use theirs"));
        labels.Should().Contain(label => label.Contains("Compare the two versions"));
    }

    [Fact]
    public void TheEditorIsToldTheirWorkIsStillHereBeforeBeingAskedToChoose()
    {
        var dialog = Render();

        dialog.Find(".cms-conflict__reassurance").TextContent
            .Should().Contain("still here").And.Contain("Nothing is discarded");
    }

    [Fact]
    public void KeepingMineIsOneClickBecauseNothingItOverwritesIsLost()
    {
        var kept = 0;
        var dialog = Render(onKeepMine: () => kept++);

        dialog.Find(".btn-primary").Click();

        kept.Should().Be(1);
    }

    [Fact]
    public void TakingTheirsAsksTwiceBecauseWhatItReplacesExistsNowhereElse()
    {
        var taken = 0;
        var dialog = Render(onTakeTheirs: () => taken++);

        dialog.Find(".btn-outline-danger").Click();

        taken.Should().Be(0, "the first press is the question, not the answer");
        dialog.Find("[role=alert]").TextContent.Should().Contain("no undo");
        dialog.Find(".btn-outline-danger").TextContent.Should().Contain("discard mine");

        dialog.Find(".btn-outline-danger").Click();

        taken.Should().Be(1);
    }

    [Fact]
    public void BackingOutOfTakingTheirsDisarmsIt()
    {
        var taken = 0;
        var dialog = Render(onTakeTheirs: () => taken++);

        dialog.Find(".btn-outline-danger").Click();
        dialog.Find(".btn-outline-secondary").Click();

        dialog.FindAll("[role=alert]").Should().BeEmpty();

        dialog.Find(".btn-outline-danger").Click();

        taken.Should().Be(0, "a destructive button must not stay armed behind another action");
    }

    [Fact]
    public void TheComparisonIsFetchedOnlyWhenItIsAskedForAndOnlyOnce()
    {
        var dialog = Render();

        _client.Compared.Should().BeEmpty("nobody has asked to compare anything yet");

        dialog.Find(".btn-outline-secondary").Click();

        dialog.WaitForAssertion(() =>
            dialog.Find(".cms-conflict__diff").TextContent.Should().Contain("heroTitle"));

        _client.Compared.Should().ContainSingle().Which.Should().Be("""{"mine":true}""");

        dialog.Find(".btn-outline-secondary").Click();
        dialog.Find(".btn-outline-secondary").Click();

        _client.Compared.Should().HaveCount(
            1,
            "neither side changes while the dialog is open, so the comparison is asked for once");
    }

    [Fact]
    public void ClosingDecidesNothing()
    {
        var kept = 0;
        var taken = 0;
        var cancelled = 0;

        var dialog = Render(
            onKeepMine: () => kept++,
            onTakeTheirs: () => taken++,
            onCancel: () => cancelled++);

        dialog.Find(".btn-close").Click();

        cancelled.Should().Be(1);
        kept.Should().Be(0);
        taken.Should().Be(0);
    }

    [Fact]
    public void ADialogThatIsNotOpenRendersNothingAtAll()
    {
        var dialog = _bunit.Render<ConflictDialog>(parameters => parameters
            .Add(conflict => conflict.IsOpen, false)
            .Add(conflict => conflict.PageId, 4)
            .Add(conflict => conflict.Theirs, Theirs));

        dialog.Markup.Trim().Should().BeEmpty();
    }

    private IRenderedComponent<ConflictDialog> Render(
        Action? onKeepMine = null,
        Action? onTakeTheirs = null,
        Action? onCancel = null) =>
        _bunit.Render<ConflictDialog>(parameters => parameters
            .Add(conflict => conflict.IsOpen, true)
            .Add(conflict => conflict.PageId, 4)
            .Add(conflict => conflict.Theirs, Theirs)
            .Add(conflict => conflict.Mine, """{"mine":true}""")
            .Add(conflict => conflict.OnKeepMine, () => onKeepMine?.Invoke())
            .Add(conflict => conflict.OnTakeTheirs, () => onTakeTheirs?.Invoke())
            .Add(conflict => conflict.OnCancel, () => onCancel?.Invoke()));

    private static DraftState Theirs { get; } = new(
        PageId: 4,
        VersionId: 11,
        VersionNumber: 5,
        ContentJson: """{"theirs":true}""",
        TemplateKey: "marketing-landing",
        TemplateRevision: 2,
        RowVersion: "AAAAAAAAB9Q=",
        SavedOn: new DateTimeOffset(2026, 8, 16, 14, 32, 0, TimeSpan.Zero));

    /// <summary>Compares an unsaved payload against the stored draft, and records what it was sent.</summary>
    private sealed class DiffingPageClient : StubPageClient
    {
        public List<string?> Compared { get; } = [];

        public override Task<ContentDiff?> DiffDraftAsync(
            int id,
            string? contentJson,
            CancellationToken cancellationToken = default)
        {
            Compared.Add(contentJson);

            return Task.FromResult<ContentDiff?>(new ContentDiff(
                id,
                11,
                11,
                5,
                5,
                [],
                [
                    new ZoneChange(
                        "heroTitle",
                        "plainText",
                        ContentChangeKind.Changed,
                        "Theirs",
                        "Mine",
                        [
                            new TextSegment("Theirs", ContentChangeKind.Removed),
                            new TextSegment("Mine", ContentChangeKind.Added),
                        ],
                        []),
                ]));
        }
    }
}
