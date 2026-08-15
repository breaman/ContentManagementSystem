using System.Text.Json;

using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ContentManagementSystem.Data.Models;

public class AuditEntry
{
    public EntityEntry Entry { get; set; }
    public int UserId { get; set; }
    public string TableName { get; set; } = null!;
    public Dictionary<string, object> KeyValues { get; } = new();
    public Dictionary<string, object> OldValues { get; } = new();
    public Dictionary<string, object> NewValues { get; } = new();
    public AuditType AuditType { get; set; }
    public List<string> ChangedColumns { get; } = new();

    public AuditEntry(EntityEntry entry)
    {
        Entry = entry;
    }

    /// <summary>Builds the row this entry is written to the audit table as.</summary>
    /// <param name="clock">
    /// Clock the timestamp is read from. Supplied by the context so an audit row and the
    /// fingerprints on the entity it describes carry the same instant, and so a suite running on a
    /// fake clock sees audit rows dated to match it.
    /// </param>
    public AuditLog ToAuditLog(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var auditLog = new AuditLog();
        auditLog.UserId = UserId;
        auditLog.Type = AuditType.ToString();
        auditLog.TableName = TableName;
        auditLog.DateTime = clock.GetUtcNow();
        auditLog.PrimaryKey = JsonSerializer.Serialize(KeyValues);
        auditLog.OldValues = OldValues.Count == 0 ? null : JsonSerializer.Serialize(OldValues);
        auditLog.NewValues = NewValues.Count == 0 ? null! : JsonSerializer.Serialize(NewValues);
        auditLog.AffectedColumns = ChangedColumns.Count == 0 ? null : JsonSerializer.Serialize(ChangedColumns);

        return auditLog;
    }
}