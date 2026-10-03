using System.Security.Claims;
using Fratak_Shop.Core.DTOs.Auth.Query;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// ساخت هویت کوکی کاربر. نقش‌های چندگانه (نقش اصلی + نقش‌های اضافی) همگی به‌عنوان
/// Claim ثبت می‌شوند تا هر Policy بتواند بر اساس آن‌ها تصمیم بگیرد.
/// </summary>
public static class AuthSignIn
{
    public static async Task SignInAsync(HttpContext context, AuthResultDto user, bool rememberMe)
    {
        var roles = user.Roles.Count > 0 ? user.Roles : [user.PrimaryRole];

        var claims = new List<Claim>
        {
            new(FratakClaims.UserId, user.UserId.ToString()),
            new(FratakClaims.FullName, user.FullName ?? user.PhoneNumber),
            new(ClaimTypes.Name, user.FullName ?? user.PhoneNumber),
            new(ClaimTypes.MobilePhone, user.PhoneNumber),
        };

        claims.AddRange(roles
            .Where(role => role != UserRole.None)
            .Distinct()
            .Select(role => new Claim(ClaimTypes.Role, role.ToString())));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null,
            });
    }

    public static async Task SignOutAsync(HttpContext context) =>
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>مسیر ورود بر اساس نقش کاربر؛ اعضای پنل به داشبورد و مشتری به پروفایل می‌رود.</summary>
    public static string LandingPath(AuthResultDto user, string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/'))
            return returnUrl;

        return user.IsStaff ? "/admin" : "/profile";
    }
}
