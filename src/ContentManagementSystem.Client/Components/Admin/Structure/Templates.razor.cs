using System.ComponentModel.DataAnnotations;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Structure;

/// <summary>
/// The template list and create form (task P1-29).
/// </summary>
/// <remarks>
/// Pre-rendered: <see cref="Items"/> carries <c>[PersistentState]</c>, so the list is in the HTML
/// the server sends and survives into the WebAssembly runtime without a second round trip. Without
/// it a developer would watch an empty table while the runtime downloads, and then watch it fill in.
/// </remarks>
public partial class Templates : ComponentBase
{
    /// <summary>Reads and writes structure, over HTTP in the browser and directly on the server.</summary>
    [Inject]
    private IStructureClient Structure { get; set; } = default!;

    /// <summary>The templates, or null while they are still loading.</summary>
    [PersistentState]
    public IReadOnlyList<TemplateSummary>? Items { get; set; }

    /// <summary>What the create form binds to.</summary>
    private CreateTemplateForm Form { get; set; } = new();

    /// <summary>Why the last create did not happen.</summary>
    private IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>Anything non-blocking the last create reported.</summary>
    private IReadOnlyList<ApiDiagnostic>? Warnings { get; set; }

    /// <summary>Whether a write is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync() =>
        Items ??= await Structure.GetTemplatesAsync();

    private async Task CreateAsync()
    {
        IsBusy = true;
        Errors = null;
        Warnings = null;

        try
        {
            var result = await Structure.CreateTemplateAsync(
                new CreateTemplateRequest(Form.Key, Form.Name, Form.Description));

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Warnings = result.Warnings;
            Form = new CreateTemplateForm();

            // Refetched rather than appended. The server decides the sort order and fills in fields
            // the request never mentioned, and a list assembled client-side would show something
            // subtly different from what a reload shows.
            Items = await Structure.GetTemplatesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>What the create form binds to.</summary>
    /// <remarks>
    /// A form model rather than the request record, because a record with <c>init</c> members cannot
    /// be two-way bound and because the form needs its own annotations. The server's rules — the key
    /// shape, whether it is taken — are checked there and reported as diagnostics.
    /// </remarks>
    private sealed class CreateTemplateForm
    {
        /// <summary>Stable key for the new template.</summary>
        [Required(ErrorMessage = "A key is required.")]
        [StringLength(100, ErrorMessage = "A key may be at most 100 characters.")]
        public string? Key { get; set; }

        /// <summary>Editor-facing display name.</summary>
        [Required(ErrorMessage = "A display name is required.")]
        [StringLength(200, ErrorMessage = "A display name may be at most 200 characters.")]
        public string? Name { get; set; }

        /// <summary>Optional help text.</summary>
        [StringLength(500, ErrorMessage = "A description may be at most 500 characters.")]
        public string? Description { get; set; }
    }
}
