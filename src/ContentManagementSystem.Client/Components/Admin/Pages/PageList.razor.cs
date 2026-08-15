using System.ComponentModel.DataAnnotations;
using System.Globalization;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Pages;

/// <summary>
/// The content tree and the create-from-template form (task P2-23).
/// </summary>
/// <remarks>
/// Pre-rendered: both collections carry <c>[PersistentState]</c>, so the tree is in the HTML the
/// server sends and survives into the WebAssembly runtime without a second round trip.
/// <para>
/// Deliberately a flattened two-level table rather than a lazily expanding tree control. The tree
/// endpoint supports incremental expansion and Phase 6 builds the real thing; what this screen has
/// to prove is that a template becomes a page and a page becomes a URL, and a table does that
/// without inventing interaction the editor will only have to unlearn.
/// </para>
/// </remarks>
public partial class PageList : ComponentBase
{
    /// <summary>Reads and writes pages, over HTTP in the browser and directly on the server.</summary>
    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>The top of the tree, or null while it is still loading.</summary>
    [PersistentState]
    public IReadOnlyList<PageTreeNode>? Roots { get; set; }

    /// <summary>Templates a page can be created from, or null while they are still loading.</summary>
    [PersistentState]
    public IReadOnlyList<TemplateSummary>? Templates { get; set; }

    /// <summary>What the create form binds to.</summary>
    private CreatePageForm Form { get; set; } = new();

    /// <summary>Why the last create did not happen.</summary>
    private IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>Anything non-blocking the last create reported, such as a non-ASCII slug.</summary>
    private IReadOnlyList<ApiDiagnostic>? Warnings { get; set; }

    /// <summary>Whether a write is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        Roots ??= await Client.GetTreeAsync(depth: 2);
        Templates ??= await Client.GetTemplatesAsync();
    }

    /// <summary>Walks the tree depth-first, which is the order a reader expects to see it in.</summary>
    private static IEnumerable<PageTreeNode> Flatten(IReadOnlyList<PageTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    /// <summary>Indents a tree row by its depth.</summary>
    /// <remarks>
    /// An inline style rather than nested markup: a nested table would break the row-per-page
    /// reading order that makes the table navigable by keyboard and by screen reader, and depth is
    /// already carried in the data.
    /// </remarks>
    private static string Indent(int depth) =>
        depth == 0
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"padding-inline-start: {depth * 1.5m:0.#}rem");

    /// <summary>Indents an option in the parent picker, where markup is not available.</summary>
    private static string Prefix(int depth) => depth == 0 ? string.Empty : new string(' ', depth * 4) + "↳ ";

    private async Task CreateAsync()
    {
        IsBusy = true;
        Errors = null;
        Warnings = null;

        try
        {
            var result = await Client.CreateAsync(new CreatePageRequest(
                Form.TemplateId,
                Form.Title,
                Form.ParentId,
                string.IsNullOrWhiteSpace(Form.Slug) ? null : Form.Slug));

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Warnings = result.Warnings;
            Form = new CreatePageForm();

            // Refetched rather than appended. The server decides the slug, the sort order, and the
            // page's place in the tree, and a list assembled here would show something subtly
            // different from what a reload shows.
            Roots = await Client.GetTreeAsync(depth: 2);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>What the create form binds to.</summary>
    /// <remarks>
    /// A form model rather than the request record, because a record with <c>init</c> members cannot
    /// be two-way bound. The server's rules — the slug shape, whether a sibling already uses it,
    /// whether the template is enabled — are checked there and reported as diagnostics.
    /// </remarks>
    private sealed class CreatePageForm
    {
        /// <summary>Template the page is laid out by.</summary>
        [Range(1, int.MaxValue, ErrorMessage = "Choose a template.")]
        public int TemplateId { get; set; }

        /// <summary>Editor-facing title of the first draft.</summary>
        [Required(ErrorMessage = "A title is required.")]
        [StringLength(300, ErrorMessage = "A title may be at most 300 characters.")]
        public string? Title { get; set; }

        /// <summary>The page's own URL segment, or null to generate one.</summary>
        [StringLength(100, ErrorMessage = "A slug may be at most 100 characters.")]
        public string? Slug { get; set; }

        /// <summary>Parent node, or null for a page at the root of the site.</summary>
        public int? ParentId { get; set; }
    }
}
