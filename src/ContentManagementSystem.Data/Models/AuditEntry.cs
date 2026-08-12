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

    public AuditLog ToAuditLog()
    {
        var auditLog = new AuditLog();
        auditLog.UserId = UserId;
        auditLog.Type = AuditType.ToString();
        auditLog.TableName = TableName;
        auditLog.DateTime = DateTimeOffset.UtcNow;
        auditLog.PrimaryKey = JsonSerializer.Serialize(KeyValues);
        auditLog.OldValues = OldValues.Count == 0 ? null : JsonSerializer.Serialize(OldValues);
        auditLog.NewValues = NewValues.Count == 0 ? null! : JsonSerializer.Serialize(NewValues);
        auditLog.AffectedColumns = ChangedColumns.Count == 0 ? null : JsonSerializer.Serialize(ChangedColumns);

        return auditLog;
    }
}