using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Web.Infrastructure.Engagement;

/// <summary>
/// In-memory queue between the notification service and <see cref="WebPushDispatcher"/>.
/// Bounded: under a flood the oldest pushes are dropped instead of using unbounded memory.
/// </summary>
public sealed class ChannelPushSender : IPushSender
{
    private readonly Channel<PushMessage> _channel = Channel.CreateBounded<PushMessage>(new BoundedChannelOptions(2000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    public ChannelReader<PushMessage> Reader => _channel.Reader;

    public void Enqueue(PushMessage message) => _channel.Writer.TryWrite(message);
}

/// <summary>
/// Delivers queued pushes (RFC 8030 / 8291 / 8292): payload encrypted for each subscribed browser,
/// signed with the shop's VAPID key. Expired subscriptions (404/410) are removed; repeated failures
/// disable a subscription. Only push-service hosts on the allow list are ever contacted.
/// </summary>
public sealed class WebPushDispatcher(
    ChannelPushSender queue,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<WebPushDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "webpush";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string JobName = "ارسال اعلان وب (Push)";

    /// <summary>
    /// نتیجهٔ آخرین تلاش ارسال (برای عیب‌یابی در صفحهٔ «اعلان‌ها» و «سیستم»).
    /// مثال: اگر سرور به fcm.googleapis.com دسترسی نداشته باشد، اینجا خطای اتصال دیده می‌شود.
    /// </summary>
    public static PushDeliveryResult? LastResult { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Queue driven: runs only when there is something to send, so it is never reported as "stale".
        Admin.JobMonitor.Register(JobName, TimeSpan.FromDays(3650));
        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await Admin.JobMonitor.TrackAsync(JobName, () => DeliverAsync(message, stoppingToken));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Web push delivery failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    /// <summary>
    /// ارسال فوری (بدون صف) و برگرداندن نتیجهٔ <b>هر دستگاه</b>. فقط برای دکمهٔ «اعلان آزمایشی» صفحهٔ /admin/inbox تا کارمند
    /// ببیند پوش واقعاً به کدام سرویس رفت و چه جوابی گرفت (مثلاً «۲۰۱ تحویل شد» یا «اتصال به fcm.googleapis.com ممکن نشد»).
    /// فهرست خالی یعنی کلید VAPID نیست یا این کاربر هیچ دستگاه فعالی ندارد.
    /// </summary>
    public Task<List<PushDeliveryResult>> SendNowAsync(PushMessage message, CancellationToken cancellationToken) =>
        DeliverAsync(message, cancellationToken);

    private async Task<List<PushDeliveryResult>> DeliverAsync(PushMessage message, CancellationToken cancellationToken)
    {
        var results = new List<PushDeliveryResult>();
        using var scope = scopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<IWebPushService>();

        var keys = await pushService.GetVapidKeysAsync();
        if (keys is null)
            return results;

        var targets = await pushService.GetTargetsAsync(message.UserId);
        if (targets.Count == 0)
            return results;

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            title = Truncate(message.Title, 120),
            body = Truncate(message.Body, 300),
            url = message.Url is { } url && url.StartsWith('/') && !url.StartsWith("//") ? url : "/",
            tag = message.Tag,
        }, Json);

        var client = httpClientFactory.CreateClient(HttpClientName);
        foreach (var target in targets)
        {
            if (!IWebPushService.IsAllowedEndpoint(target.Endpoint))
            {
                await pushService.RecordDeliveryAsync(target.Id, succeeded: false, gone: true);
                continue;
            }

            var (succeeded, gone, result) = await SendAsync(client, target, keys, payload, message.Tag, cancellationToken);
            await pushService.RecordDeliveryAsync(target.Id, succeeded, gone);
            results.Add(result);
        }
        return results;
    }

    private async Task<(bool Succeeded, bool Gone, PushDeliveryResult Result)> SendAsync(HttpClient client, PushTarget target, VapidKeys keys, byte[] payload, string? tag, CancellationToken cancellationToken)
    {
        try
        {
            var body = WebPushCrypto.Encrypt(payload, target.P256dh, target.Auth);
            using var request = new HttpRequestMessage(HttpMethod.Post, target.Endpoint);
            request.Headers.TryAddWithoutValidation("Authorization",
                WebPushCrypto.CreateVapidAuthorization(target.Endpoint, keys.Subject, keys.PublicKey, keys.PrivateKey, DateTimeOffset.UtcNow.AddHours(12)));
            request.Headers.TryAddWithoutValidation("TTL", "86400");
            request.Headers.TryAddWithoutValidation("Urgency", "normal");
            if (!string.IsNullOrEmpty(tag) && tag.Length <= 32 && tag.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                request.Headers.TryAddWithoutValidation("Topic", tag);

            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");

            using var response = await client.SendAsync(request, cancellationToken);
            var result = new PushDeliveryResult(DateTimeOffset.UtcNow, request.RequestUri?.Host ?? "", (int)response.StatusCode, response.IsSuccessStatusCode ? null : response.ReasonPhrase);
            LastResult = result;
            if (response.IsSuccessStatusCode)
                return (true, false, result);

            var gone = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;
            if (!gone)
                logger.LogInformation("Push service answered {Status} for subscription {Id}", (int)response.StatusCode, target.Id);
            return (false, gone, result);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            var failed = new PushDeliveryResult(DateTimeOffset.UtcNow, Uri.TryCreate(target.Endpoint, UriKind.Absolute, out var uri) ? uri.Host : "", null, exception.Message);
            LastResult = failed;
            logger.LogInformation(exception, "Push to subscription {Id} failed", target.Id);
            return (false, false, failed);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

/// <summary>نتیجهٔ یک ارسال پوش: زمان، سرویس مقصد، کد پاسخ HTTP (یا null اگر اصلاً وصل نشد) و متن خطا.</summary>
public sealed record PushDeliveryResult(DateTimeOffset At, string Host, int? StatusCode, string? Error)
{
    /// <summary>نام آشنای سرویس پوش. مثال: fcm.googleapis.com → «گوگل (کروم/اج/اندروید)».</summary>
    public string ServiceTitle => Host switch
    {
        "fcm.googleapis.com" or "android.googleapis.com" => "گوگل FCM (کروم، اج، اپرا، اندروید)",
        _ when Host.EndsWith("mozilla.com", StringComparison.OrdinalIgnoreCase) => "موزیلا (فایرفاکس)",
        _ when Host.EndsWith("push.apple.com", StringComparison.OrdinalIgnoreCase) => "اپل (سافاری/آیفون)",
        _ when Host.EndsWith("notify.windows.com", StringComparison.OrdinalIgnoreCase) => "ویندوز (اج قدیمی)",
        _ => Host,
    };

    /// <summary>
    /// توضیح فارسی نتیجه برای کارمند. مثال‌ها: «تحویل سرویس شد (۲۰۱)»، «کلید سرور با اشتراک نمی‌خواند (۴۰۳)…»،
    /// «سرور به این سرویس وصل نشد – در ایران معمولاً FCM گوگل از سمت سرور مسدود است…».
    /// </summary>
    public string Explain() => StatusCode switch
    {
        >= 200 and < 300 => $"تحویل سرویس شد ({StatusCode}). اگر روی دستگاه نمایش داده نشد: مرورگر باید باز یا در پس‌زمینه در حال اجرا باشد و اعلان‌های سیستم‌عامل برای مرورگر خاموش نباشد.",
        404 or 410 => $"اشتراک این دستگاه منقضی شده بود ({StatusCode}) و حذف شد؛ صفحه را روی آن دستگاه باز کنید تا خودکار دوباره ثبت شود.",
        401 or 403 => $"سرویس پوش امضای سرور را نپذیرفت ({StatusCode}) – معمولاً یعنی کلید VAPID عوض شده؛ با باز کردن سایت روی آن دستگاه اشتراک خودکار تازه می‌شود.",
        413 => "متن اعلان بیش از حد بلند بود (۴۱۳).",
        429 => "سرویس پوش موقتاً محدود کرد (۴۲۹)؛ کمی بعد دوباره امتحان کنید.",
        { } code => $"سرویس پوش خطای {code} داد{(string.IsNullOrEmpty(Error) ? "" : ": " + Error)}.",
        null => "سرور اصلاً به این سرویس وصل نشد" + (string.IsNullOrEmpty(Error) ? "" : $" ({Error})")
                + (Host.Contains("googleapis", StringComparison.OrdinalIgnoreCase)
                    ? " – سرورهای داخل ایران معمولاً به FCM گوگل دسترسی ندارند؛ در تنظیمات برنامه «WebPush:Proxy» را روی یک پراکسی خروجی بگذارید یا از فایرفاکس استفاده کنید."
                    : "."),
    };
}
