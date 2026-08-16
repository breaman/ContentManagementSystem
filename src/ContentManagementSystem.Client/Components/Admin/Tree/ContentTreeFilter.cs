using ContentManagementSystem.Shared.Contracts.Content;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Tree;

/// <summary>
/// The tree's inline filter over title, slug, and id (task P6-04, spec sections 14.2 and 17.1).
/// </summary>
/// <remarks>
/// <strong>Filtering replaces the tree with a flat list rather than pruning it.</strong> A tree that
/// loads a level at a time can only hide what it has already fetched, so a filter applied to it
/// would answer "no results" for every page the editor had not happened to expand — the one answer
/// a search must never give wrongly. The server searches the whole site; what comes back has no
/// tree shape, so it is not drawn as one.
/// <para>
/// Debounced, because the filter box is typed into a character at a time and each character is a
/// query over every page on the site.
/// </para>
/// </remarks>
public partial class ContentTree
{
    /// <summary>How long typing settles before the site is searched.</summary>
    private static readonly TimeSpan FilterDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>How many matches are shown before the editor is asked to narrow the term.</summary>
    private const int FilterLimit = 50;

    /// <summary>What has been typed into the filter box.</summary>
    private string _filter = string.Empty;

    /// <summary>The matches, or null while none have been fetched.</summary>
    private IReadOnlyList<PageSummary>? _results;

    /// <summary>Whether a search is in flight.</summary>
    private bool _searching;

    /// <summary>Cancels a search superseded by a later keystroke.</summary>
    private CancellationTokenSource? _search;

    /// <summary>Whether the filter box has something in it, so the results replace the tree.</summary>
    private bool IsFiltering => !string.IsNullOrWhiteSpace(_filter);

    /// <summary>Takes a keystroke from the filter box.</summary>
    private void OnFilterChanged(ChangeEventArgs args)
    {
        _filter = args.Value?.ToString() ?? string.Empty;

        _search?.Cancel();
        _search?.Dispose();

        if (!IsFiltering)
        {
            _search = null;
            _results = null;
            _searching = false;

            return;
        }

        var cancellation = new CancellationTokenSource();

        _search = cancellation;
        _searching = true;

        _ = SearchAsync(_filter, cancellation.Token);
    }

    /// <summary>Empties the filter box and puts the tree back.</summary>
    private void ClearFilter()
    {
        _search?.Cancel();
        _search?.Dispose();
        _search = null;

        _filter = string.Empty;
        _results = null;
        _searching = false;
    }

    /// <summary>The debounced half of <see cref="OnFilterChanged"/>.</summary>
    private async Task SearchAsync(string term, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FilterDelay, cancellationToken);

            var found = await Client.ListAsync(
                new PageQuery(Search: term, Limit: FilterLimit),
                cancellationToken);

            // The term may have moved on while this was in flight. Checked rather than relying on
            // the token alone, because the request completing and the token being cancelled is a
            // race the token cannot settle on its own.
            if (cancellationToken.IsCancellationRequested || !string.Equals(term, _filter, StringComparison.Ordinal))
            {
                return;
            }

            _results = found.Items;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke.
            return;
        }
        catch (HttpRequestException exception)
        {
            _error = exception.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _searching = false;

                await InvokeAsync(StateHasChanged);
            }
        }
    }
}
