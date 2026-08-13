namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A named group of property definitions that block types share instead of re-declaring.
/// </summary>
/// <remarks>
/// Spacing options, SEO fragments, and analytics attributes tend to repeat across every block type
/// in a design system. Composing them keeps one definition, so adding a property to the group adds
/// it everywhere (spec section 6.3).
/// </remarks>
public class Composition : FingerPrintEntityBase
{
    /// <summary>Stable identifier, such as <c>spacing-options</c>. Immutable after creation.</summary>
    public string Key { get; set; } = null!;

    /// <summary>Editor-facing display name.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Optional help text describing what the group is for.</summary>
    public string? Description { get; set; }

    /// <summary>Property definitions belonging to this composition.</summary>
    public ICollection<CompositionProperty> Properties { get; set; } = [];

    /// <summary>Block types that compose this group.</summary>
    public ICollection<BlockTypeComposition> BlockTypes { get; set; } = [];
}
