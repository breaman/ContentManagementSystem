using System.Collections.Frozen;

using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Security;

/// <summary>
/// The allowlists behind the three sanitization profiles (spec section 20.2).
/// </summary>
/// <remarks>
/// Held as data in one file rather than spread across the service, because what a profile permits is
/// the security boundary and it should be readable in one sitting by someone who is not going to
/// read the code around it.
/// <para>
/// The profiles nest strictly: <c>Extended</c> is <c>Basic</c> plus a set, <c>Developer</c> is
/// <c>Extended</c> plus a set. Nothing subtracts. That is what makes "every rule Basic enforces, the
/// wider two enforce as well" true by construction rather than by review.
/// </para>
/// <para>
/// Public because two callers outside the sanitizer need to read it. The HTML editor shows a
/// persistent banner of what the active profile permits (spec section 14.4, task P6-13), and the XSS
/// corpus suite asserts that nothing outside the list survives — an assertion it can only make if it
/// can see the list rather than restate it.
/// </para>
/// </remarks>
public static class SanitizationPolicy
{
    /// <summary>
    /// Prose and links (<c>Basic</c>).
    /// </summary>
    /// <remarks>
    /// Exactly the spec's list. Note what is absent and is regularly missed: <c>h1</c>, which belongs
    /// to the page title rather than to body content, and <c>b</c>/<c>i</c>, whose semantic
    /// equivalents are here instead. Both are unwrapped rather than deleted — see
    /// <see cref="SanitizationService"/> on keeping child nodes — so the words survive and only the
    /// markup goes.
    /// </remarks>
    private static readonly string[] BasicTags =
    [
        "p", "br", "strong", "em", "u", "s", "a",
        "ul", "ol", "li", "blockquote",
        "h2", "h3", "h4", "h5", "h6",
        "code", "pre",
    ];

    /// <summary>Tables, images, and layout containers (<c>Extended</c>, on top of <c>Basic</c>).</summary>
    /// <remarks>
    /// <c>caption</c> and <c>tfoot</c> go slightly beyond the spec's list, which names
    /// <c>table, thead, tbody, tr, th, td</c>. A table markup subset that can express a header but
    /// not a caption produces inaccessible tables, which the Phase 9 accessibility gate then fails —
    /// so the two are here rather than discovered later.
    /// </remarks>
    private static readonly string[] ExtendedTags =
    [
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption",
        "img", "figure", "figcaption", "hr", "div", "span",
    ];

    /// <summary>Embeds and media (<c>Developer</c>, on top of <c>Extended</c>).</summary>
    private static readonly string[] DeveloperTags = ["iframe", "video", "audio", "source"];

    private static readonly string[] BasicAttributes = ["href", "title", "lang", "dir", "target", "rel"];

    private static readonly string[] ExtendedAttributes =
    [
        "src", "alt", "width", "height",
        "colspan", "rowspan", "scope", "span",
        "loading", "style",
    ];

    private static readonly string[] DeveloperAttributes =
    [
        "srcset", "sizes", "type",
        "controls", "poster", "preload", "loop", "muted", "playsinline",
        "allow", "allowfullscreen", "referrerpolicy", "frameborder",
    ];

    private static readonly FrozenDictionary<SanitizationProfile, FrozenSet<string>> TagsByProfile =
        Build(BasicTags, ExtendedTags, DeveloperTags);

    private static readonly FrozenDictionary<SanitizationProfile, FrozenSet<string>> AttributesByProfile =
        Build(BasicAttributes, ExtendedAttributes, DeveloperAttributes);

    /// <summary>
    /// URL schemes permitted under every profile.
    /// </summary>
    /// <remarks>
    /// <c>data</c> is in the list so that the image case can be reached at all; it is not thereby
    /// permitted. <see cref="SanitizationService"/> refuses a <c>data:</c> URI that is not on an
    /// image element, is not base64, is not an allowlisted media type, or exceeds
    /// <see cref="SanitizationOptions.MaxDataUriBytes"/> — which is the "for images only, with a
    /// size cap" half of the rule, and it cannot be expressed as a scheme.
    /// </remarks>
    public static readonly FrozenSet<string> AllowedSchemes =
        FrozenSet.ToFrozenSet(["http", "https", "mailto", "tel", "data"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CSS properties an inline <c>style</c> attribute may set.
    /// </summary>
    /// <remarks>
    /// Typography and spacing only. Nothing here can take an element out of the document flow, cover
    /// the page, or fetch a URL: no <c>position</c>, no <c>z-index</c>, no <c>background-image</c>,
    /// no <c>content</c>. Those are the properties that turn an inline style into a clickjacking
    /// surface or a tracking beacon, which is a different problem from script injection and is not
    /// solved by the tag allowlist.
    /// </remarks>
    public static readonly FrozenSet<string> AllowedCssProperties = FrozenSet.ToFrozenSet(
        [
            "color", "background-color",
            "font-family", "font-size", "font-style", "font-weight",
            "line-height", "letter-spacing",
            "text-align", "text-decoration", "text-transform",
            "list-style-type", "vertical-align",
            "width", "height", "max-width",
            "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
            "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
            "border", "border-color", "border-style", "border-width", "border-radius",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The <c>data:</c> media types an inline image may declare.
    /// </summary>
    /// <remarks>
    /// SVG is absent on purpose and stays absent whatever answer open question Q7 gets for uploads:
    /// an inline <c>data:image/svg+xml</c> is a document that can carry script, and it arrives here
    /// as an opaque string this sanitizer would have to parse as a second document to clean.
    /// </remarks>
    public static readonly FrozenSet<string> AllowedDataUriMediaTypes = FrozenSet.ToFrozenSet(
        ["image/png", "image/jpeg", "image/gif", "image/webp"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Elements whose <c>src</c> may hold an inline <c>data:</c> image.</summary>
    public static readonly FrozenSet<string> DataUriElements =
        FrozenSet.ToFrozenSet(["img", "source"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Tags allowed under a profile.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The tag names, lower case, compared case-insensitively.</returns>
    public static FrozenSet<string> TagsFor(SanitizationProfile profile) =>
        TagsByProfile.TryGetValue(profile, out var tags) ? tags : TagsByProfile[SanitizationProfile.Basic];

    /// <summary>Attributes allowed under a profile, before the class allowlist is considered.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The attribute names, lower case, compared case-insensitively.</returns>
    /// <remarks>
    /// <c>class</c> is not here even though <c>Extended</c> and <c>Developer</c> may permit it:
    /// whether it is allowed depends on whether the deployment configured
    /// <see cref="SanitizationOptions.AllowedCssClasses"/>, which is policy the profile does not own.
    /// </remarks>
    public static FrozenSet<string> AttributesFor(SanitizationProfile profile) =>
        AttributesByProfile.TryGetValue(profile, out var attributes)
            ? attributes
            : AttributesByProfile[SanitizationProfile.Basic];

    /// <summary>Composes the three nested profiles from their incremental sets.</summary>
    /// <param name="basic">What <c>Basic</c> allows.</param>
    /// <param name="extended">What <c>Extended</c> adds.</param>
    /// <param name="developer">What <c>Developer</c> adds on top of that.</param>
    private static FrozenDictionary<SanitizationProfile, FrozenSet<string>> Build(
        string[] basic,
        string[] extended,
        string[] developer) =>
        FrozenDictionary.ToFrozenDictionary(
            new Dictionary<SanitizationProfile, FrozenSet<string>>
            {
                [SanitizationProfile.Basic] = FrozenSet.ToFrozenSet(basic, StringComparer.OrdinalIgnoreCase),
                [SanitizationProfile.Extended] =
                    FrozenSet.ToFrozenSet([.. basic, .. extended], StringComparer.OrdinalIgnoreCase),
                [SanitizationProfile.Developer] =
                    FrozenSet.ToFrozenSet([.. basic, .. extended, .. developer], StringComparer.OrdinalIgnoreCase),
            });
}
