using System.Collections.Concurrent;
using System.Text;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>
/// دور نوزدهم (A18): ربات‌های «پرمصرف» سئو/داده‌کاوی/هوش مصنوعی که هیچ خریداری نمی‌آورند ولی روی هاست ضعیف صدها صفحه در
/// دقیقه می‌خوانند (AhrefsBot، SemrushBot، MJ12bot، Bytespider، …).
/// <para>مسدود نمی‌شوند (مالک ممکن است از Ahrefs استفاده کند) بلکه آهسته می‌شوند: هر خانوادهٔ ربات حداکثر یک درخواست در هر
/// ۳ ثانیه، و وقتی سرور زیر فشار است اصلاً پاسخ نمی‌گیرند (۴۲۹ با Retry-After). گوگل، بینگ، یاندکس، دک‌دک‌گو و ربات‌های
/// پیش‌نمایش (تلگرام/واتس‌اپ) عمداً در این فهرست نیستند.</para>
/// <para>مثال: <c>HeavyCrawlers.Match("Mozilla/5.0 (compatible; AhrefsBot/7.0)")</c> → «ahrefsbot».</para>
/// </summary>
public static class HeavyCrawlers
{
    /// <summary>فاصلهٔ مجاز بین دو درخواست یک خانوادهٔ ربات.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(3);

    /// <summary>بخش‌های شناسهٔ ربات‌ها (حروف کوچک). همین فهرست در robots.txt با Crawl-delay هم می‌آید.</summary>
    public static readonly string[] Tokens =
    [
        "ahrefsbot", "semrushbot", "mj12bot", "dotbot", "petalbot", "bytespider", "blexbot", "dataforseobot",
        "serpstatbot", "barkrowler", "megaindex", "seekportbot", "imagesiftbot", "amazonbot", "claudebot",
        "gptbot", "ccbot", "omgilibot", "zoominfobot", "linkfluence", "turnitinbot", "awariobot",
    ];

    /// <summary>نام‌های نمایشی robots.txt (User-agent).</summary>
    public static readonly string[] RobotsNames =
    [
        "AhrefsBot", "SemrushBot", "MJ12bot", "DotBot", "PetalBot", "Bytespider", "BLEXBot", "DataForSeoBot",
        "serpstatbot", "Barkrowler", "MegaIndex.ru", "SeekportBot", "ImagesiftBot", "Amazonbot", "ClaudeBot", "GPTBot", "CCBot",
    ];

    private static readonly ConcurrentDictionary<string, long> LastHit = new(StringComparer.Ordinal);

    /// <summary>خانوادهٔ ربات پرمصرف در User-Agent (یا null). مثال: «… SemrushBot/7~bl …» → «semrushbot».</summary>
    public static string? Match(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent) || userAgent.Length > 1024)
            return null;
        foreach (var token in Tokens)
        {
            if (userAgent.Contains(token, StringComparison.OrdinalIgnoreCase))
                return token;
        }
        return null;
    }

    /// <summary>
    /// آیا این درخواست ربات باید الان رد شود؟ (فشار ≥ افزایش‌یافته، یا کمتر از ۳ ثانیه از درخواست قبلی همان خانواده.)
    /// مثال: دو درخواست AhrefsBot در یک ثانیه → دومی true.
    /// </summary>
    public static bool ShouldThrottle(string family, PressureLevel pressure, DateTimeOffset now)
    {
        if (pressure >= PressureLevel.Elevated)
            return true;
        var ticks = now.UtcTicks;
        var allowed = true;
        LastHit.AddOrUpdate(family, ticks, (_, previous) =>
        {
            if (ticks - previous < MinGap.Ticks)
            {
                allowed = false;
                return previous;
            }
            return ticks;
        });
        return !allowed;
    }

    /// <summary>فقط آزمون‌ها: پاک کردن زمان‌ها.</summary>
    public static void ResetForTests() => LastHit.Clear();
}

/// <summary>
/// دور نوزدهم (A19): «صف ورودی» برنامه برای وقتی که بازدید از ظرفیت هاست بیشتر است.
/// <para>بدون این، ۱۰۰۰ کاربر هم‌زمان یعنی ۱۰۰۰ درخواست هم‌زمان که هر کدام حافظه و اتصال پایگاه داده می‌گیرند ← حافظه از سقف
/// استخر IIS می‌گذرد ← پروسه کشته می‌شود ← همه ۵۰۳ می‌بینند. با این صف، حداکثر <see cref="ResourceBudget.MaxConcurrentRequests"/>
/// صفحهٔ مهمان هم‌زمان ساخته می‌شود و بقیه چند ثانیه منتظر می‌مانند (صفحه‌های کش‌شده چند میلی‌ثانیه‌اند؛ صف سریع خالی می‌شود).</para>
/// <list type="bullet">
/// <item>فقط GET/HEAD مهمان در صف می‌رود. خرید، پرداخت، ورود، پنل مدیریت و هر درخواست کاربر واردشده هرگز معطل نمی‌شود.</item>
/// <item>اگر صف هم پر شد یا انتظار طولانی شد: آخرین نسخهٔ سالم صفحه (۲۰۰)؛ اگر نبود، صفحهٔ کوتاه «چند ثانیهٔ دیگر» (۵۰۳ با
/// Retry-After که گوگل آن را موقت می‌داند) که خودش دوباره تلاش می‌کند.</item>
/// <item>ربات‌های پرمصرف (<see cref="HeavyCrawlers"/>) آهسته می‌شوند.</item>
/// </list>
/// <para>جای قرارگیری: Program.cs، بعد از فایل‌های ایستا (فایل‌های ایستا شمرده نمی‌شوند).</para>
/// </summary>
public sealed class LoadSheddingMiddleware(RequestDelegate next, PageSnapshotStore snapshots)
{
    /// <summary>صفحهٔ کوتاه «کمی شلوغ است» (بدون اسکریپت؛ با meta refresh خودش دوباره تلاش می‌کند).</summary>
    public static readonly byte[] BusyPage = Encoding.UTF8.GetBytes("""
        <!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta http-equiv="refresh" content="8"><meta name="robots" content="noindex"><title>چند لحظه صبر کنید</title>
        <style>body{font-family:Tahoma,sans-serif;background:#fcfcfc;color:#1f2937;display:flex;min-height:100vh;align-items:center;justify-content:center;margin:0}
        div{max-width:420px;padding:24px;text-align:center}h1{font-size:20px}p{color:#6b7280;line-height:1.9}</style></head>
        <body><div><h1>فروشگاه کمی شلوغ است</h1><p>درخواست شما در صف است و این صفحه تا چند ثانیهٔ دیگر خودکار دوباره باز می‌شود.</p></div></body></html>
        """);

    /// <summary>
    /// انتظار آخر برای صفحه‌ای که نسخهٔ ذخیره‌شده ندارد (پیش‌فرض ۳۰ ثانیه). مثال: صف پر است و «/search?q=…» نسخهٔ ذخیره‌شده
    /// ندارد → به‌جای ۵۰۳ فوری تا ۳۰ ثانیهٔ دیگر برای یک جای خالی صبر می‌کند (درخواست منتظر تقریباً حافظه‌ای نمی‌گیرد).
    /// </summary>
    public TimeSpan LastResortWait { get; set; } = TimeSpan.FromSeconds(30);

    public Task InvokeAsync(HttpContext http) => HandleAsync(http, ResourceGovernor.Current);

    /// <summary>پردازش یک درخواست با مدیر منابع مشخص (آزمون‌ها مدیر مستقل خودشان را می‌دهند).</summary>
    public async Task HandleAsync(HttpContext http, ResourceGovernor governor)
    {
        var request = http.Request;

        // اتصال‌های بلندمدت (SignalR) و بررسی سلامت شمرده نمی‌شوند.
        // دور ۱۹ (A31): انتظار طولانی «دستیار محلی» (/api/helper/next تا ۲۵ ثانیه بی‌کار منتظر کار است) جای صف صفحه‌ها را نمی‌گیرد.
        if (request.Path.StartsWithSegments("/hubs") || request.Path.StartsWithSegments("/health") || request.Path.StartsWithSegments("/healthz")
            || request.Path.StartsWithSegments("/api/helper"))
        {
            await next(http);
            return;
        }

        var crawler = HeavyCrawlers.Match(request.Headers.UserAgent.ToString());
        if (crawler is not null && !governor.IsUnlimited && HeavyCrawlers.ShouldThrottle(crawler, governor.Pressure, DateTimeOffset.UtcNow))
        {
            RequestMonitor.Record(http, RequestMonitor.Kind.CrawlerThrottled, crawler, StatusCodes.Status429TooManyRequests);
            // صفحهٔ خطای کامل برای ربات ساخته نشود (همین ۴۲۹ خالی کافی است).
            if (http.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } statusPages)
                statusPages.Enabled = false;
            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            http.Response.Headers.RetryAfter = governor.Pressure >= PressureLevel.Elevated ? "600" : "30";
            http.Response.Headers.CacheControl = "no-store";
            return;
        }

        var queueable = !governor.IsUnlimited && PageSnapshotStore.IsCandidate(request);
        governor.IncrementActive();
        try
        {
            if (!queueable)
            {
                await next(http);
                return;
            }

            // حافظه در خط قرمز (۹۲٪ سقف یا بیشتر): ساختن صفحهٔ تازه حافظه می‌خواهد؛ اگر نسخهٔ ذخیره‌شدهٔ همین صفحه هست همان
            // داده می‌شود (۲۰۰) تا برنامه به سقف حافظه نخورد و کشته نشود. صفحهٔ بدون نسخهٔ ذخیره‌شده عادی ساخته می‌شود.
            if (MemoryIsCritical(governor) && await snapshots.TryServeAsync(http, "memory"))
            {
                RequestMonitor.Record(http, RequestMonitor.Kind.Snapshot, "حافظهٔ سرور در خط قرمز", StatusCodes.Status200OK);
                return;
            }

            var slots = governor.RequestSlots;
            if (slots.Wait(0))
            {
                try
                {
                    await next(http);
                }
                finally
                {
                    slots.Release();
                }
                return;
            }

            // ظرفیت پر است: صف کوتاه.
            var acquired = false;
            if (governor.IncrementQueued() <= governor.Budget.QueueLength)
            {
                try
                {
                    acquired = await slots.WaitAsync(governor.Budget.QueueWait, http.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    // مرورگر قطع کرد.
                    governor.DecrementQueued();
                    return;
                }
            }
            governor.DecrementQueued();

            if (!acquired)
            {
                // نسخهٔ ذخیره‌شده هست → همان (۲۰۰، فوری).
                if (await snapshots.TryServeAsync(http, "busy"))
                {
                    RequestMonitor.Record(http, RequestMonitor.Kind.Snapshot, "شلوغی بیش از ظرفیت", StatusCodes.Status200OK);
                    return;
                }

                // نسخهٔ ذخیره‌شده نیست → آخرین فرصت: کمی بیشتر صبر برای جای خالی، به‌جای ۵۰۳.
                governor.IncrementQueued();
                try
                {
                    acquired = await slots.WaitAsync(LastResortWait, http.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    governor.DecrementQueued();
                }
            }

            if (acquired)
            {
                try
                {
                    await next(http);
                }
                finally
                {
                    slots.Release();
                }
                return;
            }

            await RejectAsync(http);
        }
        finally
        {
            governor.DecrementActive();
        }
    }

    /// <summary>
    /// حافظه در خط قرمز است؟ دو حالت:
    /// <list type="bullet">
    /// <item>حافظهٔ پروسه ۹۲٪ سقف یا بیشتر. مثال: ۴۸۰ از ۵۱۲ مگ → true؛ صف پر ولی حافظه ۵۰٪ → false.</item>
    /// <item>هیپ GC به ۸۵٪ سقف هیپ (<see cref="MemoryLimits.AppliedHeapLimitBytes"/>) رسیده؛ ساختن صفحهٔ تازه ممکن است
    /// OutOfMemoryException (خطای ۵۰۰) بدهد. مثال: هیپ ۲۵۰ از سقف ۲۸۲ مگ → true.</item>
    /// </list>
    /// </summary>
    public static bool MemoryIsCritical(ResourceGovernor governor) =>
        MemoryIsCritical(governor.LastSample, MemoryLimits.AppliedHeapLimitBytes);

    /// <summary>نسخهٔ خالص و آزمون‌پذیر. مثال: <c>MemoryIsCritical(sample(heap: 250), 282 مگ)</c> → true.</summary>
    public static bool MemoryIsCritical(ResourceSample sample, long? heapLimitBytes) =>
        sample.MemoryRatio >= 0.92
        || (heapLimitBytes is { } cap && cap > 0 && sample.GcHeapMb * 1024 * 1024 >= cap * 0.85);

    private async Task RejectAsync(HttpContext http)
    {
        if (await snapshots.TryServeAsync(http, "busy"))
        {
            RequestMonitor.Record(http, RequestMonitor.Kind.Snapshot, "شلوغی بیش از ظرفیت", StatusCodes.Status200OK);
            return;
        }

        RequestMonitor.Record(http, RequestMonitor.Kind.Overloaded, null, StatusCodes.Status503ServiceUnavailable);
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        http.Response.Headers.RetryAfter = "10";
        http.Response.Headers.CacheControl = "no-store";
        http.Response.ContentType = "text/html; charset=utf-8";
        http.Response.ContentLength = BusyPage.Length;
        if (!HttpMethods.IsHead(http.Request.Method))
            await http.Response.Body.WriteAsync(BusyPage, http.RequestAborted);
    }
}
