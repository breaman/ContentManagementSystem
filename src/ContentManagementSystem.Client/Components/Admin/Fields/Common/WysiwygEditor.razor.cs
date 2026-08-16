using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Common;

/// <summary>
/// Quill as a Blazor component — the constrained WYSIWYG surface of spec section 14.4 (task P6-08).
/// </summary>
/// <remarks>
/// The toolbar is short by design, and the two buttons an author would most expect are missing from
/// it on purpose: link and image open the CMS pickers instead (P6-11). A hand-typed URL to an
/// internal page is a copy that nothing updates when the page moves, which is the whole reason
/// ADR-0006 stores internal links by identity.
/// <para>
/// Quill has no <c>destroy()</c>, and its toolbar is a sibling node it never removes. The teardown
/// lives in the bundle beside the code that created it; what this component owes is calling it,
/// which <see cref="JsEditorComponentBase"/> guarantees.
/// </para>
/// </remarks>
public partial class WysiwygEditor : JsEditorComponentBase
{
    /// <summary>
    /// The editor's accessible name.
    /// </summary>
    /// <remarks>
    /// Set on Quill's own editable element when it mounts, not on the host <c>div</c>. See
    /// <see cref="SourceEditor.Label"/> for why both halves of that matter.
    /// </remarks>
    [Parameter]
    public string Label { get; set; } = "Formatted text";

    /// <inheritdoc />
    protected override string ModulePath => "./js/cms-wysiwyg-editor.js";

    /// <inheritdoc />
    protected override ValueTask MountAsync(
        IJSObjectReference module,
        DotNetObjectReference<JsEditorComponentBase> self) =>
        module.InvokeVoidAsync("create", EditorId, Host, Text, self, ReadOnly, Label);

    /// <summary>Links the current selection, or inserts the words when nothing is selected.</summary>
    /// <param name="href">Where the link goes.</param>
    /// <param name="text">The words to show, used only when there is no selection.</param>
    public ValueTask InsertLinkAsync(string href, string? text) =>
        InvokeAsync("insertLink", href, text);

    /// <summary>Inserts an image at the caret.</summary>
    /// <param name="src">The image's address.</param>
    /// <param name="alt">What the image says, for a reader who cannot see it.</param>
    public ValueTask InsertImageAsync(string src, string? alt) =>
        InvokeAsync("insertImage", src, alt);

    /// <summary>The plain text currently selected, which a link picker offers as the link's words.</summary>
    public async ValueTask<string> SelectionAsync() =>
        await InvokeAsync<string>("getSelection") ?? string.Empty;
}
