using Microsoft.AspNetCore.DataProtection;

namespace Fratak_Shop.Web.Infrastructure.Auth;

/// <summary>
/// The "password was correct, SMS code still missing" state of a two-step sign-in.
/// Kept in a short-lived, encrypted and signed HttpOnly cookie – no session is
/// created and no token is issued before the second step succeeds.
/// </summary>
public interface ITwoFactorTicket
{
    void Issue(HttpContext context, Guid userId, bool rememberMe);

    /// <summary>The pending sign-in, or null when the cookie is missing, tampered with or expired.</summary>
    (Guid UserId, bool RememberMe)? Read(HttpContext context);

    void Clear(HttpContext context);
}

public class TwoFactorTicket(IDataProtectionProvider provider) : ITwoFactorTicket
{
    public const string CookieName = "fratak_2fa";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("Fratak.Auth.TwoFactor.v1").ToTimeLimitedDataProtector();

    public void Issue(HttpContext context, Guid userId, bool rememberMe)
    {
        var value = _protector.Protect($"{userId:N}|{(rememberMe ? 1 : 0)}", Lifetime);
        context.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            IsEssential = true,
            Path = "/login",
            MaxAge = Lifetime,
        });
    }

    public (Guid UserId, bool RememberMe)? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var parts = _protector.Unprotect(value).Split('|');
            if (parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var userId))
                return (userId, parts[1] == "1");
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Expired or forged.
        }

        return null;
    }

    public void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/login" });
}
