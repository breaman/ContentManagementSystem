using System.ComponentModel.DataAnnotations;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Structure;

/// <summary>
/// The block type list and create form (task P1-29).
/// </summary>
public partial class BlockTypes : ComponentBase
{
    /// <summary>Reads and writes structure.</summary>
    [Inject]
    private IStructureClient Structure { get; set; } = default!;

    /// <summary>The block types, or null while loading.</summary>
    [PersistentState]
    public IReadOnlyList<BlockTypeSummary>? Items { get; set; }

    /// <summary>What the create form binds to.</summary>
    private CreateBlockTypeForm Form { get; set; } = new();

    /// <summary>Why the last create did not happen.</summary>
    private IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>Whether a write is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync() =>
        Items ??= await Structure.GetBlockTypesAsync();

    private async Task CreateAsync()
    {
        IsBusy = true;
        Errors = null;

        try
        {
            var result = await Structure.CreateBlockTypeAsync(
                new CreateBlockTypeRequest(
                    Form.Key,
                    Form.Name,
                    Form.Description,
                    SummaryTemplate: Form.SummaryTemplate));

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Form = new CreateBlockTypeForm();
            Items = await Structure.GetBlockTypesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>What the create form binds to.</summary>
    private sealed class CreateBlockTypeForm
    {
        /// <summary>Stable key for the new block type.</summary>
        [Required(ErrorMessage = "A key is required.")]
        [StringLength(100, ErrorMessage = "A key may be at most 100 characters.")]
        public string? Key { get; set; }

        /// <summary>Editor-facing name, shown in the block picker.</summary>
        [Required(ErrorMessage = "A display name is required.")]
        [StringLength(200, ErrorMessage = "A display name may be at most 200 characters.")]
        public string? Name { get; set; }

        /// <summary>Optional help text.</summary>
        [StringLength(500, ErrorMessage = "A description may be at most 500 characters.")]
        public string? Description { get; set; }

        /// <summary>Token pattern producing a collapsed block's one-line summary.</summary>
        [StringLength(500, ErrorMessage = "A summary template may be at most 500 characters.")]
        public string? SummaryTemplate { get; set; }
    }
}
