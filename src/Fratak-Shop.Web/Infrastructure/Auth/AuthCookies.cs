namespace Fratak_Shop.Web.Infrastructure.Auth;

/// <summary>
/// Browser transport of the tokens: HttpOnly cookies (not readable by JavaScript, so an
/// XSS bug can't steal them), SameSite=Lax (not sent on cross-site POSTs) and Secure on HTTPS.
/// Razor Pages additionally validate antiforgery tokens on every POST.
/// </summary>
public static class AuthCookies
{
    public const string AccessCookie = "fratak_at";
    public const string RefreshCookie = "fratak_rt";

    /// <summary>HttpContext.Items key of an access token issued during the current request.</summary>
    public const string FreshAccessTokenItem = "auth:fresh-access-token";

    public static void Write(HttpContext context, AccessToken access, string refreshToken, DateTimeOffset refreshExpires, bool persistent)
    {
        context.Response.Cookies.Append(AccessCookie, access.Token, Options(context, access.ExpiresAt));
        context.Response.Cookies.Append(RefreshCookie, refreshToken, Options(context, persistent ? refreshExpires : null));
        context.Items[FreshAccessTokenItem] = access.Token;
    }

    public static void WriteAccess(HttpContext context, AccessToken access)
    {
        context.Response.Cookies.Append(AccessCookie, access.Token, Options(context, access.ExpiresAt));
        context.Items[FreshAccessTokenItem] = access.Token;
    }

    public static void Clear(HttpContext context)
    {
        context.Response.Cookies.Delete(AccessCookie, Options(context, null));
        context.Response.Cookies.Delete(RefreshCookie, Options(context, null));
        context.Items.Remove(FreshAccessTokenItem);
    }

    private static CookieOptions Options(HttpContext context, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}
