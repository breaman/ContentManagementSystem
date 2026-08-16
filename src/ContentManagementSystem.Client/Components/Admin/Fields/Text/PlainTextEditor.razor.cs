using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Text;

/// <summary>
/// The <c>plainText</c> editor: one line, counted as it is typed, previewable in the template's own
/// typography (task P6-14, spec section 14.4).
/// </summary>
/// <remarks>
/// A single-line <c>input</c> rather than a textarea, because <c>plainText</c> refuses line breaks:
/// a control an author can press Enter in is a control that invites a value the validator will
/// reject, and the rejection arrives a screen later at publish time.
/// <para>
/// <strong>The preview is not decoration.</strong> A headline is written to fit, and it is written
/// in a backoffice font at a backoffice size against a backoffice background — none of which is what
/// a reader sees. Rendering it in the content typography layer is what lets an author find out that
/// their title wraps to three lines before a reader does.
/// </para>
/// </remarks>
public partial class PlainTextEditor : FieldEditorBase
{
    /// <summary>Whether the preview is open.</summary>
    private bool IsPreviewing { get; set; }

    /// <summary>The authored text, read out of the stored envelope.</summary>
    private string Text => StoredValue.ReadText(Value) ?? string.Empty;

    /// <summary>The enforced maximum length, when the slot configures one.</summary>
    private int? MaxLength => ConfiguredInt32(FieldSettingNames.MaxLength);

    /// <summary>The advisory length the counter starts warning at.</summary>
    private int? SoftLimit => ConfiguredInt32(FieldSettingNames.SoftLimit);

    /// <summary>The pattern every value must match, when the slot configures one.</summary>
    private string? Pattern => ConfiguredText(FieldSettingNames.Pattern);

    /// <summary>
    /// The browser-enforced ceiling, which is deliberately <em>not</em> the configured maximum.
    /// </summary>
    /// <remarks>
    /// A <c>maxlength</c> attribute set to the real limit silently swallows the keystrokes past it:
    /// the author types a longer headline, sees a shorter one, and has no idea why. The counter
    /// tells them instead, and this attribute exists only as a stop far enough out that nothing but
    /// a paste of a whole document reaches it — which is worth stopping, because the alternative is
    /// a megabyte in a payload.
    /// </remarks>
    private int HardStop => MaxLength is { } max ? Math.Max(max * 2, max + 100) : 8_000;

    /// <summary>Whether there is anything worth previewing.</summary>
    private bool CanPreview => Text.Length > 0;

    private string CountId => $"{Field.ControlId}-count";

    private string PreviewId => $"{Field.ControlId}-preview";

    private string PatternId => $"{Field.ControlId}-pattern";

    /// <summary>
    /// What names this control's help, in the order a screen reader should hear it.
    /// </summary>
    /// <remarks>
    /// The card's help text first, then the format rule, then the count. The count is last on purpose
    /// — it is the part that changes while typing, and a reader who has to sit through a running
    /// number before hearing the format rule stops listening to either.
    /// </remarks>
    private string DescribedBy => string.Join(
        ' ',
        new[] { Field.DescribedBy, Pattern is { Length: > 0 } ? PatternId : null, CountId }
            .Where(id => !string.IsNullOrEmpty(id)));

    /// <summary>What to tell an author about the format, preferring what the developer wrote.</summary>
    private string PatternHint =>
        ConfiguredText(FieldSettingNames.PatternMessage) is { Length: > 0 } message
            ? message
            : $"Must match the pattern {Pattern}.";

    private string InvalidClass => Field.Severity is ZoneSeverity.Error ? "is-invalid" : string.Empty;

    /// <summary>
    /// Writes what was typed, and keeps the preview honest while it is open.
    /// </summary>
    /// <remarks>
    /// Bound to <c>oninput</c> rather than only to <c>onchange</c>, which is what makes the counter
    /// live. The cost is a render per keystroke over a control this small, which is the point of it.
    /// </remarks>
    private Task OnInputAsync(ChangeEventArgs args) => WriteTextAsync(args.Value?.ToString());
}
