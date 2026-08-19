using System.Globalization;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Search;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ContentManagementSystem.Client.Components.Admin.Properties;

/// <summary>
/// The right-hand pane: everything about a page that is not its content (task P6-17,
/// spec sections 14.7 and 18.1).
/// </summary>
/// <remarks>
/// Four sections, in the order of spec section 14.1's sketch: the page's own identity, search and
/// social, publishing, and the editorial metadata that keeps content from quietly rotting.
/// <para>
/// <strong>The panel edits a model, never the record it was handed.</strong> What it sends is the
/// difference between the two (<see cref="PageProperties.ToPatch"/>), so a save carries the fields
/// this editor touched and nothing else — a panel that patched all twenty every time would reinstate
/// stale values for the nineteen it was not asked about, silently, because they look right on the
/// screen that sent them.
/// </para>
/// <para>
/// It owns no save button. Changes are reported upwards through <see cref="OnChanged"/> and land in
/// the same autosave the payload uses (P6-18): title and the SEO fields live on the draft version,
/// so editing them is editing the draft, and the editor should no more have to remember to save one
/// than the other.
/// </para>
/// </remarks>
public partial class PropertiesPanel : ComponentBase
{
    /// <summary>Resolves the page's own URL for the search-result preview.</summary>
    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>Who is signed in, so "take ownership" has an id to write.</summary>
    [Inject]
    private ICurrentUserClient CurrentUser { get; set; } = default!;

    /// <summary>Supplies the tag vocabulary the tag box completes against (task P8-20).</summary>
    [Inject]
    private ISearchClient Search { get; set; } = default!;

    /// <summary>The page as the server last reported it, or null while loading.</summary>
    [Parameter]
    public PageDetail? Page { get; set; }

    /// <summary>
    /// What the panel is editing.
    /// </summary>
    /// <remarks>
    /// Owned by the screen rather than by the panel, because autosave has to be able to read it
    /// without the panel being on screen — an editor can collapse this pane, and a collapsed pane is
    /// not permission to drop what they typed into it.
    /// </remarks>
    [Parameter]
    public PageProperties? Model { get; set; }

    /// <summary>Whether the fields are read-only.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>What the last save refused, so each message lands on the field it is about.</summary>
    [Parameter]
    public IReadOnlyList<ApiDiagnostic>? Diagnostics { get; set; }

    /// <summary>Raised on every edit, so the screen can mark itself unsaved.</summary>
    [Parameter]
    public EventCallback OnChanged { get; set; }

    /// <summary>Who is signed in, once resolved.</summary>
    private CurrentUser? Me { get; set; }

    /// <summary>Where the page is served, for the search-result preview.</summary>
    private string? Url { get; set; }

    /// <summary>The page id the URL was resolved for, so a re-render does not re-resolve it.</summary>
    private int? ResolvedFor { get; set; }

    /// <summary>
    /// What the owner field reads as.
    /// </summary>
    /// <remarks>
    /// The stored name is only usable while the panel has not reassigned the owner locally: the id
    /// in the model and the name on the record then describe two different people, and printing the
    /// name anyway would tell an editor their change did not take.
    /// </remarks>
    private string OwnerLabel
    {
        get
        {
            if (Model?.OwnerUserId is not { } owner) return "Nobody owns this page.";

            if (Me is not null && owner == Me.UserId) return $"You ({Me.DisplayName}).";

            return owner == Page?.OwnerUserId && Page?.OwnerName is { Length: > 0 } name
                ? name
                : "Another editor.";
        }
    }

    /// <summary>Diagnostics that name no field this panel draws, reported above the sections.</summary>
    private IReadOnlyList<ApiDiagnostic> Unplaced =>
        [.. (Diagnostics ?? []).Where(diagnostic => !IsPlaced(diagnostic))];

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        Me ??= await CurrentUser.GetAsync();

        if (Page is not null && ResolvedFor != Page.Summary.Id)
        {
            ResolvedFor = Page.Summary.Id;

            // The URL rather than the slug, because the preview is about how the page reads in a
            // result and a result shows the whole address. A page that has never been published
            // still has one; it resolves against the tree, not against what is live.
            Url = (await Client.ResolveLinkAsync(Page.Summary.Id))?.Url;

            // Fetched once per page rather than per keystroke. The vocabulary of a site is small
            // and changes slowly, and a request behind every letter typed into the tag box would be
            // a lot of traffic for a list that is the same each time.
            TagSuggestions = await Search.SuggestTagsAsync(prefix: null, limit: 50);
        }
    }

    /// <summary>What was said about one field.</summary>
    /// <param name="member">Name of the member on <see cref="PageProperties"/>.</param>
    /// <returns>Its diagnostics, or null when it has none.</returns>
    private IReadOnlyList<ApiDiagnostic>? For(string member)
    {
        var matched = (Diagnostics ?? [])
            .Where(diagnostic => string.Equals(diagnostic.Property, member, StringComparison.Ordinal))
            .ToList();

        return matched.Count == 0 ? null : matched;
    }

    /// <summary>The Bootstrap invalid class for a field the last save complained about.</summary>
    private string? Invalid(string member) => For(member) is null ? null : "is-invalid";

    /// <summary>Whether a diagnostic names a field this panel is drawing.</summary>
    private static bool IsPlaced(ApiDiagnostic diagnostic) =>
        diagnostic.Property is { Length: > 0 } property && Fields.Contains(property);

    /// <summary>Every member the panel has a control for, which is what makes a message placeable.</summary>
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        nameof(PageProperties.Title),
        nameof(PageProperties.Slug),
        nameof(PageProperties.ExplicitUrl),
        nameof(PageProperties.OwnerUserId),
        nameof(PageProperties.ReviewByDate),
        nameof(PageProperties.InternalNotes),
        nameof(PageProperties.MetaTitle),
        nameof(PageProperties.MetaDescription),
        nameof(PageProperties.CanonicalUrl),
        nameof(PageProperties.OgTitle),
        nameof(PageProperties.OgDescription),
        nameof(PageProperties.OgType),
        nameof(PageProperties.TwitterCard),
        nameof(PageProperties.StructuredDataJson),
        nameof(PageProperties.ChangeFreq),
        nameof(PageProperties.Priority),
    };

    /// <summary>Reports an edit upwards.</summary>
    private Task ChangedAsync() => OnChanged.InvokeAsync();

    /// <summary>What has been typed into the tag box but not yet committed.</summary>
    private string? TagEntry { get; set; }

    /// <summary>The tags this site already uses, offered as completions.</summary>
    private IReadOnlyList<TagSummary> TagSuggestions { get; set; } = [];

    /// <summary>Commits the tag box on Enter, and only on Enter.</summary>
    /// <remarks>
    /// Not on blur. An editor who clicks away from a half-typed word has not decided to tag the
    /// page with it, and a tag added by accident is one that has to be found again to be removed.
    /// </remarks>
    private async Task OnTagKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key is not ("Enter" or ",")) return;

        var tag = TagEntry?.Trim();

        TagEntry = null;

        if (string.IsNullOrEmpty(tag) || Model is null) return;

        // Case-insensitively, because the server folds "Product" and "product" into one tag and a
        // panel that showed both would be showing a state that cannot exist.
        if (Model.Tags.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Model.Tags.Add(tag);

        await ChangedAsync();
    }

    /// <summary>Takes one tag off the page.</summary>
    private async Task RemoveTagAsync(string tag)
    {
        if (Model is null || !Model.Tags.Remove(tag)) return;

        await ChangedAsync();
    }

    /// <summary>Applies a checkbox and reports the edit.</summary>
    private Task SetAsync(Action<bool> assign, ChangeEventArgs args)
    {
        assign(args.Value is true);

        return ChangedAsync();
    }

    /// <summary>
    /// Turns the explicit-URL flag on or off, seeding the box the first time.
    /// </summary>
    /// <remarks>
    /// Seeded with the page's current address rather than left empty, because an editor who ticks
    /// this box is almost always about to type something very close to it — and an empty required
    /// box is a save that fails before they have said anything.
    /// </remarks>
    private Task ToggleExplicitUrlAsync(ChangeEventArgs args)
    {
        Model!.UseExplicitUrl = args.Value is true;

        if (Model.UseExplicitUrl && string.IsNullOrWhiteSpace(Model.ExplicitUrl))
        {
            Model.ExplicitUrl = Url;
        }

        return ChangedAsync();
    }

    /// <summary>Reads the review date, treating a cleared box as no date at all.</summary>
    private Task OnReviewByChangedAsync(ChangeEventArgs args)
    {
        Model!.ReviewByDate = DateOnly.TryParse(
            args.Value as string,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;

        return ChangedAsync();
    }

    /// <summary>Makes the signed-in editor the page's owner.</summary>
    private Task TakeOwnershipAsync()
    {
        if (Me is null) return Task.CompletedTask;

        Model!.OwnerUserId = Me.UserId;

        return ChangedAsync();
    }

    /// <summary>Leaves the page unowned.</summary>
    private Task ClearOwnerAsync()
    {
        Model!.OwnerUserId = null;

        return ChangedAsync();
    }
}
