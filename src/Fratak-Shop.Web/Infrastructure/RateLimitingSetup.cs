using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Per-IP rate limits: strict for credential endpoints (password hashing is expensive
/// and brute force must be slowed down), moderate for other form posts and the JSON API.
/// Normal page views are not limited.
/// </summary>
public static class RateLimitingSetup
{
    private static readonly string[] CredentialPaths =
        ["/login", "/register", "/verify", "/track-order", "/api/auth/token", "/api/auth/refresh", "/profile/password", "/forgot-password"];

    public static IServiceCollection AddShopRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(Partition);
            options.OnRejected = async (context, token) =>
            {
                var response = context.HttpContext.Response;
                // مرحلهٔ هفدهم: ثبت در «درخواست‌های ردشده» تا معلوم شود کدام مسیر و IP به سقف خورده است.
                Ops.RequestMonitor.Record(context.HttpContext, Ops.RequestMonitor.Kind.RateLimited, PolicyName(context.HttpContext));
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                const string message = "تعداد درخواست‌های شما بیش از حد مجاز است؛ لطفاً کمی بعد دوباره تلاش کنید.";
                if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
                {
                    await response.WriteAsJsonAsync(new { error = message }, token);
                    return;
                }

                response.ContentType = "text/html; charset=utf-8";
                await response.WriteAsync(
                    $"<!doctype html><html lang=\"fa\" dir=\"rtl\"><meta charset=\"utf-8\"><title>429</title>" +
                    $"<body style=\"font-family:Vazirmatn,Tahoma,sans-serif;text-align:center;padding:4rem;background:#FDFDFD;color:#0B1927\"><h1>429</h1><p>{message}</p>" +
                    "<p><a href=\"/\" style=\"color:#0069FD\">بازگشت به صفحه اصلی</a></p></body></html>", token);
            };
        });

        return services;
    }

    /// <summary>
    /// نام سهمیه‌ای که این درخواست به آن خورده (برای پایش). مثال: POST /login ← «ورود/ثبت‌نام: ۱۰ در دقیقه».
    /// </summary>
    public static string PolicyName(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path;
        if (HttpMethods.IsPost(request.Method) && CredentialPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            return "ورود/ثبت‌نام: ۱۰ درخواست در دقیقه برای هر IP";
        if (path.StartsWithSegments("/api/search/image", StringComparison.OrdinalIgnoreCase))
            return "جست‌وجو با عکس: ۱۰ در دقیقه";
        if (path.StartsWithSegments("/api/search", StringComparison.OrdinalIgnoreCase))
            return "جست‌وجو: ۱۲۰ در دقیقه";
        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            return "API: سهمیهٔ دقیقه‌ای هر IP";
        return HttpMethods.IsPost(request.Method) ? "ارسال فرم: سهمیهٔ دقیقه‌ای هر IP" : "سهمیهٔ دقیقه‌ای هر IP";
    }

    private static RateLimitPartition<string> Partition(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress ?? IPAddress.None;
        // مرحلهٔ هفدهم: IP مورد اعتماد تنظیمات سرور (Security:TrustedIps، مثلاً دفتر) محدودیت تعداد ندارد.
        if (Fratak_Shop.Core.Services.Ops.FirewallState.IsTrusted(context.Connection.RemoteIpAddress))
            return RateLimitPartition.GetNoLimiter("trusted");
        var key = ip.ToString();
        var request = context.Request;
        var path = request.Path;

        if (HttpMethods.IsPost(request.Method) && CredentialPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            return Window("auth:" + key, 10, TimeSpan.FromMinutes(1));

        // دور ۲۴: جست‌وجو با عکس پردازش تصویر دارد؛ بودجهٔ جدا و کوچک (۱۰ در دقیقه).
        if (path.StartsWithSegments("/api/search/image", StringComparison.OrdinalIgnoreCase))
            return Window("imgsearch:" + key, 10, TimeSpan.FromMinutes(1));

        // Typing in the search box sends a request per pause: a larger, separate budget.
        if (path.StartsWithSegments("/api/search", StringComparison.OrdinalIgnoreCase))
            return Window("search:" + key, 120, TimeSpan.FromMinutes(1));

        // آمار بازدید (فاز ۷): یک درخواست برای هر صفحه؛ بودجهٔ جدا تا سهم API های دیگر (و کاربرانی که پشت یک IP مشترک
        // اپراتور موبایل هستند) مصرف نشود. سرور خودش هم هر بازدیدکننده را به ۶۰ ثبت در دقیقه محدود می‌کند.
        if (path.StartsWithSegments("/api/a", StringComparison.OrdinalIgnoreCase))
            return Window("beacon:" + key, 600, TimeSpan.FromMinutes(1));

        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            return Window("api:" + key, 60, TimeSpan.FromMinutes(1));

        // اتصال زندهٔ SignalR (negotiate و در نبود WebSocket، درخواست‌های long-polling): بودجهٔ جدا و بزرگ‌تر
        // تا چند تب باز یا قطع و وصل شبکه باعث خطای ۴۲۹ نشود.
        if (path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
            return Window("hubs:" + key, 600, TimeSpan.FromMinutes(1));

        // Resized images: cheap when cached on disk, CPU-heavy on the first request of a size.
        if (path.StartsWithSegments("/media", StringComparison.OrdinalIgnoreCase))
            return Window("media:" + key, 600, TimeSpan.FromMinutes(1));

        // Admin panel: staff work fast (bulk edits, status changes); only signed-in staff get past the page filter.
        if (path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase))
            return HttpMethods.IsPost(request.Method) ? Window("admin:" + key, 240, TimeSpan.FromMinutes(1)) : RateLimitPartition.GetNoLimiter("none");

        if (HttpMethods.IsPost(request.Method))
        {
            // Cart quantity / favourite buttons are clicked often; other forms (contact, address, …) less so.
            var limit = path.StartsWithSegments("/cart", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWithSegments("/product", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWithSegments("/compare", StringComparison.OrdinalIgnoreCase)
                ? 60
                : 20;
            return Window("post:" + key, limit, TimeSpan.FromMinutes(1));
        }

        return RateLimitPartition.GetNoLimiter("none");
    }

    private static RateLimitPartition<string> Window(string key, int permits, TimeSpan window) =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
}
