using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Infrastructure.Shipping;

// ═══════════════════════════════════════════════════════════════════════════
//  زیرساخت وب رهگیری پست (مرحلهٔ هفتم – فاز ۶)
//   • PostTrackingClient : پیاده‌سازی IPostTrackingClient روی سامانهٔ عمومی tracking.post.ir (نسخهٔ ۲ با کد امنیتی + روش قدیم)
//                          (همان روش کتابخانهٔ متن‌باز post-tracker؛ رایگان و بدون ثبت‌نام).
//   • PostTrackingQueue  : صف «همین الان بررسی کن» صفحهٔ /admin/orders/shipments.
//   • PostTrackingWorker : سرویس پس‌زمینه که هر ۵ دقیقه مرسوله‌های نوبت‌رسیده را بررسی می‌کند.
//  ثبت در Extensions/WebServiceCollectionExtensions.cs.
//  محدودیت: سامانهٔ پست فقط از IP ایران پاسخ می‌دهد؛ روی سرور خارج از ایران خطای اتصال/مهلت ثبت می‌شود.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// خواندن وضعیت یک کد رهگیری از tracking.post.ir (رایگان، بدون ثبت‌نام و بدون سرویس واسط).
/// <para><b>نسخهٔ ۲ سایت (با کد امنیتی) – مسیر اصلی:</b></para>
/// <list type="number">
/// <item>GET صفحهٔ جست‌وجو (<c>search.aspx?id=کد</c>) و خواندن فیلدهای مخفی فرم ASP.NET و کوکی جلسه.</item>
/// <item>GET تصویر کد امنیتی (<c>search.aspx?captcha=1&amp;t=زمان</c>) با همان کوکی؛ خواندن ۴ رقم با <see cref="IPostCaptchaSolver"/> (مدل آفلاین).</item>
/// <item>POST کامل فرم با <c>txtCaptcha</c> و دسته‌بندی پاسخ با <see cref="PostTrackingParser.Classify"/>.</item>
/// </list>
/// اگر کد امنیتی رد شود یا پاسخ مبهم باشد، کل مراحل با تصویر تازه تا <see cref="MaxAttempts"/> بار تکرار می‌شود.
/// <para><b>نسخهٔ قدیم (بدون کپچا):</b> اگر صفحه فیلد کپچا نداشت، همان ارسال ای‌جکس قبلی اجرا می‌شود (سازگاری رو به عقب و رو به جلو).</para>
/// کوکی‌ها دستی و جدا برای هر تلاش نگه داشته می‌شوند تا مرسوله‌ها/تلاش‌ها کوکی مشترک نداشته باشند.
/// </summary>
public sealed class PostTrackingClient(IHttpClientFactory factory, IPostCaptchaSolver captcha, ILogger<PostTrackingClient> logger) : IPostTrackingClient
{
    public const string ClientName = "post-tracking";

    /// <summary>حداکثر تعداد تلاش کامل (هر تلاش = صفحه + تصویر تازه + ارسال). دقت مدل روی نمونه‌ها بالای ۹۰٪ است؛ ۳ تلاش کافی است.</summary>
    public const int MaxAttempts = 3;

    private const string SiteUrl = "https://tracking.post.ir";
    private const string SearchUrl = SiteUrl + "/search.aspx";

    /// <summary>پیام راهنما برای خطاهای اتصال (رایج‌ترین علت).</summary>
    private const string IranOnlyHint = " (سامانهٔ رهگیری پست فقط از IP ایران پاسخ می‌دهد؛ سرور باید در ایران باشد.)";

    /// <summary>چند User-Agent مرورگر معمولی؛ هر تلاش یکی به تصادف.</summary>
    private static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:131.0) Gecko/20100101 Firefox/131.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Safari/537.36",
    ];

    public async Task<PostTrackingResponse> TrackAsync(string code, CancellationToken cancellationToken)
    {
        var url = SearchUrl + "?id=" + Uri.EscapeDataString(code);
        var http = factory.CreateClient(ClientName);
        PostTrackingResponse? last = null;
        var rejected = 0;

        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (attempt > 1)
                    await Task.Delay(TimeSpan.FromMilliseconds(800 + Random.Shared.Next(700)), cancellationToken);

                var session = new Session(UserAgents[Random.Shared.Next(UserAgents.Length)]);

                // ── مرحلهٔ ۱: صفحهٔ جست‌وجو ──
                using var get = session.Request(HttpMethod.Get, url, SiteUrl + "/");
                using var first = await http.SendAsync(get, cancellationToken);
                session.Absorb(first);
                if (!first.IsSuccessStatusCode)
                    return PostTrackingResponse.Fail($"سامانهٔ پست پاسخ {(int)first.StatusCode} داد." + (first.StatusCode == HttpStatusCode.Forbidden ? IranOnlyHint : ""));
                var page = await first.Content.ReadAsStringAsync(cancellationToken);
                var form = PostTrackingParser.ReadFormState(page);
                if (form is null)
                    return PostTrackingResponse.Fail("فیلدهای فرم صفحهٔ رهگیری پست پیدا نشد (ممکن است ساختار سایت پست عوض شده باشد).");

                if (!PostTrackingParser.HasCaptcha(page))
                    return await LegacyPostAsync(http, session, url, code, form.Value, cancellationToken);

                if (!captcha.IsAvailable)
                    return PostTrackingResponse.Fail("سایت پست کد امنیتی می‌خواهد ولی فایل مدل خواندن کد (Resources/Captcha/digits.onnx) کنار برنامه پیدا نشد.");

                // ── مرحلهٔ ۲: تصویر کد امنیتی ──
                var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                using var imageRequest = session.Request(HttpMethod.Get, $"{SearchUrl}?captcha=1&t={stamp}", url);
                imageRequest.Headers.Remove("Accept");
                imageRequest.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/png,image/*;q=0.8,*/*;q=0.5");
                using var imageResponse = await http.SendAsync(imageRequest, cancellationToken);
                session.Absorb(imageResponse);
                var image = imageResponse.IsSuccessStatusCode ? await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken) : Array.Empty<byte>();
                var answer = image.Length >= 100 ? captcha.Solve(image) : null;
                if (!CtcDigitDecoder.LooksValid(answer))
                {
                    last = PostTrackingResponse.Fail("تصویر کد امنیتی سایت پست دریافت یا خوانده نشد.");
                    continue;
                }

                // ── مرحلهٔ ۳: ارسال کامل فرم ──
                var fields = new List<KeyValuePair<string, string>>
                {
                    new("__EVENTTARGET", "btnSearch"),
                    new("__EVENTARGUMENT", ""),
                    new("__LASTFOCUS", ""),
                    new("txtbSearch", code),
                    new("txtCaptcha", answer!),
                    new("CustomerMob", ""),
                    new("txtVoteReason", ""),
                    new("txtVoteTel", ""),
                    new("__VIEWSTATE", form.Value.ViewState),
                    new("__VIEWSTATEGENERATOR", form.Value.Generator ?? ""),
                    new("__VIEWSTATEENCRYPTED", ""),
                    new("__EVENTVALIDATION", form.Value.EventValidation),
                };
                using var post = session.Request(HttpMethod.Post, url, url);
                post.Content = new FormUrlEncodedContent(fields);
                post.Headers.TryAddWithoutValidation("Origin", SiteUrl);
                using var second = await http.SendAsync(post, cancellationToken);
                if (!second.IsSuccessStatusCode)
                    return PostTrackingResponse.Fail($"سامانهٔ پست به جست‌وجو پاسخ {(int)second.StatusCode} داد.");
                var body = await second.Content.ReadAsStringAsync(cancellationToken);

                var (verdict, response) = PostTrackingParser.Classify(body);
                switch (verdict)
                {
                    case PostCaptchaVerdict.Result:
                    case PostCaptchaVerdict.NoData:
                        return response;
                    case PostCaptchaVerdict.CaptchaRejected:
                        rejected++;
                        break;
                }
                last = response;
                logger.LogDebug("Post tracking attempt {Attempt} for {Code}: {Verdict}", attempt, code, verdict);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PostTrackingResponse.Fail("مهلت پاسخ سامانهٔ پست تمام شد." + IranOnlyHint);
        }
        catch (HttpRequestException ex)
        {
            return PostTrackingResponse.Fail("اتصال به سامانهٔ پست ممکن نشد: " + ex.Message + IranOnlyHint);
        }

        // همهٔ تلاش‌ها بی‌نتیجه: «ناموفق» (نه «پیدا نشد») تا وضعیت مرسوله اشتباه ثبت نشود و نوبت بعد دوباره امتحان شود.
        var summary = rejected == MaxAttempts
            ? $"کد امنیتی سایت پست در {MaxAttempts} تلاش پذیرفته نشد؛ نوبت بعدی دوباره امتحان می‌شود."
            : last?.Error ?? "سامانهٔ پست نتیجه‌ای برنگرداند.";
        return PostTrackingResponse.Fail(summary);
    }

    /// <summary>
    /// روش قدیمی (پیش از کپچا): ارسال ای‌جکس ASP.NET مثل کلیک دکمهٔ جست‌وجو. فقط وقتی صفحه فیلد کپچا ندارد اجرا می‌شود.
    /// </summary>
    private static async Task<PostTrackingResponse> LegacyPostAsync(HttpClient http, Session session, string url, string code,
        (string ViewState, string EventValidation, string? Generator) form, CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("scripmanager1", "pnlMain|btnSearch"),
            new("__LASTFOCUS", ""),
            new("txtbSearch", code),
            new("txtVoteReason", ""),
            new("txtVoteTel", ""),
            new("__EVENTTARGET", "btnSearch"),
            new("__EVENTARGUMENT", ""),
            new("__VIEWSTATE", form.ViewState),
            new("__VIEWSTATEGENERATOR", form.Generator ?? "BBBC20B8"),
            new("__VIEWSTATEENCRYPTED", ""),
            new("__EVENTVALIDATION", form.EventValidation),
            new("__ASYNCPOST", "true"),
            new("", ""),
        };
        using var post = session.Request(HttpMethod.Post, url, url);
        post.Content = new FormUrlEncodedContent(fields);
        post.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded; charset=utf-8");
        post.Headers.TryAddWithoutValidation("X-MicrosoftAjax", "Delta=true");
        post.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        post.Headers.TryAddWithoutValidation("Origin", SiteUrl);
        using var second = await http.SendAsync(post, cancellationToken);
        if (!second.IsSuccessStatusCode)
            return PostTrackingResponse.Fail($"سامانهٔ پست به جست‌وجو پاسخ {(int)second.StatusCode} داد.");
        return PostTrackingParser.ParseResult(await second.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// یک «جلسهٔ مرورگر» کوچک برای یک تلاش: User-Agent ثابت و کوکی‌خانهٔ دستی (نام ← مقدار).
    /// هر پاسخ با <see cref="Absorb"/> کوکی‌هایش را اضافه/جایگزین می‌کند و هر درخواست با <see cref="Request"/> آن‌ها را می‌فرستد.
    /// </summary>
    private sealed class Session(string agent)
    {
        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

        public HttpRequestMessage Request(HttpMethod method, string url, string? referer)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("User-Agent", agent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            if (referer is not null)
                request.Headers.Referrer = new Uri(referer);
            if (_cookies.Count > 0)
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies.Select(c => c.Key + "=" + c.Value)));
            return request;
        }

        public void Absorb(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var values))
                return;
            foreach (var value in values)
            {
                var pair = value.Split(';', 2)[0].Trim();
                var eq = pair.IndexOf('=');
                if (eq > 0 && _cookies.Count < 30)
                    _cookies[pair[..eq]] = pair[(eq + 1)..];
            }
        }
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
        JobMonitor.SetRunNow(JobName, _ => queue.Request());
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
