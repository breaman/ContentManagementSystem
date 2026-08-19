using System.ComponentModel.DataAnnotations;

using ContentManagementSystem.Shared.Common;

namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>One tag, with how many pages carry it (task P8-20, spec section 17.1).</summary>
/// <param name="Id">Identity.</param>
/// <param name="Name">The label as an editor typed it.</param>
/// <param name="Slug">Normalized form, unique across the site.</param>
/// <param name="PageCount">How many pages carry it.</param>
/// <remarks>
/// The count is what makes the tag admin screen usable: renaming a tag is a change to every page
/// carrying it, and an editor about to do that should be able to see how many that is before they
/// press anything.
/// </remarks>
public sealed record TagSummary(int Id, string Name, string Slug, int PageCount);

/// <summary>Renames a tag across every page carrying it, merging it if the name is taken.</summary>
/// <remarks>
/// One request for both acts, because they are the same act seen from two sides: renaming to a name
/// that already exists <em>is</em> a merge, and refusing it would leave an editor to do the merge by
/// hand on every page. The response says which happened.
/// </remarks>
public sealed record RenameTagRequest
{
    /// <summary>The new label.</summary>
    [Required]
    [MaxLength(FieldLengths.Slug)]
    public string Name { get; init; } = string.Empty;
}

/// <summary>What a rename turned out to be.</summary>
/// <param name="Tag">The tag as it now stands.</param>
/// <param name="Merged">Whether it was merged into an existing tag rather than simply renamed.</param>
/// <param name="PagesAffected">How many pages carried the tag that was renamed.</param>
public sealed record RenameTagResult(TagSummary Tag, bool Merged, int PagesAffected);

/// <summary>Error codes the tag endpoints answer with.</summary>
public static class TagCodes
{
    /// <summary>No tag with that identity.</summary>
    public const string NotFound = "tags.notFound";

    /// <summary>The caller may not do this.</summary>
    public const string Forbidden = "tags.forbidden";

    /// <summary>The name was empty once normalized to a slug.</summary>
    public const string InvalidName = "tags.invalidName";

    /// <summary>The tag is still carried by pages, and the request refused to remove it from them.</summary>
    public const string InUse = "tags.inUse";
}
