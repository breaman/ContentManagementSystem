using Bunit;

using ContentManagementSystem.Client.Components.Admin.Properties;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Client.Tests.Properties;

/// <summary>
/// The properties panel (task P6-17, spec sections 14.7 and 18.1).
/// </summary>
/// <remarks>
/// Two things here are worth more than the rendering. The patch must carry only what the editor
/// touched — the whole reason the request contract is built out of <c>Patch&lt;T&gt;</c> — and the
/// search-result preview must show the rules an editor cannot otherwise see: that a blank meta
/// title falls back to the page title, and that both fields are truncated rather than refused.
/// </remarks>
public class PropertiesPanelTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    private readonly PanelPageClient _client = new();

    public PropertiesPanelTests()
    {
        _bunit.Services.AddSingleton<IPageClient>(_client);
        _bunit.Services.AddSingleton<ISearchClient>(new EmptySearchClient());
        _bunit.Services.AddSingleton<ICurrentUserClient>(new StubCurrentUserClient(
            new CurrentUser(7, "Elena")));
    }

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void APatchCarriesOnlyTheFieldsTheEditorTouched()
    {
        var page = Page();
        var model = PageProperties.From(page);

        model.MetaDescription = "The plans, and what each of them costs.";

        var patch = model.ToPatch(page);

        patch.MetaDescription.IsSet.Should().BeTrue();
        patch.MetaDescription.Value.Should().Be("The plans, and what each of them costs.");

        patch.Title.IsSet.Should().BeFalse(
            "sending an untouched field would reinstate this editor's copy of it over whatever a " +
            "colleague changed in the meantime");
        patch.Slug.IsSet.Should().BeFalse();
        patch.RobotsIndex.IsSet.Should().BeFalse();
        patch.InternalNotes.IsSet.Should().BeFalse();
    }

    [Test]
    public void ClearingAFieldIsSentAsAClearingRatherThanOmitted()
    {
        var page = Page(metaTitle: "Pricing | Contoso");
        var model = PageProperties.From(page);

        model.MetaTitle = "   ";

        var patch = model.ToPatch(page);

        patch.MetaTitle.IsSet.Should().BeTrue();
        patch.MetaTitle.Value.Should().BeNull(
            "an omitted member means \"leave it alone\", which is the opposite of what emptying a " +
            "box means");
    }

    [Test]
    public void WhitespaceRoundATrimmedValueIsNotAChange()
    {
        var page = Page(metaTitle: "Pricing");
        var model = PageProperties.From(page);

        model.MetaTitle = "  Pricing  ";

        model.HasChanges(page).Should().BeFalse(
            "the server stores it trimmed, so reporting this as unsaved work would leave an " +
            "indicator that never goes out");
    }

    [Test]
    public void ChangingNothingLeavesNothingToSave()
    {
        var page = Page();

        PageProperties.From(page).HasChanges(page).Should().BeFalse();
    }

    [Test]
    public void TheSearchPreviewFallsBackToThePageTitleAndSaysSo()
    {
        var page = Page();
        var panel = Render(page, PageProperties.From(page));

        var preview = panel.Find(".cms-serp__title").TextContent;

        preview.Should().Contain("Pricing").And.Contain("from the page title");
    }

    [Test]
    public void TheSearchPreviewTruncatesTheWayAResultDoes()
    {
        var page = Page();
        var model = PageProperties.From(page);

        model.MetaDescription = string.Join(' ', Enumerable.Repeat("plans", 60));

        var panel = Render(page, model);
        var description = panel.Find(".cms-serp__description").TextContent.Trim();

        description.Should().EndWith("…");
        description.Length.Should().BeLessThan(
            model.MetaDescription.Length,
            "showing the whole thing would hide the one fact this widget exists to show");
    }

    [Test]
    public void TheCountersGuideRatherThanRefuse()
    {
        var page = Page();
        var model = PageProperties.From(page);

        model.MetaDescription = new string('x', SearchResultPreview.DescriptionLimit + 1);

        var panel = Render(page, model);

        // Advisory wording, and against the soft limit rather than the column size: a long meta
        // description is truncated in results, not refused on save (task P6-12).
        panel.Find("#page-meta-description-count").TextContent
            .Should().Contain("You can still publish it");
    }

    [Test]
    public void ARefusalLandsOnTheFieldItIsAbout()
    {
        var page = Page();
        var panel = Render(
            page,
            PageProperties.From(page),
            [new ApiDiagnostic("page.slug-invalid", "A slug may not contain spaces.", "Slug")]);

        panel.Find("#page-slug").ClassList.Should().Contain("is-invalid");
        panel.Find(".cms-field-messages").TextContent.Should().Contain("may not contain spaces");

        panel.FindAll(".alert").Should().BeEmpty(
            "a message that landed on its field must not also be repeated at the top of the panel");
    }

    [Test]
    public void ARefusalAboutNothingThisPanelDrawsIsStillReported()
    {
        var page = Page();
        var panel = Render(
            page,
            PageProperties.From(page),
            [new ApiDiagnostic("page.template-disabled", "That template is disabled.")]);

        panel.Find(".alert").TextContent.Should().Contain("That template is disabled");
    }

    [Test]
    public void TakingOwnershipWritesTheSignedInEditorsOwnId()
    {
        var page = Page();
        var model = PageProperties.From(page);
        var changed = 0;

        var panel = Render(page, model, onChanged: () => changed++);

        panel.Find(".cms-properties__owner-name").TextContent.Trim()
            .Should().Be("Nobody owns this page.");

        panel.WaitForAssertion(() =>
        {
            panel.FindAll("button").Single(button => button.TextContent.Contains("Take ownership")).Click();
            model.OwnerUserId.Should().Be(7);
        });

        changed.Should().Be(1, "an edit here is an edit, and autosave is what writes it");
        model.ToPatch(page).OwnerUserId.Value.Should().Be(7);
    }

    [Test]
    public void TheOwnerIsNamedRatherThanNumbered()
    {
        var page = Page(ownerUserId: 12, ownerName: "Marcus");
        var panel = Render(page, PageProperties.From(page));

        panel.Find(".cms-properties__owner-name").TextContent.Should().Contain("Marcus");
    }

    [Test]
    public void ThePreviewShowsTheAddressThePageIsActuallyServedAt()
    {
        var page = Page();
        var panel = Render(page, PageProperties.From(page));

        panel.Find(".cms-serp__url").TextContent.Should().Contain("/plans/pricing");
        _client.Resolved.Should().ContainSingle().Which.Should().Be(4);
    }

    [Test]
    public void ATagIsAddedOnEnterAndTheSameLabelTwiceIsOneTag()
    {
        var page = Page();
        var model = PageProperties.From(page);
        var panel = Render(page, model);

        AddTag(panel, "Release notes");
        AddTag(panel, "release NOTES");

        // Slug is identity on the server, so a panel that showed both spellings would be showing a
        // state that cannot exist (task P8-20).
        model.Tags.Should().Equal("Release notes");
        panel.FindAll(".cms-properties__tag").Should().ContainSingle();
    }

    [Test]
    public void AHalfTypedTagIsNotCommittedByAnyKeyButEnter()
    {
        var page = Page();
        var model = PageProperties.From(page);
        var panel = Render(page, model);

        panel.Find("#page-tag-entry").Input("relea");
        panel.Find("#page-tag-entry").KeyDown("Tab");

        // The box carries no blur handler at all, which is the other half of this: an editor who
        // clicks away from a half-typed word has not decided to tag the page with it, and a tag
        // added by accident is one that has to be found again to be removed.
        model.Tags.Should().BeEmpty();
    }

    [Test]
    public void TagsAreSentAsTheWholeSetAndOnlyWhenTheyMoved()
    {
        var page = Page(tags: ["alpha", "beta"]);
        var model = PageProperties.From(page);

        model.ToPatch(page).Tags.IsSet.Should().BeFalse("nothing was touched");

        // Reordering is not an edit: the server returns them alphabetically and the panel appends.
        model.Tags = ["beta", "alpha"];
        model.ToPatch(page).Tags.IsSet.Should().BeFalse();

        model.Tags.Remove("alpha");

        var patch = model.ToPatch(page);

        patch.Tags.IsSet.Should().BeTrue();
        patch.Tags.Value.Should().ContainSingle("the patch carries what the page should end up with")
            .Which.Should().Be("beta");
    }

    /// <summary>Types a tag into the box and commits it the way an editor does.</summary>
    private static void AddTag(IRenderedComponent<PropertiesPanel> panel, string tag)
    {
        panel.Find("#page-tag-entry").Input(tag);
        panel.Find("#page-tag-entry").KeyDown("Enter");
    }

    private IRenderedComponent<PropertiesPanel> Render(
        PageDetail page,
        PageProperties model,
        IReadOnlyList<ApiDiagnostic>? diagnostics = null,
        Action? onChanged = null) =>
        _bunit.Render<PropertiesPanel>(parameters => parameters
            .Add(panel => panel.Page, page)
            .Add(panel => panel.Model, model)
            .Add(panel => panel.Diagnostics, diagnostics)
            .Add(panel => panel.OnChanged, () => onChanged?.Invoke()));

    private static PageDetail Page(
        string? metaTitle = null,
        int? ownerUserId = null,
        string? ownerName = null,
        IReadOnlyList<string>? tags = null) =>
        new(
            StubPageClient.Page(4, "Pricing"),
            """{"templateKey":"marketing-landing","templateRevision":2,"zones":{}}""",
            TemplateRevision: 2,
            UseExplicitUrl: false,
            ExplicitUrl: null,
            OwnerUserId: ownerUserId,
            ReviewByDate: null,
            InternalNotes: null,
            Seo: new PageSeo(metaTitle, null, null, true, true, null, null, null, null, null, null, null, null),
            RowVersion: "AAAAAAAAB9M=",
            OwnerName: ownerName,
            Tags: tags);

    /// <summary>Resolves the page's URL and records that it was asked once.</summary>
    private sealed class PanelPageClient : StubPageClient
    {
        public List<int> Resolved { get; } = [];

        public override Task<PageLink?> ResolveLinkAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            Resolved.Add(id);

            return Task.FromResult<PageLink?>(new PageLink(id, "/plans/pricing", true, "Pricing"));
        }
    }

    /// <summary>Answers with a signed-in editor, as the API's <c>/me</c> does.</summary>
    private sealed class StubCurrentUserClient(CurrentUser? user) : ICurrentUserClient
    {
        public Task<CurrentUser?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(user);
    }
}
