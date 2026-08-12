using System.Security.Claims;

using ContentManagementSystem.Data.Interfaces;

namespace ContentManagementSystem.Server.Services;

public class HttpUserService : IUserService
{
    private IHttpContextAccessor HttpContextAccessor { get; }
    public HttpUserService(IHttpContextAccessor httpContextAccessor)
    {
        HttpContextAccessor = httpContextAccessor;
    }
    public int UserId
    {
        get
        {
            return Convert.ToInt32(HttpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }
    }
}