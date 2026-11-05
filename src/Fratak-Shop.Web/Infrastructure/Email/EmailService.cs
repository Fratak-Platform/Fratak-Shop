using System.Net;
using System.Text;
using System.Threading.Channels;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Branding;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Settings;
using MimeKit;

namespace Fratak_Shop.Web.Infrastructure.Email;

// ═══════════════════════════════════════════════════════════════════════════
//  مرحلهٔ دوازدهم – سرویس ایمیل فروشگاه.
//
//  مسیر یک ایمیل:
//    NotificationService (یا هر سرویس دیگر) → IEmailSender.Enqueue → EmailQueue (صف حافظه، بدون انتظار)
//      → EmailDispatcher (سرویس پس‌زمینه؛ رعایت سقف ساعتی و یک تلاش دوباره) → EmailDelivery
//      → EmailComposer (قالب راست‌به‌چپ با لوگوی محل «ایمیل‌ها» از استودیوی لوگو) → SmtpTransport (MailKit).
//  تنظیمات: پنل ← فنی ← سرویس ایمیل (/admin/system/email)؛ رمز رمزنگاری‌شده در پایگاه داده یا متغیر محیطی Email__Password.
//  حالت «فقط لاگ»: هیچ ایمیلی نمی‌رود؛ بیرون از Production موضوع و متن در لاگ نوشته می‌شود (برای توسعه).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>آمار ارسال ایمیل از زمان روشن شدن برنامه (برای پنل و سلامت سامانه). Thread-safe.</summary>
public sealed class EmailDiagnostics
{
    private long _sent, _failed, _skipped, _dropped;
    private readonly object _gate = new();

    public long Sent => Interlocked.Read(ref _sent);
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>ارسال نشد چون سرویس خاموش/فقط‌لاگ است.</summary>
    public long Skipped => Interlocked.Read(ref _skipped);

    /// <summary>صف پر بود یا نشانی نامعتبر بود و ایمیل پذیرفته نشد.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public DateTimeOffset? LastSuccessAt { get; private set; }
    public DateTimeOffset? LastFailureAt { get; private set; }
    public string? LastFailure { get; private set; }

    public void Success()
    {
        Interlocked.Increment(ref _sent);
        lock (_gate)
            LastSuccessAt = DateTimeOffset.UtcNow;
    }

    public void Failure(string reason)
    {
        Interlocked.Increment(ref _failed);
        lock (_gate)
        {
            LastFailureAt = DateTimeOffset.UtcNow;
            LastFailure = reason.Length <= 500 ? reason : reason[..500];
        }
    }

    public void Skip() => Interlocked.Increment(ref _skipped);

    public void Drop() => Interlocked.Increment(ref _dropped);

    /// <summary>آخرین تلاش ناموفق‌تر از آخرین موفق است (برای هشدار سلامت).</summary>
    public bool LastAttemptFailed => LastFailureAt is { } failed && (LastSuccessAt is not { } ok || failed > ok);
}

/// <summary>
/// صف حافظهٔ ایمیل‌ها (پیاده‌سازی <see cref="IEmailSender"/>). محدود به ۵٬۰۰۰ پیام: اگر سرور ایمیل مدتی در دسترس نباشد
/// حافظه بی‌نهایت پر نمی‌شود و پیام‌های اضافه رد و شمرده می‌شوند. هرگز منتظر نمی‌ماند و خطا پرتاب نمی‌کند.
/// </summary>
public sealed class EmailQueue(EmailDiagnostics diagnostics, ILogger<EmailQueue> logger) : IEmailSender
{
    public const int Capacity = 5000;

    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
    });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    /// <summary>تعداد ایمیل‌های منتظر در صف.</summary>
    public int Pending => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    public bool Enqueue(EmailMessage message)
    {
        if (message is null || !EmailRules.IsValidAddress(message.To) || string.IsNullOrWhiteSpace(message.Subject) || string.IsNullOrWhiteSpace(message.Body))
        {
            diagnostics.Drop();
            return false;
        }
        if (_channel.Writer.TryWrite(message))
            return true;
        diagnostics.Drop();
        logger.LogWarning("Email queue is full ({Capacity}); an email ({Category}) was dropped", Capacity, message.Category);
        return false;
    }
}

/// <summary>
/// ساخت ایمیل برند‌دار: جدول‌های ساده و استایل درون‌خطی (سازگار با جیمیل، اوت‌لوک و برنامه‌های موبایل)، راست‌به‌چپ،
/// لوگوی محل «ایمیل‌ها» از استودیوی لوگو، دکمهٔ رنگ برند و پانویس با اطلاعات تماس. نسخهٔ متنی هم ساخته می‌شود.
/// امنیت: همهٔ متن‌ها HtmlEncode می‌شوند؛ لینک دکمه فقط مسیر همین سایت (EmailRules.AbsoluteLink) است.
/// </summary>
public static class EmailComposer
{
    private const string Font = "Tahoma,'Segoe UI',Arial,sans-serif";

    /// <summary>داده‌های ثابت هر ایمیل. BaseUrl خالی = لوگو و دکمه ساخته نمی‌شوند (ایمیل آدرس نسبی نمی‌فهمد).</summary>
    public sealed record Context(string SiteName, string? BaseUrl, BrandView Brand, string? SupportPhone, string? SupportEmail);

    /// <summary>
    /// خروجی: (موضوع یک‌خطی، HTML، متن ساده).
    /// مثال: Compose(new EmailMessage(to, "سفارش ارسال شد", "سفارش FR-1 تحویل پست شد.") { ActionUrl = "/profile/orders" }, ctx).
    /// </summary>
    public static (string Subject, string Html, string Text) Compose(EmailMessage message, Context ctx)
    {
        var subject = EmailRules.OneLine(message.Subject, 200);
        if (subject.Length == 0)
            subject = ctx.SiteName;
        var body = (message.Body ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var link = EmailRules.AbsoluteLink(message.ActionUrl, ctx.BaseUrl);
        var actionText = EmailRules.OneLine(message.ActionText, 60) is { Length: > 0 } t ? t : "مشاهده در سایت";
        var accent = ctx.Brand.Color2;

        var html = new StringBuilder(4000);
        html.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"color-scheme\" content=\"light\">")
            .Append("<title>").Append(Enc(subject)).Append("</title></head>")
            .Append("<body style=\"margin:0;padding:0;background:#F1F5F9;\">");
        // پیش‌متن پنهان: خط خاکستری کنار موضوع در فهرست صندوق.
        html.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;color:transparent\">")
            .Append(Enc(Preheader(body))).Append("</div>");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background:#F1F5F9\"><tr><td align=\"center\" style=\"padding:24px 12px\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"max-width:600px;background:#FFFFFF;border-radius:16px;border-top:4px solid ")
            .Append(accent).Append("\">");

        // سربرگ: لوگو (اگر نشانی سایت معلوم و لوگوی ایمیل روشن است) وگرنه نام فروشگاه.
        var brandHtml = ctx.BaseUrl is { } baseUrl ? BrandLockupHtml.RenderEmail(ctx.Brand, baseUrl) : "";
        html.Append("<tr><td align=\"center\" dir=\"rtl\" style=\"padding:28px 28px 6px;font-family:").Append(Font).Append("\">");
        if (brandHtml.Length > 0)
            html.Append(brandHtml);
        else if (ctx.Brand.Visible)
            html.Append("<div style=\"font-size:22px;font-weight:bold;color:").Append(ctx.Brand.Color1 == "#FFFFFF" ? "#0B1927" : ctx.Brand.Color1).Append("\">").Append(Enc(ctx.SiteName)).Append("</div>");
        html.Append("</td></tr>");

        // متن.
        html.Append("<tr><td dir=\"rtl\" style=\"padding:18px 28px 8px;font-family:").Append(Font).Append(";text-align:right;color:#0B1927\">")
            .Append("<h1 style=\"margin:0 0 14px;font-size:19px;line-height:1.7;font-weight:bold;color:#0B1927\">").Append(Enc(subject)).Append("</h1>");
        foreach (var paragraph in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            html.Append("<p style=\"margin:0 0 12px;font-size:15px;line-height:2;color:#334155\">").Append(Enc(paragraph).Replace("\n", "<br>", StringComparison.Ordinal)).Append("</p>");
        if (link is not null)
        {
            html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:18px 0 8px\"><tr><td align=\"center\" bgcolor=\"")
                .Append(accent).Append("\" style=\"border-radius:10px\"><a href=\"").Append(Enc(link))
                .Append("\" target=\"_blank\" style=\"display:inline-block;padding:12px 28px;font-family:").Append(Font)
                .Append(";font-size:14px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px\">").Append(Enc(actionText)).Append("</a></td></tr></table>");
        }
        html.Append("</td></tr>");

        // پانویس.
        html.Append("<tr><td dir=\"rtl\" style=\"padding:18px 28px 24px;font-family:").Append(Font)
            .Append(";font-size:12px;line-height:2;color:#94A3B8;text-align:center;border-top:1px solid #E2E8F0\">")
            .Append("این ایمیل به‌صورت خودکار از ");
        if (ctx.BaseUrl is not null)
            html.Append("<a href=\"").Append(Enc(ctx.BaseUrl)).Append("\" style=\"color:#64748B;text-decoration:none\">").Append(Enc(ctx.SiteName)).Append("</a>");
        else
            html.Append(Enc(ctx.SiteName));
        html.Append(" ارسال شده است.");
        var contact = new List<string>();
        if (!string.IsNullOrWhiteSpace(ctx.SupportPhone))
            contact.Add("تلفن: <span dir=\"ltr\">" + Enc(ctx.SupportPhone.Trim()) + "</span>");
        if (EmailRules.IsValidAddress(ctx.SupportEmail))
            contact.Add("ایمیل: <span dir=\"ltr\">" + Enc(ctx.SupportEmail!.Trim()) + "</span>");
        if (contact.Count > 0)
            html.Append("<br>پشتیبانی: ").Append(string.Join(" · ", contact));
        html.Append("</td></tr></table></td></tr></table></body></html>");

        var text = new StringBuilder();
        text.AppendLine(subject).AppendLine().AppendLine(body);
        if (link is not null)
            text.AppendLine().Append(actionText).Append(": ").AppendLine(link);
        text.AppendLine().Append("— ").Append(ctx.SiteName);
        if (ctx.BaseUrl is not null)
            text.Append(" · ").Append(ctx.BaseUrl);
        return (subject, html.ToString(), text.ToString());
    }

    /// <summary>۱۲۰ نویسهٔ اول متن در یک خط. مثال: «سفارش FR-1 تحویل پست شد. کد رهگیری …».</summary>
    public static string Preheader(string body) => EmailRules.OneLine(body, 120);

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}

/// <summary>
/// ارسال یک ایمیل با تنظیمات فعلی پنل: ساخت قالب، رعایت حالت سرویس و ثبت آمار. Singleton؛ برای هر ارسال یک scope تازه می‌سازد.
/// استفاده: EmailDispatcher (صف) و صفحهٔ پنل «فنی ← سرویس ایمیل» (بررسی اتصال و ایمیل آزمایشی).
/// </summary>
public sealed class EmailDelivery(IServiceScopeFactory scopes, IWebHostEnvironment environment, EmailDiagnostics diagnostics, ILogger<EmailDelivery> logger)
{
    public EmailDiagnostics Diagnostics => diagnostics;

    public async Task<EmailRuntimeConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEmailProviderService>().GetRuntimeAsync(cancellationToken);
    }

    /// <summary>
    /// ساخت و ارسال. <paramref name="forceSmtp"/> = true فقط برای «ایمیل آزمایشی» پنل: حتی در حالت «فقط لاگ» با SMTP ثبت‌شده
    /// ارسال می‌کند تا مدیر پیش از روشن کردن سرویس از درستی تنظیمات مطمئن شود.
    /// </summary>
    public async Task<DeliveryAttempt> SendAsync(EmailMessage message, CancellationToken cancellationToken, bool forceSmtp = false)
    {
        await using var scope = scopes.CreateAsyncScope();
        var config = await scope.ServiceProvider.GetRequiredService<IEmailProviderService>().GetRuntimeAsync(cancellationToken);
        var settings = await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync(cancellationToken);

        if (!forceSmtp && config.Mode != EmailMode.Smtp)
        {
            diagnostics.Skip();
            if (config.Mode == EmailMode.LogOnly && !environment.IsProduction())
                logger.LogInformation("Email (log only) to {To} [{Category}]: {Subject}\n{Body}", EmailRules.Mask(message.To), message.Category, message.Subject, message.Body);
            return new DeliveryAttempt(false, config.NotReadyReason ?? "سرویس ایمیل فعال نیست");
        }

        var smtp = ToSmtp(config, settings.SiteName);
        if (smtp is null)
        {
            var reason = string.IsNullOrWhiteSpace(config.Host) ? "نشانی سرور SMTP ثبت نشده است." : "نشانی فرستنده ثبت نشده است.";
            diagnostics.Failure(reason);
            return new DeliveryAttempt(false, reason);
        }

        var (subject, html, text) = EmailComposer.Compose(message, Context(settings, config));
        var mime = new MimeMessage();
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = subject;
        // سرآیند استاندارد پیام خودکار: پاسخ خودکار «خارج از دفتر» برنمی‌گردد.
        mime.Headers.Add("Auto-Submitted", "auto-generated");
        mime.Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody();

        var attempt = await SmtpTransport.SendAsync(smtp, mime, logger, cancellationToken);
        if (attempt.Ok)
            diagnostics.Success();
        else
            diagnostics.Failure(attempt.Message);
        return attempt;
    }

    /// <summary>بررسی اتصال و ورود SMTP بدون ارسال (حتی در حالت «فقط لاگ»، تا پیش از روشن کردن آزمایش شود).</summary>
    public async Task<DeliveryAttempt> CheckAsync(CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(config.Host))
            return new DeliveryAttempt(false, "نشانی سرور SMTP ثبت نشده است.");
        var smtp = new SmtpSettings(config.Host, config.Port, config.UserName, config.Password, config.FromAddress ?? "check@localhost", null, config.Security);
        return await SmtpTransport.CheckAsync(smtp, logger, cancellationToken);
    }

    /// <summary>پیش‌نمایش HTML همان قالبی که مشتری می‌بیند (صفحهٔ پنل، در iframe بدون اسکریپت).</summary>
    public async Task<string> PreviewAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var config = await scope.ServiceProvider.GetRequiredService<IEmailProviderService>().GetRuntimeAsync(cancellationToken);
        var settings = await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync(cancellationToken);
        return EmailComposer.Compose(message, Context(settings, config)).Html;
    }

    /// <summary>SMTP آماده با حالت اجباری (برای آزمایش در حالت «فقط لاگ» هم کار می‌کند).</summary>
    private static SmtpSettings? ToSmtp(EmailRuntimeConfig config, string? siteName) =>
        string.IsNullOrWhiteSpace(config.Host) || string.IsNullOrWhiteSpace(config.FromAddress)
            ? null
            : new SmtpSettings(config.Host, config.Port, config.UserName, config.Password, config.FromAddress,
                string.IsNullOrWhiteSpace(config.FromName) ? siteName : config.FromName, config.Security, config.ReplyTo);

    private static EmailComposer.Context Context(StoreSettingDto settings, EmailRuntimeConfig config)
    {
        var brand = BrandResolver.Resolve(BrandPlace.Email, settings, BrandStyleRules.Parse(settings.BrandStyleJson), BrandIdentity.Defaults);
        var siteName = string.IsNullOrWhiteSpace(settings.SiteName) ? "فروشگاه" : settings.SiteName.Trim();
        return new EmailComposer.Context(siteName, EmailRules.NormalizeBaseUrl(config.SiteBaseUrl), brand, settings.SupportPhone, settings.SupportEmail);
    }
}

/// <summary>
/// سرویس پس‌زمینهٔ ارسال صف ایمیل. سقف ساعتی پنل را رعایت می‌کند (ایمیل‌های اضافه منتظر می‌مانند، دور ریخته نمی‌شوند)
/// و برای خطاهای موقت (قطع اتصال، مهلت) یک بار پس از ۳۰ ثانیه دوباره تلاش می‌کند. هیچ خطایی بیرون نمی‌زند.
/// </summary>
public sealed class EmailDispatcher(EmailQueue queue, EmailDelivery delivery, ILogger<EmailDispatcher> logger) : BackgroundService
{
    public const string JobName = "ارسال ایمیل";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly Queue<DateTimeOffset> _recent = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // صف‌محور: فقط وقتی پیامی هست اجرا می‌شود، پس «کار عقب‌افتاده» گزارش نمی‌شود.
        Admin.JobMonitor.Register(JobName, TimeSpan.FromDays(3650), pausable: false);
        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    var config = await delivery.GetConfigAsync(stoppingToken);
                    if (config.Mode == EmailMode.Smtp)
                        await WaitForSlotAsync(config.MaxPerHour, stoppingToken);

                    var attempt = await Admin.JobMonitor.TrackAsync(JobName, () => delivery.SendAsync(message, stoppingToken));
                    if (config.Mode == EmailMode.Smtp)
                        _recent.Enqueue(DateTimeOffset.UtcNow);
                    if (!attempt.Ok && config.Mode == EmailMode.Smtp && IsTransient(attempt.Message))
                    {
                        await Task.Delay(RetryDelay, stoppingToken);
                        await WaitForSlotAsync(config.MaxPerHour, stoppingToken);
                        await delivery.SendAsync(message, stoppingToken);
                        _recent.Enqueue(DateTimeOffset.UtcNow);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Email delivery failed ({Category})", message.Category);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // خاموش شدن برنامه.
        }
    }

    /// <summary>
    /// اگر در یک ساعت گذشته به سقف رسیده‌ایم، تا آزاد شدن قدیمی‌ترین جا صبر می‌کند. ۰ = بدون سقف.
    /// مثال: سقف ۱۰۰ و صدمین ایمیل ساعت ۱۰:۰۰ → ایمیل بعدی پس از رسیدن ساعت به زمان اولین ایمیل + ۱ ساعت.
    /// </summary>
    private async Task WaitForSlotAsync(int maxPerHour, CancellationToken cancellationToken)
    {
        if (maxPerHour <= 0)
            return;
        while (true)
        {
            var hourAgo = DateTimeOffset.UtcNow.AddHours(-1);
            while (_recent.Count > 0 && _recent.Peek() <= hourAgo)
                _recent.Dequeue();
            if (_recent.Count < maxPerHour)
                return;
            var wait = _recent.Peek().AddHours(1) - DateTimeOffset.UtcNow;
            logger.LogInformation("Email hourly cap ({Cap}) reached; waiting {Minutes:0.#} minutes", maxPerHour, wait.TotalMinutes);
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    /// <summary>خطای موقت (ارزش دوباره‌تلاش دارد): قطع اتصال یا مهلت؛ نه رمز اشتباه یا نشانی نامعتبر.</summary>
    public static bool IsTransient(string message) =>
        message.Contains("مهلت", StringComparison.Ordinal) || message.Contains("قطع شد", StringComparison.Ordinal)
        || message.Contains("برقرار نشد", StringComparison.Ordinal) || message.Contains("(4", StringComparison.Ordinal);
}

/// <summary>رمز SMTP از تنظیمات سرور: کلید Email:Password (متغیر محیطی Email__Password).</summary>
public sealed class ConfigurationEmailServerSecret(IConfiguration configuration) : IEmailServerSecret
{
    public const string ConfigurationKey = "Email:Password";

    public string? Password => configuration[ConfigurationKey] is { Length: > 0 } value ? value : null;
}
