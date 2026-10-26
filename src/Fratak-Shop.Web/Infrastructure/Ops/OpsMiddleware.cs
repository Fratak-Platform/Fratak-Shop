using System.Net;
using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.PageCache;

namespace Fratak_Shop.Web.Infrastructure.Ops;

// ═══════════════════════════════════════════════════════════════════════════
//  میان‌افزارهای «فنی» (مرحلهٔ هفتم – فاز ۹) – ترتیب در Program.cs:
//  ForwardedHeaders → ... → FirewallMiddleware (خیلی زود، قبل از فایل‌های ثابت و صفحه‌ها)
//  ... → Authentication → MaintenanceMiddleware (بعد از شناسایی کاربر تا کارکنان رد شوند) → OutputCache → صفحه‌ها
//  و نقطهٔ پایانی /healthz برای سرویس‌های مانیتورینگ (UptimeRobot و …).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// دیوار آتش: درخواست IP مسدود یا ربات ممنوع را با ۴۰۳ کوتاه رد می‌کند (بدون رندر صفحه تا هزینه‌ای برای سرور نداشته باشد).
/// قانون‌ها در حافظه‌اند (FirewallState) و بررسی هر درخواست چند میکروثانیه است. صفحه: /admin/system/firewall
/// </summary>
public sealed class FirewallMiddleware(RequestDelegate next)
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("403 – دسترسی شما به این سایت محدود شده است.");

    public async Task InvokeAsync(HttpContext http)
    {
        var snapshot = FirewallState.Current;
        if (!snapshot.IsEmpty)
        {
            var decision = snapshot.Check(http.Connection.RemoteIpAddress, http.Request.Headers.UserAgent.ToString(), DateTimeOffset.UtcNow);
            if (decision.Blocked)
            {
                if (decision.RuleId is { } id)
                    FirewallState.CountHit(id);
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                http.Response.ContentType = "text/plain; charset=utf-8";
                http.Response.Headers.CacheControl = "no-store";
                http.Response.ContentLength = Body.Length;
                await http.Response.Body.WriteAsync(Body, http.RequestAborted);
                return;
            }
        }
        await next(http);
    }
}

/// <summary>
/// حالت تعمیرات: بازدیدکنندگان صفحهٔ «در حال به‌روزرسانی» با کد ۵۰۳ و سرآیند Retry-After می‌بینند (موتورهای جست‌وجو
/// این را موقت می‌دانند و رتبه آسیب نمی‌بیند). استثناها: پنل مدیریت، صفحه‌های ورود، کارکنان واردشده، IPهای مجاز و
/// مسیرهای فنی (/health، /healthz، robots.txt، سرویس‌ورکر). روشن/خاموش: تنظیمات ← تعمیرات و امنیت سرور.
/// </summary>
public sealed class MaintenanceMiddleware(RequestDelegate next)
{
    public const string BypassItemKey = "fratak.maintenance.bypass";

    public async Task InvokeAsync(HttpContext http, IStoreSettingService settingsService)
    {
        var settings = await settingsService.GetAsync(http.RequestAborted);
        if (!settings.IsMaintenanceMode || MaintenanceRules.IsExemptPath(http.Request.Path))
        {
            await next(http);
            return;
        }

        if (MaintenanceRules.IsAllowedIp(settings.MaintenanceAllowedIps, http.Connection.RemoteIpAddress) || await IsStaffAsync(http))
        {
            // کارکنان سایت را عادی می‌بینند (با نوار «حالت تعمیرات روشن است» در هدر) و پاسخ‌هایشان کش عمومی نمی‌شود.
            http.Items[BypassItemKey] = true;
            await next(http);
            return;
        }

        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        http.Response.Headers.RetryAfter = (Math.Clamp(settings.MaintenanceRetryMinutes, 5, 1440) * 60).ToString(System.Globalization.CultureInfo.InvariantCulture);
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Robots-Tag"] = "noindex";
        if (http.Request.Path.StartsWithSegments("/api"))
        {
            await http.Response.WriteAsJsonAsync(new { error = "maintenance", message = MaintenanceRules.Message(settings.MaintenanceMessage) }, http.RequestAborted);
            return;
        }
        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.WriteAsync(MaintenanceRules.Page(settings.SiteName, settings.MaintenanceMessage, settings.MaintenanceRetryMinutes), http.RequestAborted);
    }

    private static async Task<bool> IsStaffAsync(HttpContext http)
    {
        var userId = http.User.GetUserId();
        if (userId is null)
            return false;
        var access = await http.RequestServices.GetRequiredService<IAdminAccessService>().GetAccessAsync(userId.Value);
        return access.IsStaff;
    }

    /// <summary>آیا این درخواست به‌خاطر حالت تعمیرات از سد رد شده (کارمند/IP مجاز)؟ برای نوار هشدار در هدر سایت.</summary>
    public static bool IsBypassed(HttpContext http) => http.Items.ContainsKey(BypassItemKey);
}

/// <summary>قواعد خالص حالت تعمیرات (قابل تست).</summary>
public static class MaintenanceRules
{
    private const string DefaultMessage = "در حال به‌روزرسانی و بهبود فروشگاه هستیم و به‌زودی برمی‌گردیم. از صبر شما سپاسگزاریم.";

    /// <summary>
    /// مسیرهایی که در حالت تعمیرات هم باز می‌مانند. مثال: /admin/orders → true؛ /login → true؛ /product/x → false.
    /// </summary>
    public static bool IsExemptPath(PathString path) =>
        path.StartsWithSegments("/admin")
        || path.StartsWithSegments("/login") || path.StartsWithSegments("/logout")
        || path.StartsWithSegments("/api/auth")
        // فرم ورود توکن ضدجعل و کپچا را از این دو آدرس می‌گیرد؛ بدون آن‌ها کارکنان در حالت تعمیرات نمی‌توانستند وارد شوند.
        || path.StartsWithSegments("/api/antiforgery") || path.StartsWithSegments("/api/captcha")
        || path.StartsWithSegments("/health") || path.StartsWithSegments("/healthz")
        || path.StartsWithSegments("/hubs")
        || path.Equals("/robots.txt") || path.Equals("/sw.js") || path.Equals("/manifest.webmanifest") || path.Equals("/offline.html");

    /// <summary>مثال: («5.6.7.0/24»، 5.6.7.9) → true.</summary>
    public static bool IsAllowedIp(string? allowedIps, IPAddress? ip) =>
        !string.IsNullOrWhiteSpace(allowedIps) && ip is not null && IpList.Contains(IpList.Parse(allowedIps).Valid, ip);

    public static string Message(string? custom) => string.IsNullOrWhiteSpace(custom) ? DefaultMessage : custom.Trim();

    /// <summary>صفحهٔ سادهٔ مستقل (بدون وابستگی به قالب سایت تا در هر شرایطی نمایش داده شود).</summary>
    public static string Page(string? siteName, string? message, int retryMinutes)
    {
        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(siteName) ? "فروشگاه" : siteName);
        var text = WebUtility.HtmlEncode(Message(message)).Replace("\n", "<br>");
        var eta = retryMinutes >= 60
            ? $"حدود {PersianNumbers.Digits(Math.Max(1, (int)Math.Round(retryMinutes / 60d)))} ساعت"
            : $"حدود {PersianNumbers.Digits(retryMinutes)} دقیقه";
        return $$"""
            <!doctype html>
            <html lang="fa" dir="rtl">
            <head>
            <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex">
            <title>در حال به‌روزرسانی – {{name}}</title>
            <link rel="stylesheet" href="/assets/css/output.css">
            <link rel="stylesheet" href="/assets/css/font.css">
            <link rel="icon" href="{{BrandIdentity.FaviconSvg}}" type="image/svg+xml">
            <meta name="theme-color" content="{{BrandIdentity.Navy}}">
            <style>
            body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#FDFDFD;color:#0B1927;padding:24px}
            .box{max-width:480px;width:100%;background:#fff;border-radius:20px;padding:36px 28px;text-align:center;box-shadow:0 10px 40px rgba(11,25,39,.08);border-top:4px solid #0069FD}
            .icon{width:72px;height:72px;margin:0 auto 18px;border-radius:50%;background:#EBF5FF;display:flex;align-items:center;justify-content:center;font-size:34px}
            h1{font-size:22px;margin:0 0 12px}p{line-height:2;color:#475569;margin:0}.eta{margin-top:18px;font-size:13px;color:#64748b}
            @media (prefers-color-scheme:dark){body{background:#0B1927;color:#E1E8F1}.box{background:#102132;box-shadow:none}p{color:#C9D4E1}.icon{background:#182E44} }
            </style>
            </head>
            <body>
            <main class="box">
            <div class="icon" aria-hidden="true">🛠️</div>
            <h1>{{name}} در حال به‌روزرسانی است</h1>
            <p>{{text}}</p>
            <div class="eta">زمان تقریبی بازگشت: {{eta}}</div>
            </main>
            </body>
            </html>
            """;
    }
}

/// <summary>
/// نقطهٔ پایانی /healthz برای سرویس‌های مانیتورینگ بیرونی.
/// بدون توکن: فقط {"status":"ok"} یا "fail" (۲۰۰/۵۰۳) بر اساس دسترسی پایگاه داده.
/// با توکن درست (?token=... یا سرآیند X-Health-Token): جزئیات همهٔ بررسی‌ها، کارهای پس‌زمینه و کش.
/// توکن در «تنظیمات ← تعمیرات و امنیت سرور» تعریف می‌شود.
/// </summary>
public static class HealthEndpoint
{
    private static readonly SemaphoreSlim DetailGate = new(1, 1);
    private static (DateTimeOffset At, object Body, bool Healthy)? _cached;

    public static void MapHealthz(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", async (HttpContext http, HealthService health, IStoreSettingService settingsService, CancellationToken cancellationToken) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Robots-Tag"] = "noindex";
            var settings = await settingsService.GetAsync(cancellationToken);
            var token = http.Request.Query["token"].ToString();
            if (string.IsNullOrEmpty(token))
                token = http.Request.Headers["X-Health-Token"].ToString();

            if (!TokenMatches(settings.HealthCheckToken, token))
            {
                var (ok, _) = await health.PingDatabaseAsync(cancellationToken);
                return Results.Json(new { status = ok ? "ok" : "fail" }, statusCode: ok ? 200 : 503);
            }

            // جزئیات کامل گران‌تر است (چند پرس‌وجو و نوشتن فایل آزمایشی): ۱۵ ثانیه کش و یک اجرا در هر لحظه.
            if (_cached is { } fresh && DateTimeOffset.UtcNow - fresh.At < TimeSpan.FromSeconds(15))
                return Results.Json(fresh.Body, statusCode: fresh.Healthy ? 200 : 503);
            await DetailGate.WaitAsync(cancellationToken);
            try
            {
                if (_cached is { } again && DateTimeOffset.UtcNow - again.At < TimeSpan.FromSeconds(15))
                    return Results.Json(again.Body, statusCode: again.Healthy ? 200 : 503);
                var checks = await health.RunAsync(cancellationToken);
                var worst = checks.Count == 0 ? HealthLevel.Healthy : checks.Max(c => c.Level);
                var runtime = health.Runtime();
                var body = new
                {
                    status = worst switch { HealthLevel.Critical => "fail", HealthLevel.Warning => "warn", _ => "ok" },
                    checkedAt = DateTimeOffset.UtcNow,
                    maintenance = settings.IsMaintenanceMode,
                    uptimeMinutes = Math.Round(runtime.Uptime.TotalMinutes),
                    memoryMb = Math.Round(runtime.WorkingSetMb),
                    version = runtime.Version,
                    checks = checks.Select(c => new { name = c.Name, level = c.Level.ToString().ToLowerInvariant(), summary = c.Summary }),
                    jobs = new { total = JobMonitor.All.Count, unhealthy = JobMonitor.CountUnhealthy(DateTimeOffset.UtcNow), paused = JobMonitor.All.Count(j => JobMonitor.IsPaused(j.Name)) },
                    pageCache = new { hits = PageCachePolicy.Hits, misses = PageCachePolicy.Misses, evictions = PageCachePolicy.Evictions },
                };
                var healthy = worst != HealthLevel.Critical;
                _cached = (DateTimeOffset.UtcNow, body, healthy);
                return Results.Json(body, statusCode: healthy ? 200 : 503);
            }
            finally
            {
                DetailGate.Release();
            }
        });
    }

    /// <summary>مقایسهٔ زمان‌ثابت (جلوگیری از حدس توکن با اندازه‌گیری زمان پاسخ). توکن خالی هرگز مطابق نیست.</summary>
    public static bool TokenMatches(string? expected, string? given)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given))
            return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(given));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
