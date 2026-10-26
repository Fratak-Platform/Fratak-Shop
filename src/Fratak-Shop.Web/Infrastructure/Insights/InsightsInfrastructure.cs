using System.Net.Http.Json;
using System.Text.Json;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Insights;
using Fratak_Shop.Web.Infrastructure.Admin;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Fratak_Shop.Web.Infrastructure.Insights;

// ═══════════════════════════════════════════════════════════════════════════
//  زیرساخت وب گزارش‌های مدیریتی (مرحلهٔ هفتم – فاز ۷ب)
//   • ReportDelivery : پیاده‌سازی IReportDelivery – ایمیل با MailKit (SMTP خود فروشگاه، رایگان) و پیام ربات بله/تلگرام
//                      با API رسمی و رایگان ربات‌ها (sendMessage).
//   • InsightsWorker : سرویس پس‌زمینه؛ هر ۵ دقیقه گزارش‌های سررسیده را می‌فرستد و یادآوری سررسید هزینه‌ها را می‌سازد.
//  ثبت در Extensions/WebServiceCollectionExtensions.cs.
//  نکته: تلگرام از سرورهای داخل ایران معمولاً در دسترس نیست؛ بله (پیام‌رسان داخلی) از داخل کار می‌کند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// ارسال واقعی گزارش. هیچ‌وقت خطا پرتاب نمی‌کند؛ پیام خطا کوتاه و بدون توکن/رمز برمی‌گردد تا در «وضعیت آخرین ارسال» نمایش داده شود.
/// </summary>
public sealed class ReportDelivery(IHttpClientFactory factory, ILogger<ReportDelivery> logger) : IReportDelivery
{
    /// <summary>نام HttpClient ربات‌ها؛ لاگ پیش‌فرض آن حذف شده تا آدرس دارای توکن در لاگ‌ها ننشیند.</summary>
    public const string BotClientName = "report-bots";

    /// <summary>آدرس API ربات‌ها؛ هر دو سرویس قرارداد یکسان دارند (bot{token}/sendMessage).</summary>
    public static string BotEndpoint(ReportChannels channel, string token) => channel switch
    {
        ReportChannels.Bale => $"https://tapi.bale.ai/bot{token}/sendMessage",
        ReportChannels.Telegram => $"https://api.telegram.org/bot{token}/sendMessage",
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public static void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FratakShop-Reports/1.0");
    }

    public async Task<DeliveryAttempt> SendBotMessageAsync(ReportChannels channel, string token, string chatId, string text, CancellationToken cancellationToken)
    {
        try
        {
            var client = factory.CreateClient(BotClientName);
            // شناسهٔ عددی به‌صورت عدد فرستاده می‌شود و @کانال به‌صورت رشته (هر دو را API قبول دارد).
            object chat = long.TryParse(chatId, out var numeric) ? (object)numeric : chatId;
            using var response = await client.PostAsJsonAsync(BotEndpoint(channel, token),
                new { chat_id = chat, text, disable_web_page_preview = true }, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode && BotOk(body))
                return new DeliveryAttempt(true, "ارسال شد");

            var description = BotDescription(body);
            logger.LogWarning("Report bot {Channel} rejected the message: {Status} {Description}", channel, (int)response.StatusCode, description);
            return new DeliveryAttempt(false, (int)response.StatusCode switch
            {
                401 or 404 => "توکن ربات نامعتبر است",
                400 or 403 => $"گفت‌وگو پیدا نشد یا ربات عضو/مدیر آن نیست ({description})",
                429 => "محدودیت تعداد پیام؛ کمی بعد دوباره تلاش می‌شود",
                _ => $"خطای {(int)response.StatusCode}",
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DeliveryAttempt(false, "مهلت اتصال تمام شد (احتمالاً سرویس از این سرور در دسترس نیست)");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Report bot {Channel} connection failed: {Message}", channel, ex.Message);
            return new DeliveryAttempt(false, "اتصال به سرور ربات برقرار نشد");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // هر خطای پیش‌بینی‌نشده هم به «ارسال ناموفق» تبدیل می‌شود تا وضعیت گزارش ثبت شود و سرویس پس‌زمینه نخوابد.
            logger.LogError(ex, "Report bot {Channel} failed unexpectedly", channel);
            return new DeliveryAttempt(false, "خطای پیش‌بینی‌نشده در ارسال پیام ربات");
        }
    }

    public async Task<DeliveryAttempt> SendEmailAsync(SmtpSettings smtp, IReadOnlyList<string> to, string subject, string html, string text, CancellationToken cancellationToken)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(smtp.FromName ?? "", smtp.FromAddress));
            foreach (var address in to)
                message.To.Add(MailboxAddress.Parse(address));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody();

            using var client = new SmtpClient { Timeout = 20_000 };
            await client.ConnectAsync(smtp.Host, smtp.Port, SocketOptions(smtp.Port), cancellationToken);
            if (!string.IsNullOrEmpty(smtp.UserName))
                await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? "", cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return new DeliveryAttempt(true, to.Count == 1 ? "ارسال شد" : $"به {to.Count} گیرنده ارسال شد");
        }
        catch (AuthenticationException)
        {
            return new DeliveryAttempt(false, "نام کاربری یا رمز SMTP اشتباه است");
        }
        catch (SslHandshakeException)
        {
            return new DeliveryAttempt(false, "خطای SSL؛ درگاه (۴۶۵/۵۸۷) را بررسی کنید");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DeliveryAttempt(false, "مهلت اتصال به SMTP تمام شد");
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or IOException or System.Net.Sockets.SocketException or ParseException or TimeoutException)
        {
            logger.LogWarning("Report email failed: {Type} {Message}", ex.GetType().Name, ex.Message);
            var detail = ex.Message.Length > 150 ? ex.Message[..150] : ex.Message;
            return new DeliveryAttempt(false, $"خطای SMTP: {detail}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Report email failed unexpectedly");
            return new DeliveryAttempt(false, "خطای پیش‌بینی‌نشده در ارسال ایمیل");
        }
    }

    /// <summary>۴۶۵ = SSL از ابتدا؛ ۵۸۷ = STARTTLS اجباری؛ بقیه (مثلاً ۲۵) = STARTTLS اگر سرور پشتیبانی کند.</summary>
    public static SecureSocketOptions SocketOptions(int port) => port switch
    {
        465 => SecureSocketOptions.SslOnConnect,
        587 => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.StartTlsWhenAvailable,
    };

    private static bool BotOk(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BotDescription(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
            {
                var text = d.GetString() ?? "";
                return text.Length > 120 ? text[..120] : text;
            }
        }
        catch (JsonException)
        {
        }
        return "بدون توضیح";
    }
}

/// <summary>
/// سرویس پس‌زمینهٔ گزارش‌ها: هر ۵ دقیقه (۱) گزارش‌های زمان‌بندی‌شدهٔ سررسیده را ارسال می‌کند،
/// (۲) از ساعت ۸ صبح به بعد، یادآوری سررسید هزینه‌های تکراری را (روزی یک بار برای هر هزینه) می‌فرستد.
/// وضعیت در صفحهٔ «سیستم» (JobMonitor) دیده می‌شود.
/// </summary>
public sealed class InsightsWorker(IServiceScopeFactory scopeFactory, ILogger<InsightsWorker> logger) : BackgroundService
{
    public const string JobName = "گزارش‌های خودکار و یادآوری هزینه‌ها";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(JobName, TimeSpan.FromMinutes(30));
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var count = await JobMonitor.TrackAsync(JobName, async () =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var now = DateTimeOffset.UtcNow;
                    var sent = await scope.ServiceProvider.GetRequiredService<IScheduledReportService>().RunDueAsync(now, stoppingToken);
                    if (IranClock.ToLocal(now).Hour >= 8)
                        sent += await scope.ServiceProvider.GetRequiredService<IExpenseService>().NotifyDueAsync(IranClock.Today(now), 3, stoppingToken);
                    return sent;
                });
                if (count > 0)
                    logger.LogInformation("Insights worker handled {Count} reports/reminders", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Insights worker run failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
