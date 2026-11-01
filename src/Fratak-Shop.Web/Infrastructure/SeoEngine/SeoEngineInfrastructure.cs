using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Infrastructure.SeoEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  زیرساخت وب موتور سئو (مرحلهٔ هفتم – فاز ۸)
//   • SeoWebSource    : پیاده‌سازی ISeoWebSource با منابع رایگان و بدون کلید:
//                       پیشنهاد جست‌وجوی گوگل/بینگ، نتایج HTML بینگ و داک‌داک‌گو، و خواندن صفحهٔ رقبا.
//   • SeoEngineQueue  : صف «همین الان اجرا کن» صفحهٔ /admin/seo/engine.
//   • SeoEngineWorker : سرویس پس‌زمینه که هر ۵ دقیقه نوبت زمان‌بندی را بررسی و موتور را اجرا می‌کند.
//  ثبت در Extensions/WebServiceCollectionExtensions.cs.
//  ایمنی: فقط http/https، بدون اتصال به IP داخلی/خصوصی (جلوگیری از SSRF)، سقف حجم ۱٫۵ مگابایت،
//  فاصلهٔ حداقل ۲ ثانیه بین درخواست‌ها به یک دامنه، و بدون کوکی.
//  اگر سرور به این سایت‌ها دسترسی نداشته باشد، خطا فقط ثبت می‌شود و موتور با داده‌های داخلی کار می‌کند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// منبع داده‌های اینترنتی رایگان موتور سئو. برای افزودن منبع تازه (مثلاً موتور جست‌وجوی دیگر)
/// یک متد Try… بنویسید و در زنجیرهٔ SearchAsync/SuggestAsync اضافه کنید.
/// </summary>
/// <remarks>
/// مرحلهٔ سیزدهم: هر موتور (پیشنهاد گوگل، پیشنهاد بینگ، پیشنهاد داک‌داک‌گو، نتایج بینگ، نتایج داک‌داک‌گو) از دروازهٔ مشترک
/// <see cref="Core.Utilities.IExternalSourceGate"/> می‌گذرد: اگر ما را محدود کرد (۴۲۹/۴۰۳/۵۰۳ یا صفحهٔ کپچا)، تا پایان استراحت
/// (۱۰ دقیقه تا ۲ ساعت یا طبق Retry-After) سراغش نمی‌رویم و مستقیم منبع بعدی امتحان می‌شود. برای صفحهٔ رقبا هم ۴۲۹/۵۰۳ همان دامنه را
/// موقتاً (طبق Retry-After، حداکثر ۱۰ دقیقه) کنار می‌گذارد. کپچا دور زده نمی‌شود.
/// </remarks>
public sealed class SeoWebSource(IHttpClientFactory factory, ILogger<SeoWebSource> logger, Core.Utilities.IExternalSourceGate sourceGate) : ISeoWebSource
{
    public const string ClientName = "seo-engine";

    /// <summary>حداکثر حجم HTML یک صفحه (بیشتر از این، فقط ابتدای صفحه خوانده می‌شود).</summary>
    public const int MaxPageBytes = 1_500_000;

    /// <summary>حداقل فاصلهٔ دو درخواست به یک دامنه (مؤدبانه و برای جلوگیری از مسدود شدن).</summary>
    private static readonly TimeSpan HostDelay = TimeSpan.FromSeconds(2);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> HostGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> HostLast = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:127.0) Gecko/20100101 Firefox/127.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15",
    ];

    /// <summary>
    /// پیشنهادهای جست‌وجو: اول گوگل، اگر نشد بینگ، بعد داک‌داک‌گو. خروجی هر سه قالب OpenSearch است: ["عبارت", ["پیشنهاد۱", …]].
    /// موتوری که در حال استراحت است (قبلاً محدودمان کرده) اصلاً درخواست نمی‌گیرد.
    /// </summary>
    public async Task<WebResult<IReadOnlyList<string>>> SuggestAsync(string query, CancellationToken cancellationToken)
    {
        var q = Uri.EscapeDataString(query);
        var sources = new (string Name, string Url)[]
        {
            ("google-suggest", $"https://suggestqueries.google.com/complete/search?client=firefox&hl=fa&ie=utf-8&oe=utf-8&q={q}"),
            ("bing-suggest", $"https://api.bing.com/osjson.aspx?query={q}&market=fa-IR"),
            ("duckduckgo-suggest", $"https://duckduckgo.com/ac/?q={q}&type=list&kl=ir-fa"),
        };
        string? lastError = null;
        foreach (var (name, url) in sources)
        {
            var body = await GetFromEngineAsync(name, url, "application/json", cancellationToken);
            if (!body.Ok)
            {
                lastError = $"{name}: {body.Error}";
                continue;
            }
            var items = SeoParsers.ParseSuggestions(body.Value!);
            return WebResult<IReadOnlyList<string>>.Success(items, name);
        }
        return WebResult<IReadOnlyList<string>>.Fail(lastError ?? "no source");
    }

    /// <summary>نتایج برتر: اول بینگ (نسخهٔ HTML)، اگر نتیجه‌ای نداد داک‌داک‌گو.</summary>
    public async Task<WebResult<IReadOnlyList<SerpItem>>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var q = Uri.EscapeDataString(query);
        string? lastError = null;

        var bing = await GetFromEngineAsync("bing", $"https://www.bing.com/search?q={q}&setlang=fa&cc=IR&count=10", "text/html", cancellationToken);
        if (bing.Ok)
        {
            var items = SeoParsers.ParseBing(bing.Value!);
            if (items.Count > 0)
                return WebResult<IReadOnlyList<SerpItem>>.Success(items, "bing");
            lastError = "bing: نتیجه‌ای خوانده نشد";
            // صفحهٔ بی‌نتیجه که کپچا/چالش است ← استراحت موتور (فقط وقتی نتیجه‌ای نبود، تا متن عادی صفحه اشتباه گرفته نشود).
            if (Core.Services.Pricing.CompetitorPriceService.LooksLikeChallenge(bing.Value!))
            {
                sourceGate.ReportBlocked("bing", null, "صفحهٔ کپچا");
                lastError = "bing: صفحهٔ کپچا/ضدربات";
            }
        }
        else
            lastError = $"bing: {bing.Error}";

        var ddg = await GetFromEngineAsync("duckduckgo", $"https://html.duckduckgo.com/html/?q={q}&kl=ir-fa", "text/html", cancellationToken);
        if (ddg.Ok)
        {
            var items = SeoParsers.ParseDuckDuckGo(ddg.Value!);
            if (items.Count > 0)
                return WebResult<IReadOnlyList<SerpItem>>.Success(items, "duckduckgo");
            lastError += "؛ duckduckgo: نتیجه‌ای خوانده نشد";
            if (Core.Services.Pricing.CompetitorPriceService.LooksLikeChallenge(ddg.Value!))
                sourceGate.ReportBlocked("duckduckgo", null, "صفحهٔ کپچا");
        }
        else
            lastError += $"؛ duckduckgo: {ddg.Error}";

        return WebResult<IReadOnlyList<SerpItem>>.Fail(lastError);
    }

    public async Task<WebResult<string>> FetchPageAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return WebResult<string>.Fail("آدرس نامعتبر");
        var (result, _, _) = await GetAsync(uri.AbsoluteUri, "text/html", cancellationToken);
        return result.Ok ? WebResult<string>.Success(result.Value!, uri.Host) : WebResult<string>.Fail(result.Error!, uri.Host);
    }

    /// <summary>
    /// درخواست به یک موتور جست‌وجو از راه دروازهٔ منابع: اگر موتور در حال استراحت است، بدون درخواست خطا برمی‌گردد؛
    /// پاسخ ۴۲۹/۴۰۳/۵۰۳ یا صفحهٔ کپچا ← استراحت موتور؛ پاسخ سالم ← صفر شدن شمارندهٔ محدودیت.
    /// </summary>
    private async Task<WebResult<string>> GetFromEngineAsync(string name, string url, string accept, CancellationToken cancellationToken)
    {
        using var lease = await sourceGate.TryEnterAsync(name, TimeSpan.FromSeconds(1), cancellationToken);
        if (lease is null)
            return WebResult<string>.Fail("موقتاً در حال استراحت (قبلاً درخواست‌ها را محدود کرده بود)");

        var (result, status, retryAfter) = await GetAsync(url, accept, cancellationToken);
        if (status is 403 or 429 or 503)
        {
            sourceGate.ReportBlocked(name, retryAfter, $"HTTP {status}");
            return WebResult<string>.Fail($"HTTP {status} (محدودیت)");
        }
        if (result.Ok)
            sourceGate.ReportSuccess(name);
        else
            sourceGate.ReportFailure(name, result.Error ?? "خطا");
        return result;
    }

    /// <summary>
    /// یک GET با فاصلهٔ مؤدبانه برای هر دامنه، سقف حجم و تشخیص کدگذاری. خطا هرگز پرتاب نمی‌شود.
    /// خروجی: نتیجه، کد HTTP (۰ = بدون پاسخ) و Retry-After (اگر سرور فرستاده باشد).
    /// پاسخ ۴۲۹/۵۰۳ همان دامنه را تا Retry-After (حداقل ۱ و حداکثر ۱۰ دقیقه) کنار می‌گذارد.
    /// </summary>
    private async Task<(WebResult<string> Result, int Status, TimeSpan? RetryAfter)> GetAsync(string url, string accept, CancellationToken cancellationToken)
    {
        var uri = new Uri(url);
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
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgents[Random.Shared.Next(UserAgents.Length)]);
            request.Headers.TryAddWithoutValidation("Accept", accept + ",*/*;q=0.5");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            HostLast[uri.Host] = DateTimeOffset.UtcNow;
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = RetryAfter(response);
                if (status is 429 or 503)
                {
                    // دامنه گفته «آهسته‌تر»: تا Retry-After (۱ تا ۱۰ دقیقه) درخواست بعدی به همین دامنه صبر می‌کند.
                    var pause = retryAfter ?? TimeSpan.FromMinutes(1);
                    pause = pause < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : pause > TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : pause;
                    HostLast[uri.Host] = DateTimeOffset.UtcNow + pause - HostDelay;
                }
                return (WebResult<string>.Fail($"HTTP {status}"), status, retryAfter);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (accept == "text/html" && mediaType.Length > 0 && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return (WebResult<string>.Fail("محتوای غیر HTML"), status, null);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[MaxPageBytes];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)) > 0)
                total += read;
            return (WebResult<string>.Success(Decode(buffer.AsSpan(0, total), response.Content.Headers.ContentType?.CharSet), uri.Host), status, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            HostLast[uri.Host] = DateTimeOffset.UtcNow;
            return (WebResult<string>.Fail("مهلت تمام شد"), 0, null);
        }
        catch (HttpRequestException ex)
        {
            HostLast[uri.Host] = DateTimeOffset.UtcNow;
            logger.LogDebug(ex, "SEO web request failed: {Host}", uri.Host);
            return (WebResult<string>.Fail("اتصال برقرار نشد"), 0, null);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>مقدار هدر Retry-After (ثانیه یا تاریخ)؛ null اگر نبود.</summary>
    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;
        if (header?.Date is { } date && date > DateTimeOffset.UtcNow)
            return date - DateTimeOffset.UtcNow;
        return null;
    }

    /// <summary>کدگذاری از هدر؛ اگر نبود یا ناشناخته بود UTF-8 (رایج‌ترین برای سایت‌های فارسی).</summary>
    private static string Decode(ReadOnlySpan<byte> bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim('"', '\'', ' ')).GetString(bytes);
            }
            catch (ArgumentException)
            {
            }
        }
        return Encoding.UTF8.GetString(bytes);
    }

    public static void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("fa-IR"));
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("fa", 0.9));
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.5));
    }

    /// <summary>
    /// هندلر امن: بدون کوکی، با فشرده‌سازی و حداکثر ۵ ریدایرکت. ConnectCallback پیش از اتصال، IP مقصد را بررسی می‌کند
    /// تا هیچ آدرسی (حتی بعد از ریدایرکت یا DNS فریبنده) به شبکهٔ داخلی سرور وصل نشود.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(8),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var allowed = addresses.FirstOrDefault(IsPublic)
                ?? throw new HttpRequestException("مقصد داخلی یا نامعتبر است.");
            var socket = new Socket(allowed.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(allowed, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// <summary>آیا IP عمومی است؟ (loopback، شبکه‌های خصوصی، link-local، CGNAT و مالتی‌کست ممنوع‌اند.)</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 10
                || b[0] == 0
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || b[0] >= 224);
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC); // fc00::/7 (unique local)
        }
        return false;
    }
}

/// <summary>صف «همین الان اجرا کن». حداکثر یک درخواست در صف.</summary>
public sealed class SeoEngineQueue
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
/// سرویس پس‌زمینهٔ موتور سئو: ۴ دقیقه بعد از روشن شدن سرور شروع می‌کند و هر ۵ دقیقه نوبت را بررسی می‌کند
/// (<see cref="SeoEngineSchedule"/>؛ پیش‌فرض ۳ بار در روز از ساعت ۲ بامداد). اجرای دستی از صف هم همین‌جا انجام می‌شود
/// تا درخواست صفحهٔ پنل منتظر نماند. وضعیت در «فنی ← کارهای پس‌زمینه» دیده می‌شود.
/// </summary>
public sealed class SeoEngineWorker(IServiceScopeFactory scopeFactory, SeoEngineQueue queue, ILogger<SeoEngineWorker> logger) : BackgroundService
{
    public const string JobName = "موتور هوشمند سئو";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(JobName, TimeSpan.FromHours(13));
        JobMonitor.SetRunNow(JobName, _ => queue.Request());
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(4), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(manual: false, stoppingToken);

            var manual = false;
            try
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wait.CancelAfter(Interval);
                if (await queue.Reader.WaitToReadAsync(wait.Token) && queue.Reader.TryRead(out _))
                    manual = true;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (manual)
                await RunOnceAsync(manual: true, stoppingToken);
        }
    }

    private async Task RunOnceAsync(bool manual, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var settings = await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync(stoppingToken);
            string? slotKey = null;
            if (!manual)
            {
                if (!settings.IsSeoEngineEnabled)
                    return;
                slotKey = SeoEngineSchedule.CurrentSlot(DateTimeOffset.UtcNow, settings.SeoEngineRunsPerDay).Key;
            }

            var engine = scope.ServiceProvider.GetRequiredService<ISeoEngineService>();
            // بررسی ارزان پیش از اجرا: نوبتی که انجام شده، در مانیتور کارها «اجرا» ثبت نمی‌کند.
            if (slotKey is not null && await engine.HasRunAsync(slotKey, stoppingToken))
                return;

            var trigger = manual ? SeoRunTrigger.Manual : SeoRunTrigger.Scheduled;
            // کار در «کارهای پس‌زمینه» متوقف شده؛ نوبت ثبت نمی‌شود تا پس از ادامه اجرا شود.
            if (JobMonitor.ShouldSkip(JobName))
                return;
            var result = await JobMonitor.TrackAsync(JobName, () => engine.RunAsync(trigger, slotKey, stoppingToken));
            if (result.Ran)
                logger.LogInformation("SEO engine ({Trigger}): {Message}", trigger, result.Message);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "SEO engine run failed");
        }
    }
}
