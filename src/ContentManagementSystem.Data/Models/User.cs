using System.ComponentModel.DataAnnotations;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Data.Interfaces;

using Microsoft.AspNetCore.Identity;

namespace ContentManagementSystem.Data.Models;

public class User : IdentityUser<int>, IEntityBase
{
    [MaxLength(FieldLengths.PersonName)]
    public string? FirstName { get; set; }
    [MaxLength(FieldLengths.PersonName)]
    public string? LastName { get; set; }
    public DateTimeOffset MemberSince { get; set; }
}