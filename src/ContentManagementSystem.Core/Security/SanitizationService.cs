using AngleSharp.Dom;

using ContentManagementSystem.Shared.Contracts.Security;

using Ganss.Xss;

namespace ContentManagementSystem.Core.Security;

/// <summary>
/// The one implementation of <see cref="IContentSanitizer"/>, over HtmlSanitizer (task P1-18,
/// spec section 20.2).
/// </summary>
/// <remarks>
/// HtmlSanitizer parses to a real DOM through AngleSharp rather than pattern-matching, which is why
/// the spec names it: a regex-based sanitizer is defeated by malformed markup and tag poisoning, and
/// the corpus in <c>Core.Tests/Security</c> is largely made of exactly those.
/// <para>
/// What this class adds on top of the library is the policy: the three profiles, and the four
/// cross-profile rules the library has no opinion about — <c>data:</c> URIs restricted to capped
/// inline images, <c>iframe</c> restricted to an allowlist of hosts, <c>rel="noopener noreferrer"</c>
/// forced onto targeted links, and code-bearing elements deleted outright rather than unwrapped.
/// </para>
/// <para>
/// <strong>Unknown elements are unwrapped, not deleted.</strong> A <c>&lt;section&gt;</c> that a
/// paste brought in under the <c>Basic</c> profile loses the tag and keeps its paragraphs. That is
/// the right default for prose — deleting the subtree silently eats an author's work, which is
/// risk R3 — but it is the wrong default for an element whose children are code rather than text,
/// because <c>&lt;script&gt;alert(1)&lt;/script&gt;</c> would unwrap to the visible text
/// <c>alert(1)</c>. Hence <see cref="DeletedOutright"/>.
/// </para>
/// <para>
/// Instances are thread-safe. One sanitizer per profile is built once and shared, because
/// HtmlSanitizer documents <c>Sanitize</c> as safe to call concurrently on a shared instance and the
/// save and render paths call it constantly.
/// </para>
/// </remarks>
public sealed class SanitizationService : IContentSanitizer
{
    /// <summary>
    /// Elements removed with their contents instead of being unwrapped.
    /// </summary>
    /// <remarks>
    /// Everything here holds script, style, metadata, or embedded content rather than prose, so
    /// there is nothing inside worth keeping and quite a lot worth losing. <c>iframe</c>,
    /// <c>video</c>, <c>audio</c>, and <c>source</c> are on the list too: under <c>Basic</c> and
    /// <c>Extended</c> they are not allowed, and unwrapping an embed leaves its fallback text
    /// stranded in the middle of a paragraph.
    /// </remarks>
    private static readonly HashSet<string> DeletedOutright = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "xmp", "plaintext",
        "head", "title", "base", "link", "meta",
        "object", "embed", "applet", "param", "frame", "frameset",
        "iframe", "video", "audio", "source", "track", "canvas",
        "svg", "math",
        "form", "input", "button", "select", "option", "optgroup", "textarea", "label",
    };

    private const string DataUriPrefix = "data:";

    private readonly SanitizationOptions _options;
    private readonly HtmlSanitizer _basic;
    private readonly HtmlSanitizer _extended;
    private readonly HtmlSanitizer _developer;

    /// <summary>Creates the service.</summary>
    /// <param name="options">Deployment policy, or null for the defaults.</param>
    public SanitizationService(SanitizationOptions? options = null)
    {
        _options = options ?? new SanitizationOptions();

        _basic = Build(SanitizationProfile.Basic, record: null);
        _extended = Build(SanitizationProfile.Extended, record: null);
        _developer = Build(SanitizationProfile.Developer, record: null);
    }

    /// <inheritdoc />
    public string Sanitize(string? html, SanitizationProfile profile)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        return Cached(profile).Sanitize(html);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Builds a sanitizer for this call rather than using the shared one. The removal events carry
    /// no per-call context, so a handler writing into a caller's list on a shared instance would
    /// hand one request another request's removals — and this path is the editor preview and the
    /// test suite, not the save path, so the extra allocation buys correctness cheaply.
    /// </remarks>
    public SanitizationResult SanitizeWithReport(string? html, SanitizationProfile profile)
    {
        if (string.IsNullOrEmpty(html))
        {
            return SanitizationResult.Unchanged(string.Empty);
        }

        var removals = new List<SanitizationRemoval>();
        var sanitizer = Build(profile, removals.Add);

        return new SanitizationResult(sanitizer.Sanitize(html), removals);
    }

    private HtmlSanitizer Cached(SanitizationProfile profile) => profile switch
    {
        SanitizationProfile.Extended => _extended,
        SanitizationProfile.Developer => _developer,
        _ => _basic,
    };

    /// <summary>Builds a sanitizer configured for one profile.</summary>
    /// <param name="profile">The profile to enforce.</param>
    /// <param name="record">Where to report removals, or null to report none.</param>
    private HtmlSanitizer Build(SanitizationProfile profile, Action<SanitizationRemoval>? record)
    {
        var sanitizer = new HtmlSanitizer();

        // Replace the library's defaults wholesale. Its default tag list is ninety-odd elements
        // chosen to be broadly useful; a profile here is an allowlist chosen to be small, and
        // starting from "everything reasonable" and subtracting is how an allowlist rots.
        Replace(sanitizer.AllowedTags, SanitizationPolicy.TagsFor(profile));
        Replace(sanitizer.AllowedAttributes, SanitizationPolicy.AttributesFor(profile));
        Replace(sanitizer.AllowedSchemes, SanitizationPolicy.AllowedSchemes);
        Replace(sanitizer.AllowedCssProperties, SanitizationPolicy.AllowedCssProperties);
        // No profile allows <style>, so there is no stylesheet for an at-rule to live in.
        sanitizer.AllowedAtRules.Clear();

        // Data attributes are the Developer profile's, per the spec's table. They are inert on their
        // own but they are how a third-party widget is wired up, which is the point of that profile.
        sanitizer.AllowDataAttributes = profile is SanitizationProfile.Developer;

        // An empty allowlist means no class attribute at all, rather than every class — see the
        // remarks on the option. Below Extended the attribute is not on the list to begin with.
        if (profile is not SanitizationProfile.Basic && _options.AllowedCssClasses.Count > 0)
        {
            sanitizer.AllowedAttributes.Add("class");
            Replace(sanitizer.AllowedClasses, _options.AllowedCssClasses);
        }

        // Unwrap rather than delete, so stripping an unknown wrapper does not take the prose inside
        // it. DeletedOutright is the exception, applied in OnRemovingTag below.
        sanitizer.KeepChildNodes = true;

        sanitizer.RemovingTag += (_, e) => OnRemovingTag(e, record);
        sanitizer.RemovingAttribute += (_, e) => OnRemovingAttribute(e, record);
        sanitizer.RemovingStyle += (_, e) => Report(
            record,
            new SanitizationRemoval(
                SanitizationRemovalKind.Style,
                e.Style.Name,
                e.Tag.LocalName,
                SanitizationRemoval.Truncate(e.Style.Value)));
        sanitizer.RemovingCssClass += (_, e) => Report(
            record,
            new SanitizationRemoval(SanitizationRemovalKind.CssClass, e.CssClass, e.Tag.LocalName));
        sanitizer.RemovingComment += (_, e) => Report(
            record,
            new SanitizationRemoval(
                SanitizationRemovalKind.Comment,
                string.Empty,
                Value: SanitizationRemoval.Truncate(e.Comment.TextContent)));

        sanitizer.FilterUrl += (_, e) => OnFilterUrl(e, profile);
        sanitizer.PostProcessDom += (_, e) => OnPostProcessDom(e, record);

        return sanitizer;
    }

    /// <summary>
    /// Decides whether a disallowed element is unwrapped or deleted with its contents.
    /// </summary>
    /// <param name="e">The event.</param>
    /// <param name="record">Where to report the removal.</param>
    private static void OnRemovingTag(RemovingTagEventArgs e, Action<SanitizationRemoval>? record)
    {
        Report(record, new SanitizationRemoval(SanitizationRemovalKind.Tag, e.Tag.LocalName));

        if (!DeletedOutright.Contains(e.Tag.LocalName))
        {
            return;
        }

        // Cancelling suppresses the library's own removal, which would honour KeepChildNodes and
        // leave a script body behind as visible text. Removing the node here takes the subtree.
        e.Tag.Remove();
        e.Cancel = true;
    }

    /// <summary>Reports an attribute removal, separating a refused URL from a disallowed attribute.</summary>
    /// <param name="e">The event.</param>
    /// <param name="record">Where to report the removal.</param>
    private static void OnRemovingAttribute(RemovingAttributeEventArgs e, Action<SanitizationRemoval>? record)
    {
        // ClassAttributeEmpty and StyleAttributeEmpty fire when an attribute is dropped because
        // everything in it was already removed and reported. Reporting them too would show an author
        // two removals for one edit.
        if (e.Reason is RemoveReason.ClassAttributeEmpty or RemoveReason.StyleAttributeEmpty)
        {
            return;
        }

        var kind = e.Reason is RemoveReason.NotAllowedUrlValue
            ? SanitizationRemovalKind.Url
            : SanitizationRemovalKind.Attribute;

        Report(
            record,
            new SanitizationRemoval(
                kind,
                e.Attribute.Name,
                e.Tag.LocalName,
                SanitizationRemoval.Truncate(e.Attribute.Value)));
    }

    /// <summary>
    /// Applies the two URL rules the scheme allowlist cannot express.
    /// </summary>
    /// <param name="e">The event. Setting <c>SanitizedUrl</c> to null drops the attribute.</param>
    /// <param name="profile">The profile in force.</param>
    private void OnFilterUrl(FilterUrlEventArgs e, SanitizationProfile profile)
    {
        if (e.SanitizedUrl is not { Length: > 0 } url)
        {
            return;
        }

        if (url.StartsWith(DataUriPrefix, StringComparison.OrdinalIgnoreCase) && !IsPermittedInlineImage(e.Tag, url))
        {
            e.SanitizedUrl = null;

            return;
        }

        if (e.Tag.LocalName.Equals("iframe", StringComparison.OrdinalIgnoreCase) && !IsPermittedFrame(url, profile))
        {
            e.SanitizedUrl = null;
        }
    }

    /// <summary>
    /// Whether a <c>data:</c> URI is an inline image small enough to keep.
    /// </summary>
    /// <param name="element">The element carrying the URL.</param>
    /// <param name="url">The <c>data:</c> URI.</param>
    /// <remarks>
    /// Three conditions, all required: it is on an image element, it declares an allowlisted raster
    /// image media type, and it is base64 whose decoded length is within the cap. Non-base64
    /// <c>data:</c> URIs are refused rather than measured — their payload is percent-encoded text,
    /// which is the shape a <c>text/html</c> or <c>image/svg+xml</c> payload takes when it is trying
    /// not to look like one.
    /// </remarks>
    private bool IsPermittedInlineImage(IElement element, string url)
    {
        if (!SanitizationPolicy.DataUriElements.Contains(element.LocalName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var comma = url.IndexOf(',', StringComparison.Ordinal);

        if (comma < 0)
        {
            return false;
        }

        var header = url[DataUriPrefix.Length..comma];

        if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var mediaType = header[..^";base64".Length];

        if (!SanitizationPolicy.AllowedDataUriMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // Measured rather than decoded: the point of the cap is to not hold the payload, and
        // decoding to find out how big it is would do exactly that. Four base64 characters carry
        // three bytes, so this over-estimates by at most two.
        var decodedBytes = (long)(url.Length - comma - 1) * 3 / 4;

        return decodedBytes <= _options.MaxDataUriBytes;
    }

    /// <summary>
    /// Whether an <c>iframe</c> may point at this URL.
    /// </summary>
    /// <param name="url">The frame source.</param>
    /// <param name="profile">The profile in force.</param>
    /// <remarks>
    /// Only <c>Developer</c> allows the element at all, so anything reaching here under a narrower
    /// profile is refused. HTTPS is required because a framed HTTP document on an HTTPS page is
    /// blocked as mixed content anyway, and an <c>iframe</c> that renders nothing is worse than one
    /// that was never stored. The host must match an allowlist entry in full — a suffix match would
    /// accept <c>www.youtube.com.evil.test</c>.
    /// </remarks>
    private bool IsPermittedFrame(string url, SanitizationProfile profile) =>
        profile is SanitizationProfile.Developer &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        _options.AllowedIframeHosts.Contains(uri.Host);

    /// <summary>
    /// Applies the two rules that need the finished document.
    /// </summary>
    /// <param name="e">The event.</param>
    /// <param name="record">Where to report removals.</param>
    private static void OnPostProcessDom(PostProcessDomEventArgs e, Action<SanitizationRemoval>? record)
    {
        foreach (var anchor in e.Document.QuerySelectorAll("a[target]"))
        {
            ForceSafeRel(anchor);
        }

        // An iframe whose src did not survive OnFilterUrl frames the embedding page's own origin in
        // some browsers and an empty box in the rest. Neither is what the author wrote, so the
        // element goes with the URL.
        foreach (var frame in e.Document.QuerySelectorAll("iframe"))
        {
            if (!string.IsNullOrEmpty(frame.GetAttribute("src")))
            {
                continue;
            }

            frame.Remove();

            Report(record, new SanitizationRemoval(SanitizationRemovalKind.Tag, "iframe"));
        }
    }

    /// <summary>
    /// Forces <c>rel="noopener noreferrer"</c> onto a link that opens elsewhere.
    /// </summary>
    /// <param name="anchor">The anchor element.</param>
    /// <remarks>
    /// Without <c>noopener</c>, the opened document can navigate the opener through
    /// <c>window.opener</c> — a phishing primitive that needs no script on this page at all. Existing
    /// <c>rel</c> tokens are kept: an author's <c>nofollow</c> is an SEO decision, not something
    /// this is entitled to overwrite.
    /// </remarks>
    private static void ForceSafeRel(IElement anchor)
    {
        if (anchor.GetAttribute("target") is not { Length: > 0 } target ||
            target.Equals("_self", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tokens = new List<string>(
            (anchor.GetAttribute("rel") ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var required in (string[])["noopener", "noreferrer"])
        {
            if (!tokens.Contains(required, StringComparer.OrdinalIgnoreCase))
            {
                tokens.Add(required);
            }
        }

        anchor.SetAttribute("rel", string.Join(' ', tokens));
    }

    private static void Report(Action<SanitizationRemoval>? record, SanitizationRemoval removal) =>
        record?.Invoke(removal);

    private static void Replace(ISet<string> target, IEnumerable<string> values)
    {
        target.Clear();

        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
