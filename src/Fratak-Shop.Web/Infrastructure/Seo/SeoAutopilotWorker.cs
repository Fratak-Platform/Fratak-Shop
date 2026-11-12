using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Threading.Channels;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Media;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Seo;

/// <summary>
/// صف «صفحه‌های تازه ذخیره‌شده» برای خلبان خودکار: شنوندهٔ <see cref="SeoChangeFeed"/> تغییرها را اینجا می‌گذارد.
/// <para>تکراری‌ها یکی می‌شوند (ده بار ذخیرهٔ پشت‌سرهم یک محصول = یک بار پردازش) و سقف ۲۰۰۰ دارد.</para>
/// </summary>
public sealed class SeoAutopilotQueue
{
    private readonly Channel<SeoChange> _channel = Channel.CreateBounded<SeoChange>(new BoundedChannelOptions(2000)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private readonly ConcurrentDictionary<(Data.Entities.SeoEngine.SeoEntityKind, Guid), bool> _pending = new();
    private int _manual;

    public void Enqueue(IReadOnlyList<SeoChange> changes)
    {
        foreach (var change in changes)
        {
            if (_pending.TryAdd((change.Kind, change.Id), change.Added))
                _channel.Writer.TryWrite(change);
        }
    }

    /// <summary>درخواست اجرای فوری کار شبانه (دکمهٔ «اجرای اکنون» پنل). تکرار تا اجرای قبلی تمام نشده بی‌اثر است.</summary>
    public bool RequestNightly() => Interlocked.Exchange(ref _manual, 1) == 0;

    internal bool TakeNightlyRequest() => Interlocked.Exchange(ref _manual, 0) == 1;

    internal ChannelReader<SeoChange> Reader => _channel.Reader;

    internal void Done(SeoChange change) => _pending.TryRemove((change.Kind, change.Id), out _);

    /// <summary>تعداد صفحه‌های منتظر (نمایش در پنل).</summary>
    public int Pending => _pending.Count;
}

/// <summary>
/// کارگر پس‌زمینهٔ سئوی خودکار (موارد ۱۵۰ تا ۱۶۴): سایت بدون کارشناس سئو.
/// <list type="number">
/// <item>هر صفحه‌ای که ذخیره شود (پنل، درون‌ریزی، همگام‌سازی) ۱۰ ثانیه بعد پردازش می‌شود: فیلدهای خالی سئو پر، HTML مرتب،
/// Alt تصویرها نوشته و آدرس به IndexNow (بینگ/یاندکس) اعلام می‌شود.</item>
/// <item>هر ۵ دقیقه سیگنال‌های ربات و سرعت در دیتابیس نوشته می‌شود.</item>
/// <item>هر شب (ساعت تنظیم‌شده به وقت تهران): نامک انگلیسی صفحه‌های قدیمی، تبدیل تصویرهای قدیمی به WebP، کار شبانهٔ
/// خلبان (بک‌لاگ، تأیید امن، بازنویسی داده‌محور، داوری آزمایش عنوان)، خزش کامل سایت و گزارش هفتگی.</item>
/// <item>بار اول (بلافاصله بعد از انتشار) کار شبانه ۳ دقیقه بعد از شروع اجرا می‌شود تا نامک‌ها و تصویرها فوراً اصلاح شوند.</item>
/// </list>
/// <para>همهٔ ذخیره‌های خود کارگر داخل <see cref="SeoChangeFeed.Suppress"/> است (حلقه نمی‌شود). هیچ خطایی برنامه را از کار نمی‌اندازد.</para>
/// </summary>
public sealed class SeoAutopilotWorker(
    IServiceScopeFactory scopeFactory,
    SeoAutopilotQueue queue,
    SeoSignalBuffer signals,
    ILogger<SeoAutopilotWorker> logger) : BackgroundService
{
    public const string JobName = "سئوی خودکار (شبانه)";
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FlushEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(3);

    private DateTimeOffset _lastFlush = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastScheduleCheck = DateTimeOffset.MinValue;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(JobName, TimeSpan.FromHours(26));
        JobMonitor.SetRunNow(JobName, _ => queue.RequestNightly());
        SeoChangeFeed.Saved += OnSaved;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessQueueAsync(stoppingToken);
                    if (DateTimeOffset.UtcNow - _lastFlush >= FlushEvery)
                        await FlushSignalsAsync(stoppingToken);
                    if (queue.TakeNightlyRequest())
                        await RunNightlyAsync(manual: true, stoppingToken);
                    else if (DateTimeOffset.UtcNow - _lastScheduleCheck >= TimeSpan.FromMinutes(10))
                        await RunNightlyIfDueAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "SEO autopilot loop failed");
                }

                try
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    wait.CancelAfter(TimeSpan.FromSeconds(30));
                    await queue.Reader.WaitToReadAsync(wait.Token);
                    await Task.Delay(Debounce, stoppingToken);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            SeoChangeFeed.Saved -= OnSaved;
            // آخرین سیگنال‌ها از دست نروند.
            try
            {
                await FlushSignalsAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Final SEO signal flush failed");
            }
        }
    }

    private void OnSaved(IReadOnlyList<SeoChange> changes) => queue.Enqueue(changes);

    /// <summary>پردازش صفحه‌های منتظر + اعلام IndexNow.</summary>
    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        var batch = new List<SeoChange>();
        while (batch.Count < 100 && queue.Reader.TryRead(out var change))
            batch.Add(change);
        if (batch.Count == 0)
            return;

        var paths = new List<string>();
        using var scope = scopeFactory.CreateScope();
        var autopilot = scope.ServiceProvider.GetRequiredService<ISeoAutopilotService>();
        var provider = scope.ServiceProvider.GetRequiredService<ISeoLinkTargetProvider>();
        foreach (var change in batch)
        {
            try
            {
                var result = await autopilot.ProcessAsync(change.Kind, change.Id, stoppingToken);
                if (result.Path is not null && (result.Changed || change.Added))
                    paths.Add(result.Path);
                if (result.Changed)
                    logger.LogInformation("SEO autopilot {Kind} {Id}: {Summary}", change.Kind, change.Id, string.Join(" | ", result.Summary));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "SEO autopilot failed for {Kind} {Id}", change.Kind, change.Id);
            }
            finally
            {
                queue.Done(change);
            }
        }

        // مقصدهای لینک داخلی (نام/نامک تازه) از نو ساخته شوند.
        provider.Invalidate();
        var options = await provider.OptionsAsync(stoppingToken);
        if (options.Enabled && options.IndexNow && paths.Count > 0)
            await scope.ServiceProvider.GetRequiredService<ISeoPinger>().PingAsync(paths.Distinct(StringComparer.Ordinal).ToList(), stoppingToken);
    }

    private async Task FlushSignalsAsync(CancellationToken stoppingToken)
    {
        _lastFlush = DateTimeOffset.UtcNow;
        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISeoLinkTargetProvider>();
        var options = await provider.OptionsAsync(stoppingToken);
        signals.BotLogEnabled = options.Enabled && options.BotLog;
        signals.VitalsEnabled = options.Enabled && options.RealUserMetrics;
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        await scope.ServiceProvider.GetRequiredService<SeoSignalWriter>().FlushAsync(context, stoppingToken);
    }

    /// <summary>
    /// آیا کار شبانه سررسیده است؟ (ایستا برای تست)
    /// <example><code>
    /// IsNightlyDue(null, start+4min, start, 4)          ← true  (بار اول بعد از انتشار)
    /// IsNightlyDue(دیروز ۴ صبح, امروز ۴:۱۰ تهران, …, 4) ← true
    /// IsNightlyDue(امروز ۴ صبح, امروز ۱۵:۰۰, …, 4)      ← false
    /// </code></example>
    /// </summary>
    public static bool IsNightlyDue(DateTimeOffset? lastRun, DateTimeOffset now, DateTimeOffset startedAt, int nightlyHour)
    {
        if (lastRun is null)
            return now - startedAt >= FirstRunDelay;
        if (now - lastRun.Value < TimeSpan.FromHours(20))
            return false;
        var tehran = now.ToOffset(TimeSpan.FromHours(3.5));
        // در ساعت تنظیم‌شده یا بعد از آن (اگر سرور آن ساعت خاموش بوده، همان روز جبران می‌شود)؛ یا اگر بیش از ۳۰ ساعت گذشته.
        return tehran.Hour >= nightlyHour || now - lastRun.Value > TimeSpan.FromHours(30);
    }

    private async Task RunNightlyIfDueAsync(CancellationToken stoppingToken)
    {
        _lastScheduleCheck = DateTimeOffset.UtcNow;
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var config = await context.SeoAutopilotConfigs.AsNoTracking().OrderBy(c => c.CreatedAt)
            .Select(c => new { c.LastNightlyRunAt, c.OptionsJson }).FirstOrDefaultAsync(stoppingToken);
        var options = SeoAutopilotOptions.Parse(config?.OptionsJson);
        if (!options.Enabled)
            return;
        if (IsNightlyDue(config?.LastNightlyRunAt, DateTimeOffset.UtcNow, _startedAt, options.NightlyHour))
            await RunNightlyAsync(manual: false, stoppingToken);
    }

    /// <summary>کار شبانهٔ کامل؛ هر مرحله جدا محافظت می‌شود (خطای یک مرحله بقیه را متوقف نمی‌کند).</summary>
    private async Task RunNightlyAsync(bool manual, CancellationToken stoppingToken)
    {
        if (JobMonitor.ShouldSkip(JobName))
            return;
        await JobMonitor.TrackAsync(JobName, async () =>
        {
            using var _ = SeoChangeFeed.Suppress();
            var parts = new List<string>();
            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var provider = services.GetRequiredService<ISeoLinkTargetProvider>();
            provider.Invalidate();
            var options = await provider.OptionsAsync(stoppingToken);

            await StepAsync("slugs", async () =>
            {
                var changed = await services.GetRequiredService<ISeoMaintenanceService>().MigrateSlugsAsync(stoppingToken);
                if (changed > 0)
                    parts.Add($"{changed} نامک انگلیسی شد");
            });

            if (options.Can(options.Images))
            {
                await StepAsync("webp", async () =>
                {
                    var result = await services.GetRequiredService<LegacyImageConverter>().RunAsync(services.GetRequiredService<ISeoMaintenanceService>(), stoppingToken);
                    if (result.Converted > 0 || result.RowsUpdated > 0)
                        parts.Add(result.Message);
                });
            }

            await StepAsync("nightly", async () =>
            {
                var result = await services.GetRequiredService<ISeoAutopilotService>().RunNightlyAsync(stoppingToken);
                parts.Add(result.Message);
            });

            if (options.Crawl)
            {
                await StepAsync("crawl", async () =>
                {
                    var entries = await services.GetRequiredService<ISitemapService>().GetEntriesAsync(stoppingToken);
                    var run = await services.GetRequiredService<ISeoCrawlService>().CrawlAsync(entries.Select(e => e.Path).ToList(), options.CrawlMaxPages, stoppingToken);
                    parts.Add($"خزش: {run.Pages} صفحه، سلامت {run.HealthScore}");
                });
            }

            if (options.WeeklyReport)
                await StepAsync("weekly", () => services.GetRequiredService<ISeoReportService>().SendWeeklyIfDueAsync(stoppingToken));

            provider.Invalidate();
            await StepAsync("summary", async () =>
            {
                var context = services.GetRequiredService<FratakShopDbContext>();
                var config = await context.SeoAutopilotConfigs.AsTracking().OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(stoppingToken);
                if (config is not null)
                {
                    var text = (manual ? "اجرای دستی – " : "") + string.Join("؛ ", parts);
                    config.LastNightlyRunAt = DateTimeOffset.UtcNow;
                    config.LastNightlySummary = text.Length > 1000 ? text[..1000] : text;
                    await context.SaveChangesAsync(stoppingToken);
                }
            });
            logger.LogInformation("SEO autopilot nightly: {Summary}", string.Join(" | ", parts));
        });
    }

    private async Task StepAsync(string name, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "SEO autopilot nightly step {Step} failed", name);
        }
    }
}

/// <summary>
/// گرفتن صفحه‌های خود سایت برای خزندهٔ سئو (<see cref="ISeoPageFetcher"/>): فقط دامنهٔ اصلی (Shop:CanonicalHost)،
/// بدون دنبال کردن ریدایرکت، با User-Agent «FratakSeoBot» و فاصلهٔ ۲۵۰ میلی‌ثانیه بین درخواست‌ها (بار سرور).
/// دامنه تنظیم نشده (توسعه/تست) ← وضعیت ۰ (خزش انجام نمی‌شود).
/// </summary>
public sealed class SeoPageFetcher(IHttpClientFactory factory, ISeoHost host) : ISeoPageFetcher
{
    public const string ClientName = "seo-self";
    public const string UserAgent = "Mozilla/5.0 (compatible; FratakSeoBot/1.0; +https://shop.fratak.ir)";
    private const int MaxHtmlBytes = 3 * 1024 * 1024;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _last = DateTimeOffset.MinValue;

    public async Task<CrawlResponse> FetchAsync(string path, CancellationToken cancellationToken)
    {
        if (host.BaseUrl is null || string.IsNullOrEmpty(path) || !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal))
            return new CrawlResponse(0, null, null, 0);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var wait = _last + TimeSpan.FromMilliseconds(250) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            using var request = new HttpRequestMessage(HttpMethod.Get, host.BaseUrl + path);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
            using var response = await factory.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            string? html = null;
            var type = response.Content.Headers.ContentType?.MediaType;
            if ((int)response.StatusCode == 200 && type is "text/html" or "application/xhtml+xml")
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                html = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, MaxHtmlBytes));
            }

            var location = response.Headers.Location?.ToString();
            return new CrawlResponse((int)response.StatusCode, html, location, (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return new CrawlResponse(0, null, null, 0);
        }
        finally
        {
            _last = DateTimeOffset.UtcNow;
            Gate.Release();
        }
    }

    public static void Configure(HttpClient client) => client.Timeout = TimeSpan.FromSeconds(20);

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };
}

/// <summary>
/// IndexNow (رایگان؛ بینگ، یاندکس، ناور، سیک و Yep): آدرس‌های تازه/تغییرکرده فوراً اعلام می‌شوند.
/// <para>کلید: ردیف تنظیمات خلبان (۳۲ هگز تصادفی)؛ فایل تأیید: <c>/indexnow/{key}.txt</c>.</para>
/// <para>دامنه تنظیم نشده (توسعه/تست) ← هیچ درخواستی ارسال نمی‌شود.</para>
/// </summary>
public sealed class IndexNowPinger(IHttpClientFactory factory, ISeoHost host, IServiceScopeFactory scopeFactory, ILogger<IndexNowPinger> logger) : ISeoPinger
{
    public const string ClientName = "indexnow";
    public const string Endpoint = "https://api.indexnow.org/indexnow";

    public async Task<bool> PingAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default)
    {
        if (host.Host is null || host.BaseUrl is null || paths.Count == 0)
            return false;
        try
        {
            string? key;
            using (var scope = scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
                key = await context.SeoAutopilotConfigs.AsNoTracking().OrderBy(c => c.CreatedAt).Select(c => c.IndexNowKey).FirstOrDefaultAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(key))
                return false;
            var urls = paths.Where(p => p.StartsWith('/') && !p.StartsWith("//", StringComparison.Ordinal))
                .Select(p => host.BaseUrl + string.Join('/', p.Split('/').Select(Uri.EscapeDataString)))
                .Distinct(StringComparer.Ordinal).Take(10_000).ToList();
            if (urls.Count == 0)
                return false;
            var payload = new { host = host.Host, key, keyLocation = $"{host.BaseUrl}/indexnow/{key}.txt", urlList = urls };
            using var response = await factory.CreateClient(ClientName).PostAsJsonAsync(Endpoint, payload, cancellationToken);
            var ok = (int)response.StatusCode is 200 or 202;
            if (!ok)
                logger.LogInformation("IndexNow answered {Status} for {Count} URLs", (int)response.StatusCode, urls.Count);
            return ok;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            logger.LogInformation(exception, "IndexNow is not reachable");
            return false;
        }
    }

    public static void Configure(HttpClient client) => client.Timeout = TimeSpan.FromSeconds(15);
}
