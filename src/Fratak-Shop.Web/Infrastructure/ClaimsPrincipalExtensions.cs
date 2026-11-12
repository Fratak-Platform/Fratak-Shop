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

    /// <summary>
    /// نام کامل کاربر (نام + نام خانوادگی، همان ادعای Name توکن)؛ بی‌نام ← شمارهٔ موبایل؛ هیچ‌کدام ← «کاربر».
    /// دور ۳۱: منوی کشویی هدر همین را زیر نام نشان می‌دهد (بدون «جان»).
    /// </summary>
    public static string GetDisplayName(this ClaimsPrincipal? principal)
    {
        var name = principal?.FindFirstValue(ShopClaimTypes.Name);
        return !string.IsNullOrWhiteSpace(name) ? name : principal.GetPhoneNumber() ?? "کاربر";
    }

    /// <summary>
    /// دور ۲۹: خطاب مناسب نسل («محمد عزیز»، «ناظری گرامی»). توکن قدیمی بدون این ادعا ← «{نام} عزیز» یا «کاربر عزیز».
    /// </summary>
    public static string GetGreeting(this ClaimsPrincipal? principal)
    {
        var greeting = principal?.FindFirstValue(ShopClaimTypes.Greeting);
        if (!string.IsNullOrWhiteSpace(greeting))
            // دور ۳۱: توکن‌های صادرشده پیش از این دور ممکن است هنوز «محمد جان» داشته باشند؛ «جان» هرگز نمایش داده نمی‌شود.
            return greeting.EndsWith(" جان", StringComparison.Ordinal) ? greeting[..^4] + " عزیز" : greeting;
        var name = principal?.FindFirstValue(ShopClaimTypes.Name);
        return !string.IsNullOrWhiteSpace(name) ? name + " عزیز" : Fratak_Shop.Core.Services.Users.UserGreeting.Anonymous;
    }

    public static string? GetPhoneNumber(this ClaimsPrincipal? principal)
        => principal?.FindFirstValue(ShopClaimTypes.Phone);

    public static bool IsSignedIn(this ClaimsPrincipal? principal)
        => principal?.Identity?.IsAuthenticated == true && principal.GetUserId() is not null;
}
