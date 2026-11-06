using System.Security.Claims;
using Fratak_Shop.Web.Infrastructure.Auth;

namespace Fratak_Shop.Web.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirstValue(ShopClaimTypes.UserId);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static string GetDisplayName(this ClaimsPrincipal? principal)
    {
        var name = principal?.FindFirstValue(ShopClaimTypes.Name);
        return !string.IsNullOrWhiteSpace(name) ? name : principal.GetPhoneNumber() ?? "کاربر";
    }

    public static string? GetPhoneNumber(this ClaimsPrincipal? principal)
        => principal?.FindFirstValue(ShopClaimTypes.Phone);

    public static bool IsSignedIn(this ClaimsPrincipal? principal)
        => principal?.Identity?.IsAuthenticated == true && principal.GetUserId() is not null;
}
