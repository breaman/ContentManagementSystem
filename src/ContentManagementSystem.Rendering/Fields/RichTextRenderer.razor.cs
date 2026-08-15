using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>richText</c> value, in either of the two formats it can be stored in
/// (spec section 7.1).
/// </summary>
/// <remarks>
/// <strong>Both formats are sanitized here, on the way out</strong> (ADR 0008). Markdown must be,
/// because <c>richText</c> stores markdown exactly as authored and never sanitizes it on write —
/// raw HTML that markdown carries through cannot be cleaned without parsing the markdown around it.
/// HTML must be too, because a payload that reached the database through an import, a restored
/// backup, or a since-tightened profile never passed the write-time check at all.
/// <para>
/// The markdown path goes through <see cref="IMarkdownRenderer"/> rather than through a converter of
/// its own. That is what makes the editor's preview and the delivered page byte-identical for the
/// same source (task P1-19, acceptance criterion P1 #7); a second pipeline here would be a preview
/// that lies.
/// </para>
/// <para>
/// The <c>format</c> member is read from the payload, never from configuration: it describes the
/// value that was written, so a property switched from markdown to HTML must still render what is
/// already stored. A value carrying no recognised format renders nothing and logs — guessing is not
/// available, since markdown rendered as HTML shows its source and HTML rendered as markdown escapes
/// its markup.
/// </para>
/// </remarks>
public partial class RichTextRenderer : CmsFieldRendererBase
{
    /// <summary>The member naming how the stored value is written.</summary>
    private const string FormatMember = "format";

    [Inject]
    private IMarkdownRenderer Markdown { get; set; } = default!;

    [Inject]
    private IContentSanitizer Sanitizer { get; set; } = default!;

    [Inject]
    private ILogger<RichTextRenderer> Logger { get; set; } = default!;

    /// <summary>The sanitized markup to emit; empty when there is nothing renderable.</summary>
    protected MarkupString Html { get; private set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        Html = default;

        if (ValueText is not { Length: > 0 } source) return;

        var format = StringMember(FormatMember);
        var profile = Profile();

        var html = format switch
        {
            RichTextFieldType.MarkdownFormat => Markdown.ToHtml(source, profile),
            RichTextFieldType.HtmlFormat => Sanitizer.Sanitize(source, profile),
            _ => null,
        };

        if (html is null)
        {
            Logger.LogWarning(
                "Rich text in '{PropertyKey}' on page {PageId} version {VersionId} declares no " +
                "readable format ('{Format}'), so it renders nothing.",
                PropertyKey,
                Context?.Page.Id,
                Context?.Page.VersionId,
                format);

            return;
        }

        Html = new MarkupString(html);
    }

    /// <summary>
    /// Which allowlist this property's markup is cleaned under.
    /// </summary>
    /// <returns>The configured profile, or <see cref="SanitizationProfile.Basic"/>.</returns>
    /// <remarks>
    /// An unrecognised or absent setting falls back to the most restrictive profile rather than to
    /// the one the property probably meant. A mistyped <c>profile</c> can then only ever strip more
    /// than intended, never less, which is the direction a sanitization mistake has to fail in.
    /// <c>Developer</c> is unreachable from here for the same reason the field type refuses it: the
    /// role gate that justifies iframes lives on the <c>html</c> field type.
    /// </remarks>
    private SanitizationProfile Profile() =>
        Configuration.GetString("profile") switch
        {
            "extended" => SanitizationProfile.Extended,
            _ => SanitizationProfile.Basic,
        };
}
