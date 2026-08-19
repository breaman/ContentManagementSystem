using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Workflow;

/// <summary>
/// Threaded review remarks on a page, optionally narrowed to one zone (task P7-12).
/// </summary>
/// <remarks>
/// The same component serves the page-level panel and the per-zone thread on an editing card. What
/// differs is <see cref="ZoneKey"/>: set, it shows and creates remarks about that zone alone, which
/// is what turns "the hero headline is wrong" into a note on the hero card rather than a paragraph
/// the author has to match to a card by reading it (spec section 11.9).
/// <para>
/// Bodies are rendered as text. They are whatever somebody typed, and the one thing they must never
/// become is markup — there is no <c>MarkupString</c> anywhere in this component and there must not
/// be.
/// </para>
/// </remarks>
public partial class CommentThread : ComponentBase
{
    /// <summary>Reads and writes comments.</summary>
    [Inject]
    private IWorkflowClient Client { get; set; } = default!;

    /// <summary>The page being discussed.</summary>
    [Parameter]
    [EditorRequired]
    public int PageId { get; set; }

    /// <summary>
    /// The zone this thread is about, or null for the page as a whole.
    /// </summary>
    /// <remarks>
    /// Also decides what a new comment is anchored to, so a thread opened on a card cannot produce a
    /// remark that then appears somewhere else.
    /// </remarks>
    [Parameter]
    public string? ZoneKey { get; set; }

    /// <summary>Heading above the list.</summary>
    [Parameter]
    public string Heading { get; set; } = "Comments";

    /// <summary>The threads, oldest first, or null before the first load.</summary>
    /// <remarks>
    /// Nullable rather than defaulting to an empty list: <c>[PersistentState]</c> refuses an
    /// initializer, because the value it restores would be overwritten by it during binding.
    /// </remarks>
    [PersistentState]
    public IReadOnlyList<CommentSummary>? Loaded { get; set; }

    /// <summary>The threads, or none while they are still loading.</summary>
    private IReadOnlyList<CommentSummary> Threads => Loaded ?? [];

    /// <summary>Whether a request is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <summary>Element id prefix, unique per zone so two threads on one screen do not collide.</summary>
    private string HeadingId => ZoneKey is { Length: > 0 } zone
        ? $"cms-comments-{PageId}-{zone}"
        : $"cms-comments-{PageId}";

    private string _draft = string.Empty;
    private string _reply = string.Empty;
    private int? _replyingTo;
    private int _loadedFor;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedFor != PageId)
        {
            _loadedFor = PageId;
            await ReloadAsync();
        }
    }

    private void StartReply(int commentId)
    {
        _replyingTo = _replyingTo == commentId ? null : commentId;
        _reply = string.Empty;
    }

    private Task AddAsync() =>
        PostAsync(new CreateCommentRequest(_draft.Trim(), ZoneKey), () => _draft = string.Empty);

    private Task SendReplyAsync(int parentId) =>
        PostAsync(
            new CreateCommentRequest(_reply.Trim(), ZoneKey, parentId),
            () =>
            {
                _reply = string.Empty;
                _replyingTo = null;
            });

    private async Task PostAsync(CreateCommentRequest request, Action onPosted)
    {
        IsBusy = true;

        try
        {
            var posted = await Client.AddCommentAsync(PageId, request);

            if (posted is null) return;

            onPosted();

            // Reloaded rather than appended. A reply has to land under its parent, and a thread that
            // grew a root-level comment because the client guessed wrongly is a thread nobody can
            // straighten out afterwards.
            await ReloadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResolveAsync(CommentSummary thread)
    {
        IsBusy = true;

        try
        {
            if (await Client.ResolveCommentAsync(thread.Id, thread.ResolvedOn is null) is not null)
            {
                await ReloadAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAsync()
    {
        var all = await Client.GetCommentsAsync(PageId);

        Loaded = ZoneKey is { Length: > 0 } zone
            ? [.. all.Where(thread => string.Equals(thread.ZoneKey, zone, StringComparison.Ordinal))]
            : all;
    }
}
