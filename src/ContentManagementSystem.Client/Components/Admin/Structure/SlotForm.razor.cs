using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Structure;

/// <summary>
/// The add-or-edit form shared by zones and block-type properties.
/// </summary>
/// <remarks>
/// The key and field type inputs go read-only once the slot exists, rather than being hidden. The
/// server refuses to change either (spec section 8.5), and a developer looking at the form needs to
/// see what the values <em>are</em> — hiding them makes an immutable field look like a missing one.
/// </remarks>
public partial class SlotForm : ComponentBase
{
    /// <summary>What the form binds to.</summary>
    [Parameter]
    [EditorRequired]
    public SlotFormModel Model { get; set; } = new();

    /// <summary>The field types available to bind to.</summary>
    [Parameter]
    public IReadOnlyList<FieldTypeDescriptor> FieldTypes { get; set; } = [];

    /// <summary>What the slot is called in this context — <c>zone</c> or <c>property</c>.</summary>
    [Parameter]
    public string Noun { get; set; } = "zone";

    /// <summary>Whether to offer the in-context editing flag, which only zones have.</summary>
    [Parameter]
    public bool ShowInlineEditable { get; set; }

    /// <summary>Whether a save is in flight, which disables the whole fieldset.</summary>
    [Parameter]
    public bool IsBusy { get; set; }

    /// <summary>Raised when the form is submitted and passes its client-side checks.</summary>
    [Parameter]
    public EventCallback<SlotFormModel> OnSubmit { get; set; }

    /// <summary>Raised when the developer abandons the form.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>
    /// Distinguishes this form from others on the page during static rendering.
    /// </summary>
    /// <remarks>
    /// A template's editor shows an add form and an edit form for the same shape, and without
    /// distinct names the framework cannot tell which one was posted.
    /// </remarks>
    private string FormName => Model.IsNew ? $"add-{Noun}" : $"edit-{Noun}-{Model.Id}";

    /// <summary>
    /// The settings the chosen field type declares, listed under the configuration box.
    /// </summary>
    /// <remarks>
    /// Read from the JSON Schema the registry serves (task P1-24), so an extension author's field
    /// type documents itself here with no change to this screen. Null when nothing is chosen or the
    /// schema declares no settings, in which case the hint is omitted rather than shown empty.
    /// </remarks>
    private IReadOnlyList<string>? SelectedSchema
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Model.FieldTypeKey)) return null;

            var descriptor = FieldTypes.FirstOrDefault(candidate => candidate.Key == Model.FieldTypeKey);

            if (descriptor is null) return null;

            if (!descriptor.ConfigurationSchema.TryGetProperty("properties", out var properties)) return null;

            var names = properties.EnumerateObject().Select(member => member.Name).ToList();

            return names.Count > 0 ? names : null;
        }
    }

    private Task SubmitAsync() => OnSubmit.InvokeAsync(Model);
}
