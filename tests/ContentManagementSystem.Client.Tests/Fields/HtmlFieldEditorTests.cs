using Bunit;

using ContentManagementSystem.Client.Components.Admin.Fields.Html;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Client.Tests.Fields;

/// <summary>
/// The HTML editor's permitted-tags banner and strip warning (task P6-13, acceptance criterion P6 #3).
/// </summary>
/// <remarks>
/// The criterion is "warns <em>before</em> save", so the assertion that matters most here is that the
/// warning appears while the editor is in Write mode with no preview open — which is where an author
/// actually is when they paste something in.
/// </remarks>
public class HtmlFieldEditorTests : IDisposable
{
    private readonly FieldEditorHarness _harness = new();

    private readonly StubPreviewClient _preview = new();

    public HtmlFieldEditorTests()
    {
        _harness.Bunit.Services.AddSingleton<IMarkupPreviewClient>(_preview);
        _harness.Bunit.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void WhatWillBeStrippedIsSaidWhileTheAuthorIsStillWriting()
    {
        _preview.Removals = [new SanitizationRemoval(SanitizationRemovalKind.Tag, "script")];

        var editor = Render("""{"type":"html","value":"<p>Hi</p><script>alert(1)</script>"}""");

        // Write mode, no preview open — which is where somebody pasting an embed actually is.
        editor.FindAll(".cms-field__pane--preview").Should().BeEmpty();

        editor.WaitForAssertion(() =>
        {
            var warning = editor.Find(".cms-field__strip-warning");

            warning.TextContent.Should().Contain("removed when you save");
            warning.TextContent.Should().Contain("<script> was removed");
        });
    }

    [Test]
    public void TheWarningIsALiveRegionSoItIsHeardAndNotOnlySeen()
    {
        var editor = Render(Markup);

        var warning = editor.Find(".cms-field__strip-warning");

        warning.GetAttribute("role").Should().Be("status");
        warning.GetAttribute("aria-live").Should().Be("polite");
    }

    [Test]
    public void TheCheckAsksTheServerUnderTheProfileTheFieldTypeActuallyUses()
    {
        var editor = Render(Markup);

        editor.WaitForAssertion(() =>
        {
            _preview.Requests.Should().NotBeEmpty();

            var request = _preview.Requests[^1];

            // The same sanitizer the save will run (ADR-0008). A client-side approximation of the
            // allowlist would be wrong in both directions — silent about something that will be
            // stripped, and alarming about something that will not.
            request.Format.Should().Be(MarkupFormats.Html);
            request.Profile.Should().Be(nameof(SanitizationProfile.Developer));
        });
    }

    [Test]
    public void ThePermittedElementsAreListedBeforeAnythingIsPastedIn()
    {
        var editor = Render(string.Empty);

        editor.WaitForAssertion(() =>
        {
            var banner = editor.Find(".cms-field__allowlist");

            banner.TextContent.Should().Contain("iframe").And.Contain("p");
        });
    }

    [Test]
    public void NothingToWarnAboutMeansNoWarning()
    {
        var editor = Render(Markup);

        editor.WaitForAssertion(() =>
            editor.Find(".cms-field__strip-warning").TextContent.Trim().Should().BeEmpty());
    }

    private IRenderedComponent<HtmlFieldEditor> Render(string value)
    {
        var slot = FieldEditorHarness.Slot(FieldTypeKeys.Html);

        return _harness.Render<HtmlFieldEditor>(FieldEditorHarness.Context(slot), value);
    }

    private const string Markup = """{"type":"html","value":"<p>Hi</p>"}""";

    /// <summary>Answers with whatever removals the test wants, and records what it was asked.</summary>
    private sealed class StubPreviewClient : IMarkupPreviewClient
    {
        public List<MarkupPreviewRequest> Requests { get; } = [];

        public IReadOnlyList<SanitizationRemoval> Removals { get; set; } = [];

        public Task<MarkupPreviewResult?> RenderAsync(
            MarkupPreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Task.FromResult<MarkupPreviewResult?>(
                new MarkupPreviewResult(request.Source ?? string.Empty, Removals));
        }

        public Task<IReadOnlyList<SanitizationProfileDescriptor>> GetProfilesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SanitizationProfileDescriptor>>(
            [
                new SanitizationProfileDescriptor(
                    nameof(SanitizationProfile.Developer),
                    ["a", "em", "iframe", "p", "strong"]),
            ]);
    }
}
