namespace ContentManagementSystem.Client.Components.Admin.Shell;

/// <summary>
/// The pane geometry one editor has chosen for the backoffice shell (task P6-01, spec section 14.1).
/// </summary>
/// <remarks>
/// A record rather than four parameters on the component, because it is round-tripped through
/// <c>localStorage</c> as one JSON object: a layout half-restored — the tree's width remembered but
/// its collapsed state forgotten — is worse than one not restored at all, since the editor cannot
/// tell which half they are looking at.
/// <para>
/// Widths are stored in CSS pixels rather than as a fraction of the viewport. An editor sizes the
/// tree until the titles they work with stop wrapping, and that width should survive moving the
/// window to a second monitor.
/// </para>
/// </remarks>
public sealed record ShellLayout
{
    /// <summary>Narrowest a pane may be dragged before it stops being usable.</summary>
    public const double MinPaneWidth = 180;

    /// <summary>Widest a pane may be dragged, so the canvas can never be squeezed out entirely.</summary>
    public const double MaxPaneWidth = 640;

    /// <summary>Width the content tree starts at.</summary>
    public const double DefaultTreeWidth = 288;

    /// <summary>Width the properties panel starts at.</summary>
    public const double DefaultPropertiesWidth = 352;

    /// <summary>How much one arrow-key press moves a separator.</summary>
    public const double KeyboardStep = 16;

    /// <summary>How much an arrow-key press with Shift held moves a separator.</summary>
    public const double CoarseKeyboardStep = 64;

    /// <summary>What an editor who has never resized anything sees.</summary>
    public static ShellLayout Default { get; } = new();

    /// <summary>Width of the content tree pane, in CSS pixels.</summary>
    public double TreeWidth { get; init; } = DefaultTreeWidth;

    /// <summary>Width of the properties pane, in CSS pixels.</summary>
    public double PropertiesWidth { get; init; } = DefaultPropertiesWidth;

    /// <summary>Whether the content tree is collapsed to its rail.</summary>
    public bool TreeCollapsed { get; init; }

    /// <summary>Whether the properties panel is collapsed to its rail.</summary>
    public bool PropertiesCollapsed { get; init; }

    /// <summary>
    /// Brings a layout back inside its limits.
    /// </summary>
    /// <returns>The layout, with both widths clamped and any non-finite value replaced.</returns>
    /// <remarks>
    /// Applied to anything read from storage. <c>localStorage</c> is writable by anything running on
    /// the origin, and a width of <c>NaN</c> or <c>1e9</c> produces a shell with no canvas in it and
    /// no way to drag one back — a state the editor can only escape by clearing site data.
    /// </remarks>
    public ShellLayout Sanitized() =>
        this with
        {
            TreeWidth = Clamp(TreeWidth, DefaultTreeWidth),
            PropertiesWidth = Clamp(PropertiesWidth, DefaultPropertiesWidth),
        };

    /// <summary>Clamps one width, falling back when the stored value is not a usable number.</summary>
    private static double Clamp(double value, double fallback) =>
        double.IsFinite(value)
            ? Math.Clamp(value, MinPaneWidth, MaxPaneWidth)
            : fallback;
}
