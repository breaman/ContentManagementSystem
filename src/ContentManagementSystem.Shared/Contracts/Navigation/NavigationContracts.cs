using System.ComponentModel.DataAnnotations;

using ContentManagementSystem.Shared.Common;

namespace ContentManagementSystem.Shared.Contracts.Navigation;

/// <summary>One managed menu, as the list screen shows it (task P8-16).</summary>
/// <param name="Id">Identity.</param>
/// <param name="Key">Stable key a template asks for the menu by.</param>
/// <param name="Name">Editor-facing name.</param>
/// <param name="Description">What the menu is for.</param>
/// <param name="ItemCount">How many entries it holds.</param>
public sealed record NavigationMenuSummary(
    int Id,
    string Key,
    string Name,
    string? Description,
    int ItemCount);

/// <summary>One entry of a managed menu.</summary>
/// <param name="Id">Identity.</param>
/// <param name="ParentId">The entry this one is nested under, or null at the top level.</param>
/// <param name="Label">The link text.</param>
/// <param name="PageId">The page it points at, or null for an external link.</param>
/// <param name="PageTitle">That page's title, for the editor's benefit.</param>
/// <param name="PageUrl">That page's published URL, or null while it has none.</param>
/// <param name="ExternalUrl">Where it goes when it is not a page.</param>
/// <param name="OpenInNewTab">Whether it opens in a new browsing context.</param>
/// <param name="SortOrder">Order among siblings.</param>
/// <remarks>
/// <see cref="PageUrl"/> is null for an internal item whose page is not published, which is exactly
/// the state the public menu drops the item in. Reported rather than hidden, so the menu editor can
/// see why an entry they added is not appearing.
/// </remarks>
public sealed record NavigationItemDetail(
    int Id,
    int? ParentId,
    string Label,
    int? PageId,
    string? PageTitle,
    string? PageUrl,
    string? ExternalUrl,
    bool OpenInNewTab,
    int SortOrder);

/// <summary>One managed menu and everything in it.</summary>
/// <param name="Id">Identity.</param>
/// <param name="Key">Stable key.</param>
/// <param name="Name">Editor-facing name.</param>
/// <param name="Description">What the menu is for.</param>
/// <param name="Items">The entries, in order.</param>
public sealed record NavigationMenuDetail(
    int Id,
    string Key,
    string Name,
    string? Description,
    IReadOnlyList<NavigationItemDetail> Items);

/// <summary>Creates a menu.</summary>
public sealed record CreateNavigationMenuRequest
{
    /// <summary>Stable key a template asks for the menu by.</summary>
    [Required]
    [MaxLength(FieldLengths.ContentKey)]
    public string Key { get; init; } = string.Empty;

    /// <summary>Editor-facing name.</summary>
    [Required]
    [MaxLength(FieldLengths.EntityName)]
    public string Name { get; init; } = string.Empty;

    /// <summary>What the menu is for.</summary>
    [MaxLength(FieldLengths.ShortDescription)]
    public string? Description { get; init; }
}

/// <summary>Renames a menu. The key is not editable — it is the address templates hold.</summary>
public sealed record UpdateNavigationMenuRequest
{
    /// <summary>Editor-facing name.</summary>
    [Required]
    [MaxLength(FieldLengths.EntityName)]
    public string Name { get; init; } = string.Empty;

    /// <summary>What the menu is for.</summary>
    [MaxLength(FieldLengths.ShortDescription)]
    public string? Description { get; init; }
}

/// <summary>Creates or replaces one entry of a menu.</summary>
public sealed record SaveNavigationItemRequest
{
    /// <summary>The entry this one is nested under, or null at the top level.</summary>
    public int? ParentId { get; init; }

    /// <summary>The link text.</summary>
    [Required]
    [MaxLength(FieldLengths.EntityName)]
    public string Label { get; init; } = string.Empty;

    /// <summary>The page it points at. Exactly one of this and <see cref="ExternalUrl"/> is set.</summary>
    public int? PageId { get; init; }

    /// <summary>Where it goes when it is not a page.</summary>
    [MaxLength(FieldLengths.Url)]
    public string? ExternalUrl { get; init; }

    /// <summary>Whether it opens in a new browsing context.</summary>
    public bool OpenInNewTab { get; init; }

    /// <summary>Order among siblings.</summary>
    public int SortOrder { get; init; }
}

/// <summary>Error codes the navigation endpoints answer with.</summary>
public static class NavigationCodes
{
    /// <summary>No menu or item with that identity.</summary>
    public const string NotFound = "navigation.notFound";

    /// <summary>Another menu already uses that key.</summary>
    public const string DuplicateKey = "navigation.duplicateKey";

    /// <summary>The caller may not do this.</summary>
    public const string Forbidden = "navigation.forbidden";

    /// <summary>An entry named both a page and an external URL, or neither.</summary>
    public const string TargetRequired = "navigation.targetRequired";

    /// <summary>The named page does not exist.</summary>
    public const string PageNotFound = "navigation.pageNotFound";

    /// <summary>The entry was nested under one that belongs to another menu, or under itself.</summary>
    public const string InvalidParent = "navigation.invalidParent";
}
