namespace S3.EditorInterop.Client.Components;

/// <summary>Quill — the constrained WYSIWYG surface from spec §14.4.</summary>
public partial class RichTextEditor
{
    protected override string CreateFunction => "createRichTextEditor";
}
