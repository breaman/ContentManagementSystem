using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Text;

/// <summary>
/// The <c>multilineText</c> editor: unmarked prose over several lines, counted as it is typed
/// (task P6-14, spec section 14.4).
/// </summary>
/// <remarks>
/// The same control as <see cref="PlainTextEditor"/> grown a second dimension, plus a word count,
/// which is the number an author writing a standfirst or a summary is actually working to.
/// <para>
/// The preview splits on both line ending conventions and joins with <c>&lt;br&gt;</c>, which is
/// what <c>MultilineTextRenderer</c> does — the same rule rather than a similar one, because a value
/// pasted from a Windows editor arrives with <c>\r\n</c> and a preview that drew an extra blank line
/// for it would send an author hunting for a break that is not there.
/// </remarks>
public partial class MultilineTextEditor : FieldEditorBase
{
    /// <summary>Both line ending conventions, collapsing to one break each.</summary>
    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];

    /// <summary>Whether the preview is open.</summary>
    private bool IsPreviewing { get; set; }

    /// <summary>The authored text, read out of the stored envelope.</summary>
    private string Text => StoredValue.ReadText(Value) ?? string.Empty;

    /// <summary>The text split the way the renderer splits it.</summary>
    private IReadOnlyList<string> Lines =>
        Text is { Length: > 0 } text ? text.Split(LineBreaks, StringSplitOptions.None) : [];

    /// <summary>The enforced maximum length, when the slot configures one.</summary>
    private int? MaxLength => ConfiguredInt32(FieldSettingNames.MaxLength);

    /// <summary>The advisory length the counter starts warning at.</summary>
    private int? SoftLimit => ConfiguredInt32(FieldSettingNames.SoftLimit);

    /// <summary>The browser-enforced ceiling; see <see cref="PlainTextEditor"/> for why it is loose.</summary>
    private int HardStop => MaxLength is { } max ? Math.Max(max * 2, max + 100) : 40_000;

    /// <summary>Whether there is anything worth previewing.</summary>
    private bool CanPreview => Text.Length > 0;

    private string CountId => $"{Field.ControlId}-count";

    private string PreviewId => $"{Field.ControlId}-preview";

    private string DescribedBy => string.Join(
        ' ',
        new[] { Field.DescribedBy, CountId }.Where(id => !string.IsNullOrEmpty(id)));

    private string InvalidClass => Field.Severity is ZoneSeverity.Error ? "is-invalid" : string.Empty;

    private Task OnInputAsync(ChangeEventArgs args) => WriteTextAsync(args.Value?.ToString());
}
