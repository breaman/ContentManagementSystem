namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A page shape: a stable key, a Razor component that lays out the markup, and the set of
/// <see cref="Zone"/> definitions an editor fills in.
/// </summary>
/// <remarks>
/// The component and the zone definitions live in different places on purpose (spec section 8.1).
/// Markup is code, versioned with the deployment; zone definitions are data a <c>Developer</c> edits
/// in the backoffice, because content-modelling decisions change far more often than layout does.
/// <c>TemplateReconciler</c> keeps the two in agreement at startup.
/// </remarks>
public class Template : FingerPrintEntityBase
{
    /// <summary>
    /// Stable identifier written into every content payload authored against this template.
    /// </summary>
    /// <remarks>
    /// Immutable after creation. Renaming it would orphan the payload of every page using it, so
    /// the service layer refuses the change (spec section 8.5).
    /// </remarks>
    public string Key { get; set; } = null!;

    /// <summary>Editor-facing display name. Free to rename at any time.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Optional help text shown when an editor picks a template.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Assembly-qualified name of the Razor component that renders pages of this template.
    /// </summary>
    /// <remarks>
    /// Null for a template created through the backoffice before its component is deployed. Such a
    /// template is orphaned until the code catches up.
    /// </remarks>
    public string? ComponentTypeName { get; set; }

    /// <summary>
    /// True when the database holds this template but no code component declares it.
    /// </summary>
    /// <remarks>
    /// Set by <c>TemplateReconciler</c> at startup. Orphaned templates cannot be assigned to new
    /// pages, and the <c>cms-templates</c> health check degrades while any orphan still has live
    /// pages — a bad deployment stays visible without taking the site down.
    /// </remarks>
    public bool IsOrphaned { get; set; }

    /// <summary>Whether editors may create new pages from this template.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Revision number of the newest <see cref="TemplateRevision"/>, denormalised so the editor can
    /// tell a page's captured revision from the current one without a second query.
    /// </summary>
    public int CurrentRevision { get; set; }

    /// <summary>Order this template appears in the create-page picker.</summary>
    public int SortOrder { get; set; }

    /// <summary>Zone definitions belonging to this template.</summary>
    public ICollection<Zone> Zones { get; set; } = [];

    /// <summary>Structural revision history.</summary>
    public ICollection<TemplateRevision> Revisions { get; set; } = [];
}
