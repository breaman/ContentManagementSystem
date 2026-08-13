namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// Body of <c>POST /api/cms/v1/templates</c>.
/// </summary>
/// <param name="Key">
/// Stable key for the new template. Chosen once and never changed — every payload authored against
/// the template quotes it (spec section 8.5).
/// </param>
/// <param name="Name">Editor-facing display name.</param>
/// <param name="Description">Optional help text shown when picking a template.</param>
/// <param name="SortOrder">Order in the create-page picker.</param>
/// <remarks>
/// Deliberately narrow. <c>ComponentTypeName</c> and <c>IsOrphaned</c> are not settable: they are
/// findings of the startup reconciler, and a client that could set them could make a template claim
/// a component that does not exist. A template created here is orphaned until code declares its key.
/// </remarks>
public sealed record CreateTemplateRequest(
    string? Key,
    string? Name,
    string? Description = null,
    int SortOrder = 0);
