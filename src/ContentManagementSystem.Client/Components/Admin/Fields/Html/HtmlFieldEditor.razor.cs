using ContentManagementSystem.Client.Components.Admin.Fields.Common;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Html;

/// <summary>
/// The <c>html</c> editor — source, a permitted-tags banner, and a running account of what saving
/// will remove (task P6-13, acceptance criterion P6 #3, spec section 14.4).
/// </summary>
/// <remarks>
/// <strong>The warning is the feature.</strong> Silent stripping is the number-one "the CMS ate my
/// content" support ticket, and the answer is not to stop stripping — the allowlist is what stands
/// between a pasted widget and a stored-XSS incident — but to stop it being silent. An author who
/// pastes an embed sees, while they are still looking at it, that the <c>&lt;script&gt;</c> in it
/// will not survive.
/// <para>
/// So the check runs in <em>every</em> mode, including Edit, and does not depend on the preview pane
/// being open. That costs a second request while split mode is showing, both of them an in-memory
/// sanitize on the server; the alternative is a warning that appears only once an author thinks to
/// look at the preview, which is exactly the author who will not.
/// </para>
/// <para>
/// This field type is <c>DeveloperOnly</c> and sanitizes under the <c>Developer</c> profile, which
/// is the widest of the three — iframes against a host allowlist, media elements, data attributes.
/// Wider is not unchecked: <c>&lt;script&gt;</c>, <c>on*</c> handlers, and off-allowlist URL schemes
/// are refused under every profile, and a role is an authorization decision rather than a reason to
/// store markup unexamined.
/// </para>
/// </remarks>
public partial class HtmlFieldEditor : FieldEditorBase, IDisposable
{
    /// <summary>How long to wait after the last keystroke before checking.</summary>
    private static readonly TimeSpan CheckDebounce = TimeSpan.FromMilliseconds(400);

    /// <summary>How many removals to list before summarising the rest.</summary>
    private const int RemovalsShown = 8;

    /// <summary>The profile this field type's values are cleaned under.</summary>
    private const string ProfileName = nameof(SanitizationProfile.Developer);

    [Inject]
    private IMarkupPreviewClient Preview { get; set; } = default!;

    /// <summary>Which of the three surfaces is showing.</summary>
    private EditorMode Mode { get; set; } = EditorMode.Edit;

    /// <summary>The CodeMirror surface.</summary>
    private SourceEditor? Source { get; set; }

    /// <summary>How far down the source editor is, for split mode.</summary>
    private double? SourceFraction { get; set; }

    /// <summary>What saving this markup will remove.</summary>
    private IReadOnlyList<SanitizationRemoval> Removals { get; set; } = [];

    /// <summary>The elements the profile keeps.</summary>
    private IReadOnlyList<string> Allowed { get; set; } = [];

    /// <summary>The authored markup, read out of the stored envelope.</summary>
    private string Text => StoredValue.ReadText(Value) ?? string.Empty;

    /// <summary>The enforced maximum length, when the slot configures one.</summary>
    private int? MaxLength => ConfiguredInt32(FieldSettingNames.MaxLength);

    /// <summary>The advisory length the counter starts warning at.</summary>
    private int? SoftLimit => ConfiguredInt32(FieldSettingNames.SoftLimit);

    /// <summary>The editing surface's accessible name, which the card's heading cannot reach.</summary>
    private string SurfaceLabel => $"{Field.Slot.Name}, HTML source";

    private string CountId => $"{Field.ControlId}-count";

    private string PreviewId => $"{Field.ControlId}-preview";

    private string WarningId => $"{Field.ControlId}-strip";

    /// <summary>Cancels the check a newer keystroke has superseded.</summary>
    private CancellationTokenSource? _check;

    /// <summary>The markup the last check ran against.</summary>
    private string? _checked;

    private string AllowedSummary => Allowed.Count == 0
        ? "What this property keeps"
        : $"What this property keeps — {Allowed.Count} elements";

    private string WarningSummary => Removals.Count == 1
        ? "One thing here will be removed when you save:"
        : $"{Removals.Count} things here will be removed when you save:";

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var profiles = await Preview.GetProfilesAsync();

        Allowed = profiles
            .FirstOrDefault(profile => string.Equals(profile.Profile, ProfileName, StringComparison.OrdinalIgnoreCase))
            ?.Tags ?? [];
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(Text, _checked, StringComparison.Ordinal)) return;

        await ScheduleCheckAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _check?.Cancel();
        _check?.Dispose();
        _check = null;

        GC.SuppressFinalize(this);
    }

    /// <summary>Waits for the typing to pause, then asks the server what it would strip.</summary>
    /// <remarks>
    /// Through the same endpoint the preview uses, which is the same sanitizer the save will run
    /// (ADR-0008). A client-side approximation of the allowlist would be a warning that is wrong in
    /// both directions — silent about something that will be stripped, and alarming about something
    /// that will not.
    /// </remarks>
    private async Task ScheduleCheckAsync()
    {
        _check?.Cancel();
        _check?.Dispose();

        var cancellation = _check = new CancellationTokenSource();
        var markup = Text;

        if (markup.Length == 0)
        {
            _checked = markup;
            Removals = [];

            return;
        }

        try
        {
            await Task.Delay(CheckDebounce, cancellation.Token);

            var result = await Preview.RenderAsync(
                new MarkupPreviewRequest(MarkupFormats.Html, markup, ProfileName),
                cancellation.Token);

            if (cancellation.IsCancellationRequested) return;

            _checked = markup;

            // A failed check leaves the previous account showing rather than clearing it. Clearing
            // would say "nothing will be removed", which is the one thing this control must never
            // say without having asked.
            if (result is not null) Removals = result.Removals;
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke owns the check now; it will set the state when it lands.
        }
    }

    private Task OnTextChangedAsync(string text) => WriteTextAsync(text);

    private Task OnModeChangedAsync(EditorMode mode)
    {
        Mode = mode;

        if (mode is not EditorMode.Split) SourceFraction = null;

        return Task.CompletedTask;
    }

    /// <summary>
    /// The scroll callback the source editor gets, which is nothing outside split mode.
    /// </summary>
    /// <remarks>
    /// An unset callback is what stops the editor subscribing to its own scrolling at all, so a
    /// zone nobody has put into split mode pays for no scroll interop. It is built here rather than
    /// as a conditional in the markup because <c>EventCallback</c> is a struct and a target-typed
    /// ternary cannot produce one.
    /// </remarks>
    private EventCallback<double> ScrollCallback => Mode is EditorMode.Split
        ? EventCallback.Factory.Create<double>(this, OnSourceScrolled)
        : default;

    private void OnSourceScrolled(double fraction) => SourceFraction = fraction;
}
