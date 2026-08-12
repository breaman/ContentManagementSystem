using ContentManagementSystem.Data.Interfaces;

using Microsoft.AspNetCore.Identity;

namespace ContentManagementSystem.Data.Models;

public class Role : IdentityRole<int>, IEntityBase
{
}