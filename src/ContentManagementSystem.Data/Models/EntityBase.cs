using ContentManagementSystem.Data.Interfaces;

namespace ContentManagementSystem.Data.Models;

public abstract class EntityBase : IEntityBase
{
    public int Id { get; set; }
}