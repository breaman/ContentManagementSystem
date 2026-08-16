using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;

using Microsoft.AspNetCore.Components.Web;

namespace ContentManagementSystem.Client.Components.Admin.Tree;

/// <summary>
/// The move half of the content tree: drag, keyboard commands, and the confirmation (task P6-03).
/// </summary>
/// <remarks>
/// Split into its own file rather than its own component, because a move is a fact about the whole
/// tree — where a page came from, where it is going, and which two levels have to be re-read
/// afterwards — and a child component would have to be handed all of it.
/// <para>
/// <strong>Drag is never the only path.</strong> Every move a pointer can make, four keyboard
/// commands can make too: <kbd>Alt</kbd> with the arrow keys moves a page up, down, out of its
/// parent, or into the sibling above it. That is the requirement of spec section 28 and acceptance
/// criterion P6 #4, and it is also the faster path once an editor knows it.
/// </para>
/// </remarks>
public partial class ContentTree
{
    /// <summary>The page being dragged, or null when nothing is.</summary>
    private PageSummary? _dragging;

    /// <summary>The move waiting on the editor's confirmation, or null when none is.</summary>
    private PendingMove? _pending;

    /// <summary>Whether a move is being written.</summary>
    private bool _moving;

    /// <summary>Why the last move was refused, if it was.</summary>
    private IReadOnlyList<ApiDiagnostic>? _moveErrors;

    /// <summary>Whether a drag is in progress, which is when the drop gaps are worth drawing.</summary>
    internal bool IsDragging => _dragging is not null;

    /// <summary>Whether this page is the one being dragged.</summary>
    internal bool IsDragged(int id) => _dragging?.Id == id;

    /// <summary>Records the page a drag started on.</summary>
    internal void BeginDrag(PageSummary page)
    {
        _dragging = page;

        StateHasChanged();
    }

    /// <summary>Forgets the drag, whether it ended in a drop or in nothing.</summary>
    internal void EndDrag()
    {
        if (_dragging is null) return;

        _dragging = null;

        StateHasChanged();
    }

    /// <summary>
    /// Drops the dragged page into another page, at the end of its children.
    /// </summary>
    /// <param name="target">The page dropped onto.</param>
    internal async Task DropIntoAsync(PageSummary target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var moving = _dragging;

        EndDrag();

        // A page dropped onto itself is a gesture that ended where it started, not a move.
        if (moving is null || moving.Id == target.Id) return;

        await RequestMoveAsync(moving, target.Id, position: null);
    }

    /// <summary>
    /// Drops the dragged page into a gap between two siblings.
    /// </summary>
    /// <param name="parentId">Parent of the level the gap is in.</param>
    /// <param name="position">Index of the gap within that level.</param>
    internal async Task DropBeforeAsync(int? parentId, int position)
    {
        var moving = _dragging;

        EndDrag();

        if (moving is null) return;

        await RequestMoveAsync(moving, parentId, position);
    }

    /// <summary>
    /// Handles the four keyboard move commands.
    /// </summary>
    /// <param name="args">The key press, already known to have Alt held.</param>
    /// <param name="page">The page the tree's focus is on.</param>
    /// <returns>Whether the key press was a move command.</returns>
    private async Task<bool> TryKeyboardMoveAsync(KeyboardEventArgs args, PageSummary page)
    {
        var siblings = SiblingsOf(page.ParentId);
        var index = siblings.FindIndex(sibling => sibling.Id == page.Id);

        if (index < 0) return false;

        switch (args.Key)
        {
            case "ArrowUp" when index > 0:
                await RequestMoveAsync(page, page.ParentId, index - 1);

                return true;

            case "ArrowDown" when index < siblings.Count - 1:
                await RequestMoveAsync(page, page.ParentId, index + 1);

                return true;

            case "ArrowRight" when index > 0:
                // Into the sibling above, which is the only reparenting a single key press can mean
                // without ambiguity — it is what an outline editor's Tab does.
                await RequestMoveAsync(page, siblings[index - 1].Id, position: null);

                return true;

            case "ArrowLeft" when page.ParentId is { } parentId:
                // Out to the grandparent, landing immediately after the page's former parent so the
                // page stays where the eye last saw it.
                var grandparent = FindParentId(parentId);
                var among = SiblingsOf(grandparent);
                var parentIndex = among.FindIndex(sibling => sibling.Id == parentId);

                await RequestMoveAsync(page, grandparent, parentIndex < 0 ? null : parentIndex + 1);

                return true;

            case "ArrowUp":
            case "ArrowDown":
            case "ArrowLeft":
            case "ArrowRight":
                // A move command that has nowhere to go. Swallowed rather than falling through to
                // the navigation keys, so Alt+Up on the first sibling does not silently move the
                // selection instead of the page.
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Works out what a move would do, and asks the editor about it if it would change any URL.
    /// </summary>
    /// <param name="page">The page to move.</param>
    /// <param name="parentId">Its new parent, or null for the root of the site.</param>
    /// <param name="position">Where among its new siblings, or null to append it.</param>
    /// <remarks>
    /// A move that changes no URL — a reorder among siblings — is applied without a dialog. The
    /// confirmation exists to show URL changes and redirects, and one that says "nothing will
    /// change, are you sure" trains editors to dismiss the dialog that matters.
    /// </remarks>
    private async Task RequestMoveAsync(PageSummary page, int? parentId, int? position)
    {
        _moveErrors = null;

        var preview = await Client.MoveAsync(
            page.Id,
            new MovePageRequest(parentId, position, Preview: true));

        if (!preview.IsSuccess)
        {
            _moveErrors = preview.Errors;

            StateHasChanged();

            return;
        }

        if (preview.Value!.UrlChanges.Count == 0)
        {
            await ApplyMoveAsync(page, parentId, position);

            return;
        }

        _pending = new PendingMove(page, parentId, position, preview.Value);

        StateHasChanged();
    }

    /// <summary>Applies the move the editor confirmed.</summary>
    private async Task ConfirmMoveAsync()
    {
        if (_pending is not { } pending) return;

        await ApplyMoveAsync(pending.Page, pending.ParentId, pending.Position);
    }

    /// <summary>Backs out of a move the editor did not confirm.</summary>
    private void CancelMove()
    {
        _pending = null;

        StateHasChanged();
    }

    /// <summary>Writes the move and re-reads the two levels it changed.</summary>
    private async Task ApplyMoveAsync(PageSummary page, int? parentId, int? position)
    {
        _moving = true;

        try
        {
            var result = await Client.MoveAsync(
                page.Id,
                new MovePageRequest(parentId, position));

            if (!result.IsSuccess)
            {
                _moveErrors = result.Errors;

                return;
            }

            _pending = null;

            // Both ends of the move. The level the page left is as wrong as the one it joined, and
            // a tree that refreshed only the destination shows the page twice.
            await RefreshAsync(page.ParentId);

            if (parentId != page.ParentId)
            {
                await RefreshAsync(parentId);
            }

            // Opened, so the page is where the editor can see it landed rather than inside a node
            // they now have to go and find.
            if (parentId is { } destination)
            {
                _expanded.Add(destination);
            }

            Toasts.ShowSuccess(
                result.Value!.RedirectCount == 0
                    ? $"“{page.Title}” was moved."
                    : $"“{page.Title}” was moved. {result.Value.RedirectCount} redirect(s) " +
                      "were created at the old addresses.",
                "Moved");
        }
        finally
        {
            _moving = false;

            StateHasChanged();
        }
    }

    /// <summary>The pages at one level, in the order the tree draws them.</summary>
    /// <remarks>
    /// Read from what has been fetched rather than asked of the server. Every level a move command
    /// can act on is by definition one the editor is looking at, so it is loaded; and a keyboard
    /// command that had to await a round trip before deciding whether it was even possible would
    /// make Alt+Up feel broken on the first press.
    /// </remarks>
    private List<PageSummary> SiblingsOf(int? parentId) =>
        parentId is { } id
            ? [.. ChildrenOf(id)]
            : [.. (Roots ?? []).Select(node => node.Page)];

    /// <summary>Finds a loaded page's own parent.</summary>
    private int? FindParentId(int pageId)
    {
        foreach (var (parentId, children) in _children)
        {
            if (children.Any(child => child.Id == pageId))
            {
                return parentId == RootKey ? null : parentId;
            }
        }

        return null;
    }

    /// <summary>A move the editor has been asked to confirm.</summary>
    /// <param name="Page">The page being moved.</param>
    /// <param name="ParentId">Its new parent, or null for the root of the site.</param>
    /// <param name="Position">Where among its new siblings, or null to append it.</param>
    /// <param name="Preview">What the server said would happen.</param>
    private sealed record PendingMove(
        PageSummary Page,
        int? ParentId,
        int? Position,
        PageMoveResult Preview);
}
