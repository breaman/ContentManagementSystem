using ContentManagementSystem.Client.Components.Admin.Fields.Common;
using ContentManagementSystem.Client.Components.Admin.Pickers;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.RichText;

/// <summary>
/// The <c>richText</c> editor — Edit, Preview, and Split, over either stored format
/// (tasks P6-08 to P6-12, spec section 14.4).
/// </summary>
/// <remarks>
/// <strong>The surface follows the value, not the configuration.</strong> A <c>richText</c> value
/// carries its own <c>format</c>, and a property switched from markdown to HTML must still be able
/// to edit what is already stored — so a markdown value opens in CodeMirror and an HTML value opens
/// in Quill, whatever the zone was most recently configured to prefer. Guessing is not available:
/// markdown shown as HTML displays its source, and HTML shown as markdown escapes its markup.
/// <para>
/// <strong>Preview is rendered by the server</strong> through the same Markdig configuration and the
/// same sanitizer the public site uses (P6-09), which is what acceptance criterion P6 #2 asks for.
/// Split mode drives the preview's scroll position from the source editor's, as a fraction rather
/// than a pixel offset (P6-10).
/// </para>
/// <para>
/// <strong>Link and picture open the CMS pickers</strong> (P6-11). What the author never does is
/// type an address — the one that lands in the document is the one the CMS resolved. For a property
/// this would go further and store the page id (ADR-0006); prose is text and has to carry an
/// address, and the redirect a move creates is what catches it going stale.
/// </para>
/// </remarks>
public partial class RichTextFieldEditor : FieldEditorBase
{
    [Inject]
    private IPageClient Pages { get; set; } = default!;

    [Inject]
    private IMediaClient Media { get; set; } = default!;

    /// <summary>Which of the three surfaces is showing.</summary>
    private EditorMode Mode { get; set; } = EditorMode.Edit;

    /// <summary>The CodeMirror surface, when the value is markdown.</summary>
    private SourceEditor? Source { get; set; }

    /// <summary>The Quill surface, when the value is HTML.</summary>
    private WysiwygEditor? Wysiwyg { get; set; }

    /// <summary>How far down the source editor is, for split mode.</summary>
    private double? SourceFraction { get; set; }

    /// <summary>Whether the link picker is open.</summary>
    private bool IsPickingLink { get; set; }

    /// <summary>Whether the image picker is open.</summary>
    private bool IsPickingImage { get; set; }

    /// <summary>What was selected when the link picker was opened, offered as the link's words.</summary>
    private string SelectedText { get; set; } = string.Empty;

    /// <summary>The authored source, read out of the stored envelope.</summary>
    private string Text => StoredValue.ReadText(Value) ?? string.Empty;

    /// <summary>Whether the stored value is HTML rather than markdown.</summary>
    private bool IsHtml => FormatOf(Value) is RichTextFormats.Html;

    /// <summary>Which allowlist the preview should clean under.</summary>
    private string? Profile => ConfiguredText(FieldSettingNames.Profile);

    /// <summary>The enforced maximum length, when the slot configures one.</summary>
    private int? MaxLength => ConfiguredInt32(FieldSettingNames.MaxLength);

    /// <summary>The advisory length the counter starts warning at.</summary>
    private int? SoftLimit => ConfiguredInt32(FieldSettingNames.SoftLimit);

    /// <summary>The editing surface's accessible name, which the card's heading cannot reach.</summary>
    private string SurfaceLabel => $"{Field.Slot.Name}, {(IsHtml ? "formatted text" : "markdown source")}";

    private string CountId => $"{Field.ControlId}-count";

    private string PreviewId => $"{Field.ControlId}-preview";

    /// <summary>
    /// Writes what the editor holds, keeping the format the value was stored with.
    /// </summary>
    /// <remarks>
    /// The format is written on every save rather than only when it is missing. A value that arrived
    /// without one — from an import, or from a build before the member existed — would otherwise stay
    /// unreadable forever, and the field type treats an absent format as an error rather than as a
    /// default.
    /// </remarks>
    private Task OnTextChangedAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return WriteAsync(string.Empty);

        var format = FormatOf(Value);

        return WriteAsync(StoredValue.Write(Value, FieldTypeKey, stored =>
        {
            stored[RichTextFormats.Member] = format;
            stored[StoredValue.ValueMember] = text;
        }));
    }

    /// <summary>
    /// Changes mode, and stops driving the preview's scroll when split is no longer showing.
    /// </summary>
    /// <remarks>
    /// Clearing the fraction matters: it is what stops the preview being scrolled to wherever the
    /// source editor happened to be the last time anybody looked at both, when it is next opened on
    /// its own.
    /// </remarks>
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

    /// <summary>Opens the link picker, offering whatever is selected as the link's words.</summary>
    private async Task OpenLinkAsync()
    {
        SelectedText = Source is not null
            ? await Source.SelectionAsync()
            : Wysiwyg is not null
                ? await Wysiwyg.SelectionAsync()
                : string.Empty;

        IsPickingLink = true;
    }

    /// <summary>Resolves the chosen destination to an address and inserts it.</summary>
    /// <remarks>
    /// An internal target is resolved here rather than in the picker, because the picker's other
    /// caller — a <c>link</c> property — deliberately does not want an address at all. A target that
    /// cannot be resolved inserts nothing and leaves the document alone, which is the honest outcome
    /// for a link nobody can follow.
    /// </remarks>
    private async Task OnLinkPickedAsync(LinkPick pick)
    {
        IsPickingLink = false;

        var href = pick.Href ?? await ResolveAsync(pick);

        if (string.IsNullOrEmpty(href)) return;

        var words = pick.Text is { Length: > 0 } text ? text : SelectedText;

        if (Wysiwyg is not null)
        {
            await Wysiwyg.InsertLinkAsync(href, words);

            return;
        }

        if (Source is null) return;

        // Markdown link syntax. The words fall back to the address, because [](…) renders as an
        // empty anchor — a link a sighted reader cannot see and a screen reader announces as blank.
        var label = words is { Length: > 0 } ? words : href;

        await Source.InsertAsync($"[{Escape(label)}]({href})", selectInserted: false);
    }

    /// <summary>Inserts the chosen picture, with the alternative text the library holds for it.</summary>
    private async Task OnImagePickedAsync(MediaDetail item)
    {
        IsPickingImage = false;

        // A client cannot sign a rendition URL, so the address comes from the server exactly as it
        // does for the media renderer (spec section 13.5).
        var links = await Media.LinksAsync([item.Id]);

        // The preview rendition rather than the original: an editor inserting a photograph into
        // prose wants the one the page will show, not the twelve-megapixel source behind it. The
        // original is the fallback for a file that has no preview, such as a vector.
        if (!links.TryGetValue(item.Id, out var resolved) ||
            (resolved.PreviewUrl ?? resolved.OriginalUrl) is not { Length: > 0 } src)
        {
            return;
        }

        // The library's own description. A picture marked decorative gets an empty alt, which is the
        // correct markup for one — and anything else gets what the library holds, so a picture with
        // no description in the library arrives with none here and the publish check says so.
        var alt = item.IsDecorative ? string.Empty : item.AltText ?? string.Empty;

        if (Wysiwyg is not null)
        {
            await Wysiwyg.InsertImageAsync(src, alt);

            return;
        }

        if (Source is null) return;

        await Source.InsertAsync($"![{Escape(alt)}]({src})", selectInserted: false);
    }

    /// <summary>Resolves an internal destination to the address the page currently sits at.</summary>
    private async Task<string?> ResolveAsync(LinkPick pick) => pick.Kind switch
    {
        LinkKinds.Page when pick.Members[LinkKinds.PageIdMember]?.GetValue<int>() is { } pageId =>
            (await Pages.ResolveLinkAsync(pageId))?.Url,
        LinkKinds.Media when pick.Members[LinkKinds.MediaIdMember]?.GetValue<int>() is { } mediaId =>
            (await Media.LinksAsync([mediaId])).GetValueOrDefault(mediaId)?.OriginalUrl,
        _ => null,
    };

    /// <summary>
    /// Escapes the characters that would end a markdown link label early.
    /// </summary>
    /// <remarks>
    /// A title containing a bracket — "Pricing [2026]" — would otherwise close the label three
    /// characters in and leave the rest as literal text beside a broken link.
    /// </remarks>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
}
