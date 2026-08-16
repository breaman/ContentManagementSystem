using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Pickers;

/// <summary>
/// What a link was chosen to be.
/// </summary>
/// <param name="Kind">Which destination member applies.</param>
/// <param name="Members">The stored members, ready to merge into a payload value.</param>
/// <param name="Href">
/// A best-effort address for a surface that has to write one now — a markdown or HTML editor
/// inserting an anchor. Null for an internal destination, which cannot be resolved in the browser.
/// </param>
/// <param name="Text">The words the link should read as, when the author gave any.</param>
public sealed record LinkPick(string Kind, JsonObject Members, string? Href, string? Text);

/// <summary>
/// The one link picker (task P6-15, spec section 14.3, ADR-0006).
/// </summary>
/// <remarks>
/// <strong>The point of this dialog is that the internal choice is the easy one.</strong> A link
/// stored as a page id resolves to that page's current URL at render time, so it still works after
/// the target has been moved and renamed twice; a hand-typed URL to the same page is a copy that
/// nothing updates. Putting "a page on this site" at the top of the list, with a browser behind it,
/// is what makes the durable option also the shortest path.
/// <para>
/// One picker serves both callers, which is the "unified" in the task. A <c>link</c> property stores
/// what it returns verbatim; a rich-text editor (P6-11) turns it into an anchor. They ask the same
/// questions and must offer the same destinations, and two dialogs would have drifted the first time
/// either gained a kind.
/// </para>
/// </remarks>
public partial class LinkPicker : ComponentBase
{
    /// <summary>Whether the picker is on screen.</summary>
    [Parameter]
    public bool IsOpen { get; set; }

    /// <summary>Heading of the dialog.</summary>
    [Parameter]
    public string Title { get; set; } = "Link to";

    /// <summary>Label of the button that goes ahead.</summary>
    [Parameter]
    public string ConfirmLabel { get; set; } = "Use this link";

    /// <summary>The kinds the slot accepts, empty when it accepts all of them.</summary>
    [Parameter]
    public IReadOnlyList<string> AllowedKinds { get; set; } = [];

    /// <summary>Whether the dialog asks for the words the link reads as.</summary>
    /// <remarks>
    /// On for a <c>link</c> property, which stores its own text. Off when a rich-text editor already
    /// has a selection to wrap, where asking again would offer to overwrite what is highlighted.
    /// </remarks>
    [Parameter]
    public bool WantsText { get; set; } = true;

    /// <summary>The stored link this dialog is editing, as JSON text, or empty for a new one.</summary>
    [Parameter]
    public string? Existing { get; set; }

    /// <summary>Prefills the words the link reads as — a rich-text editor's current selection.</summary>
    [Parameter]
    public string? InitialText { get; set; }

    /// <summary>Raised with the chosen link.</summary>
    [Parameter]
    public EventCallback<LinkPick> OnPicked { get; set; }

    /// <summary>Raised when the editor backs out.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>Which destination is being filled in.</summary>
    private string Kind { get; set; } = LinkKinds.Page;

    private int? PageId { get; set; }

    private string? PageTitle { get; set; }

    private string? PageSlug { get; set; }

    private int? MediaId { get; set; }

    private string? MediaName { get; set; }

    private string? Url { get; set; }

    private string? Email { get; set; }

    private string? Anchor { get; set; }

    private string? Text { get; set; }

    private bool IsNewWindow { get; set; }

    private bool IsBrowsingPages { get; set; }

    private bool IsBrowsingMedia { get; set; }

    /// <summary>Whether the dialog has been filled from <see cref="Existing"/> for this opening.</summary>
    private bool _loaded;

    /// <summary>Distinguishes this picker's control ids from another's on the same page.</summary>
    private string PickerId { get; } = $"link-picker-{Guid.NewGuid():n}";

    /// <summary>The kinds this dialog offers.</summary>
    private IReadOnlyList<string> Kinds =>
        AllowedKinds.Count == 0
            ? LinkKinds.All
            : [.. LinkKinds.All.Where(kind => AllowedKinds.Contains(kind, StringComparer.Ordinal))];

    /// <inheritdoc />
    /// <remarks>
    /// Read once per opening. Re-reading on every parameter set would undo what the author is in the
    /// middle of typing; not reading at all would show them a blank dialog when they clicked "edit
    /// this link".
    /// </remarks>
    protected override void OnParametersSet()
    {
        if (!IsOpen)
        {
            _loaded = false;

            return;
        }

        if (_loaded) return;

        _loaded = true;

        Load(Existing);
    }

    /// <summary>Whether there is enough here to make a link.</summary>
    private bool CanConfirm => Kind switch
    {
        LinkKinds.Page => PageId is not null,
        LinkKinds.Media => MediaId is not null,
        LinkKinds.External => UrlProblem is null && !string.IsNullOrWhiteSpace(Url),
        LinkKinds.Email => !string.IsNullOrWhiteSpace(Email) && Email.Contains('@'),
        LinkKinds.Anchor => !string.IsNullOrWhiteSpace(Anchor),
        _ => false,
    };

    /// <summary>
    /// What is wrong with the typed address, or null while it is fine or empty.
    /// </summary>
    /// <remarks>
    /// Only the schemes the field type accepts. A <c>javascript:</c> URL typed into a link property
    /// is refused on write and again on render, and saying so here is a great deal more use than a
    /// publish check reporting it two screens later.
    /// </remarks>
    private string? UrlProblem
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Url)) return null;

            if (!Uri.TryCreate(Url, UriKind.Absolute, out var parsed))
            {
                return "That is not a complete address. Start it with https://.";
            }

            return parsed.Scheme is "http" or "https"
                ? null
                : $"Links can only use http or https, not {parsed.Scheme}.";
        }
    }

    /// <summary>Fills the dialog from a stored link.</summary>
    private void Load(string? json)
    {
        var stored = StoredLink.Parse(json);

        Kind = stored?.Kind is { Length: > 0 } kind && Kinds.Contains(kind, StringComparer.Ordinal)
            ? kind
            : Kinds.Count > 0 ? Kinds[0] : LinkKinds.Page;

        PageId = stored?.PageId;
        PageTitle = null;
        PageSlug = null;
        MediaId = stored?.MediaId;
        MediaName = null;
        Url = stored?.Url;
        Email = stored?.Email;
        Anchor = stored?.Anchor;
        Text = stored?.Text ?? InitialText;
        IsNewWindow = stored?.Target == "_blank";
    }

    private void OnPagePicked(PageSummary page)
    {
        PageId = page.Id;
        PageTitle = page.Title;
        PageSlug = page.Slug;
        IsBrowsingPages = false;

        // The page's title is the obvious thing for the link to read as, offered rather than
        // imposed: an author who has already written the words keeps them.
        Text ??= page.Title;
    }

    private void OnMediaPicked(MediaDetail item)
    {
        MediaId = item.Id;
        MediaName = item.OriginalFileName;
        IsBrowsingMedia = false;

        Text ??= item.Title is { Length: > 0 } title ? title : item.OriginalFileName;
    }

    /// <summary>Builds the stored members and hands them back.</summary>
    private async Task ConfirmAsync()
    {
        if (!CanConfirm) return;

        var members = new JsonObject
        {
            [StoredLinkMembers.Type] = FieldTypeKeys.Link,
            [LinkKinds.KindMember] = Kind,
        };

        string? href = null;

        switch (Kind)
        {
            case LinkKinds.Page:
                members[LinkKinds.PageIdMember] = PageId;
                break;

            case LinkKinds.Media:
                members[LinkKinds.MediaIdMember] = MediaId;
                break;

            case LinkKinds.External:
                members[LinkKinds.UrlMember] = Url!.Trim();
                href = Url.Trim();
                break;

            case LinkKinds.Email:
                members[LinkKinds.EmailMember] = Email!.Trim();
                href = $"mailto:{Email.Trim()}";
                break;

            case LinkKinds.Anchor:
                var fragment = Anchor!.Trim().TrimStart('#');
                members[LinkKinds.AnchorMember] = fragment;
                href = $"#{fragment}";
                break;
        }

        if (!string.IsNullOrWhiteSpace(Text)) members[LinkKinds.TextMember] = Text.Trim();

        // Only when it is not the default. Writing "_self" into every link would put a member in
        // every payload that says exactly what its absence already said.
        if (IsNewWindow) members[LinkKinds.TargetMember] = "_blank";

        await OnPicked.InvokeAsync(new LinkPick(Kind, members, href, Text?.Trim()));
    }
}
