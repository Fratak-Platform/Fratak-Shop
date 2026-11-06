using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Loyalty;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Shareable invite links: any storefront URL with <c>?ref=CODE</c> stores the (validated) referral
/// code in a cookie for <c>ReferralCookieDays</c>, so the friend can browse first and sign up later
/// (password or SMS sign-up both read it). The first valid link wins; signed-in visitors are ignored.
/// </summary>
public static partial class ReferralCookie
{
    public const string Name = "fratak_ref";

    [GeneratedRegex("^[A-Za-z0-9-]{4,20}$")]
    private static partial Regex CodePattern();

    public static string? Read(HttpContext http)
    {
        var value = http.Request.Cookies[Name];
        return value is not null && CodePattern().IsMatch(value) ? value : null;
    }

    public static void Clear(HttpContext http) => http.Response.Cookies.Delete(Name);

    public static IApplicationBuilder UseReferralCapture(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        if (HttpMethods.IsGet(http.Request.Method)
            && http.Request.Query.TryGetValue("ref", out var values)
            && values.ToString() is { } code
            && CodePattern().IsMatch(code)
            && http.User.Identity?.IsAuthenticated != true
            && Read(http) is null)
        {
            var settings = await http.RequestServices.GetRequiredService<Core.Services.Settings.IStoreSettingService>().GetAsync();
            if (settings.IsReferralEnabled
                && await http.RequestServices.GetRequiredService<ILoyaltyService>().IsValidReferralCodeAsync(code))
            {
                http.Response.Cookies.Append(Name, code.ToUpperInvariant(), new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    Secure = http.Request.IsHttps,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromDays(Math.Clamp(settings.ReferralCookieDays, 1, 365)),
                });
            }
        }

        await next(http);
    });
}
