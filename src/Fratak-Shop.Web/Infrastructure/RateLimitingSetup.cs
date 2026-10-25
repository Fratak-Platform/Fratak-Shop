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
        ["/login", "/register", "/verify", "/track-order", "/api/auth/token", "/api/auth/refresh", "/profile/password"];

    public static IServiceCollection AddShopRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(Partition);
            options.OnRejected = async (context, token) =>
            {
                var response = context.HttpContext.Response;
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
                    $"<body style=\"font-family:sans-serif;text-align:center;padding:4rem\"><h1>429</h1><p>{message}</p>" +
                    "<p><a href=\"/\">بازگشت به صفحه اصلی</a></p></body></html>", token);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> Partition(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress ?? IPAddress.None;
        var key = ip.ToString();
        var request = context.Request;
        var path = request.Path;

        if (HttpMethods.IsPost(request.Method) && CredentialPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            return Window("auth:" + key, 10, TimeSpan.FromMinutes(1));

        // Typing in the search box sends a request per pause: a larger, separate budget.
        if (path.StartsWithSegments("/api/search", StringComparison.OrdinalIgnoreCase))
            return Window("search:" + key, 120, TimeSpan.FromMinutes(1));

        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            return Window("api:" + key, 60, TimeSpan.FromMinutes(1));

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
