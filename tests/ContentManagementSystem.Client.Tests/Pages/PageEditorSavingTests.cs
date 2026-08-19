using Bunit;

using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Client.Components.Admin.Pages;
using ContentManagementSystem.Client.Components.Admin.Shortcuts;
using ContentManagementSystem.Client.Services;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ContentManagementSystem.Client.Tests.Pages;

/// <summary>
/// The page editor's saving loop (tasks P6-18 to P6-21).
/// </summary>
/// <remarks>
/// The component tests either side of this one cover the pieces; what only the screen can be wrong
/// about is how they are wired together. Three things here are wiring rather than behaviour, and all
/// three are silent when they break: the payload is written before the metadata, both writes adopt
/// the row version the server answers with, and a conflict opens the dialog instead of being
/// retried into a second lost race.
/// </remarks>
public class PageEditorSavingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 14, 30, 0, TimeSpan.Zero);

    private readonly BunitContext _bunit = new();

    private readonly FakeTimeProvider _clock = new(Now);

    private readonly EditorPageClient _client = new();

    public PageEditorSavingTests()
    {
        _bunit.Services.AddSingleton<IPageClient>(_client);
        _bunit.Services.AddSingleton<IToastService>(new SilentToastService());
        _bunit.Services.AddSingleton<TimeProvider>(_clock);
        _bunit.Services.AddSingleton<ICurrentUserClient>(new NobodyCurrentUserClient());
        // The page editor carries the review, schedule, and comment panels since P7-12. They ask
        // the server what to draw and draw nothing when the answer is nothing, which is what this
        // stub says — so a suite about the editor stays about the editor.
        _bunit.Services.AddSingleton<IWorkflowClient>(new SilentWorkflowClient());
        _bunit.Services.AddSingleton<IFieldEditorCatalog>(new FieldEditorCatalog());

        // The pinned-placement panel resolves this on construction and renders nothing when the
        // page has no pinned placement, which this fixture's does not.
        _bunit.Services.AddSingleton<IReusableClient>(new UnusedReusableClient());
        _bunit.JSInterop.Mode = JSRuntimeMode.Loose;

        _bunit.AddAuthorization().SetAuthorized("Elena").SetRoles(CmsRoles.Administrator);
    }

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void AScreenNobodyTypesIntoWritesNothing()
    {
        var editor = Render();

        _clock.Advance(AutosaveController.IdleDelay);

        editor.WaitForAssertion(() => _client.Writes.Should().BeEmpty());
    }

    [Test]
    public void TypingIntoAZoneIsSavedTwentySecondsLater()
    {
        var editor = Render();

        Type(editor, "A better headline");

        _clock.Advance(AutosaveController.IdleDelay);

        editor.WaitForAssertion(() =>
        {
            _client.Writes.Should().Equal("draft");
            _client.SavedPayloads.Should().ContainSingle()
                .Which.Should().Contain("A better headline");
        });
    }

    [Test]
    public void ThePayloadIsWrittenBeforeTheMetadataSoTheSecondWriteHoldsTheRightToken()
    {
        var editor = Render();

        Type(editor, "A better headline");
        editor.Find("#page-meta-description").Input("What our plans cost.");

        _clock.Advance(AutosaveController.IdleDelay);

        editor.WaitForAssertion(() =>
        {
            // A metadata patch writes the title and the SEO fields to the same draft row, so it
            // moves the row version the payload save is conditional on. The other order would make
            // an editor's next keystroke a conflict with themselves.
            _client.Writes.Should().Equal("draft", "metadata");

            _client.Preconditions.Should().ContainSingle().Which.Should().Be(
                "rv-1",
                "the draft save carries the token the screen was opened with");
        });
    }

    [Test]
    public void OnlyTheFieldsTheEditorTouchedAreSent()
    {
        var editor = Render();

        editor.Find("#page-meta-description").Input("What our plans cost.");

        _clock.Advance(AutosaveController.IdleDelay);

        editor.WaitForAssertion(() =>
        {
            var patch = _client.Patches.Should().ContainSingle().Subject;

            patch.MetaDescription.Value.Should().Be("What our plans cost.");
            patch.Title.IsSet.Should().BeFalse();
            patch.Slug.IsSet.Should().BeFalse();
        });
    }

    [Test]
    public void ARefusedDetailIsReportedInThePaneThatCanActOnItAndNotTheOther()
    {
        _client.NextPatchFails = true;

        var editor = Render();

        editor.Find("#page-slug").Input("not a slug");

        _clock.Advance(AutosaveController.IdleDelay);

        editor.WaitForAssertion(() =>
        {
            // Beside the slug box, where the editor can fix it — and nowhere else. Pooling the two
            // writes' diagnostics would print this again on the canvas, in the pane that cannot act
            // on it.
            editor.Find("#page-slug").ClassList.Should().Contain("is-invalid");
            editor.Find(".cms-field-messages").TextContent.Should().Contain("may not contain spaces");
            editor.FindAll(".cms-canvas .alert").Should().BeEmpty();
        });
    }

    [Test]
    public void ASaveThatLosesARaceOpensTheConflictDialogRatherThanBeingRetried()
    {
        _client.NextSaveConflicts = true;

        var editor = Render();

        Type(editor, "Mine");

        _clock.Advance(AutosaveController.IdleDelay);

        editor.WaitForAssertion(() =>
            editor.Find(".cms-conflict").TextContent.Should().Contain("saved this page first"));

        // And it stays there. Autosave must not keep firing the same losing request every twenty
        // seconds: it needs a decision from a person (acceptance criterion P6 #6).
        _clock.Advance(TimeSpan.FromMinutes(2));

        editor.WaitForAssertion(() => _client.Writes.Should().Equal("draft"));

        editor.Find(".cms-save-state").TextContent.Should().Contain("Not saved");
    }

    [Test]
    public void KeepingMineResendsTheSameTextWithTheWinnersToken()
    {
        _client.NextSaveConflicts = true;

        var editor = Render();

        Type(editor, "Mine");

        _clock.Advance(AutosaveController.IdleDelay);

        editor.WaitForAssertion(() => editor.Find(".cms-conflict").Should().NotBeNull());

        editor.WaitForAssertion(() =>
        {
            editor.Find(".cms-conflict .btn-primary").Click();
            _client.Writes.Should().Equal("draft", "draft");
        });

        _client.Preconditions.Should().Equal(
            "rv-1",
            "rv-theirs");
        _client.SavedPayloads[1].Should().Contain(
            "Mine",
            "nothing an editor typed is dropped by choosing to keep it");
    }

    [Test]
    public void TheSaveStateSaysWhenRatherThanJustThatItSaved()
    {
        var editor = Render();

        Type(editor, "A better headline");

        _clock.Advance(AutosaveController.IdleDelay);

        editor.WaitForAssertion(() =>
            editor.Find(".cms-save-state").TextContent.Should().Contain(
                Now.ToLocalTime().ToString("HH:mm"),
                "\"Saved\" on its own is indistinguishable from \"saved twenty minutes ago and " +
                "quietly broken since\""));
    }

    [Test]
    public async Task TheSaveShortcutRunsTheSameSaveTheButtonDoes()
    {
        var editor = Render();

        Type(editor, "Typed and then saved by chord");

        // Driven through the listener rather than through a key press, because the press it answers
        // to lands on the document (task P6-23). What this pins is the wiring: the chord reaches the
        // screen's own save, without waiting out the idle delay.
        var claimed = await editor.InvokeAsync(() => editor.FindComponent<ShortcutListener>()
            .Instance.MatchAsync("s", control: true, shift: false, alt: false));

        claimed.Should().BeTrue("the chord is one the editor defined, so the browser must not keep it");

        _client.SavedPayloads.Should().ContainSingle()
            .Which.Should().Contain("Typed and then saved by chord");
    }

    [Test]
    public async Task TheHelpShortcutOpensTheListOfShortcuts()
    {
        var editor = Render();

        await editor.InvokeAsync(() => editor.FindComponent<ShortcutListener>()
            .Instance.MatchAsync("?", control: false, shift: true, alt: false));

        editor.Find("[role=dialog]").TextContent.Should().Contain("Keyboard shortcuts");

        editor.FindAll("[role=dialog] .btn-outline-secondary").First().Click();
    }

    private IRenderedComponent<PageEditor> Render() =>
        _bunit.Render<PageEditor>(parameters => parameters.Add(editor => editor.Id, EditorPageClient.PageId));

    /// <summary>Types into the one zone the fixture's template declares.</summary>
    private static void Type(IRenderedComponent<PageEditor> editor, string text) =>
        editor.Find("#zone-hero input[type=text]").Input(text);

    /// <summary>A page with one plain-text zone, and a record of every write it was sent.</summary>
    private sealed class EditorPageClient : StubPageClient
    {
        public const int PageId = 4;

        /// <summary>Which endpoints were called, in order.</summary>
        public List<string> Writes { get; } = [];

        /// <summary>The row version each draft save was conditional on.</summary>
        public List<string?> Preconditions { get; } = [];

        /// <summary>The payloads that were sent.</summary>
        public List<string?> SavedPayloads { get; } = [];

        /// <summary>The metadata patches that were sent.</summary>
        public List<PatchPageMetadataRequest> Patches { get; } = [];

        /// <summary>Whether the next draft save should lose a race.</summary>
        public bool NextSaveConflicts { get; set; }

        /// <summary>Whether the next metadata patch should be refused.</summary>
        public bool NextPatchFails { get; set; }

        private PageDetail _page = Detail("rv-1");

        public override Task<PageDetail?> GetAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PageDetail?>(_page);

        public override Task<PageLink?> ResolveLinkAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PageLink?>(new PageLink(id, "/pricing", true, "Pricing"));

        public override Task<IReadOnlyList<CapturedSlot>> GetZonesAsync(
            int templateId,
            int revision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CapturedSlot>>(
                [new("hero", "Hero", FieldTypeKeys.PlainText, false, 0, null)]);

        public override Task<StructureClientResult<DraftSaveResult>> SaveDraftAsync(
            int id,
            SaveDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            Writes.Add("draft");
            Preconditions.Add(request.ExpectedRowVersion);
            SavedPayloads.Add(request.ContentJson);

            if (NextSaveConflicts)
            {
                NextSaveConflicts = false;

                // The refusal carries the draft that won, exactly as the API's 409 body does.
                return Task.FromResult(StructureClientResult<DraftSaveResult>.Refused(
                    new DraftSaveResult(Draft("rv-theirs", """{"theirs":true}"""), [], 0),
                    [new ApiDiagnostic(
                        PageCodes.ConcurrentChange,
                        "This draft was saved by someone else after you opened it.",
                        nameof(SaveDraftRequest.ExpectedRowVersion))]));
            }

            return Task.FromResult(StructureClientResult<DraftSaveResult>.Success(
                new DraftSaveResult(Draft("rv-2", request.ContentJson ?? "{}"), [], 1)));
        }

        public override Task<StructureClientResult<PageDetail>> PatchMetadataAsync(
            int id,
            PatchPageMetadataRequest request,
            CancellationToken cancellationToken = default)
        {
            Writes.Add("metadata");
            Patches.Add(request);

            if (NextPatchFails)
            {
                NextPatchFails = false;

                return Task.FromResult(StructureClientResult<PageDetail>.Failure(
                    [new ApiDiagnostic(
                        "page.slug-invalid",
                        "A slug may not contain spaces.",
                        nameof(PatchPageMetadataRequest.Slug))]));
            }

            _page = _page with
            {
                RowVersion = "rv-3",
                Seo = _page.Seo with
                {
                    MetaDescription = request.MetaDescription.Or(_page.Seo.MetaDescription),
                },
            };

            return Task.FromResult(StructureClientResult<PageDetail>.Success(_page));
        }

        private static DraftState Draft(string rowVersion, string contentJson) => new(
            PageId,
            11,
            2,
            contentJson,
            "marketing-landing",
            2,
            rowVersion,
            Now);

        private static PageDetail Detail(string rowVersion) => new(
            StubPageClient.Page(PageId, "Pricing"),
            """{"schemaVersion":1,"templateKey":"marketing-landing","templateRevision":2,"zones":{}}""",
            TemplateRevision: 2,
            UseExplicitUrl: false,
            ExplicitUrl: null,
            OwnerUserId: null,
            ReviewByDate: null,
            InternalNotes: null,
            Seo: new PageSeo(null, null, null, true, true, null, null, null, null, null, null, null, null),
            RowVersion: rowVersion);
    }

    /// <summary>
    /// The reusable client the pinned-placement panel resolves and never calls here.
    /// </summary>
    /// <remarks>
    /// It reads the payload first and asks about nothing when no placement is pinned, which this
    /// fixture's payload has none of — so every member is left refusing, and a screen that started
    /// calling one would say so loudly.
    /// </remarks>
    private sealed class UnusedReusableClient : StubReusableClient;

    /// <summary>Nobody is signed in as far as the owner field is concerned.</summary>
    private sealed class NobodyCurrentUserClient : ICurrentUserClient
    {
        public Task<CurrentUser?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CurrentUser?>(null);
    }
}
