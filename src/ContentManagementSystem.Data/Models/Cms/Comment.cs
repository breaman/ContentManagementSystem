namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A review remark on a page, optionally anchored to the zone it is about (spec section 11.9).
/// </summary>
/// <remarks>
/// The anchor is the point. Feedback that arrives as a paragraph of prose has to be re-read against
/// the page to be acted on; feedback attached to <c>heroHeadline</c> shows up on that card in the
/// editor, and the author fixes it without translating anything.
/// <para>
/// Comments belong to the <em>page</em>, not to a version, and survive rejection deliberately — the
/// reason a version was sent back is the reason the next draft has to be different, so losing the
/// thread at exactly the moment the draft is recreated would discard the only useful record
/// (criterion P7 #4). <see cref="PageVersionId"/> records which version was on screen when the
/// remark was made, so a thread can be shown as historical once the page has moved on.
/// </para>
/// </remarks>
public class Comment : FingerPrintEntityBase
{
    /// <summary>Page being discussed.</summary>
    public int PageId { get; set; }

    /// <summary>Page being discussed.</summary>
    public Page Page { get; set; } = null!;

    /// <summary>The version on screen when the remark was made, if it was made against one.</summary>
    public int? PageVersionId { get; set; }

    /// <summary>The version on screen when the remark was made.</summary>
    public PageVersion? PageVersion { get; set; }

    /// <summary>
    /// Zone the remark is about, or null for a remark about the page as a whole.
    /// </summary>
    /// <remarks>
    /// A key rather than a foreign key to <c>Zone</c>: zones come and go with template revisions,
    /// and a comment about a zone that has since been removed should still read as being about it
    /// rather than fail to load.
    /// </remarks>
    public string? ZoneKey { get; set; }

    /// <summary>The comment this one replies to, or null when it opens a thread.</summary>
    public int? ParentCommentId { get; set; }

    /// <summary>The comment this one replies to.</summary>
    public Comment? ParentComment { get; set; }

    /// <summary>Replies to this comment.</summary>
    public ICollection<Comment> Replies { get; set; } = [];

    /// <summary>What was said. Plain text — never rendered as markup.</summary>
    /// <remarks>
    /// Stored and displayed verbatim, with no sanitization pass, because nothing ever interprets it
    /// as HTML. The moment a screen renders it through <c>MarkupString</c> this becomes an injection
    /// surface, so it must not.
    /// </remarks>
    public string Body { get; set; } = null!;

    /// <summary>When the thread was marked dealt with, or null while it is still open.</summary>
    public DateTimeOffset? ResolvedOn { get; set; }

    /// <summary>Who marked it dealt with.</summary>
    public int? ResolvedByUserId { get; set; }

    /// <summary>Who marked it dealt with.</summary>
    public User? ResolvedBy { get; set; }
}
