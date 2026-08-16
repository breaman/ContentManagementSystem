namespace ContentManagementSystem.Client.Components.Admin.Fields.Reference;

/// <summary>
/// The <c>media</c> editor (task P6-15, spec sections 7.1 and 13.4).
/// </summary>
/// <remarks>
/// An adapter and nothing more. <c>MediaSlotEditor</c> was built in P5-19 against the same contract
/// this one exposes — a stored value as JSON text in, a rewritten one out — so the field editor
/// catalog needs only a component with the three parameters
/// <see cref="ContentManagementSystem.Client.Components.Admin.Fields.FieldEditorBase"/> defines to
/// put it on the canvas.
/// <para>
/// Reimplementing it here would have meant a second opinion about what a usage-scope crop is, which
/// is precisely the drift the media library's own documentation warns about.
/// </para>
/// </remarks>
public partial class MediaFieldEditor : FieldEditorBase
{
}
