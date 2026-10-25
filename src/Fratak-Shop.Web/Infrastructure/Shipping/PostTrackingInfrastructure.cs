using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Infrastructure.Shipping;

// ═══════════════════════════════════════════════════════════════════════════
//  زیرساخت وب رهگیری پست (مرحلهٔ هفتم – فاز ۶)
//   • PostTrackingClient : پیاده‌سازی IPostTrackingClient با دو درخواست به سامانهٔ عمومی tracking.post.ir
//                          (همان روش کتابخانهٔ متن‌باز post-tracker؛ رایگان و بدون ثبت‌نام).
//   • PostTrackingQueue  : صف «همین الان بررسی کن» صفحهٔ /admin/orders/shipments.
//   • PostTrackingWorker : سرویس پس‌زمینه که هر ۵ دقیقه مرسوله‌های نوبت‌رسیده را بررسی می‌کند.
//  ثبت در Extensions/WebServiceCollectionExtensions.cs.
//  محدودیت: سامانهٔ پست فقط از IP ایران پاسخ می‌دهد؛ روی سرور خارج از ایران خطای اتصال/مهلت ثبت می‌شود.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// خواندن وضعیت یک کد رهگیری از tracking.post.ir.
/// <para>مرحلهٔ ۱: GET صفحهٔ جست‌وجو و خواندن فیلدهای مخفی فرم ASP.NET (__VIEWSTATE و __EVENTVALIDATION) و کوکی‌ها.</para>
/// <para>مرحلهٔ ۲: POST همان فرم مثل کلیک دکمهٔ «جست‌وجو» (درخواست ای‌جکس ASP.NET) و تجزیهٔ پاسخ با <see cref="PostTrackingParser"/>.</para>
/// کوکی‌ها دستی بین دو درخواست منتقل می‌شوند تا مرسوله‌های مختلف کوکی مشترک نداشته باشند.
/// </summary>
public sealed class PostTrackingClient(IHttpClientFactory factory) : IPostTrackingClient
{
    public const string ClientName = "post-tracking";

    private const string SearchUrl = "https://tracking.post.ir/search.aspx";

    /// <summary>پیام راهنما برای خطاهای اتصال (رایج‌ترین علت).</summary>
    private const string IranOnlyHint = " (سامانهٔ رهگیری پست فقط از IP ایران پاسخ می‌دهد؛ سرور باید در ایران باشد.)";

    /// <summary>چند User-Agent مرورگر معمولی؛ هر بار یکی به تصادف (مثل کتابخانهٔ اصلی).</summary>
    private static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:125.0) Gecko/20100101 Firefox/125.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0 Safari/537.36",
    ];

    public async Task<PostTrackingResponse> TrackAsync(string code, CancellationToken cancellationToken)
    {
        var url = SearchUrl + "?id=" + Uri.EscapeDataString(code);
        var http = factory.CreateClient(ClientName);
        var agent = UserAgents[Random.Shared.Next(UserAgents.Length)];

        try
        {
            // ── مرحلهٔ ۱: صفحهٔ جست‌وجو ──
            using var get = new HttpRequestMessage(HttpMethod.Get, url);
            get.Headers.TryAddWithoutValidation("User-Agent", agent);
            get.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
            using var first = await http.SendAsync(get, cancellationToken);
            if (!first.IsSuccessStatusCode)
                return PostTrackingResponse.Fail($"سامانهٔ پست پاسخ {(int)first.StatusCode} داد." + (first.StatusCode == HttpStatusCode.Forbidden ? IranOnlyHint : ""));
            var page = await first.Content.ReadAsStringAsync(cancellationToken);
            var form = PostTrackingParser.ReadFormState(page);
            if (form is null)
                return PostTrackingResponse.Fail("فیلدهای فرم صفحهٔ رهگیری پست پیدا نشد (ممکن است ساختار سایت پست عوض شده باشد).");

            // ── مرحلهٔ ۲: ارسال فرم مثل دکمهٔ جست‌وجو ──
            var fields = new List<KeyValuePair<string, string>>
            {
                new("scripmanager1", "pnlMain|btnSearch"),
                new("__LASTFOCUS", ""),
                new("txtbSearch", code),
                new("txtVoteReason", ""),
                new("txtVoteTel", ""),
                new("__EVENTTARGET", "btnSearch"),
                new("__EVENTARGUMENT", ""),
                new("__VIEWSTATE", form.Value.ViewState),
                new("__VIEWSTATEGENERATOR", form.Value.Generator ?? "BBBC20B8"),
                new("__VIEWSTATEENCRYPTED", ""),
                new("__EVENTVALIDATION", form.Value.EventValidation),
                new("__ASYNCPOST", "true"),
                new("", ""),
            };
            using var post = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
            post.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded; charset=utf-8");
            post.Headers.TryAddWithoutValidation("User-Agent", agent);
            post.Headers.TryAddWithoutValidation("X-MicrosoftAjax", "Delta=true");
            post.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            post.Headers.TryAddWithoutValidation("Origin", "https://tracking.post.ir");
            post.Headers.Referrer = new Uri(url);
            if (CookieHeader(first) is { } cookies)
                post.Headers.TryAddWithoutValidation("Cookie", cookies);

            using var second = await http.SendAsync(post, cancellationToken);
            if (!second.IsSuccessStatusCode)
                return PostTrackingResponse.Fail($"سامانهٔ پست به جست‌وجو پاسخ {(int)second.StatusCode} داد.");
            var body = await second.Content.ReadAsStringAsync(cancellationToken);
            return PostTrackingParser.ParseResult(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PostTrackingResponse.Fail("مهلت پاسخ سامانهٔ پست تمام شد." + IranOnlyHint);
        }
        catch (HttpRequestException ex)
        {
            return PostTrackingResponse.Fail("اتصال به سامانهٔ پست ممکن نشد: " + ex.Message + IranOnlyHint);
        }
    }

    /// <summary>کوکی‌های پاسخ اول (مثلاً ASP.NET_SessionId) به شکل سرآیند Cookie برای درخواست دوم.</summary>
    private static string? CookieHeader(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return null;
        var pairs = values
            .Select(v => v.Split(';', 2)[0].Trim())
            .Where(v => v.Contains('='))
            .ToList();
        return pairs.Count == 0 ? null : string.Join("; ", pairs);
    }

    /// <summary>تنظیم HttpClient: مهلت ۲۵ ثانیه، سقف پاسخ ۲ مگابایت.</summary>
    public static void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(25);
        client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("fa-IR"));
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("fa", 0.9));
    }

    /// <summary>هندلر بدون کوکی‌خانهٔ مشترک (کوکی‌ها دستی منتقل می‌شوند) با فشرده‌سازی خودکار.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(10),
    };
}

/// <summary>صف «همین الان همه را بررسی کن». حداکثر یک درخواست در صف.</summary>
public sealed class PostTrackingQueue
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    /// <summary>درخواست اجرای فوری؛ اگر درخواستی در صف باشد false.</summary>
    public bool Request() => _channel.Writer.TryWrite(true);

    public bool HasPending => _channel.Reader.Count > 0;

    internal ChannelReader<bool> Reader => _channel.Reader;
}

/// <summary>
/// سرویس پس‌زمینهٔ رهگیری پست: ۳ دقیقه بعد از روشن شدن سرور شروع می‌کند و هر ۵ دقیقه (یا با درخواست فوری)
/// <see cref="IShipmentTrackingService.RunDueAsync"/> را صدا می‌زند. خود سرویس تصمیم می‌گیرد کدام مرسوله نوبتش رسیده
/// (مثلاً با ۴ بار در روز، هر مرسوله در هر بازهٔ ۶ ساعته یک بار). وضعیت در «فنی ← کارهای پس‌زمینه» دیده می‌شود.
/// </summary>
public sealed class PostTrackingWorker(IServiceScopeFactory scopeFactory, PostTrackingQueue queue, ILogger<PostTrackingWorker> logger) : BackgroundService
{
    public const string JobName = "رهگیری مرسوله‌های پستی";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(JobName, TimeSpan.FromMinutes(30));
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(force: false, stoppingToken);

            var force = false;
            try
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wait.CancelAfter(Interval);
                if (await queue.Reader.WaitToReadAsync(wait.Token) && queue.Reader.TryRead(out _))
                    force = true;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (force)
                await RunOnceAsync(force: true, stoppingToken);
        }
    }

    private async Task RunOnceAsync(bool force, CancellationToken stoppingToken)
    {
        try
        {
            var count = await JobMonitor.TrackAsync(JobName, async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var tracking = scope.ServiceProvider.GetRequiredService<IShipmentTrackingService>();
                return await tracking.RunDueAsync(stoppingToken, force);
            });
            if (count > 0)
                logger.LogInformation("Post tracking checked {Count} shipments", count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Post tracking run failed");
        }
    }
}
