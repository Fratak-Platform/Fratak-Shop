using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;
using Fratak_Shop.Core.Services.Seo.Tools;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.SeoEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  زیرساخت وب ابزارهای سئو (مرحلهٔ هفتم – فاز ۸ب)
//   • SeoLinkProbe       : پیاده‌سازی ISeoLinkProbe — بررسی HTTP لینک‌های بیرونی (امن در برابر SSRF، بدون
//                          ریدایرکت خودکار تا ریدایرکت‌ها دیده شوند)، مسیرهای ثابت سایت و وجود فایل‌ها در wwwroot.
//   • SearchConsoleHttp  : پیاده‌سازی ISearchConsoleHttp — HttpClient جدا برای هر پروکسی.
//   • SeoToolsQueue      : صف «همین الان اجرا کن» صفحه‌های لینک‌ها و Search Console.
//   • SeoToolsWorker     : سرویس پس‌زمینه: بررسی لینک‌ها هر ~۲۳ ساعت و همگام‌سازی Search Console هر ~۲۰ ساعت.
//  ثبت در Extensions/WebServiceCollectionExtensions.cs (بعد از TryAdd لایهٔ Core تا نسخه‌های آفلاین جایگزین شوند).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>بررسی لینک‌ها برای SeoLinkAuditService.</summary>
public sealed class SeoLinkProbe(IHttpClientFactory factory, EndpointDataSource endpoints, IWebHostEnvironment environment, ILogger<SeoLinkProbe> logger)
    : ISeoLinkProbe
{
    public const string ClientName = "seo-links";

    /// <summary>حداقل فاصلهٔ دو درخواست به یک دامنه.</summary>
    private static readonly TimeSpan HostDelay = TimeSpan.FromSeconds(1);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> HostGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> HostLast = new(StringComparer.OrdinalIgnoreCase);

    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    private IReadOnlySet<string>? _staticPaths;

    /// <summary>
    /// مسیرهای بدون پارامتر همهٔ صفحه‌ها و endpointها (مثل «/blog»، «/faq»، «/sitemap.xml»). یک بار ساخته و نگه داشته می‌شود.
    /// مسیرهای پارامتردار (مثل /product/{slug}) با دیتابیس بررسی می‌شوند.
    /// </summary>
    public IReadOnlySet<string> StaticPaths => _staticPaths ??= BuildStaticPaths();

    private HashSet<string> BuildStaticPaths()
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { "/" };
        foreach (var endpoint in endpoints.Endpoints.OfType<RouteEndpoint>())
            if (endpoint.RoutePattern.Parameters.Count == 0)
                set.Add(SeoPaths.Normalize("/" + (endpoint.RoutePattern.RawText ?? "").TrimStart('/')));
        return set;
    }

    /// <summary>وجود فایل زیر wwwroot (مسیر بیرون از wwwroot بررسی نمی‌شود و «موجود» حساب می‌شود).</summary>
    public bool LocalFileExists(string path)
    {
        try
        {
            var root = Path.GetFullPath(environment.WebRootPath);
            var full = Path.GetFullPath(Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return true;
            return File.Exists(full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }

    /// <summary>
    /// بررسی یک لینک بیرونی: ریدایرکت‌ها دستی دنبال می‌شوند (حداکثر ۵) تا «دائمی بودن» مشخص شود.
    /// اول HEAD (سبک)؛ اگر سرور خطا داد، یک GET (فقط هدرها) چون برخی سرورها HEAD را درست جواب نمی‌دهند.
    /// </summary>
    public async Task<LinkProbeResult> CheckExternalAsync(Uri url, CancellationToken cancellationToken)
    {
        var current = url;
        var redirected = false;
        var permanent = true;
        for (var hop = 0; hop <= 5; hop++)
        {
            var (status, location, error) = await SendAsync(current, cancellationToken);
            if (status is null)
                return new LinkProbeResult(null, redirected ? current.AbsoluteUri : null, false, error);
            if (status is 301 or 302 or 303 or 307 or 308 && location is not null)
            {
                if (!Uri.TryCreate(current, location, out var next) || next.Scheme is not ("http" or "https"))
                    return new LinkProbeResult(status, current.AbsoluteUri, false, "ریدایرکت به آدرس نامعتبر");
                redirected = true;
                if (status is not (301 or 308))
                    permanent = false;
                current = next;
                continue;
            }
            return new LinkProbeResult(status, redirected ? current.AbsoluteUri : null, redirected && permanent);
        }
        return new LinkProbeResult(null, current.AbsoluteUri, false, "ریدایرکت‌های پشت سر هم بیش از حد (احتمالاً حلقه)");
    }

    private async Task<(int? Status, Uri? Location, string? Error)> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        var gate = HostGates.GetOrAdd(uri.Host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (HostLast.TryGetValue(uri.Host, out var last))
            {
                var wait = last + HostDelay - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken);
            }
            var http = factory.CreateClient(ClientName);
            var (status, location) = await RequestAsync(http, HttpMethod.Head, uri, cancellationToken);
            if (status >= 400 && status is not (404 or 410))
                (status, location) = await RequestAsync(http, HttpMethod.Get, uri, cancellationToken);
            return (status, location, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, null, "مهلت پاسخ تمام شد");
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Link check failed: {Host}", uri.Host);
            return (null, null, ex.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "دامنه پیدا نشد (احتمالاً منقضی شده)",
                HttpRequestError.SecureConnectionError => "گواهی امنیتی (SSL) سایت مقصد نامعتبر است",
                _ => "اتصال برقرار نشد (ممکن است سایت فیلتر یا خاموش باشد)",
            });
        }
        finally
        {
            HostLast[uri.Host] = DateTimeOffset.UtcNow;
            gate.Release();
        }
    }

    private static async Task<(int Status, Uri? Location)> RequestAsync(HttpClient http, HttpMethod method, Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return ((int)response.StatusCode, response.Headers.Location);
    }

    public static void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("fa-IR"));
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.5));
    }

    /// <summary>همان هندلر امن موتور سئو (بدون اتصال به IP داخلی) ولی بدون ریدایرکت خودکار.</summary>
    public static SocketsHttpHandler CreateHandler()
    {
        var handler = SeoWebSource.CreateHandler();
        handler.AllowAutoRedirect = false;
        return handler;
    }
}

/// <summary>
/// ارسال درخواست به گوگل برای SearchConsoleService. برای هر پروکسی یک HttpClient ماندگار (با PooledConnectionLifetime)
/// ساخته می‌شود؛ پروکسی خالی = تنظیم پیش‌فرض سیستم (متغیرهای HTTPS_PROXY).
/// </summary>
public sealed class SearchConsoleHttp(ILogger<SearchConsoleHttp> logger) : ISearchConsoleHttp
{
    private static readonly ConcurrentDictionary<string, HttpClient> Clients = new(StringComparer.Ordinal);

    private static HttpClient Client(string? proxy) => Clients.GetOrAdd(proxy?.Trim() ?? "", key =>
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        if (key.Length > 0)
        {
            handler.Proxy = new WebProxy(new Uri(key));
            handler.UseProxy = true;
        }
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
    });

    public async Task<GscHttpResponse> SendAsync(GscHttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(request.Method, request.Url);
            if (request.Form is not null)
                message.Content = new FormUrlEncodedContent(request.Form);
            else if (request.Json is not null)
                message.Content = new StringContent(request.Json, Encoding.UTF8, "application/json");
            if (request.Bearer is not null)
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.Bearer);
            message.Headers.TryAddWithoutValidation("User-Agent", "Fratak-Shop/1.0 (Search Console sync)");

            using var response = await Client(request.Proxy).SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new GscHttpResponse((int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GscHttpResponse(null, "", "مهلت پاسخ تمام شد");
        }
        catch (HttpRequestException ex)
        {
            // پیام خطا هرگز توکن را ندارد (توکن فقط در هدر است)؛ ثبت امن است.
            logger.LogWarning(ex, "Search Console request failed: {Host}", new Uri(request.Url).Host);
            return new GscHttpResponse(null, "", ex.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "نام دامنهٔ گوگل پیدا نشد",
                HttpRequestError.ProxyTunnelError => "اتصال از طریق پروکسی برقرار نشد",
                HttpRequestError.SecureConnectionError => "اتصال امن (SSL) برقرار نشد",
                _ => "اتصال برقرار نشد",
            });
        }
        catch (Exception ex) when (ex is UriFormatException or NotSupportedException or ArgumentException)
        {
            return new GscHttpResponse(null, "", "آدرس پروکسی پشتیبانی نمی‌شود");
        }
    }
}

/// <summary>کارهای صف ابزارهای سئو.</summary>
public enum SeoToolsJob : byte
{
    LinkAudit = 1,
    SearchConsoleSync = 2,
}

/// <summary>صف اجرای فوری. هر نوع کار حداکثر یک بار در صف.</summary>
public sealed class SeoToolsQueue
{
    private readonly Channel<SeoToolsJob> _channel = Channel.CreateBounded<SeoToolsJob>(new BoundedChannelOptions(4)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private readonly ConcurrentDictionary<SeoToolsJob, bool> _pending = new();

    /// <summary>درخواست اجرا؛ اگر همین کار در صف باشد false.</summary>
    public bool Request(SeoToolsJob job)
    {
        if (!_pending.TryAdd(job, true))
            return false;
        if (_channel.Writer.TryWrite(job))
            return true;
        _pending.TryRemove(job, out _);
        return false;
    }

    public bool IsPending(SeoToolsJob job) => _pending.ContainsKey(job);

    internal ChannelReader<SeoToolsJob> Reader => _channel.Reader;

    internal void Done(SeoToolsJob job) => _pending.TryRemove(job, out _);
}

/// <summary>
/// سرویس پس‌زمینهٔ ابزارهای سئو: ۶ دقیقه بعد از روشن شدن سرور و سپس هر ۱۵ دقیقه بررسی می‌کند که
/// بررسی لینک‌ها (هر ~۲۳ ساعت) یا همگام‌سازی Search Console (هر ~۲۰ ساعت؛ بعد از خطا ۳ ساعت بعد) لازم است یا نه.
/// درخواست‌های دستی صفحه‌های پنل هم همین‌جا اجرا می‌شوند. وضعیت در «فنی ← کارهای پس‌زمینه».
/// </summary>
public sealed class SeoToolsWorker(IServiceScopeFactory scopeFactory, SeoToolsQueue queue, ILogger<SeoToolsWorker> logger) : BackgroundService
{
    public const string LinkJobName = "یابندهٔ لینک شکسته";
    public const string GscJobName = "همگام‌سازی Search Console";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LinkEvery = TimeSpan.FromHours(23);
    private static readonly TimeSpan GscEvery = TimeSpan.FromHours(20);
    private static readonly TimeSpan GscRetry = TimeSpan.FromHours(3);

    /// <summary>آخرین تلاش ناموفق همگام‌سازی (برای فاصلهٔ تلاش دوباره؛ فقط در حافظه).</summary>
    private DateTimeOffset _gscLastAttempt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(LinkJobName, TimeSpan.FromHours(25));
        JobMonitor.Register(GscJobName, TimeSpan.FromHours(22));
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(6), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunDueAsync(stoppingToken);

            try
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wait.CancelAfter(Interval);
                while (await queue.Reader.WaitToReadAsync(wait.Token))
                {
                    while (queue.Reader.TryRead(out var job))
                    {
                        await RunAsync(job, manual: true, stoppingToken);
                        queue.Done(job);
                    }
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>اجرای کارهای سررسیده بر اساس زمان آخرین اجرا در دیتابیس.</summary>
    private async Task RunDueAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var config = await context.SeoToolsConfigs.AsNoTracking().OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(stoppingToken);
            var now = DateTimeOffset.UtcNow;
            var linkEnabled = config?.IsLinkCheckEnabled ?? true;
            if (linkEnabled && (config?.LinkCheckAt is null || now - config.LinkCheckAt > LinkEvery))
                await RunAsync(SeoToolsJob.LinkAudit, manual: false, stoppingToken);
            if (config is { IsSearchConsoleEnabled: true } && (config.SearchConsoleSyncedAt is null || now - config.SearchConsoleSyncedAt > GscEvery)
                && now - _gscLastAttempt > GscRetry)
            {
                _gscLastAttempt = now;
                await RunAsync(SeoToolsJob.SearchConsoleSync, manual: false, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "SEO tools schedule check failed");
        }
    }

    private async Task RunAsync(SeoToolsJob job, bool manual, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            if (job == SeoToolsJob.LinkAudit)
            {
                var audit = scope.ServiceProvider.GetRequiredService<ISeoLinkAuditService>();
                var result = await JobMonitor.TrackAsync(LinkJobName, () => audit.RunAsync(manual, stoppingToken));
                if (result.Ran)
                    logger.LogInformation("Link audit: {Message}", result.Message);
            }
            else
            {
                var gsc = scope.ServiceProvider.GetRequiredService<ISearchConsoleService>();
                var result = await JobMonitor.TrackAsync(GscJobName, () => gsc.SyncAsync(manual, stoppingToken));
                logger.LogInformation("Search Console sync: {Message}", result.Message);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "SEO tools job {Job} failed", job);
        }
    }
}
