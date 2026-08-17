using ContentManagementSystem.Client.Services;
using ContentManagementSystem.Shared.Contracts.Content;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Components.Admin.Tree;

/// <summary>
/// The context menu, the clipboard, and the delete confirmation (task P6-04, spec section 14.2).
/// </summary>
/// <remarks>
/// <strong>Copy and move are one clipboard, not two gestures.</strong> The spec's menu lists "copy"
/// and "move" beside "duplicate", and the difference between them is what happens at the far end:
/// copy then paste is a deep duplicate into the target, move then paste is the same move a drag
/// would have made — confirmation and all. Building both on operations that already exist is what
/// keeps the URL confirmation on the move path rather than needing a second one.
/// <para>
/// Everything here is reachable from the keyboard, because the menu is: <kbd>Shift</kbd>+<kbd>F10</kbd>
/// opens it on the focused row and the arrow keys walk it.
/// </para>
/// </remarks>
public partial class ContentTree
{
    /// <summary>Path of the collocated interop module, relative to the host page.</summary>
    private const string ModulePath = "./Components/Admin/Tree/ContentTree.razor.js";

    /// <summary>The page whose menu is open, or null when none is.</summary>
    private PageSummary? _menuFor;

    /// <summary>Where the open menu is drawn.</summary>
    private double _menuX;

    /// <summary>Where the open menu is drawn.</summary>
    private double _menuY;

    /// <summary>What is on the tree's clipboard, or null when nothing is.</summary>
    private (PageSummary Page, ClipboardMode Mode)? _clipboard;

    /// <summary>What the delete confirmation is about, or null when it is closed.</summary>
    private SubtreeImpact? _deleting;

    /// <summary>The page the delete confirmation is about.</summary>
    private PageSummary? _deleteSubject;

    /// <summary>What a branch publish would cover, or null when none is being confirmed.</summary>
    private BulkImpact? _branchImpact;

    /// <summary>The branch publish's progress, or null before it has been confirmed.</summary>
    private BulkJobStatus? _branchJob;

    /// <summary>The page a branch publish is rooted at.</summary>
    private PageSummary? _branchSubject;

    /// <summary>Whether a menu command is in flight.</summary>
    private bool _working;

    /// <summary>The imported interop module, used to place a keyboard-opened menu.</summary>
    private IJSObjectReference? _module;

    /// <summary>Whether a page is on the clipboard, which is when Paste exists.</summary>
    internal bool HasClipboard => _clipboard is not null;

    /// <summary>Whether this page is the one on the clipboard, so the row can show it.</summary>
    internal bool IsOnClipboard(int id) => _clipboard?.Page.Id == id;

    /// <summary>
    /// Builds the menu for one page.
    /// </summary>
    /// <param name="page">The page the menu acts on.</param>
    /// <returns>The entries, in the order they are shown.</returns>
    /// <remarks>
    /// Entries that cannot do anything are left out rather than disabled. A disabled "Unpublish" on
    /// a page that was never published is a control an editor has to read and dismiss every time;
    /// its absence says the same thing and costs nothing to skip.
    /// <para>
    /// "Publish branch" is offered only on a page that has children, for the same reason: on a leaf
    /// it is the entry above it under a longer name. It runs through <c>IBulkOperationService</c>
    /// (P6-29) rather than looping over the tree here — the impact is confirmed before anything
    /// happens, the batch reports per page, and a branch of more than
    /// <see cref="BulkLimits.BackgroundThreshold"/> pages runs on the server rather than tying up the
    /// browser.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<TreeCommand> CommandsFor(PageSummary page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var commands = new List<TreeCommand>
        {
            TreeCommand.NewChild,
            TreeCommand.Duplicate,
        };

        if (page.HasChildren)
        {
            commands.Add(TreeCommand.DuplicateDeep);
        }

        commands.Add(TreeCommand.Copy);
        commands.Add(TreeCommand.Cut);

        // Pasting a page inside itself is the one target that cannot work, and the menu is the place
        // to say so by omission rather than the server by refusal.
        if (_clipboard is { } clipboard && clipboard.Page.Id != page.Id)
        {
            commands.Add(TreeCommand.Paste);
        }

        commands.Add(TreeCommand.Publish);

        if (page.HasChildren)
        {
            commands.Add(TreeCommand.PublishBranch);
        }

        if (page.PublishedVersionNumber is not null)
        {
            commands.Add(TreeCommand.Unpublish);
        }

        commands.Add(TreeCommand.Delete);

        return commands;
    }

    /// <summary>Opens the menu where a right-click landed.</summary>
    internal void OpenMenu(PageSummary page, MouseEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        _menuFor = page;
        _menuX = args.ClientX;
        _menuY = args.ClientY;

        StateHasChanged();
    }

    /// <summary>Dismisses the menu.</summary>
    internal void CloseMenu()
    {
        _menuFor = null;

        StateHasChanged();
    }

    /// <summary>Opens the menu under a row, for the keyboard path.</summary>
    private async Task OpenMenuForFocusedAsync(PageSummary page)
    {
        var anchor = await AnchorAsync(page.Id);

        _menuFor = page;
        _menuX = anchor.X;
        _menuY = anchor.Y;

        StateHasChanged();
    }

    /// <summary>Asks the browser where a row is, so a keyboard-opened menu lands under it.</summary>
    private async Task<Anchor> AnchorAsync(int pageId)
    {
        if (!_rows.TryGetValue(pageId, out var row))
        {
            return new Anchor(0, 0);
        }

        try
        {
            _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);

            return await _module.InvokeAsync<Anchor>("anchorOf", row);
        }
        catch (Exception exception) when (exception is JSException
                                              or JSDisconnectedException
                                              or InvalidOperationException
                                              or TaskCanceledException)
        {
            // No JavaScript. The menu opens at the top-left rather than not at all, which is a
            // cosmetic failure where refusing to open would be a functional one.
            return new Anchor(0, 0);
        }
    }

    /// <summary>Runs the chosen command.</summary>
    private async Task RunCommandAsync(TreeCommand command)
    {
        if (_menuFor is not { } page) return;

        CloseMenu();

        _moveErrors = null;
        _working = true;

        try
        {
            switch (command.Id)
            {
                case "new-child":
                    _expanded.Add(page.Id);

                    await OnCreateRequested.InvokeAsync(page);

                    break;

                case "duplicate":
                    await DuplicateAsync(page, deep: false, page.ParentId);

                    break;

                case "duplicate-deep":
                    await DuplicateAsync(page, deep: true, page.ParentId);

                    break;

                case "copy":
                    _clipboard = (page, ClipboardMode.Copy);
                    Toasts.ShowInfo($"“{page.Title}” is on the clipboard. Paste it inside another page.");

                    break;

                case "cut":
                    _clipboard = (page, ClipboardMode.Move);
                    Toasts.ShowInfo($"“{page.Title}” is ready to move. Paste it inside another page.");

                    break;

                case "paste":
                    await PasteAsync(page);

                    break;

                case "publish":
                    await PublishAsync(page);

                    break;

                case "publish-branch":
                    await AskToPublishBranchAsync(page);

                    break;

                case "unpublish":
                    await UnpublishAsync(page);

                    break;

                case "delete":
                    await AskToDeleteAsync(page);

                    break;
            }
        }
        finally
        {
            _working = false;

            StateHasChanged();
        }
    }

    /// <summary>Copies a page, shallow or deep, and shows the copy.</summary>
    private async Task DuplicateAsync(PageSummary page, bool deep, int? parentId)
    {
        var result = await Client.DuplicateAsync(page.Id, deep, parentId);

        if (!result.IsSuccess)
        {
            _moveErrors = result.Errors;

            return;
        }

        await RefreshAsync(parentId);

        Toasts.ShowSuccess(
            deep
                ? $"“{page.Title}” and everything beneath it were copied."
                : $"“{page.Title}” was copied.",
            "Duplicated");
    }

    /// <summary>Pastes the clipboard inside a page, as a copy or as a move.</summary>
    private async Task PasteAsync(PageSummary target)
    {
        if (_clipboard is not { } clipboard) return;

        _clipboard = null;

        if (clipboard.Mode is ClipboardMode.Copy)
        {
            // Deep, because pasting half a section somewhere is never what was meant. The shallow
            // copy has its own menu entry, next to the page it copies.
            await DuplicateAsync(clipboard.Page, deep: true, target.Id);

            _expanded.Add(target.Id);

            return;
        }

        // Straight into the move path, so the URL confirmation happens here exactly as it does for
        // a drag. This is why paste is built on the move rather than on a second implementation.
        await RequestMoveAsync(clipboard.Page, target.Id, position: null);
    }

    /// <summary>Publishes one page's draft.</summary>
    private async Task PublishAsync(PageSummary page)
    {
        var result = await Client.PublishAsync(page.Id);

        if (!result.IsSuccess)
        {
            // Reported rather than forced through. A publish blocked by an unfilled required zone is
            // a list of zones to go and fill in, and the editor screen is where that is done.
            _moveErrors = result.Errors;

            return;
        }

        await RefreshAsync(page.ParentId);

        Toasts.ShowSuccess($"“{page.Title}” is live.", "Published");
    }

    /// <summary>
    /// Asks the server what publishing a branch would cover, and puts the answer in front of the
    /// editor (task P6-04, spec section 14.11).
    /// </summary>
    /// <remarks>
    /// The count comes from the server, resolved by the same code that will run the batch. The tree
    /// could count the descendants it has fetched instead, and would be wrong every time: it lazily
    /// loads one level at a time, so a branch it has never opened looks like a leaf.
    /// </remarks>
    private async Task AskToPublishBranchAsync(PageSummary page)
    {
        _branchSubject = page;

        var result = await Client.PreviewBulkAsync(BranchRequest(page));

        if (!result.IsSuccess)
        {
            _branchSubject = null;
            _moveErrors = result.Errors;

            return;
        }

        _branchImpact = result.Value;
    }

    /// <summary>Runs the confirmed branch publish, redrawing on every report of its progress.</summary>
    private async Task ConfirmPublishBranchAsync()
    {
        if (_branchSubject is not { } page) return;

        _working = true;

        try
        {
            var result = await BulkOperationRunner.RunAsync(
                Client,
                Clock,
                BranchRequest(page),
                status =>
                {
                    _branchJob = status;

                    StateHasChanged();

                    return Task.CompletedTask;
                });

            if (!result.IsSuccess)
            {
                _branchImpact = null;
                _branchSubject = null;
                _moveErrors = result.Errors;

                return;
            }

            // The level under the page is re-read rather than the whole tree: a branch publish
            // changes publishing state throughout the subtree, and the rows an editor can see are
            // the ones that have to stop saying "draft".
            await RefreshAsync(page.Id);
            await RefreshAsync(page.ParentId);
        }
        finally
        {
            _working = false;

            StateHasChanged();
        }
    }

    /// <summary>Closes the branch dialog, at the confirmation or at the report.</summary>
    /// <remarks>
    /// Closing never cancels anything. A background job belongs to the server the moment it is
    /// accepted, and a dialog that implied otherwise would have editors closing it to stop a publish
    /// that then went ahead.
    /// </remarks>
    private void CloseBranchDialog()
    {
        _branchImpact = null;
        _branchJob = null;
        _branchSubject = null;

        StateHasChanged();
    }

    /// <summary>The one request shape both the preview and the run are built from.</summary>
    private static BulkOperationRequest BranchRequest(PageSummary page) =>
        new(BulkOperation.Publish, new BulkSelection([page.Id], IncludeDescendants: true));

    /// <summary>Takes one page off the public site.</summary>
    private async Task UnpublishAsync(PageSummary page)
    {
        var result = await Client.UnpublishAsync(page.Id);

        if (!result.IsSuccess)
        {
            _moveErrors = result.Errors;

            return;
        }

        await RefreshAsync(page.ParentId);

        Toasts.ShowSuccess($"“{page.Title}” is no longer on the public site.", "Unpublished");
    }

    /// <summary>
    /// Asks the server what a delete would take, and puts the answer in front of the editor.
    /// </summary>
    /// <remarks>
    /// The count comes first, always. One delete takes a whole subtree with it, and "delete this
    /// page?" is not the question being asked when the page has forty descendants and eleven of
    /// them are live (acceptance criterion P6 #10).
    /// </remarks>
    private async Task AskToDeleteAsync(PageSummary page)
    {
        _deleteSubject = page;
        _deleting = await Client.DescribeDeleteAsync(page.Id);

        if (_deleting is null)
        {
            _deleteSubject = null;

            Toasts.ShowError("That page could not be read, so it was not deleted.");
        }
    }

    /// <summary>Sends the confirmed delete.</summary>
    private async Task ConfirmDeleteAsync()
    {
        if (_deleteSubject is not { } page) return;

        _working = true;

        try
        {
            var result = await Client.DeleteAsync(page.Id);

            if (!result.IsSuccess)
            {
                _moveErrors = result.Errors;

                return;
            }

            _deleting = null;
            _deleteSubject = null;

            await RefreshAsync(page.ParentId);

            var affected = result.Value!.AffectedPageIds.Count;

            Toasts.ShowSuccess(
                affected == 1
                    ? $"“{page.Title}” is in the recycle bin."
                    : $"“{page.Title}” and {affected - 1} page(s) beneath it are in the recycle bin.",
                "Deleted");
        }
        finally
        {
            _working = false;

            StateHasChanged();
        }
    }

    /// <summary>Backs out of a delete.</summary>
    private void CancelDelete()
    {
        _deleting = null;
        _deleteSubject = null;

        StateHasChanged();
    }

    /// <summary>
    /// Where a keyboard-opened menu should be drawn.
    /// </summary>
    /// <param name="X">Viewport x-coordinate.</param>
    /// <param name="Y">Viewport y-coordinate.</param>
    /// <remarks>
    /// A struct, so a runtime that answers the interop call with nothing — a stub, a pre-render —
    /// yields the origin rather than a null the caller would dereference.
    /// </remarks>
    private readonly record struct Anchor(double X, double Y);
}
