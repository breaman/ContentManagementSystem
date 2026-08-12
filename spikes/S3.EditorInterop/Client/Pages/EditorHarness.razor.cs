using Microsoft.AspNetCore.Components;

namespace S3.EditorInterop.Client.Pages;

/// <summary>
/// The single screen of the spike: two editors that can be mounted and unmounted repeatedly, with
/// their values bound to .NET state so both directions of the binding are observable in the DOM.
/// </summary>
public partial class EditorHarness : ComponentBase
{
    private const string InitialMarkdown = "## Why teams choose us\n\nWe help teams ship faster.";
    private const string InitialRichText = "<p>Editors stopped filing tickets.</p>";

    private bool EditorsMounted { get; set; } = true;

    private int MountCycles { get; set; } = 1;

    private string MarkdownValue { get; set; } = InitialMarkdown;

    private string RichTextValue { get; set; } = InitialRichText;

    private void ToggleEditors()
    {
        EditorsMounted = !EditorsMounted;

        if (EditorsMounted)
        {
            MountCycles++;
        }
    }

    /// <summary>Proves the .NET → JavaScript direction: the editors must adopt these values.</summary>
    private void SetFromDotNet()
    {
        MarkdownValue = "# Set from .NET";
        RichTextValue = "<p>Set from .NET</p>";
    }
}
