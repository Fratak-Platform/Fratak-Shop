using System.Collections.Concurrent;
using System.Text.Json;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Marketing;

/// <summary>
/// شمارندهٔ نمایش و کلیک بنرها در حافظه (دور ۲۳ – آزمون A/B). هر نمایش/کلیک فقط یک عدد در حافظه را زیاد می‌کند و
/// هر ۳ دقیقه یک‌جا با ExecuteUpdate در پایگاه داده افزوده می‌شود؛ پس ۱۰ هزار بازدید روزانه حتی یک INSERT اضافه هم نمی‌سازد.
/// <para>محافظ‌ها: هر IP برای هر بنر هر ۳۰ دقیقه فقط یک نمایش و یک کلیک حساب می‌شود (رفرش پشت سر هم آمار را خراب نمی‌کند)؛
/// ربات‌ها شمرده نمی‌شوند؛ حداکثر ۲۰۰۰ بنر متفاوت در حافظه (شناسه‌های جعلی حافظه را پر نمی‌کنند).</para>
/// <para>ثبت: Singleton. استفاده: <see cref="BannerEndpoints"/> و <see cref="BannerStatsFlusher"/>.</para>
/// </summary>
public sealed class BannerStats(IMemoryCache cache)
{
    /// <summary>بیشترین بنر متفاوت در بافر.</summary>
    public const int MaxEntries = 2000;

    private static readonly TimeSpan Dedupe = TimeSpan.FromMinutes(30);
    private ConcurrentDictionary<Guid, int[]> counts = new();

    /// <summary>
    /// ثبت یک نمایش یا کلیک. خروجی: پذیرفته شد؟ (تکراری همان IP، ربات یا بافر پر ← false).
    /// مثال: Record(id, "1.2.3.4", click: false) دو بار پشت سر هم ← true سپس false.
    /// </summary>
    public bool Record(Guid bannerId, string? visitor, bool click)
    {
        if (bannerId == Guid.Empty)
            return false;
        var key = $"banner:{(click ? 'c' : 'v')}:{bannerId:N}:{visitor ?? "-"}";
        if (cache.TryGetValue(key, out _))
            return false;
        cache.Set(key, true, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Dedupe, Size = 1 });

        var map = counts;
        if (!map.ContainsKey(bannerId) && map.Count >= MaxEntries)
            return false;
        var slot = map.GetOrAdd(bannerId, _ => new int[2]);
        Interlocked.Increment(ref slot[click ? 1 : 0]);
        return true;
    }

    /// <summary>برداشتن همهٔ شمارش‌ها و خالی کردن بافر (برای ذخیره).</summary>
    public Dictionary<Guid, (int Views, int Clicks)> Drain()
    {
        var old = Interlocked.Exchange(ref counts, new ConcurrentDictionary<Guid, int[]>());
        return old.ToDictionary(p => p.Key, p => (Volatile.Read(ref p.Value[0]), Volatile.Read(ref p.Value[1])));
    }

    /// <summary>تعداد بنرهای منتظر ذخیره (برای آزمون).</summary>
    public int Pending => counts.Count;
}

/// <summary>
/// ذخیرهٔ دوره‌ای شمارش بنرها (هر ۳ دقیقه و هنگام خاموش شدن برنامه). خطا فقط ثبت می‌شود و شمارش آن دوره از دست می‌رود
/// (آمار تقریبی است؛ هرگز درخواست کاربر را کند نمی‌کند).
/// </summary>
public sealed class BannerStatsFlusher(IServiceScopeFactory scopeFactory, BannerStats stats, ILogger<BannerStatsFlusher> logger) : BackgroundService
{
    public const string JobName = "ذخیرهٔ آمار بنرها";
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(JobName, TimeSpan.FromMinutes(15), pausable: false);
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await FlushAsync(timeout.Token);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var batch = stats.Drain();
        if (batch.Count == 0)
            return;
        try
        {
            await JobMonitor.TrackAsync(JobName, async () =>
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IBannerService>().AddStatsAsync(batch, cancellationToken);
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Banner stats flush failed ({Count} banners)", batch.Count);
        }
    }
}

/// <summary>
/// مسیرهای آمار بنر (دور ۲۳):
/// <list type="bullet">
/// <item><c>POST /api/banners/track</c> – بدنهٔ کوچک <c>{"v":["id",…],"c":["id"]}</c> از <c>assets/js/banners.js</c> (sendBeacon).
/// نمایش وقتی شمرده می‌شود که دست‌کم نیمی از بنر واقعاً دیده شود. حداکثر ۱ کیلوبایت و ۱۲ شناسه.</item>
/// <item><c>GET /b/{id}</c> – لینک قابل اشتراک (پیامک، اینستاگرام): کلیک را می‌شمارد و به لینک امن بنر می‌رود (بنر غیرفعال ← صفحهٔ نخست).</item>
/// </list>
/// <para>بدون کوکی و اطلاعات شخصی؛ پاسخ no-store؛ محدودیت نرخ عمومی /api.</para>
/// </summary>
public static class BannerEndpoints
{
    public const int MaxBody = 1024;
    public const int MaxIds = 12;

    public static void MapBannerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/banners/track", async (HttpContext http, BannerStats stats) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (http.Request.ContentLength is > MaxBody || TrafficRules.IsBot(ClientInfo.UserAgent(http)))
                return Results.NoContent();
            var buffer = new byte[MaxBody + 1];
            var total = 0;
            int read;
            while (total <= MaxBody && (read = await http.Request.Body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), http.RequestAborted)) > 0)
                total += read;
            if (total > MaxBody)
                return Results.NoContent();
            Accept(System.Text.Encoding.UTF8.GetString(buffer, 0, total), stats, ClientInfo.IpAddress(http));
            return Results.NoContent();
        }).DisableAntiforgery();

        app.MapGet("/b/{id:guid}", async (Guid id, HttpContext http, IBannerService banners, BannerStats stats) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            var target = await banners.GetClickTargetAsync(id);
            if (target is not null && !TrafficRules.IsBot(ClientInfo.UserAgent(http)))
                stats.Record(id, ClientInfo.IpAddress(http), click: true);
            return Results.Redirect(target ?? "/", permanent: false);
        });
    }

    /// <summary>
    /// پذیرش بدنه. خروجی = تعداد رویدادهای پذیرفته‌شده (بدنهٔ خراب ← ۰). مثال: {"v":["…"],"c":[]} ← ۱.
    /// </summary>
    public static int Accept(string? body, BannerStats stats, string? visitor)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBody)
            return 0;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return 0;
            var accepted = 0;
            var seen = 0;
            foreach (var (name, click) in new[] { ("v", false), ("c", true) })
            {
                if (!document.RootElement.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var item in list.EnumerateArray())
                {
                    if (++seen > MaxIds)
                        return accepted;
                    if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id) && stats.Record(id, visitor, click))
                        accepted++;
                }
            }
            return accepted;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
