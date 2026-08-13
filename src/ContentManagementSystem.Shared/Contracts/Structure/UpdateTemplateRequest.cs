namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// Body of <c>PUT /api/cms/v1/templates/{id}</c>.
/// </summary>
/// <param name="Key">
/// The template's key. Must equal the stored key: a change is refused with
/// <see cref="StructureCodes.KeyImmutable"/>.
/// </param>
/// <param name="Name">Editor-facing display name. Free to change at any time.</param>
/// <param name="Description">Optional help text.</param>
/// <param name="IsEnabled">Whether editors may create new pages from the template.</param>
/// <param name="SortOrder">Order in the create-page picker.</param>
/// <remarks>
/// The key is carried on the request even though it cannot change. An edit form round-trips what it
/// was given, and refusing the change by name is a better answer than silently discarding it — a
/// developer who mistypes a key otherwise sees a successful save that did nothing.
/// </remarks>
public sealed record UpdateTemplateRequest(
    string? Key,
    string? Name,
    string? Description = null,
    bool IsEnabled = true,
    int SortOrder = 0);
