namespace ContentManagementSystem.Server.Delivery.Preview;

/// <summary>
/// The widths the preview frame can be constrained to (task P3-21, spec section 12.3).
/// </summary>
/// <remarks>
/// Three named widths rather than a free-form pixel value. The question an editor is answering is
/// "does this layout survive a phone", not "what happens at 412 pixels", and a named set is what
/// lets the choice be a link a static page can render — no JavaScript, no interactivity, and
/// therefore no departure from the static-SSR rule the whole delivery path is built on
/// (spec section 5.3).
/// </remarks>
public enum PreviewDevice
{
    /// <summary>Full width, which is the page exactly as the delivery path serves it.</summary>
    Desktop = 0,

    /// <summary>A tablet in portrait.</summary>
    Tablet = 1,

    /// <summary>A phone.</summary>
    Mobile = 2,
}

/// <summary>
/// Reads and describes the device widths the preview frame offers.
/// </summary>
public static class PreviewDevices
{
    /// <summary>Every device, in the order the toolbar offers them.</summary>
    public static IReadOnlyList<PreviewDevice> All { get; } =
        [PreviewDevice.Desktop, PreviewDevice.Tablet, PreviewDevice.Mobile];

    /// <summary>
    /// Reads the device from a query string value.
    /// </summary>
    /// <param name="value">What the request asked for, if anything.</param>
    /// <returns>The device, defaulting to <see cref="PreviewDevice.Desktop"/>.</returns>
    /// <remarks>
    /// An unreadable value falls back rather than failing. The parameter is a view preference in a
    /// URL people copy and paste to each other; refusing the request over it would turn a mangled
    /// link into an error page instead of a preview at the default width.
    /// </remarks>
    public static PreviewDevice Parse(string? value) =>
        Enum.TryParse<PreviewDevice>(value, ignoreCase: true, out var device) &&
        Enum.IsDefined(device)
            ? device
            : PreviewDevice.Desktop;

    /// <summary>The query string value that selects a device.</summary>
    /// <param name="device">The device.</param>
    public static string Key(PreviewDevice device) => device.ToString().ToLowerInvariant();

    /// <summary>The label the toolbar button carries.</summary>
    /// <param name="device">The device.</param>
    public static string Label(PreviewDevice device) => device switch
    {
        PreviewDevice.Tablet => "Tablet",
        PreviewDevice.Mobile => "Mobile",
        _ => "Desktop",
    };

    /// <summary>
    /// The width the frame is constrained to, as a label for the reader.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <remarks>
    /// The constraint itself is a CSS class in <c>preview.css</c>, not an inline style: authored
    /// content already renders under a policy that forbids inline styles (spec section 20.5), and
    /// the preview chrome must not be the one page that needs the policy relaxed.
    /// </remarks>
    public static string WidthLabel(PreviewDevice device) => device switch
    {
        PreviewDevice.Tablet => "834px",
        PreviewDevice.Mobile => "390px",
        _ => "full width",
    };

    /// <summary>The CSS class that constrains the frame for a device.</summary>
    /// <param name="device">The device.</param>
    public static string CssClass(PreviewDevice device) =>
        $"cms-preview-viewport cms-preview-viewport--{Key(device)}";
}
