using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Preview;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Pages;

/// <summary>
/// Issuing and revoking shareable preview links for one page (task P3-19, spec section 12.2).
/// </summary>
/// <remarks>
/// The screen is built around the one thing that cannot be undone: <strong>the secret is shown
/// once</strong>. Only its hash is stored, so this component is the last place the link exists in a
/// readable form, and the banner says so rather than leaving somebody to discover it by closing the
/// tab.
/// <para>
/// Revoked and expired links stay in the table. The question this screen is opened to answer is
/// usually "why did the link I sent stop working", and a list that filtered them out could only
/// answer "there is no such link" — which reads as a bug in the CMS rather than as an expiry.
/// </para>
/// </remarks>
public partial class PagePreviewLinks : ComponentBase
{
    /// <summary>Identity of the page whose links these are, from the route.</summary>
    [Parameter]
    public int Id { get; set; }

    /// <summary>Reads and writes pages, over HTTP in the browser and directly on the server.</summary>
    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>The links, newest first, or null while loading.</summary>
    [PersistentState]
    public IReadOnlyList<PreviewTokenSummary>? Tokens { get; set; }

    /// <summary>The page's versions, so a link can be issued for one that is not the draft.</summary>
    [PersistentState]
    public IReadOnlyList<PageVersionSummary>? Versions { get; set; }

    /// <summary>The link just issued, and the only sight of its secret.</summary>
    private IssuedPreviewToken? Issued { get; set; }

    /// <summary>Version chosen in the form; zero means the page's current draft.</summary>
    private int NewVersionId { get; set; }

    /// <summary>Lifetime chosen in the form, in days.</summary>
    private int NewExpiryDays { get; set; } = 7;

    /// <summary>View limit chosen in the form; zero or less means unlimited.</summary>
    private int NewMaxUses { get; set; }

    /// <summary>Who the link is for. Housekeeping only, and the thing that makes the list readable.</summary>
    private string? NewNotes { get; set; }

    /// <summary>Why the last action did not happen.</summary>
    private IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>Whether a request is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <summary>Whether anything would be revoked by the bulk button.</summary>
    private bool HasLiveTokens => Tokens?.Any(token => token.RevokedOn is null) ?? false;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        Tokens ??= await Client.GetPreviewTokensAsync(Id);
        Versions ??= await Client.GetVersionsAsync(Id);
    }

    private async Task IssueAsync()
    {
        IsBusy = true;
        Errors = null;
        Issued = null;

        try
        {
            var result = await Client.IssuePreviewTokenAsync(new CreatePreviewTokenRequest(
                Id,

                // Zero is the form's way of saying "whatever the draft is when I press the button",
                // which the service reads as an absent version rather than as version zero.
                NewVersionId > 0 ? NewVersionId : null,
                NewExpiryDays,
                NewMaxUses > 0 ? NewMaxUses : null,
                string.IsNullOrWhiteSpace(NewNotes) ? null : NewNotes));

            if (result.IsSuccess)
            {
                Issued = result.Value;
                NewNotes = null;
                await ReloadAsync();
            }
            else
            {
                Errors = result.Errors;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RevokeAsync(int tokenId)
    {
        IsBusy = true;
        Errors = null;

        try
        {
            var result = await Client.RevokePreviewTokenAsync(tokenId);

            if (result.IsSuccess)
            {
                // The banner is cleared on any revocation, including of another link. Leaving a
                // secret on screen after somebody came here to take access away is the wrong
                // default even when the two are unrelated.
                Issued = null;
                await ReloadAsync();
            }
            else
            {
                Errors = result.Errors;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RevokeAllAsync()
    {
        IsBusy = true;
        Errors = null;

        try
        {
            var result = await Client.RevokeAllPreviewTokensAsync(Id);

            if (result.IsSuccess)
            {
                Issued = null;
                await ReloadAsync();
            }
            else
            {
                Errors = result.Errors;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Re-reads the list after a change, so use counts and states are not stale.</summary>
    private async Task ReloadAsync() => Tokens = await Client.GetPreviewTokensAsync(Id);
}
