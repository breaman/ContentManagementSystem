using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Structure;

/// <summary>
/// One block type's property definitions: add, edit, and remove (task P1-29).
/// </summary>
/// <remarks>
/// Shows the flattened property set — the block type's own followed by each composed group's — which
/// is the order an editor sees and the order the revision snapshot records. Composed properties are
/// listed but not editable here; the composition owns them, and offering an edit control would let a
/// developer fork one shared definition into several without noticing.
/// </remarks>
public partial class BlockTypeEditor : ComponentBase
{
    /// <summary>Identity of the block type being edited.</summary>
    [Parameter]
    public int Id { get; set; }

    /// <summary>Reads and writes structure.</summary>
    [Inject]
    private IStructureClient Structure { get; set; } = default!;

    /// <summary>The block type and its properties, or null while loading.</summary>
    [PersistentState]
    public BlockTypeDetail? Detail { get; set; }

    /// <summary>The field types a property may bind to.</summary>
    /// <remarks>
    /// Nullable rather than defaulted to an empty list: a <c>[PersistentState]</c> property must not
    /// carry an initializer, because the initializer runs after the restored state is applied and
    /// would silently throw it away.
    /// </remarks>
    [PersistentState]
    public IReadOnlyList<FieldTypeDescriptor>? FieldTypes { get; set; }

    /// <summary>What the add-or-edit form binds to.</summary>
    private SlotFormModel Form { get; set; } = SlotFormModel.ForNew(0);

    /// <summary>Why the last write did not happen.</summary>
    private IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>Anything non-blocking the last write reported.</summary>
    private IReadOnlyList<ApiDiagnostic>? Warnings { get; set; }

    /// <summary>Whether a write is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        Detail ??= await Structure.GetBlockTypeAsync(Id);

        FieldTypes ??= await Structure.GetFieldTypesAsync();

        Reset();
    }

    private void Edit(PropertyDefinition property)
    {
        Form = SlotFormModel.From(property);
        Errors = null;
        Warnings = null;
    }

    /// <summary>Returns the form to adding, positioned after the block type's own properties.</summary>
    private void Reset() => Form = SlotFormModel.ForNew(Detail?.Properties.Count ?? 0);

    private async Task SaveAsync(SlotFormModel model)
    {
        if (!model.TryReadConfiguration(out var configuration, out var error))
        {
            Errors = [new ApiDiagnostic("client.configuration-json", $"Configuration is not valid JSON: {error}")];

            return;
        }

        IsBusy = true;
        Errors = null;
        Warnings = null;

        try
        {
            var result = model.IsNew
                ? await Structure.CreatePropertyAsync(
                    Id,
                    new CreatePropertyRequest(
                        model.Key,
                        model.Name,
                        model.FieldTypeKey,
                        configuration,
                        model.Description,
                        model.IsRequired,
                        model.Group,
                        model.SortOrder))
                : await Structure.UpdatePropertyAsync(
                    Id,
                    model.Id!.Value,
                    new UpdatePropertyRequest(
                        model.Key,
                        model.Name,
                        model.FieldTypeKey,
                        configuration,
                        model.Description,
                        model.IsRequired,
                        model.Group,
                        model.SortOrder));

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Warnings = result.Warnings;

            await ReloadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveAsync(PropertyDefinition property)
    {
        IsBusy = true;
        Errors = null;
        Warnings = null;

        try
        {
            var result = await Structure.DeletePropertyAsync(Id, property.Id);

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            await ReloadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAsync()
    {
        Detail = await Structure.GetBlockTypeAsync(Id);

        Reset();
    }
}
