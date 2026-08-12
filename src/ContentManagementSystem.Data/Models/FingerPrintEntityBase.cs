namespace ContentManagementSystem.Data.Models;

public abstract class FingerPrintEntityBase : EntityBase
{
    public DateTimeOffset? CreatedOn { get; set; }
    public int CreatedBy { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }
    public int ModifiedBy { get; set; }
}