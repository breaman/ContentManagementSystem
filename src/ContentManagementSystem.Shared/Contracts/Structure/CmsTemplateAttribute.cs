namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// Declares that a Razor component is the renderer for a CMS template (spec section 8.2).
/// </summary>
/// <param name="key">
/// Stable key written into every payload authored against this template. Immutable once any content
/// uses it.
/// </param>
/// <param name="name">Editor-facing display name, used only when the template is first created.</param>
/// <example>
/// <code>
/// @attribute [CmsTemplate("marketing-landing", "Marketing Landing Page",
///     Description = "Hero, flexible body, and a shared footer.")]
/// @inherits CmsTemplateBase
/// </code>
/// </example>
/// <remarks>
/// The attribute carries only what code owns: the key, which is code's to declare because the markup
/// is what makes the template real, and a name and description used as the initial values when the
/// reconciler creates the row. It deliberately does not carry zone definitions — those are data a
/// developer edits in the backoffice and promotes as JSON (spec sections 8.1 and 27.1), because
/// content-modelling decisions change far more often than layout does.
/// <para>
/// It lives in <c>Shared</c> rather than beside the component base class in <c>Rendering</c> so that
/// <c>TemplateReconciler</c> in <c>Core</c> can read it. <c>Core</c> sits below <c>Rendering</c> in
/// the reference graph and cannot see anything declared there.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CmsTemplateAttribute(string key, string name) : Attribute
{
    /// <summary>Stable key written into every payload authored against this template.</summary>
    public string Key { get; } = key;

    /// <summary>Editor-facing display name, applied when the template row is first created.</summary>
    public string Name { get; } = name;

    /// <summary>Optional help text shown when an editor picks a template.</summary>
    public string? Description { get; init; }

    /// <summary>Order this template appears in the create-page picker.</summary>
    public int SortOrder { get; init; }
}

/// <summary>
/// Declares that a Razor component is the renderer for a CMS block type (spec section 8.2).
/// </summary>
/// <param name="key">Stable key written into every block instance of this type.</param>
/// <param name="name">Editor-facing name, used only when the block type is first created.</param>
/// <remarks>
/// The block-level counterpart of <see cref="CmsTemplateAttribute"/>, read by the same reconciler
/// and subject to the same rule: code declares that the type exists and what renders it, the
/// database owns what its properties are.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CmsBlockTypeAttribute(string key, string name) : Attribute
{
    /// <summary>Stable key written into every block instance of this type.</summary>
    public string Key { get; } = key;

    /// <summary>Editor-facing name, applied when the block type row is first created.</summary>
    public string Name { get; } = name;

    /// <summary>Optional help text describing when to reach for this block.</summary>
    public string? Description { get; init; }

    /// <summary>Icon identifier shown against this block type in the picker.</summary>
    public string? IconKey { get; init; }

    /// <summary>Token pattern producing a collapsed block's one-line summary, such as <c>{headline}</c>.</summary>
    public string? SummaryTemplate { get; init; }
}
