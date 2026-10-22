using System.Security.Claims;

namespace Fratak_Shop.Web.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static string GetDisplayName(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirstValue(ClaimTypes.GivenName)
               ?? principal?.FindFirstValue(ClaimTypes.MobilePhone)
               ?? "کاربر";
    }

    public static string? GetPhoneNumber(this ClaimsPrincipal? principal)
        => principal?.FindFirstValue(ClaimTypes.MobilePhone);

    public static bool IsSignedIn(this ClaimsPrincipal? principal)
        => principal?.Identity?.IsAuthenticated == true && principal.GetUserId() is not null;
}
