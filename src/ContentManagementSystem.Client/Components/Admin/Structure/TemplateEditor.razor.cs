using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Structure;

/// <summary>
/// One template's zone definitions: add, edit, and remove (task P1-29).
/// </summary>
/// <remarks>
/// The screen behind acceptance criteria <c>P1 #1</c>, <c>P1 #3</c>, and <c>P1 #4</c>. It offers no
/// control at all for the two changes spec section 8.5 forbids — the key and field type inputs go
/// read-only once a zone exists — but it does not rely on that for correctness: the service refuses
/// both regardless, and the refusal is rendered here as a diagnostic.
/// </remarks>
public partial class TemplateEditor : ComponentBase
{
    /// <summary>Identity of the template being edited.</summary>
    [Parameter]
    public int Id { get; set; }

    /// <summary>Reads and writes structure.</summary>
    [Inject]
    private IStructureClient Structure { get; set; } = default!;

    /// <summary>The template and its zones, or null while loading.</summary>
    [PersistentState]
    public TemplateDetail? Detail { get; set; }

    /// <summary>The field types a zone may bind to.</summary>
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
        Detail ??= await Structure.GetTemplateAsync(Id);

        FieldTypes ??= await Structure.GetFieldTypesAsync();

        Reset();
    }

    /// <summary>Loads an existing zone into the form.</summary>
    private void Edit(ZoneDefinition zone)
    {
        Form = SlotFormModel.From(zone);
        Errors = null;
        Warnings = null;
    }

    /// <summary>Returns the form to adding, positioned after the zones that exist.</summary>
    private void Reset() => Form = SlotFormModel.ForNew(Detail?.Zones.Count ?? 0);

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
                ? await Structure.CreateZoneAsync(
                    Id,
                    new CreateZoneRequest(
                        model.Key,
                        model.Name,
                        model.FieldTypeKey,
                        configuration,
                        model.Description,
                        model.IsRequired,
                        model.IsInlineEditable,
                        model.Group,
                        model.SortOrder))
                : await Structure.UpdateZoneAsync(
                    Id,
                    model.Id!.Value,
                    new UpdateZoneRequest(
                        model.Key,
                        model.Name,
                        model.FieldTypeKey,
                        configuration,
                        model.Description,
                        model.IsRequired,
                        model.IsInlineEditable,
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

    private async Task RemoveAsync(ZoneDefinition zone)
    {
        IsBusy = true;
        Errors = null;
        Warnings = null;

        try
        {
            var result = await Structure.DeleteZoneAsync(Id, zone.Id);

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

    /// <summary>
    /// Refetches the template after a write.
    /// </summary>
    /// <remarks>
    /// Every write cuts or may cut a revision, and the revision number on screen is the thing a
    /// developer checks to see whether their change was structural. Patching the local copy would
    /// leave that number stale, which is worse than a round trip on a screen used a handful of times
    /// a day.
    /// </remarks>
    private async Task ReloadAsync()
    {
        Detail = await Structure.GetTemplateAsync(Id);

        Reset();
    }
}
