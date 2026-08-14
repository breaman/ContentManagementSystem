namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Body of <c>POST /api/cms/v1/pages</c>.
/// </summary>
/// <param name="TemplateId">Template the page is laid out by. Immutable once the page exists.</param>
/// <param name="Title">
/// Editor-facing title of the first draft. Versioned, so a later publish records the title as it
/// stood (spec section 23.2).
/// </param>
/// <param name="ParentId">
/// Parent node, or null to create the page at the root of the site. The site root is synthetic —
/// there is no row to create first.
/// </param>
/// <param name="Slug">
/// The page's own URL segment. Omitted, it is generated from <paramref name="Title"/> and then
/// freely editable (spec section 10.2).
/// </param>
/// <param name="ShowInNavigation">Whether generated navigation menus include the page.</param>
/// <remarks>
/// Deliberately small. Everything else a page carries is editorial metadata that a
/// <c>PATCH /pages/{id}/metadata</c> sets afterwards, and putting it here would mean two code paths
/// writing the same columns with two sets of rules. Status is not settable at all — a page is
/// created as a draft and reaches any other state only through the dedicated lifecycle endpoints
/// (spec section 20.1).
/// </remarks>
public sealed record CreatePageRequest(
    int TemplateId,
    string? Title,
    int? ParentId = null,
    string? Slug = null,
    bool ShowInNavigation = true);
