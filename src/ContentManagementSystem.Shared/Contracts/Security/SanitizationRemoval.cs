namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// One thing the sanitizer took out of a piece of authored markup.
/// </summary>
/// <param name="Kind">What sort of thing was removed.</param>
/// <param name="Name">
/// The tag name, attribute name, CSS property, or CSS class that went. Empty for a comment, which
/// has no name.
/// </param>
/// <param name="TagName">The element it was removed from, where that is meaningful.</param>
/// <param name="Value">
/// A truncated excerpt of what was removed, for showing an author what they are about to lose.
/// </param>
/// <remarks>
/// Over-stripping is the other half of the sanitization problem (risk R3). A service that returns
/// only clean markup can be verified safe but cannot be verified <em>non-destructive</em>, and
/// silent stripping is the most common "the CMS ate my content" support ticket
/// (<see cref="IContentSanitizer.SanitizeWithReport"/>, spec section 14.4).
/// <para>
/// <see cref="Value"/> holds attacker-influenced text by construction. It is data to be displayed,
/// never markup to be interpolated — render it through a component that encodes, and never through
/// <c>MarkupString</c>.
/// </para>
/// </remarks>
public sealed record SanitizationRemoval(
    SanitizationRemovalKind Kind,
    string Name,
    string? TagName = null,
    string? Value = null)
{
    /// <summary>Longest excerpt <see cref="Truncate"/> keeps.</summary>
    /// <remarks>
    /// A report is written to logs and shown in the editor. An unbounded excerpt makes one pasted
    /// document able to fill either.
    /// </remarks>
    public const int MaxValueLength = 200;

    /// <summary>A one-line description, for logs and test output.</summary>
    public string Describe() => Kind switch
    {
        SanitizationRemovalKind.Tag => $"<{Name}> was removed",
        SanitizationRemovalKind.Attribute => $"{Name} was removed from <{TagName}>",
        SanitizationRemovalKind.Url => $"the URL in {Name} on <{TagName}> was refused",
        SanitizationRemovalKind.Style => $"the {Name} style was removed from <{TagName}>",
        SanitizationRemovalKind.CssClass => $"the '{Name}' class was removed from <{TagName}>",
        SanitizationRemovalKind.Comment => "an HTML comment was removed",
        _ => $"{Name} was removed",
    };

    /// <summary>Cuts an excerpt down to <see cref="MaxValueLength"/>.</summary>
    /// <param name="value">The text to excerpt.</param>
    public static string? Truncate(string? value) =>
        value is { Length: > MaxValueLength } ? value[..MaxValueLength] + "…" : value;
}
