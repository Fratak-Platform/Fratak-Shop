using System.Collections.Concurrent;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Analytics;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Analytics;

// ═══════════════════════════════════════════════════════════════════════════
//  آمار بازدید داخلی (مرحلهٔ هفتم – فاز ۷) – بخش وب
//  جریان کار:
//    ۱) engagement.js بعد از بارگذاری هر صفحهٔ فروشگاه یک درخواست کوچک POST /api/a/hit می‌فرستد.
//    ۲) این درخواست در AnalyticsBuffer (حافظه) جمع می‌شود؛ هیچ کوئری دیتابیسی در لحظه اجرا نمی‌شود.
//    ۳) AnalyticsFlushWorker هر دقیقه بافر را خالی و با IAnalyticsService در دیتابیس ادغام می‌کند.
//    ۴) صفحه‌های سبد خرید و تسویه حساب با AnalyticsTracker مرحلهٔ قیف را ثبت می‌کنند و منبع سفارش را روی آن می‌نویسند.
//  حریم خصوصی: IP ذخیره نمی‌شود؛ شناسهٔ بازدیدکننده یک عدد تصادفی در کوکی fr_vid است.
//  ربات‌ها، صفحه‌های پنل مدیریت و درخواست‌های بیش از حد شمرده نمی‌شوند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// کوکی‌های آمار: fr_vid (شناسهٔ تصادفی بازدیدکننده، ۱ سال) و fr_src (آخرین منبع غیرمستقیم، ۳۰ روز).
/// <para>فقط روی پاسخ‌های غیرقابل‌کش (POST beacon) نوشته می‌شوند تا کش صفحه هرگز کوکی شخص دیگری را پخش نکند.</para>
/// </summary>
public static class AnalyticsCookies
{
    public const string VisitorCookie = "fr_vid";
    public const string SourceCookie = "fr_src";

    /// <summary>مدت اعتبار منبع برای نسبت دادن سفارش. مثال: از اینستاگرام آمده و ۱۰ روز بعد مستقیم خرید کرده → اینستاگرام.</summary>
    public static readonly TimeSpan SourceLifetime = TimeSpan.FromDays(30);

    /// <summary>شناسهٔ بازدیدکنندهٔ فعلی یا null (کوکی ندارد یا دستکاری شده).</summary>
    public static string? ReadVisitor(HttpContext http)
    {
        var value = http.Request.Cookies[VisitorCookie];
        return TrafficRules.IsValidVisitorKey(value) ? value : null;
    }

    /// <summary>شناسهٔ فعلی؛ اگر نبود یکی می‌سازد و کوکی را می‌نویسد. خروجی دوم: آیا بازدیدکنندهٔ تازه است.</summary>
    public static (string Key, bool IsNew) EnsureVisitor(HttpContext http)
    {
        if (ReadVisitor(http) is { } existing)
            return (existing, false);
        var key = TrafficRules.NewVisitorKey();
        http.Response.Cookies.Append(VisitorCookie, key, Options(http, TimeSpan.FromDays(365)));
        return (key, true);
    }

    /// <summary>آخرین منبع غیرمستقیم (برای نسبت دادن سفارش) یا null.</summary>
    public static TrafficTouch? ReadSource(HttpContext http) => TrafficRules.DecodeTouch(http.Request.Cookies[SourceCookie]);

    public static void WriteSource(HttpContext http, TrafficTouch touch) =>
        http.Response.Cookies.Append(SourceCookie, TrafficRules.EncodeTouch(touch), Options(http, SourceLifetime));

    private static CookieOptions Options(HttpContext http, TimeSpan lifetime) => new()
    {
        HttpOnly = true,
        IsEssential = true,
        Secure = http.Request.IsHttps,
        SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
        MaxAge = lifetime,
        Path = "/",
    };
}

/// <summary>
/// بافر حافظهٔ آمار (Singleton). همهٔ متدها thread-safe و بسیار سریع‌اند (فقط به‌روزرسانی دیکشنری).
/// <para>برای جلوگیری از پر شدن حافظه در حملهٔ ربات، حداکثر <see cref="MaxVisitors"/> بازدیدکنندهٔ تازه بین دو ذخیره نگه داشته می‌شود.</para>
/// </summary>
public sealed class AnalyticsBuffer
{
    public const int MaxVisitors = 50_000;
    public const int MaxCounters = 50_000;

    /// <summary>خلاصهٔ در حال جمع شدن یک بازدیدکننده (قفل روی خود شیء).</summary>
    private sealed class Pending
    {
        public required DateOnly Day;
        public required string Key;
        public Guid? UserId;
        public int Stages;
        public int PageViews;
        public required byte FirstHour;
        public required string Device;
        public required TrafficTouch Touch;
        public string? LandingPath;
        public required DateTimeOffset FirstSeenAt;
        public DateTimeOffset LastSeenAt;
    }

    private ConcurrentDictionary<(DateOnly, string), Pending> visitors = new();
    private ConcurrentDictionary<(DateOnly, string, string), StrongBox> counters = new();

    private sealed class StrongBox
    {
        public int Value;
    }

    /// <summary>تعداد بازدیدکننده‌های در صف ذخیره (برای صفحهٔ وضعیت فنی).</summary>
    public int PendingVisitors => visitors.Count;

    /// <summary>
    /// ثبت بازدید یک صفحه (از beacon). منبع، صفحهٔ ورود و دستگاه از «اولین» بازدید روز می‌ماند.
    /// </summary>
    /// <param name="stages">مراحل اضافهٔ این درخواست؛ مثلاً صفحهٔ محصول → ProductView.</param>
    public void Visit(string key, Guid? userId, string device, TrafficTouch touch, string? landingPath, FunnelStage stages, DateTimeOffset now) =>
        Update(key, userId, now, (int)(stages | FunnelStage.Visit), pageViews: 1, device, touch, landingPath);

    /// <summary>
    /// ثبت یک مرحلهٔ قیف بدون بازدید صفحه (افزودن به سبد، ورود به تسویه). مثال: Stage(key, userId, FunnelStage.AddToCart).
    /// اگر برای امروز رکوردی در بافر نبود با منبع «مستقیم» ساخته می‌شود؛ هنگام ادغام، منبع رکورد موجود دیتابیس حفظ می‌شود.
    /// </summary>
    public void Stage(string key, Guid? userId, FunnelStage stage, DateTimeOffset now) =>
        Update(key, userId, now, (int)stage, pageViews: 0, "desktop", TrafficTouch.Direct, null);

    /// <summary>افزایش شمارندهٔ روزانه. مثال: Count(AnalyticsMetrics.ProductView, productId.ToString("N")).</summary>
    public void Count(string metric, string key, DateTimeOffset now, int amount = 1)
    {
        var map = counters;
        var id = (IranClock.Today(now), metric, key);
        if (!map.TryGetValue(id, out var box))
        {
            if (map.Count >= MaxCounters)
                return;
            box = map.GetOrAdd(id, _ => new StrongBox());
        }
        Interlocked.Add(ref box.Value, amount);
    }

    private void Update(string key, Guid? userId, DateTimeOffset now, int stages, int pageViews, string device, TrafficTouch touch, string? landingPath)
    {
        var map = visitors;
        var local = IranClock.ToLocal(now);
        var day = DateOnly.FromDateTime(local.DateTime);
        if (!map.TryGetValue((day, key), out var pending))
        {
            if (map.Count >= MaxVisitors)
                return;
            pending = map.GetOrAdd((day, key), _ => new Pending
            {
                Day = day, Key = key, FirstHour = (byte)local.Hour, Device = device, Touch = touch,
                LandingPath = landingPath, FirstSeenAt = now, LastSeenAt = now,
            });
        }

        lock (pending)
        {
            pending.Stages |= stages;
            pending.PageViews += pageViews;
            pending.UserId ??= userId;
            pending.LandingPath ??= landingPath;
            if (now > pending.LastSeenAt)
                pending.LastSeenAt = now;
        }
    }

    /// <summary>
    /// خالی کردن بافر و برگرداندن یک بسته برای ذخیره. دیکشنری‌ها اتمی جایگزین می‌شوند تا درخواست‌های هم‌زمان گم نشوند
    /// (درخواستی که در همان لحظه به دیکشنری قدیمی نوشته، تا پایان این متد هنوز در آن است؛ یک مکث کوتاه این را پوشش می‌دهد).
    /// </summary>
    public async Task<AnalyticsBatch> DrainAsync()
    {
        var oldVisitors = Interlocked.Exchange(ref visitors, new ConcurrentDictionary<(DateOnly, string), Pending>());
        var oldCounters = Interlocked.Exchange(ref counters, new ConcurrentDictionary<(DateOnly, string, string), StrongBox>());
        await Task.Delay(50);

        var visitorHits = oldVisitors.Values.Select(p =>
        {
            lock (p)
                return new VisitorHit(p.Day, p.Key, p.UserId, p.Stages, p.PageViews, p.FirstHour, p.Device, p.Touch, p.LandingPath, p.FirstSeenAt, p.LastSeenAt);
        }).ToList();
        var counterHits = oldCounters.Select(c => new CounterHit(c.Key.Item1, c.Key.Item2, c.Key.Item3, Volatile.Read(ref c.Value.Value)))
            .Where(c => c.Count > 0).ToList();
        return new AnalyticsBatch(visitorHits, counterHits);
    }
}

/// <summary>
/// ابزار صفحه‌ها برای ثبت مراحل قیف و منبع سفارش (Scoped). همهٔ متدها «بهترین تلاش»اند و هرگز خطا به کاربر نمی‌دهند.
/// <para>استفاده: Pages/Cart (افزودن به سبد)، Pages/Checkout (ورود به تسویه و ثبت سفارش).</para>
/// </summary>
public sealed class AnalyticsTracker(AnalyticsBuffer buffer, IStoreSettingService settings, IAnalyticsService analytics, ILogger<AnalyticsTracker> logger)
{
    /// <summary>ثبت مرحلهٔ قیف برای بازدیدکنندهٔ فعلی (اگر کوکی شناسه دارد). مثال: await tracker.StageAsync(HttpContext, FunnelStage.Checkout).</summary>
    public async Task StageAsync(HttpContext http, FunnelStage stage, Guid? productId = null)
    {
        try
        {
            if (!(await settings.GetAsync(http.RequestAborted)).IsAnalyticsEnabled)
                return;
            var now = DateTimeOffset.UtcNow;
            if (productId is { } id && stage == FunnelStage.AddToCart)
                buffer.Count(AnalyticsMetrics.AddToCart, id.ToString("N"), now);
            if (AnalyticsCookies.ReadVisitor(http) is { } key && !TrafficRules.IsBot(ClientInfo.UserAgent(http)))
                buffer.Stage(key, http.User.GetUserId(), stage, now);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Analytics stage {Stage} was not recorded", stage);
        }
    }

    /// <summary>
    /// نوشتن منبع ورودی (کوکی fr_src، وگرنه «مستقیم») و شناسهٔ بازدیدکننده روی سفارش تازه.
    /// مثال: بعد از CreateOrderAsync موفق → await tracker.AttributeOrderAsync(HttpContext, order.Id).
    /// </summary>
    public async Task AttributeOrderAsync(HttpContext http, Guid orderId)
    {
        try
        {
            if (!(await settings.GetAsync(http.RequestAborted)).IsAnalyticsEnabled)
                return;
            await analytics.AttributeOrderAsync(orderId, AnalyticsCookies.ReadVisitor(http), AnalyticsCookies.ReadSource(http), http.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Order {OrderId} traffic source was not recorded", orderId);
        }
    }
}

/// <summary>
/// سرویس پس‌زمینه: هر ۶۰ ثانیه بافر را در دیتابیس ذخیره می‌کند و روزی یک بار داده‌های قدیمی‌تر از «مدت نگهداری» را پاک می‌کند.
/// هنگام خاموش شدن برنامه هم آخرین بافر ذخیره می‌شود.
/// </summary>
public sealed class AnalyticsFlushWorker(IServiceScopeFactory scopeFactory, AnalyticsBuffer buffer, ILogger<AnalyticsFlushWorker> logger) : BackgroundService
{
    public const string JobName = "ذخیرهٔ آمار بازدید";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private DateTimeOffset lastPurge = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(JobName, TimeSpan.FromMinutes(5));
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await FlushAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // خاموش شدن برنامه.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        // ذخیرهٔ آخرین بافر با حداکثر ۱۰ ثانیه مهلت.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await FlushAsync(timeout.Token);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            var batch = await buffer.DrainAsync();
            var purge = DateTimeOffset.UtcNow - lastPurge > TimeSpan.FromHours(24);
            if (batch.IsEmpty && !purge)
                return;

            await JobMonitor.TrackAsync(JobName, async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var analytics = scope.ServiceProvider.GetRequiredService<IAnalyticsService>();
                await analytics.SaveAsync(batch, cancellationToken);
                if (purge)
                {
                    lastPurge = DateTimeOffset.UtcNow;
                    var settings = await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync(cancellationToken);
                    var removed = await analytics.PurgeAsync(settings.AnalyticsRetentionDays, cancellationToken);
                    if (removed > 0)
                        logger.LogInformation("Analytics purge removed {Count} old visitor rows", removed);
                }
                return batch.Visitors.Count + batch.Counters.Count;
            });
        }
        catch (OperationCanceledException)
        {
            // خاموش شدن برنامه.
        }
        catch (Exception exception)
        {
            // دادهٔ این دقیقه از دست می‌رود ولی سایت هرگز به خاطر آمار مختل نمی‌شود.
            logger.LogError(exception, "Saving analytics failed");
        }
    }
}

/// <summary>
/// نقطهٔ پایانی beacon: POST /api/a/hit با بدنهٔ JSON مثل {"p":"/product/x","q":"?utm_source=insta","r":"https://t.me/","pid":"…"}.
/// پاسخ همیشه 204 بدون محتوا و no-store است (حتی وقتی چیزی ثبت نشد) تا رفتار برای ربات‌ها قابل تشخیص نباشد.
/// </summary>
public static class AnalyticsEndpoints
{
    /// <summary>حداکثر درخواست هر بازدیدکننده در دقیقه؛ بیشتر از آن نادیده گرفته می‌شود.</summary>
    private const int MaxHitsPerMinute = 60;

    public sealed record HitInput(string? P, string? Q, string? R, string? Pid);

    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/a/hit", async (HttpContext http, HitInput? input, AnalyticsBuffer buffer, IStoreSettingService settings, IMemoryCache cache) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var userAgent = ClientInfo.UserAgent(http);
            if (input is null || TrafficRules.IsBot(userAgent) || !(await settings.GetAsync(http.RequestAborted)).IsAnalyticsEnabled)
                return Results.NoContent();

            var path = CleanPath(input.P);
            if (path is null || path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                return Results.NoContent();

            var (key, _) = AnalyticsCookies.EnsureVisitor(http);
            if (!AllowHit(cache, key))
                return Results.NoContent();

            var touch = TrafficRules.Classify(Cut(input.Q, 2000), Cut(input.R, 1000), http.Request.Host.Host);
            if (!touch.IsDirect || touch.Campaign is not null)
                AnalyticsCookies.WriteSource(http, touch);

            var now = DateTimeOffset.UtcNow;
            var stages = FunnelStage.Visit;
            if (Guid.TryParse(input.Pid, out var productId))
            {
                stages |= FunnelStage.ProductView;
                // هر بازدیدکننده هر محصول را در ۳۰ دقیقه فقط یک بار «بازدید» حساب می‌کند (رفرش پشت سر هم شمرده نمی‌شود).
                var viewKey = $"a:pv:{key}:{productId:N}";
                if (!cache.TryGetValue(viewKey, out _))
                {
                    cache.Set(viewKey, true, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30), Size = 1 });
                    buffer.Count(AnalyticsMetrics.ProductView, productId.ToString("N"), now);
                }
            }

            buffer.Visit(key, http.User.GetUserId(), TrafficRules.Device(userAgent), touch, path, stages, now);
            return Results.NoContent();
        }).WithMetadata(new RequestSizeLimitAttribute(8 * 1024));
    }

    /// <summary>محدودیت ساده: حداکثر ۶۰ ثبت در دقیقه برای هر بازدیدکننده.</summary>
    private static bool AllowHit(IMemoryCache cache, string key)
    {
        var counter = cache.GetOrCreate($"a:rate:{key}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            entry.Size = 1;
            return new int[1];
        })!;
        return Interlocked.Increment(ref counter[0]) <= MaxHitsPerMinute;
    }

    /// <summary>
    /// مسیر صفحه بدون پرس‌وجو و حداکثر ۳۰۰ نویسه. مثال: «/product/abc?x=1» → «/product/abc». مسیر نامعتبر → null.
    /// </summary>
    public static string? CleanPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal))
            return null;
        var end = path.IndexOfAny(['?', '#']);
        if (end >= 0)
            path = path[..end];
        // نشانی‌های فارسی رمزگذاری‌شده‌اند (%D8%A7…)؛ برای خوانایی در گزارش برگردانده می‌شوند.
        path = Uri.UnescapeDataString(path);
        path = path.Length > 1 ? path.TrimEnd('/') : path;
        return path.Length > 300 ? path[..300] : path;
    }

    private static string? Cut(string? value, int max) => value is null ? null : value.Length > max ? value[..max] : value;
}
