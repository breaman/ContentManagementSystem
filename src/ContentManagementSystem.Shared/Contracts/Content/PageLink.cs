namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Where a page currently sits, for a surface that has to write a URL down (task P6-11).
/// </summary>
/// <param name="PageId">The page.</param>
/// <param name="Url">Its current URL, or null when it has none the caller may see.</param>
/// <param name="IsPublished">Whether the target is live.</param>
/// <param name="Title">Its current title, offered as the words a link reads as.</param>
/// <remarks>
/// <strong>This exists for prose, and only for prose.</strong> A <c>link</c> or
/// <c>pageReference</c> property stores the page id and never a URL (ADR-0006), which is what makes
/// those links survive the target being moved. Markdown and HTML zones cannot do that: their content
/// is text, and an anchor in text has an <c>href</c> in it.
/// <para>
/// So the guarantee P6-11 can keep for prose is the narrower one ADR-0006 actually asks of the
/// editing UI — that the author opens the picker rather than typing an address, and the address that
/// lands in the document is the one the CMS resolved rather than one somebody remembered. A link
/// inside prose still goes stale when its target moves, and the redirect created by the move is what
/// catches it; a link in a <c>link</c> property does not go stale at all.
/// </para>
/// <para>
/// <see cref="IsPublished"/> is not the same question as whether <see cref="Url"/> is null: an
/// unpublished page has a URL an editor may be shown and an anonymous visitor may not.
/// </para>
/// </remarks>
public sealed record PageLink(int PageId, string? Url, bool IsPublished, string? Title);
