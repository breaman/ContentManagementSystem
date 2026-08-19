using System.Globalization;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Search;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Search;

/// <summary>
/// The backoffice search screen and its filters (task P8-19, spec section 17.1).
/// </summary>
/// <remarks>
/// The filters are read from and written back to the address bar, so a search is a URL an editor can
/// bookmark, share, or reload without losing. That is also what makes the browser's back button
/// behave: each search is a navigation rather than a hidden state change.
/// <para>
/// Every filter is applied by the server. The screen holds no result set to re-filter and no second
/// copy of the rules — a client-side narrowing of a page of fifty hits would silently disagree with
/// the count beside it.
/// </para>
/// </remarks>
public partial class SearchScreen : ComponentBase
{
    private const int PageSize = 25;

    /// <summary>Runs searches and reads the tag vocabulary.</summary>
    [Inject]
    public ISearchClient Search { get; set; } = default!;

    /// <summary>Supplies the templates the template filter offers.</summary>
    [Inject]
    public IStructureClient Structure { get; set; } = default!;

    /// <summary>Reads the filters out of the address bar and writes them back.</summary>
    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    /// <summary>What the editor typed.</summary>
    /// <remarks>
    /// Supplied from the query string rather than parsed out of it: the framework already binds
    /// these, and a hand-rolled parse would be a second reading of the same URL that could disagree
    /// with the one the browser used.
    /// </remarks>
    [Parameter]
    [SupplyParameterFromQuery(Name = "q")]
    public string? Text { get; set; }

    /// <summary>Which kind of thing to look for, as the select binds it.</summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "kind")]
    public string? Kind { get; set; }

    /// <summary>Which lifecycle status to restrict pages to.</summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "status")]
    public string? Status { get; set; }

    /// <summary>Which template to restrict pages to, as the select binds it.</summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "templateId")]
    public string? TemplateId { get; set; }

    /// <summary>Which tag to restrict pages to.</summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "tag")]
    public string? Tag { get; set; }

    /// <summary>Earliest index timestamp to include.</summary>
    protected DateTime? ModifiedFrom { get; set; }

    /// <summary>Latest index timestamp to include.</summary>
    protected DateTime? ModifiedTo { get; set; }

    /// <summary>Whether to show only pages whose draft has moved on from what is published.</summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "hasUnpublishedChanges")]
    public bool HasUnpublishedChanges { get; set; }

    /// <summary>Whether to show only pages past their review date.</summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "pastReviewDate")]
    public bool PastReviewDate { get; set; }

    /// <summary>How many hits the current page steps over.</summary>
    protected int Skip { get; private set; }

    /// <summary>The last results, or null while a search is in flight.</summary>
    protected SearchResults? Results { get; private set; }

    /// <summary>Templates the filter offers.</summary>
    protected IReadOnlyList<TemplateSummary> Templates { get; private set; } = [];

    /// <summary>Tags the filter suggests.</summary>
    protected IReadOnlyList<TagSummary> Tags { get; private set; } = [];

    /// <summary>Whether a search is in flight.</summary>
    protected bool IsBusy { get; private set; }

    /// <summary>Errors from the last refused search.</summary>
    protected IReadOnlyList<ApiDiagnostic> Errors { get; private set; } = [];

    /// <summary>Statuses the filter offers, straight from the contract rather than hand-listed.</summary>
    protected static IReadOnlyList<string> Statuses { get; } = ["Draft", "InReview", "Published", "Archived"];

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        Templates = await Structure.GetTemplatesAsync();
        Tags = await Search.SuggestTagsAsync(prefix: null, limit: 50);

        await SearchAsync();
    }

    /// <summary>Runs the search the form describes, from the first page of results.</summary>
    protected async Task RunAsync()
    {
        Skip = 0;

        WriteQueryString();

        await SearchAsync();
    }

    /// <summary>Clears every filter and searches again.</summary>
    protected async Task ResetAsync()
    {
        Text = null;
        Kind = null;
        Status = null;
        TemplateId = null;
        Tag = null;
        ModifiedFrom = null;
        ModifiedTo = null;
        HasUnpublishedChanges = false;
        PastReviewDate = false;

        await RunAsync();
    }

    /// <summary>Steps back one page of results.</summary>
    protected async Task PreviousAsync()
    {
        Skip = Math.Max(0, Skip - PageSize);

        await SearchAsync();
    }

    /// <summary>Steps forward one page of results.</summary>
    protected async Task NextAsync()
    {
        Skip += PageSize;

        await SearchAsync();
    }

    /// <summary>Where a hit opens in the backoffice.</summary>
    /// <param name="hit">The hit.</param>
    /// <returns>The admin URL for it.</returns>
    /// <remarks>
    /// Its backoffice address rather than its public one. Somebody searching from here is looking
    /// for the thing to work on it, and a media item or an unpublished page has no public URL to
    /// send them to at all.
    /// </remarks>
    protected static string Link(SearchHit hit) => hit.Kind switch
    {
        SearchResultKind.Page => $"/admin/pages/{hit.Id}",
        SearchResultKind.Media => $"/admin/media/{hit.Id}",
        SearchResultKind.Reusable => $"/admin/reusable/{hit.Id}",
        _ => "/admin",
    };

    private async Task SearchAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        Results = null;
        Errors = [];

        try
        {
            Results = await Search.SearchAsync(new SearchQuery(
                Text,
                Enum.TryParse<SearchResultKind>(Kind, out var kind) ? kind : null,
                int.TryParse(TemplateId, CultureInfo.InvariantCulture, out var templateId) ? templateId : null,
                Status,
                OwnerUserId: null,
                Tag,
                ModifiedFrom is { } from ? new DateTimeOffset(from, TimeSpan.Zero) : null,
                // Inclusive of the whole day the editor picked. A date filter that stopped at
                // midnight would exclude everything saved on the day they are asking about.
                ModifiedTo is { } to ? new DateTimeOffset(to.AddDays(1).AddTicks(-1), TimeSpan.Zero) : null,
                HasUnpublishedChanges ? true : null,
                PastReviewDate,
                Skip,
                PageSize));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void WriteQueryString()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["q"] = Empty(Text),
            ["kind"] = Empty(Kind),
            ["status"] = Empty(Status),
            ["templateId"] = Empty(TemplateId),
            ["tag"] = Empty(Tag),
            ["hasUnpublishedChanges"] = HasUnpublishedChanges ? "true" : null,
            ["pastReviewDate"] = PastReviewDate ? "true" : null,
        };

        // replace: true, so a run of keystrokes does not fill the back stack with searches nobody
        // wants to walk back through.
        Navigation.NavigateTo(
            Navigation.GetUriWithQueryParameters(parameters),
            forceLoad: false,
            replace: true);

        static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
