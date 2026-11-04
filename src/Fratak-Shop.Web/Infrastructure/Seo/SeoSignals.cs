using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Seo;

/// <summary>
/// بافر حافظه‌ای سیگنال‌های سئو (موارد ۷۷ و ۱۳۸ تا ۱۴۹): بازدید ربات‌های موتور جست‌وجو و سرعت واقعی کاربران (Core Web Vitals).
/// <para>چرا بافر؟ ثبت هر درخواست در دیتابیس سایت را کند می‌کند؛ اینجا فقط در حافظه جمع می‌شود و کارگر خلبان خودکار هر ۵ دقیقه
/// یک‌جا (به صورت تجمیعی: یک ردیف برای هر روز/ربات/مسیر) در دیتابیس می‌نویسد.</para>
/// <para>سقف‌ها: حداکثر ۲۰۰۰۰ کلید ربات و ۵۰۰۰ نمونهٔ سرعت در هر بازه؛ بیشتر از آن دور ریخته می‌شود (حمله نمی‌تواند حافظه را پر کند).</para>
/// <para>ثبت: Singleton. استفاده: <see cref="BotLogMiddleware"/>، مسیر <c>/api/rum</c> و <see cref="SeoAutopilotWorker"/>.</para>
/// </summary>
public sealed class SeoSignalBuffer
{
    private const int MaxBotKeys = 20_000;
    private const int MaxVitalSamples = 5_000;

    /// <summary>کلید تجمیع بازدید ربات.</summary>
    public readonly record struct BotKey(DateOnly Date, string Bot, string Path, int Status);

    /// <summary>جمع بازدیدهای یک کلید (آی‌پی‌ها برای تأیید DNS؛ حداکثر ۵ آی‌پی متفاوت).</summary>
    public sealed class BotTally
    {
        public int Hits;
        public long TotalMs;
        public DateTimeOffset LastAt;
        public readonly ConcurrentDictionary<string, int> Ips = new(StringComparer.Ordinal);
    }

    /// <summary>یک نمونهٔ سرعت.</summary>
    public readonly record struct VitalSample(DateOnly Date, string PageType, string Device, string Metric, double Value);

    private ConcurrentDictionary<BotKey, BotTally> _bots = new();
    private ConcurrentQueue<VitalSample> _vitals = new();
    private int _vitalCount;

    /// <summary>ثبت ربات روشن است؟ (کارگر از تنظیمات به‌روز می‌کند.)</summary>
    public volatile bool BotLogEnabled = true;

    /// <summary>جمع‌آوری سرعت روشن است؟</summary>
    public volatile bool VitalsEnabled = true;

    /// <summary>ثبت یک بازدید ربات. مسیر به ۳۰۰ نویسه بریده می‌شود؛ کوئری ثبت نمی‌شود.</summary>
    public void RecordBot(KnownBot bot, string path, int status, long milliseconds, IPAddress? ip, DateTimeOffset now)
    {
        if (!BotLogEnabled)
            return;
        var date = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(3.5)).DateTime);
        var key = new BotKey(date, bot.Name, path.Length > 300 ? path[..300] : path, status);
        if (!_bots.TryGetValue(key, out var tally))
        {
            if (_bots.Count >= MaxBotKeys)
                return;
            tally = _bots.GetOrAdd(key, _ => new BotTally());
        }

        Interlocked.Increment(ref tally.Hits);
        Interlocked.Add(ref tally.TotalMs, Math.Max(0, milliseconds));
        tally.LastAt = now;
        if (ip is not null && tally.Ips.Count < 5)
            tally.Ips.AddOrUpdate(ip.ToString(), 1, (_, n) => n + 1);
    }

    /// <summary>ثبت یک نمونهٔ سرعت معتبر (نامعتبر/خارج از بازه نادیده گرفته می‌شود).</summary>
    public bool RecordVital(string pageType, string device, string metric, double value, DateTimeOffset now)
    {
        if (!VitalsEnabled || !SeoVitals.IsValid(metric, value) || !SeoVitals.PageTypes.Contains(pageType) || device is not ("mobile" or "desktop"))
            return false;
        if (Interlocked.Increment(ref _vitalCount) > MaxVitalSamples)
        {
            Interlocked.Decrement(ref _vitalCount);
            return false;
        }

        var date = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(3.5)).DateTime);
        _vitals.Enqueue(new VitalSample(date, pageType, device, metric, value));
        return true;
    }

    /// <summary>برداشتن همهٔ داده‌های جمع‌شده (بافر خالی می‌شود).</summary>
    public (IReadOnlyDictionary<BotKey, BotTally> Bots, IReadOnlyList<VitalSample> Vitals) Drain()
    {
        var bots = Interlocked.Exchange(ref _bots, new ConcurrentDictionary<BotKey, BotTally>());
        var vitals = Interlocked.Exchange(ref _vitals, new ConcurrentQueue<VitalSample>());
        Interlocked.Exchange(ref _vitalCount, 0);
        return (bots, vitals.ToList());
    }
}

/// <summary>
/// ثبت بازدید ربات‌های موتور جست‌وجو (گوگل‌بات، بینگ‌بات، ترب…) – فقط صفحه‌ها (نه فایل‌های ایستا).
/// <para>هزینه: یک جست‌وجوی رشته در User-Agent؛ برای مرورگرهای معمولی هیچ کاری انجام نمی‌شود.</para>
/// <para>ثبت: Program.cs بعد از فایل‌های ایستا.</para>
/// </summary>
public sealed class BotLogMiddleware(RequestDelegate next, SeoSignalBuffer buffer)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var bot = buffer.BotLogEnabled ? SeoBots.Detect(context.Request.Headers.UserAgent.ToString()) : null;
        if (bot is null || !IsPage(context.Request.Path))
        {
            await next(context);
            return;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            buffer.RecordBot(bot, context.Request.Path.Value ?? "/", context.Response.StatusCode, elapsed, context.Connection.RemoteIpAddress, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>صفحه یا فایل سئو (نه asset، api، پنل). مثال: «/product/a» ← true، «/assets/js/a.js» ← false.</summary>
    public static bool IsPage(PathString path)
    {
        var value = path.Value ?? "/";
        if (value.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/media/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
            return false;
        var extension = System.IO.Path.GetExtension(value);
        return extension.Length == 0 || extension is ".xml" or ".txt";
    }
}

/// <summary>
/// نوشتن سیگنال‌های بافر در دیتابیس + تأیید ربات‌های واقعی با DNS معکوس (روش رسمی گوگل و بینگ).
/// <para>ربات «جعلی»: خود را Googlebot معرفی کرده ولی آی‌پی‌اش به googlebot.com/google.com برنمی‌گردد. این‌ها در گزارش
/// جدا شمرده می‌شوند (ربات‌های کپی‌کار معمولاً خود را گوگل معرفی می‌کنند).</para>
/// </summary>
public sealed class SeoSignalWriter(SeoSignalBuffer buffer, ILogger<SeoSignalWriter> logger)
{
    private static readonly ConcurrentDictionary<string, (bool? Verified, DateTimeOffset At)> DnsCache = new(StringComparer.Ordinal);
    private const int MaxDnsLookupsPerFlush = 40;

    /// <summary>نگه‌داری داده‌ها (روز).</summary>
    public const int KeepDays = 120;

    public async Task FlushAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var (bots, vitals) = buffer.Drain();
        if (bots.Count == 0 && vitals.Count == 0)
            return;
        var lookups = 0;
        foreach (var (key, tally) in bots)
        {
            var known = SeoBots.Detect(key.Bot.ToLowerInvariant()) ?? new KnownBot(key.Bot, [], false);
            int verified = 0, fake = 0;
            foreach (var (ip, hits) in tally.Ips)
            {
                var result = await VerifyAsync(known, ip, lookups++ < MaxDnsLookupsPerFlush, cancellationToken);
                if (result == true) verified += hits;
                else if (result == false) fake += hits;
            }

            var row = await context.SeoBotVisits.AsTracking().FirstOrDefaultAsync(v => v.Date == key.Date && v.Bot == key.Bot && v.Path == key.Path && v.Status == key.Status, cancellationToken);
            if (row is null)
            {
                row = new SeoBotVisit { Date = key.Date, Bot = key.Bot, Path = key.Path, Status = key.Status };
                context.SeoBotVisits.Add(row);
            }

            row.Hits += tally.Hits;
            row.TotalMs += tally.TotalMs;
            row.VerifiedHits += verified;
            row.FakeHits += fake;
            row.LastAt = tally.LastAt;
        }

        foreach (var group in vitals.GroupBy(v => (v.Date, v.PageType, v.Device, v.Metric)))
        {
            var values = group.Select(v => v.Value).ToList();
            var p75 = SeoVitals.P75(values);
            var good = values.Count(v => SeoVitals.Rate(group.Key.Metric, v) == 1);
            var poor = values.Count(v => SeoVitals.Rate(group.Key.Metric, v) == -1);
            var row = await context.SeoVitalStats.AsTracking().FirstOrDefaultAsync(v => v.Date == group.Key.Date && v.PageType == group.Key.PageType
                && v.Device == group.Key.Device && v.Metric == group.Key.Metric, cancellationToken);
            if (row is null)
            {
                row = new SeoVitalStat { Date = group.Key.Date, PageType = group.Key.PageType, Device = group.Key.Device, Metric = group.Key.Metric };
                context.SeoVitalStats.Add(row);
            }

            row.P75 = MergeP75(row.P75, row.Samples, p75, values.Count);
            row.Samples += values.Count;
            row.Good += good;
            row.Poor += poor;
            row.AppVersion = typeof(SeoSignalWriter).Assembly.GetName().Version?.ToString();
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // برخورد هم‌زمان (دو سرور) نادر است؛ این دسته از دست می‌رود ولی سایت آسیبی نمی‌بیند.
            logger.LogWarning(exception, "Saving SEO signals failed");
        }

        // پاک‌سازی داده‌های قدیمی (هر بار ارزان: با ایندکس تاریخ).
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-KeepDays));
        await context.SeoBotVisits.Where(v => v.Date < cutoff).ExecuteDeleteAsync(cancellationToken);
        await context.SeoVitalStats.Where(v => v.Date < cutoff).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// ادغام صدک ۷۵ ذخیره‌شده با دستهٔ تازه (میانگین وزنی؛ تقریب قابل‌قبول برای گزارش روزانه).
    /// مثال: (۲۰۰۰ با ۱۰ نمونه، ۳۰۰۰ با ۱۰ نمونه) ← ۲۵۰۰.
    /// </summary>
    public static double MergeP75(double oldP75, int oldSamples, double newP75, int newSamples) =>
        oldSamples <= 0 ? newP75 : newSamples <= 0 ? oldP75 : (oldP75 * oldSamples + newP75 * newSamples) / (oldSamples + newSamples);

    /// <summary>تأیید DNS معکوس + رفت‌وبرگشت. ربات بدون پسوند رسمی ← null (نامعلوم). بدون اجازهٔ جست‌وجوی تازه ← از کش یا null.</summary>
    private async Task<bool?> VerifyAsync(KnownBot bot, string ip, bool allowLookup, CancellationToken cancellationToken)
    {
        if (bot.DnsSuffixes.Length == 0 || !IPAddress.TryParse(ip, out var address))
            return null;
        if (DnsCache.TryGetValue(ip, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromHours(24))
            return cached.Verified;
        if (!allowLookup)
            return null;
        bool? verified;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var entry = await Dns.GetHostEntryAsync(address.ToString(), timeout.Token);
            verified = SeoBots.HostMatches(bot, entry.HostName);
            if (verified == true)
            {
                var forward = await Dns.GetHostAddressesAsync(entry.HostName, timeout.Token);
                verified = forward.Any(a => a.Equals(address));
            }
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or OperationCanceledException or ArgumentException)
        {
            // نام معکوس ندارد = جعلی؛ مهلت تمام شد = نامعلوم.
            verified = exception is System.Net.Sockets.SocketException ? false : null;
        }

        if (DnsCache.Count > 5000)
            DnsCache.Clear();
        DnsCache[ip] = (verified, DateTimeOffset.UtcNow);
        return verified;
    }
}

/// <summary>
/// مسیر دریافت سرعت واقعی کاربران: <c>POST /api/rum</c> با بدنهٔ کوچک JSON از <c>assets/js/rum.js</c>.
/// <para>مثال بدنه: <c>{"t":"product","d":"mobile","m":{"LCP":2100,"CLS":0.02,"INP":120}}</c>. بدون کوکی و بدون اطلاعات شخصی.</para>
/// <para>امنیت: حداکثر ۲ کیلوبایت، فقط شاخص‌ها و مقدارهای مجاز (<see cref="SeoVitals.IsValid"/>)، محدودیت نرخ مسیرهای /api.</para>
/// </summary>
public static class RumEndpoint
{
    public const int MaxBody = 2048;

    public static void MapRum(IEndpointRouteBuilder app) =>
        app.MapPost("/api/rum", async (HttpContext http, SeoSignalBuffer buffer) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!buffer.VitalsEnabled || http.Request.ContentLength is > MaxBody)
                return Results.NoContent();
            var body = await ReadAsync(http.Request.Body, http.RequestAborted);
            Accept(body, buffer, DateTimeOffset.UtcNow);
            return Results.NoContent();
        }).DisableAntiforgery();

    /// <summary>پذیرش بدنه؛ خروجی: تعداد نمونه‌های پذیرفته‌شده (برای تست). بدنهٔ خراب ← ۰.</summary>
    public static int Accept(string? body, SeoSignalBuffer buffer, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBody)
            return 0;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("t", out var type) || type.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("d", out var device) || device.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("m", out var metrics) || metrics.ValueKind != JsonValueKind.Object)
                return 0;
            var accepted = 0;
            foreach (var metric in metrics.EnumerateObject().Take(6))
            {
                if (metric.Value.ValueKind == JsonValueKind.Number && metric.Value.TryGetDouble(out var value)
                    && buffer.RecordVital(type.GetString()!, device.GetString()!, metric.Name, value, now))
                    accepted++;
            }

            return accepted;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static async Task<string?> ReadAsync(Stream body, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxBody + 1];
        var total = 0;
        int read;
        while (total <= MaxBody && (read = await body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)) > 0)
            total += read;
        return total > MaxBody ? null : System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }
}
