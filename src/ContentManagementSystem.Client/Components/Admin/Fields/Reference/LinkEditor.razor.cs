using ContentManagementSystem.Client.Components.Admin.Pickers;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Reference;

/// <summary>
/// The <c>link</c> editor — a summary of where the link goes, and the picker behind it
/// (tasks P6-15 and P6-11, spec section 7.1).
/// </summary>
/// <remarks>
/// The control itself is a sentence and two buttons. Everything that decides what a link is lives in
/// <see cref="LinkPicker"/>, which the rich-text editors also open (P6-11): a link authored in a
/// property and one authored inside prose have to offer the same destinations, and the way to
/// guarantee that is for there to be one dialog.
/// <para>
/// <strong>The summary resolves an internal target's name.</strong> A stored page link holds an id
/// and nothing else, and "Page 44" is not something an author can check. One request per link is
/// worth paying to make the control readable — and it is one request, made when the value changes
/// rather than on every render.
/// </para>
/// </remarks>
public partial class LinkEditor : FieldEditorBase
{
    [Inject]
    private IPageClient Pages { get; set; } = default!;

    [Inject]
    private IMediaClient Media { get; set; } = default!;

    /// <summary>Whether the picker is open.</summary>
    private bool IsPicking { get; set; }

    /// <summary>Title of the linked page, once resolved.</summary>
    private string? PageTitle { get; set; }

    /// <summary>Name of the linked file, once resolved.</summary>
    private string? MediaName { get; set; }

    /// <summary>The stored link, or null when nothing is set.</summary>
    private StoredLink? Link => StoredLink.Parse(Value);

    /// <summary>The kinds the slot accepts, empty when it accepts all of them.</summary>
    private IReadOnlyList<string> AllowedKinds => ConfiguredTextList(FieldSettingNames.AllowedKinds);

    /// <summary>An icon for the kind, beside the words rather than instead of them.</summary>
    private string Icon => Link?.Kind switch
    {
        LinkKinds.Page => "bi-file-earmark-text",
        LinkKinds.Media => "bi-paperclip",
        LinkKinds.External => "bi-box-arrow-up-right",
        LinkKinds.Email => "bi-envelope",
        LinkKinds.Anchor => "bi-hash",
        _ => "bi-link-45deg",
    };

    /// <summary>The value the summary was last resolved against.</summary>
    /// <remarks>
    /// Keyed on the value rather than on whether a name was found, because "found nothing" is a
    /// perfectly good answer — an external link resolves no name at all, and a guard that keyed on
    /// the name being null would re-ask on every render for the rest of the session.
    /// </remarks>
    private string? _resolved;

    /// <inheritdoc />
    /// <remarks>
    /// Resolves the target's name only when the value changed, so opening a page with a dozen links
    /// on it costs a dozen requests once rather than a dozen per render.
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(Value, _resolved, StringComparison.Ordinal)) return;

        await ResolveAsync();
    }

    /// <summary>Looks up whatever the link points at, so the summary can name it.</summary>
    private async Task ResolveAsync()
    {
        _resolved = Value;
        PageTitle = null;
        MediaName = null;

        switch (Link)
        {
            case { Kind: LinkKinds.Page, PageId: { } pageId }:
                // A page that has since been deleted resolves to null and the summary falls back to
                // the id. The publish check is what reports the broken reference; the control's job
                // is to stay readable either way.
                PageTitle = (await Pages.GetAsync(pageId))?.Summary.Title;
                break;

            case { Kind: LinkKinds.Media, MediaId: { } mediaId }:
                MediaName = (await Media.GetAsync(mediaId))?.OriginalFileName;
                break;
        }
    }

    /// <summary>Stores the chosen link, keeping any member this build did not write.</summary>
    private async Task OnPickedAsync(LinkPick pick)
    {
        IsPicking = false;

        var stored = StoredValue.ParseOrNew(Value, FieldTypeKey);

        // Every destination member is cleared first. A link changed from external to page would
        // otherwise keep its old url beside the new pageId, and the validator would be judging a
        // value that names two destinations.
        foreach (var member in DestinationMembers)
        {
            stored.Remove(member);
        }

        foreach (var (name, value) in pick.Members)
        {
            stored[name] = value?.DeepClone();
        }

        await WriteAsync(stored.ToJsonString());
        await ResolveAsync();
    }

    private Task ClearAsync() => WriteAsync(string.Empty);

    /// <summary>The members only one kind of link at a time may carry.</summary>
    private static readonly string[] DestinationMembers =
    [
        LinkKinds.PageIdMember,
        LinkKinds.MediaIdMember,
        LinkKinds.UrlMember,
        LinkKinds.AnchorMember,
        LinkKinds.EmailMember,
        LinkKinds.TextMember,
        LinkKinds.TargetMember,
    ];
}
