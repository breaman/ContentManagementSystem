using Microsoft.AspNetCore.Components;

namespace S3.EditorInterop.Client.Components;

/// <summary>CodeMirror 6 source editor — the Markdown and HTML source modes from spec §14.4.</summary>
public partial class MarkdownEditor
{
    /// <summary>Either <c>markdown</c> or <c>html</c>; selects the CodeMirror language extension.</summary>
    [Parameter]
    public string Language { get; set; } = "markdown";

    protected override string CreateFunction => "createMarkdownEditor";
}
