namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A cooperative note that somebody has a page open in the editor.
/// </summary>
/// <remarks>
/// <strong>A lock never prevents editing</strong> (spec section 11.8, ADR 0012). It exists so a
/// second editor is told "Elena is editing this, last active 12 s ago" and can decide, not so the
/// system can refuse them. Locks that block are locks that get stuck, and the authoritative defence
/// against a lost update is the <c>rowversion</c> on <see cref="PageVersion"/> — which works whether
/// or not anyone acquired anything.
/// <para>
/// Keyed on <see cref="PageId"/> rather than carrying a surrogate id: at most one lock exists per
/// page, and a table whose primary key <em>is</em> that fact cannot hold two.
/// </para>
/// <para>
/// Written every 30 seconds per open editor, which is why it is exempt from audit capture — see
/// <c>AuditLogInterceptor</c>.
/// </para>
/// </remarks>
public class EditLock
{
    /// <summary>Page being edited. The primary key.</summary>
    public int PageId { get; set; }

    /// <summary>Page being edited.</summary>
    public Page Page { get; set; } = null!;

    /// <summary>Editor holding the lock.</summary>
    public int UserId { get; set; }

    /// <summary>Editor holding the lock.</summary>
    public User User { get; set; } = null!;

    /// <summary>When the editor opened the page.</summary>
    public DateTimeOffset AcquiredOn { get; set; }

    /// <summary>
    /// Last sign of life. The reaper expires a lock two minutes after this, so a closed laptop
    /// releases the page without anybody having to ask.
    /// </summary>
    public DateTimeOffset HeartbeatOn { get; set; }
}
