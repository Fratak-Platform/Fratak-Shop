using System.Security.Claims;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Services.Users;

namespace Fratak_Shop.Web.Services;

public interface ICurrentUserService
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? PhoneNumber { get; }
    ClaimsPrincipal? Principal { get; }
    Task<UserDto?> GetCurrentUserAsync();
}

public class CurrentUserService(IHttpContextAccessor httpContextAccessor, IUserService userService) : ICurrentUserService
{
    private readonly IHttpContextAccessor _http = httpContextAccessor;
    private readonly IUserService _userService = userService;

    public bool IsAuthenticated => _http.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var idStr = _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(idStr, out var g) ? g : null;
        }
    }

    public string? PhoneNumber => _http.HttpContext?.User?.FindFirstValue(ClaimTypes.MobilePhone)
                               ?? _http.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);

    public ClaimsPrincipal? Principal => _http.HttpContext?.User;

    public async Task<UserDto?> GetCurrentUserAsync()
    {
        var uid = UserId;
        if (uid is null) return null;
        return await _userService.GetUserByIdAsync(uid.Value);
    }
}
