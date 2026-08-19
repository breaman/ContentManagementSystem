using System.Text.RegularExpressions;

using AngleSharp.Dom;

using Bunit;

using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Client.Components.Admin.Pages;
using ContentManagementSystem.Client.Components.Admin.Pickers;
using ContentManagementSystem.Client.Components.Admin.Properties;
using ContentManagementSystem.Client.Components.Admin.Reusable;
using ContentManagementSystem.Client.Services;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Preview;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ContentManagementSystem.Client.Tests.Versions;

/// <summary>
/// The "v5" labels that every screen writes a version number into.
/// </summary>
/// <remarks>
/// These screens all render a version as the letter <c>v</c> followed by an expression, and Razor
/// reads a bare <c>@</c> that follows a letter as the start of an email address rather than as a
/// transition — so <c>v@@version.VersionNumber</c> compiles clean, warns about nothing, and prints
/// its own source onto the page. Twenty of these were live at once, because nothing here was ever
/// asserted on and the parenthesised form beside a broken one looks identical at a glance.
/// <para>
/// So each test pins the number an editor is meant to read, and <see cref="NoLabelLeakedItsSource"/>
/// sweeps every one of these screens for the failure's signature — which is what catches the next
/// label, in markup no other assertion happens to look at. The fixture is deliberately not numbered
/// 1 and 2: a draft at v7 published at v5 cannot be matched by an accident.
/// </para>
/// </remarks>
public class VersionLabelTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    public VersionLabelTests()
    {
        _bunit.JSInterop.Mode = JSRuntimeMode.Loose;
        _bunit.Services.AddSingleton<IToastService>(new SilentToastService());
        _bunit.Services.AddSingleton<TimeProvider>(
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero)));
        _bunit.Services.AddSingleton<ICurrentUserClient>(new SignedInClient());
        // The page editor carries the review, schedule, and comment panels since P7-12. They ask
        // the server what to draw and draw nothing when the answer is nothing, which is what this
        // stub says — so a suite about the editor stays about the editor.
        _bunit.Services.AddSingleton<IWorkflowClient>(new SilentWorkflowClient());
        _bunit.Services.AddSingleton<IFieldEditorCatalog>(new FieldEditorCatalog());
        _bunit.Services.AddSingleton<IPageClient>(new VersionPageClient());
        _bunit.Services.AddSingleton<IReusableClient>(new VersionReusableClient());

        _bunit.AddAuthorization().SetAuthorized("Elena").SetRoles(CmsRoles.Administrator);
    }

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void ThePageListNumbersBothTheDraftAndTheLiveVersion()
    {
        var cells = PageList().Find("tbody tr").QuerySelectorAll("td");

        // Columns are: slug, template, draft, published, state.
        cells[2].TextContent.Trim().Should().Be("v7");
        cells[3].TextContent.Trim().Should().Be("v5");
    }

    [Test]
    public void TheReusableLibraryNumbersBothTheDraftAndTheLiveVersion()
    {
        var cells = ReusableLibrary().Find("tbody tr").QuerySelectorAll("td");

        // Columns are: key, shape, draft, published, state.
        cells[2].TextContent.Trim().Should().Be("v7");
        cells[3].TextContent.Trim().Should().Be("v5");
    }

    [Test]
    public void ThePageHistoryNumbersEveryRow() =>
        PageVersions().FindAll("tbody th[scope=row]")
            .Select(VersionLabel).Should().Equal("v7", "v5");

    [Test]
    public void TheReusableHistoryNumbersEveryRow() =>
        ReusableEditor().FindAll("tbody th[scope=row]")
            .Select(VersionLabel).Should().Equal("v7", "v5");

    [Test]
    public void ADiffHeadingNamesTheTwoVersionsItCompared()
    {
        var history = PageVersions();

        // The screen preselects the published version against the draft, so this compares v5 with
        // v7 without anything being chosen first.
        history.FindAll("button").Single(button => button.TextContent.Contains("Compare")).Click();

        history.WaitForAssertion(() =>
            Flattened(history.Find("h2").TextContent).Should().Be("v5 → v7"));
    }

    [Test]
    [Arguments(nameof(PageEditor))]
    [Arguments(nameof(ReusableEditor))]
    public void AnEditorSaysWhichVersionIsStillLiveWhileItsDraftHasMovedOn(string screen)
    {
        var warning = screen == nameof(PageEditor)
            ? PageEditor().Find(".alert-warning")
            : ReusableEditor().Find(".alert-warning");

        Flattened(warning.TextContent)
            .Should().EndWith("still shows v5 until you publish again.");
    }

    [Test]
    public void ThePageEditorsHeaderNumbersTheDraftAndTheLiveVersion() =>
        Flattened(PageEditor().Find("h1 + p").TextContent)
            .Should().EndWith("draft v7 · published v5");

    [Test]
    public void TheReusableEditorsHeaderNumbersTheDraftAndTheLiveVersion() =>
        Flattened(ReusableEditor().Find("h1 + p").TextContent)
            .Should().EndWith("draft v7 · published v5");

    [Test]
    public void ThePropertiesPanelNumbersTheDraftAndTheLiveVersion()
    {
        var panel = PropertiesPanel();
        var terms = panel.FindAll("dd").Select(term => Flattened(term.TextContent)).ToList();

        terms.Should().Contain(term => term.StartsWith("v7", StringComparison.Ordinal));
        terms.Should().Contain(term => term.Contains("v5", StringComparison.Ordinal));
    }

    [Test]
    public void EveryVersionOfferedAsAPreviewLinkIsNumbered() =>
        PreviewLinks().FindAll("#version option")
            .Skip(1)  // "Current draft", which has no number to print.
            .Select(option => option.TextContent.Trim().Split(' ')[0])
            .Should().Equal("v7", "v5");

    [Test]
    public void AnAlreadyIssuedPreviewLinkNamesTheVersionItShows() =>
        PreviewLinks().Find("tbody th[scope=row]").TextContent.Trim().Should().Be("v5");

    [Test]
    public void AFreshlyIssuedPreviewLinkNamesTheVersionItShows()
    {
        var links = PreviewLinks();

        links.FindAll("button").Single(button => button.TextContent.Contains("Issue link")).Click();

        links.WaitForAssertion(() =>
            Flattened(links.Find(".alert-success").TextContent).Should().Contain("Shows v7, expires"));
    }

    [Test]
    public void ThePickerNamesTheVersionAPlacementWouldActuallyShow() =>
        Flattened(ReusablePicker().Find(".cms-picker__option-meta").TextContent)
            .Should().EndWith("· showing v5");

    /// <summary>
    /// Every screen above, swept for the shape the bug takes: a version label that printed the
    /// expression instead of evaluating it.
    /// </summary>
    /// <remarks>
    /// The tests above read the cells a person reads, which is the better failure message when one
    /// of them breaks. This is the net under them, and the only thing that covers a label added
    /// tomorrow.
    /// </remarks>
    [Test]
    [Arguments(nameof(PageList))]
    [Arguments(nameof(PageEditor))]
    [Arguments(nameof(PageVersions))]
    [Arguments(nameof(PagePreviewLinks))]
    [Arguments(nameof(PropertiesPanel))]
    [Arguments(nameof(ReusableLibrary))]
    [Arguments(nameof(ReusableEditor))]
    [Arguments(nameof(ReusablePicker))]
    public void NoLabelLeakedItsSource(string screen)
    {
        var markup = screen switch
        {
            nameof(PageList) => PageList().Markup,
            nameof(PageEditor) => PageEditor().Markup,
            nameof(PageVersions) => PageVersions().Markup,
            nameof(PagePreviewLinks) => PreviewLinks().Markup,
            nameof(PropertiesPanel) => PropertiesPanel().Markup,
            nameof(ReusableLibrary) => ReusableLibrary().Markup,
            nameof(ReusableEditor) => ReusableEditor().Markup,
            _ => ReusablePicker().Markup,
        };

        // "v@" is the signature itself; the rest catch a leak whose prefix is not the letter v.
        markup.Should().NotContainAny(
            ["v@", "@version.", "@node.", "@item.", "@Item.", "@Page.", "@Diff.", "@token.",
             "@Issued.", "@published", "@retired"],
            "a version label that reaches the browser as its own source means Razor read the `@` as " +
            "the start of an email address; the fix is to parenthesise it, as in " +
            "`v@(version.VersionNumber)`");
    }

    private IRenderedComponent<PageList> PageList() =>
        _bunit.Render<PageList>();

    private IRenderedComponent<PageEditor> PageEditor() =>
        _bunit.Render<PageEditor>(parameters => parameters.Add(screen => screen.Id, PageId));

    private IRenderedComponent<PageVersions> PageVersions() =>
        _bunit.Render<PageVersions>(parameters => parameters.Add(screen => screen.Id, PageId));

    private IRenderedComponent<PagePreviewLinks> PreviewLinks() =>
        _bunit.Render<PagePreviewLinks>(parameters => parameters.Add(screen => screen.Id, PageId));

    private IRenderedComponent<PropertiesPanel> PropertiesPanel() =>
        _bunit.Render<PropertiesPanel>(parameters => parameters
            .Add(panel => panel.Page, PageDetail())
            .Add(panel => panel.Model, PageProperties.From(PageDetail())));

    private IRenderedComponent<ReusableLibrary> ReusableLibrary() =>
        _bunit.Render<ReusableLibrary>();

    private IRenderedComponent<ReusableEditor> ReusableEditor() =>
        _bunit.Render<ReusableEditor>(parameters => parameters.Add(screen => screen.Id, ItemId));

    private IRenderedComponent<ReusablePicker> ReusablePicker() =>
        _bunit.Render<ReusablePicker>(parameters => parameters.Add(picker => picker.IsOpen, true));

    /// <summary>
    /// The version number at the head of a history row, without the status badge that abuts it.
    /// </summary>
    /// <remarks>
    /// A match rather than a trim on purpose: a row that leaked its source matches nothing and
    /// fails, where <c>StartWith("v7")</c> would pass anything beginning with those two characters.
    /// </remarks>
    private static string VersionLabel(IElement header) =>
        Regex.Match(header.TextContent.Trim(), @"^v\d+").Value;

    /// <summary>Collapses the newlines and indentation Razor leaves inside a block of text.</summary>
    private static string Flattened(string text) =>
        Regex.Replace(text, @"\s+", " ").Trim();

    private const int PageId = 4;

    private const int ItemId = 9;

    /// <summary>A page whose draft is at v7 while v5 is what the public site still serves.</summary>
    private static PageSummary PricingPage() =>
        StubPageClient.Page(PageId, "Pricing", published: 5, unpublishedChanges: true) with
        {
            DraftVersionNumber = 7,
        };

    private static PageDetail PageDetail() =>
        new(
            PricingPage(),
            """{"templateKey":"marketing-landing","templateRevision":2,"zones":{}}""",
            TemplateRevision: 2,
            UseExplicitUrl: false,
            ExplicitUrl: null,
            OwnerUserId: null,
            ReviewByDate: null,
            InternalNotes: null,
            Seo: new PageSeo(null, null, null, true, true, null, null, null, null, null, null, null, null),
            RowVersion: "AAAAAAAAB9M=",
            OwnerName: null);

    /// <summary>The same story, told about a reusable item.</summary>
    private static ReusableContentSummary FooterItem() =>
        new(
            Id: ItemId,
            Key: "site-footer",
            Name: "Site footer",
            Description: null,
            FolderId: null,
            BlockTypeId: 1,
            BlockTypeKey: "rawHtml",
            Status: "Draft",
            HasUnpublishedChanges: true,
            DraftVersionNumber: 7,
            PublishedVersionNumber: 5,
            ModifiedOn: DateTimeOffset.UnixEpoch);

    /// <summary>Everything the page-side screens read, and nothing else.</summary>
    private sealed class VersionPageClient : StubPageClient
    {
        public override Task<IReadOnlyList<PageTreeNode>> GetTreeAsync(
            int? parentId = null,
            int depth = 1,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PageTreeNode>>([new PageTreeNode(PricingPage(), [])]);

        public override Task<IReadOnlyList<TemplateSummary>> GetTemplatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TemplateSummary>>(
                [new TemplateSummary(1, "marketing-landing", "Marketing landing page", null, null,
                    IsOrphaned: false, IsEnabled: true, CurrentRevision: 2, SortOrder: 1, ZoneCount: 2)]);

        public override Task<PageDetail?> GetAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PageDetail?>(PageDetail());

        public override Task<IReadOnlyList<CapturedSlot>> GetZonesAsync(
            int templateId,
            int revision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CapturedSlot>>([]);

        public override Task<PageLink?> ResolveLinkAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PageLink?>(new PageLink(id, "/plans/pricing", true, "Pricing"));

        public override Task<IReadOnlyList<PageVersionSummary>> GetVersionsAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PageVersionSummary>>(
            [
                Version(70, 7, "Draft", isDraft: true, isPublished: false),
                Version(50, 5, "Published", isDraft: false, isPublished: true),
            ]);

        public override Task<ContentDiff?> GetDiffAsync(
            int id,
            int fromVersionId,
            int toVersionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ContentDiff?>(new ContentDiff(id, 50, 70, 5, 7, [], []));

        public override Task<IReadOnlyList<PreviewTokenSummary>> GetPreviewTokensAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PreviewTokenSummary>>([Token(5)]);

        /// <summary>Issues against the draft, so the banner's number differs from the table's.</summary>
        public override Task<StructureClientResult<IssuedPreviewToken>> IssuePreviewTokenAsync(
            CreatePreviewTokenRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StructureClientResult<IssuedPreviewToken>.Success(
                new IssuedPreviewToken(Token(7), "secret", "https://example.test/preview/secret")));

        private static PageVersionSummary Version(
            int id,
            int number,
            string status,
            bool isDraft,
            bool isPublished) =>
            new(id, number, status, Label: null, Title: "Pricing", TemplateRevision: 2,
                isDraft, isPublished, CreatedOn: DateTimeOffset.UnixEpoch, CreatedBy: 7,
                PublishedOn: isPublished ? DateTimeOffset.UnixEpoch : null,
                PublishedBy: isPublished ? 7 : null);

        private static PreviewTokenSummary Token(int versionNumber) =>
            new(Id: versionNumber, PageId: PageId, PageVersionId: versionNumber * 10, versionNumber,
                VersionStatus: "Published", CreatedOn: DateTimeOffset.UnixEpoch, CreatedBy: 7,
                ExpiresOn: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), MaxUses: null,
                UseCount: 0, RevokedOn: null, IsActive: true, Notes: null);
    }

    /// <summary>Everything the reusable-side screens read, and nothing else.</summary>
    private sealed class VersionReusableClient : StubReusableClient
    {
        public override Task<IReadOnlyList<ReusableContentSummary>> ListAsync(
            int? folderId = null,
            string? search = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReusableContentSummary>>([FooterItem()]);

        public override Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BlockTypeSummary>>([]);

        public override Task<ReusableContentDetail?> GetAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ReusableContentDetail?>(new ReusableContentDetail(
                FooterItem(), "{}", BlockTypeRevision: 1, RowVersion: "AAAAAAAAB9M="));

        public override Task<IReadOnlyList<CapturedSlot>> GetPropertiesAsync(
            int blockTypeId,
            int revision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CapturedSlot>>([]);

        public override Task<IReadOnlyList<ReusableVersionSummary>> GetVersionsAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReusableVersionSummary>>(
            [
                new(70, 7, "Draft", Label: null, BlockTypeRevision: 1, IsDraft: true,
                    IsPublished: false, CreatedOn: DateTimeOffset.UnixEpoch, CreatedBy: 7,
                    PublishedOn: null, PublishedBy: null),
                new(50, 5, "Published", Label: null, BlockTypeRevision: 1, IsDraft: false,
                    IsPublished: true, CreatedOn: DateTimeOffset.UnixEpoch, CreatedBy: 7,
                    PublishedOn: DateTimeOffset.UnixEpoch, PublishedBy: 7),
            ]);

        public override Task<ReferenceImpact> WhereUsedAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReferenceImpact.None);
    }

    /// <summary>Answers with a signed-in editor, as the API's <c>/me</c> does.</summary>
    private sealed class SignedInClient : ICurrentUserClient
    {
        public Task<CurrentUser?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CurrentUser?>(new CurrentUser(7, "Elena"));
    }
}
