using System.Security.Claims;
using Fratak_Shop.Core.DTOs.Users.Query;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Fratak_Shop.Web.Services;

/// <summary>
/// Cookie sign-in / sign-out for storefront customers. Signing in also moves the
/// guest cart into the customer's cart so nothing added before login is lost.
/// </summary>
public interface IShopAuthService
{
    Task SignInAsync(UserDto user, bool isPersistent);
    Task SignOutAsync();
}

public class ShopAuthService(IHttpContextAccessor httpContextAccessor, ICartSessionService cartSession) : IShopAuthService
{
    public async Task SignInAsync(UserDto user, bool isPersistent)
    {
        var httpContext = httpContextAccessor.HttpContext
                          ?? throw new InvalidOperationException("No active HTTP context.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.PhoneNumber),
            new(ClaimTypes.MobilePhone, user.PhoneNumber),
            new(ClaimTypes.Role, user.Role.ToString()),
        };

        if (!string.IsNullOrWhiteSpace(user.FullName))
            claims.Add(new Claim(ClaimTypes.GivenName, user.FullName));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                AllowRefresh = true,
            });

        await cartSession.MergeGuestCartAsync(user.Id);
    }

    public async Task SignOutAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return;

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
