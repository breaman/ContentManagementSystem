using Bunit;

using ContentManagementSystem.Client.Components.Admin.Fields.RichText;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Client.Tests.Fields;

/// <summary>
/// The rich-text editor's mode switching and preview wiring (tasks P6-08 to P6-10, and P6-31).
/// </summary>
/// <remarks>
/// bUnit has no JavaScript, so CodeMirror and Quill never actually mount here — their host elements
/// render and their interop calls go to the stub runtime. What that leaves testable is exactly what
/// belongs in a component test: which surface is chosen, which mode is showing, what the preview is
/// asked to render, and that the format travels with the value. The editors' own behaviour, and the
/// teardown that matters for R14, are E2E's (P6-31a).
/// </remarks>
public class RichTextFieldEditorTests : IDisposable
{
    private readonly FieldEditorHarness _harness = new();

    private readonly RecordingPreviewClient _preview = new();

    public RichTextFieldEditorTests()
    {
        _harness.Bunit.Services.AddSingleton<IMarkupPreviewClient>(_preview);

        // The pickers the toolbar opens (P6-11). Neither is exercised here — a picker needs a
        // browser to be useful — but the editor injects both, and a component test that stubbed
        // them away would stop noticing if it started calling them on first render.
        _harness.Bunit.Services.AddSingleton<IPageClient>(new UnusedPages());
        _harness.Bunit.Services.AddSingleton<IMediaClient>(new UnusedMedia());

        // Loose, because CodeMirror and Quill are never mounted here: their modules are imported on
        // first render and every call into them is a no-op that this test does not assert on.
        _harness.Bunit.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void AMarkdownValueOpensInTheSourceSurfaceAndAnHtmlOneInTheWysiwyg()
    {
        // The surface follows the value's own format, not the property's configuration: a property
        // switched from markdown to HTML must still be able to edit what is already stored.
        Render(Markdown).FindAll(".cms-source-editor").Should().ContainSingle();
        Render(Html).FindAll(".cms-wysiwyg-editor").Should().ContainSingle();
    }

    [Test]
    public void AValueWithNoFormatAtAllIsTreatedAsMarkdown()
    {
        var editor = Render("""{ "type": "richText", "value": "Three tiers." }""");

        editor.FindAll(".cms-source-editor").Should().ContainSingle();
    }

    [Test]
    public void TheThreeModesAreOneChoiceWithOneAnswer()
    {
        var editor = Render(Markdown);

        var group = editor.Find("[role=radiogroup]");
        var options = group.QuerySelectorAll("[role=radio]");

        // Three buttons that merely look pressed announce nothing; a radio group announces which
        // one is chosen and moves between them with the arrow keys for free.
        options.Should().HaveCount(3);
        options.Count(option => option.GetAttribute("aria-checked") == "true").Should().Be(1);
    }

    [Test]
    public void WriteModeShowsNoPreviewAndPreviewModeShowsNoEditor()
    {
        var editor = Render(Markdown);

        editor.FindAll(".cms-field__pane--preview").Should().BeEmpty();

        Choose(editor, "Preview");

        editor.FindAll(".cms-field__pane--source").Should().BeEmpty();
        editor.FindAll(".cms-field__pane--preview").Should().ContainSingle();
    }

    [Test]
    public void BothModeShowsTheEditorAndThePreviewTogether()
    {
        var editor = Render(Markdown);

        Choose(editor, "Both");

        editor.FindAll(".cms-field__pane--source").Should().ContainSingle();
        editor.FindAll(".cms-field__pane--preview").Should().ContainSingle();
    }

    [Test]
    public void ThePreviewIsRenderedByTheServerFromTheValuesOwnFormat()
    {
        var editor = Render(Markdown);

        Choose(editor, "Preview");

        editor.WaitForAssertion(() =>
        {
            _preview.Requests.Should().NotBeEmpty();

            var request = _preview.Requests[^1];

            // The same pipeline the published page goes through, asked for by name (P6-09,
            // acceptance criterion P6 #2).
            request.Format.Should().Be(MarkupFormats.Markdown);
            request.Source.Should().Be("Three tiers.");
        });
    }

    [Test]
    public void ThePreviewIsAskedForTheProfileTheZoneConfigures()
    {
        var editor = Render(
            Markdown,
            configuration: $$"""{ "{{FieldSettingNames.Profile}}": "extended" }""");

        Choose(editor, "Preview");

        editor.WaitForAssertion(() =>
            _preview.Requests.Should().NotBeEmpty().And.Subject.Last().Profile.Should().Be("extended"));
    }

    [Test]
    public void ThePreviewSaysWhatPublishingWillRemove()
    {
        _preview.Removals =
        [
            new SanitizationRemoval(SanitizationRemovalKind.Tag, "iframe", Value: "<iframe src=…>"),
        ];

        var editor = Render(Markdown);

        Choose(editor, "Preview");

        editor.WaitForAssertion(() =>
        {
            var report = editor.Find(".cms-preview__removals");

            report.TextContent.Should().Contain("removed").And.Contain("<iframe> was removed");
        });
    }

    [Test]
    public void TheCountIsOfTheAuthoredSourceAndIsShownInEveryMode()
    {
        var editor = Render(Markdown);

        editor.Find(".cms-field-count").TextContent.Should().Contain("12 characters");
    }

    [Test]
    public void TheEditingSurfaceCarriesNoAriaOfItsOwnUntilTheLibraryMountsIntoIt()
    {
        var editor = Render(Markdown);

        // The name belongs on CodeMirror's own contenteditable element and is set there when it
        // mounts. On the host <div> it would be both prohibited — aria-label is not permitted on a
        // generic element — and useless, because the card's aria-labelledby cannot reach a control
        // the library created. The accessibility gate catches the first half; this catches a
        // regression that put it back.
        var host = editor.Find(".cms-source-editor");

        host.HasAttribute("aria-label").Should().BeFalse();
        host.HasAttribute("role").Should().BeFalse();
    }

    private IRenderedComponent<RichTextFieldEditor> Render(string value, string? configuration = null)
    {
        var slot = FieldEditorHarness.Slot(FieldTypeKeys.RichText, configuration);

        return _harness.Render<RichTextFieldEditor>(FieldEditorHarness.Context(slot), value);
    }

    private static void Choose(IRenderedComponent<RichTextFieldEditor> editor, string label) =>
        editor.FindAll("[role=radio]")
            .Single(option => option.TextContent.Contains(label, StringComparison.Ordinal))
            .Click();

    private const string Markdown = """{ "type": "richText", "format": "markdown", "value": "Three tiers." }""";

    private const string Html = """{ "type": "richText", "format": "html", "value": "<p>Three tiers.</p>" }""";

    /// <summary>Refuses every call, because none should be made without a picker being opened.</summary>
    private sealed class UnusedPages : StubPageClient;

    /// <summary>Refuses every call, for the same reason.</summary>
    private sealed class UnusedMedia : StubMediaClient;

    /// <summary>Records what the editor asked the server to render, and answers with a stub.</summary>
    private sealed class RecordingPreviewClient : IMarkupPreviewClient
    {
        public List<MarkupPreviewRequest> Requests { get; } = [];

        public IReadOnlyList<SanitizationRemoval> Removals { get; set; } = [];

        public Task<MarkupPreviewResult?> RenderAsync(
            MarkupPreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Task.FromResult<MarkupPreviewResult?>(
                new MarkupPreviewResult($"<p>{request.Source}</p>", Removals));
        }

        public Task<IReadOnlyList<SanitizationProfileDescriptor>> GetProfilesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SanitizationProfileDescriptor>>([]);
    }
}
