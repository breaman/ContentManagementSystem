namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// An immutable snapshot of a template's zone definitions, taken whenever they change structurally.
/// </summary>
/// <remarks>
/// This is what makes template evolution safe (spec section 8.5). A page version records the
/// revision number it was authored against, and published content renders against that captured
/// snapshot — so changing a template can never retroactively alter what is already live. Editors
/// adopt the new revision the next time they open and publish the page.
/// </remarks>
public class TemplateRevision : FingerPrintEntityBase
{
    /// <summary>Template this revision belongs to.</summary>
    public int TemplateId { get; set; }

    /// <summary>Template this revision belongs to.</summary>
    public Template Template { get; set; } = null!;

    /// <summary>Monotonically increasing number, starting at 1, unique within the template.</summary>
    public int RevisionNumber { get; set; }

    /// <summary>
    /// Serialised copy of every <see cref="Zone"/> definition as it stood when the revision was cut.
    /// </summary>
    public string ZoneSnapshotJson { get; set; } = null!;

    /// <summary>Optional note explaining what changed and why.</summary>
    public string? Notes { get; set; }
}
