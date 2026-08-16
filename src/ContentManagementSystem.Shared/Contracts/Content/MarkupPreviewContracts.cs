using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// What the editor asks the server to render, so its preview is the delivered page rather than an
/// approximation of it (task P6-09, spec section 14.4).
/// </summary>
/// <param name="Format">
/// How the source is written — <c>markdown</c> or <c>html</c>, the two values a <c>richText</c>
/// value's <c>format</c> member can hold.
/// </param>
/// <param name="Source">The authored source, exactly as the editor holds it.</param>
/// <param name="Profile">
/// Which allowlist to clean the converted markup under, matched against
/// <see cref="SanitizationProfile"/> by name. An unrecognised value is refused rather than defaulted,
/// because a preview rendered under a profile the caller did not ask for is a preview that lies in
/// the one direction that matters.
/// </param>
/// <remarks>
/// <strong>The source is sent to the server rather than rendered in the browser.</strong> That is the
/// whole point of the task: there is one Markdig configuration and one sanitizer, they live in
/// <c>Core</c>, and the backoffice runs in WebAssembly where <c>Core</c> is not loaded. Carrying a
/// second Markdig into the browser would produce a preview that diverges from the published page
/// the first time either side is upgraded — which is precisely what acceptance criterion P6 #2
/// forbids.
/// </remarks>
public sealed record MarkupPreviewRequest(string Format, string? Source, string? Profile = null);

/// <summary>
/// The rendered preview, and what the profile took out of it.
/// </summary>
/// <param name="Html">
/// The markup the page will show. Safe to emit: it has been through the same allowlist the delivery
/// path applies (ADR-0008).
/// </param>
/// <param name="Removals">
/// Everything the profile removed, in document order. Empty when nothing was.
/// </param>
/// <remarks>
/// The removals are what turns a preview into a warning. An author who pasted an
/// <c>&lt;iframe&gt;</c> into a markdown zone finds out here that it will not survive publishing,
/// rather than by looking at the live page afterwards — and the HTML editor (P6-13) shows the same
/// list as a running "these tags will be stripped on save" banner.
/// <para>
/// <see cref="SanitizationRemoval.Value"/> holds attacker-influenced text by construction. Render it
/// through something that encodes, never through <c>MarkupString</c>.
/// </para>
/// </remarks>
public sealed record MarkupPreviewResult(string Html, IReadOnlyList<SanitizationRemoval> Removals)
{
    /// <summary>Whether the profile removed anything at all.</summary>
    public bool RemovedAnything => Removals.Count > 0;
}

/// <summary>
/// What one sanitization profile keeps, for the editor's permitted-tags banner (task P6-13).
/// </summary>
/// <param name="Profile">The profile's name, as <see cref="SanitizationProfile"/> spells it.</param>
/// <param name="Tags">Every element the profile allows, in alphabetical order.</param>
/// <remarks>
/// Sent to the backoffice rather than duplicated there, because the allowlist is the server's and a
/// second copy in the browser is a banner that eventually lies about what will survive a save. It is
/// public information in the only sense that matters: it describes what this deployment will do to
/// markup somebody is about to author, and they are already authenticated to author it.
/// </remarks>
public sealed record SanitizationProfileDescriptor(string Profile, IReadOnlyList<string> Tags);

/// <summary>How a previewed source is written.</summary>
/// <remarks>
/// Mirrors the <c>format</c> member of a stored <c>richText</c> value, and the constants on
/// <c>RichTextFieldType</c> that the backoffice cannot reference.
/// </remarks>
public static class MarkupFormats
{
    /// <summary>Markdown source, converted and then sanitized.</summary>
    public const string Markdown = "markdown";

    /// <summary>HTML, sanitized directly.</summary>
    public const string Html = "html";
}
