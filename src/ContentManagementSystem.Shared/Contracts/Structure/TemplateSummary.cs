namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// A template as it appears in the structure list and the create-page picker.
/// </summary>
/// <param name="Id">Database identity, used to address the template in the API.</param>
/// <param name="Key">Stable key written into every payload authored against the template.</param>
/// <param name="Name">Editor-facing display name.</param>
/// <param name="Description">Optional help text shown when picking a template.</param>
/// <param name="ComponentTypeName">
/// Assembly-qualified name of the Razor component that renders the template, or null when no
/// deployed code claims this key.
/// </param>
/// <param name="IsOrphaned">
/// Whether the database holds the template but no code component declares it. Orphaned templates
/// cannot be assigned to new pages (spec section 8.4).
/// </param>
/// <param name="IsEnabled">Whether editors may create new pages from the template.</param>
/// <param name="CurrentRevision">Revision number of the newest structural snapshot.</param>
/// <param name="SortOrder">Order in the create-page picker.</param>
/// <param name="ZoneCount">How many zones the template currently defines.</param>
public sealed record TemplateSummary(
    int Id,
    string Key,
    string Name,
    string? Description,
    string? ComponentTypeName,
    bool IsOrphaned,
    bool IsEnabled,
    int CurrentRevision,
    int SortOrder,
    int ZoneCount);
