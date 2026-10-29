using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Services.Security;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>آنچه فرم پنل نشان می‌دهد؛ خود رمز SMTP هرگز بیرون نمی‌آید (فقط HasPassword).</summary>
public sealed record EmailProviderView(EmailMode Mode, string? Preset, string? Host, int Port, EmailSecurity Security, string? UserName, bool HasPassword,
    string? FromAddress, string? FromName, string? ReplyTo, string? SiteBaseUrl, int MaxPerHour);

/// <summary>
/// ورودی فرم «سرویس ایمیل». اعداد به‌صورت متن می‌آیند تا رقم فارسی هم پذیرفته شود.
/// Password خالی = رمز قبلی بماند؛ ClearPassword = رمز پاک شود.
/// </summary>
public sealed record EmailProviderInput(EmailMode Mode, string? Preset, string? Host, string? Port, EmailSecurity Security, string? UserName, string? Password,
    bool ClearPassword, string? FromAddress, string? FromName, string? ReplyTo, string? SiteBaseUrl, string? MaxPerHour);

/// <summary>
/// تنظیمات آمادهٔ ارسال (رمز رمزگشایی‌شده) برای صف ایمیل در لایهٔ وب و گزارش‌های خودکار.
/// PasswordSource: «پنل مدیریت»، «تنظیمات سرور» یا null (رمزی نیست).
/// </summary>
public sealed record EmailRuntimeConfig(EmailMode Mode, string? Host, int Port, EmailSecurity Security, string? UserName, string? Password, string? PasswordSource,
    string? FromAddress, string? FromName, string? ReplyTo, string? SiteBaseUrl, int MaxPerHour)
{
    public static readonly EmailRuntimeConfig Default = new(EmailMode.LogOnly, null, 587, EmailSecurity.Auto, null, null, null, null, null, null, null,
        Round12DefaultsSeeder.DefaultEmailMaxPerHour);

    /// <summary>برای ارسال واقعی همه چیز لازم هست؟ (حالت SMTP + سرور + فرستنده.)</summary>
    public bool IsSmtpReady => Mode == EmailMode.Smtp && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    /// <summary>
    /// تنظیمات اتصال برای ارسال‌کنندهٔ MailKit؛ اگر SMTP آماده نیست null.
    /// استفاده: صف ایمیل و «گزارش‌های خودکار/هشدارها» وقتی SMTP جدای خودشان را ندارند.
    /// </summary>
    public SmtpSettings? ToSmtp(string? fallbackFromName = null) => IsSmtpReady
        ? new SmtpSettings(Host!, Port, UserName, Password, FromAddress!, string.IsNullOrWhiteSpace(FromName) ? fallbackFromName : FromName, Security, ReplyTo)
        : null;

    /// <summary>چرا ایمیل واقعی نمی‌رود؟ برای پیام پنل. مثال: «حالت فقط لاگ است».</summary>
    public string? NotReadyReason => Mode switch
    {
        EmailMode.Disabled => "سرویس ایمیل خاموش است",
        EmailMode.LogOnly => "حالت «فقط لاگ» فعال است (ایمیل واقعی ارسال نمی‌شود)",
        _ when string.IsNullOrWhiteSpace(Host) => "نشانی سرور SMTP ثبت نشده",
        _ when string.IsNullOrWhiteSpace(FromAddress) => "نشانی فرستنده ثبت نشده",
        _ => null,
    };
}

/// <summary>
/// یک سرویس ایمیل آماده برای «اتصال سریع»: با انتخاب آن، سرور، درگاه و رمزنگاری خودکار پر می‌شود.
/// Host ممکن است «{domain}» داشته باشد (ایمیل هاست) که با دامنهٔ سایت جایگزین می‌شود. مثال: mail.{domain} → mail.fratak.ir.
/// </summary>
public sealed record EmailPreset(string Key, string Title, string Host, int Port, EmailSecurity Security, string UserHint, string Help);

/// <summary>فهرست سرویس‌های آماده (رایگان یا دارای پلن رایگان). استفاده: فرم پنل و assets/js/admin-email.js.</summary>
public static class EmailPresets
{
    public static readonly IReadOnlyList<EmailPreset> All =
    [
        new("gmail", "جیمیل (Gmail)", "smtp.gmail.com", 587, EmailSecurity.StartTls, "you@gmail.com",
            "رمز معمولی جیمیل کار نمی‌کند: در حساب گوگل «تأیید دومرحله‌ای» را روشن کنید، سپس در myaccount.google.com/apppasswords یک «App Password» شانزده‌حرفی بسازید و همان را اینجا بگذارید. فرستنده باید همان حساب جیمیل باشد. سقف تقریبی: ۵۰۰ ایمیل در روز."),
        new("cpanel", "ایمیل هاست (سی‌پنل / دایرکت‌ادمین / پلسک)", "mail.{domain}", 465, EmailSecurity.SslOnConnect, "no-reply@your-domain.ir",
            "در پنل هاست یک صندوق ایمیل (مثلاً no-reply@دامنهٔ‌شما) بسازید. نام کاربری = نشانی کامل همان ایمیل و رمز = رمز همان صندوق. اگر ۴۶۵ وصل نشد، ۵۸۷ با STARTTLS را امتحان کنید. ایمیل با دامنهٔ خودتان کمتر به اسپم می‌رود."),
        new("zoho", "زوهو میل (Zoho)", "smtp.zoho.com", 465, EmailSecurity.SslOnConnect, "you@your-domain.ir",
            "اگر تأیید دومرحله‌ای روشن است، «App Specific Password» بسازید. برای حساب‌های اروپایی smtp.zoho.eu. در برخی پلن‌های رایگان باید دسترسی SMTP/IMAP را از تنظیمات Zoho روشن کنید."),
        new("yandex", "یاندکس (Yandex)", "smtp.yandex.com", 465, EmailSecurity.SslOnConnect, "you@yandex.com",
            "در تنظیمات یاندکس «App password» از نوع Mail بسازید و دسترسی برنامه‌های ایمیل (IMAP/SMTP) را روشن کنید."),
        new("yahoo", "یاهو (Yahoo)", "smtp.mail.yahoo.com", 465, EmailSecurity.SslOnConnect, "you@yahoo.com",
            "در Account Security یاهو «Generate app password» بزنید و همان رمز را اینجا بگذارید."),
        new("outlook", "آفیس ۳۶۵ / اوت‌لوک", "smtp.office365.com", 587, EmailSecurity.StartTls, "you@your-domain.com",
            "مایکروسافت ورود ساده با رمز را برای بیشتر حساب‌ها بسته است؛ فقط اگر مدیر حساب SMTP AUTH را روشن کرده باشد کار می‌کند. اگر خطای ورود گرفتید، سرویس دیگری انتخاب کنید."),
        new("brevo", "برِوو (Brevo – ۳۰۰ ایمیل رایگان در روز)", "smtp-relay.brevo.com", 587, EmailSecurity.StartTls, "xxxx@smtp-brevo.com",
            "در پنل Brevo ← SMTP & API ← تب SMTP: «Login» را نام کاربری و یک «SMTP key» تازه را رمز بگذارید. نشانی فرستنده باید در Brevo تأیید شده باشد."),
        new("mailgun", "میل‌گان (Mailgun)", "smtp.mailgun.org", 587, EmailSecurity.StartTls, "postmaster@mg.your-domain.ir",
            "در Mailgun ← Sending ← Domain settings ← SMTP credentials یک کاربر بسازید. برای حساب‌های اروپایی smtp.eu.mailgun.org."),
    ];

    public static EmailPreset? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(p => string.Equals(p.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// قاعده‌های خالص ایمیل (بدون پایگاه داده؛ آزمون‌پذیر): اعتبار نشانی، سرور، نشانی سایت و پاک‌سازی سرصفحه‌ها.
/// امنیت: هر مقداری که به سرصفحهٔ ایمیل می‌رود (موضوع، نام فرستنده) از CR/LF پاک می‌شود تا «Header Injection» ممکن نباشد.
/// </summary>
public static partial class EmailRules
{
    /// <summary>بیشترین سقف ساعتی قابل تنظیم.</summary>
    public const int MaxHourlyCap = 10_000;

    /// <summary>
    /// یک نشانی ایمیل معتبر (همان قاعدهٔ گزارش‌های خودکار). مثال: «ali@example.com» ✓، «ali@x» ✗، «a@b.ir, c@d.ir» ✗ (دو نشانی).
    /// </summary>
    public static bool IsValidAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            return false;
        var (valid, invalid) = ScheduledReportRules.ParseEmails(value);
        // نشانی تکراری با جداکننده («a@b.co a@b.co») هم یک مورد معتبر می‌دهد؛ پس کل متن باید دقیقاً همان یک نشانی باشد.
        return valid.Count == 1 && invalid.Count == 0 && string.Equals(valid[0], value.Trim(), StringComparison.Ordinal);
    }

    /// <summary>نام سرور یا IPv4 بدون پروتکل و مسیر. مثال: smtp.gmail.com ✓، mail.fratak.ir ✓، https://x ✗، «smtp gmail» ✗.</summary>
    public static bool IsValidHost(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && HostRegex().IsMatch(value);

    /// <summary>
    /// متن یک‌خطی امن برای سرصفحه: نویسه‌های کنترلی (از جمله خط جدید) → فاصله، فاصله‌های پشت‌سرهم → یکی، و برش به حداکثر طول.
    /// مثال: «سلام\r\nBcc: x@y» → «سلام Bcc: x@y» (دیگر سرصفحهٔ جدا نیست).
    /// </summary>
    public static string OneLine(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var chars = value.Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        var text = SpacesRegex().Replace(new string(chars), " ").Trim();
        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd();
    }

    /// <summary>
    /// نشانی سایت برای لینک‌های ایمیل: فقط http/https مطلق، بدون نام کاربری، پرس‌وجو یا #. خروجی بدون / پایانی.
    /// مثال: «https://Fratak.ir/» → «https://fratak.ir»؛ «fratak.ir» → null؛ «javascript:alert(1)» → null.
    /// </summary>
    public static string? NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return null;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || string.IsNullOrEmpty(uri.Host))
            return null;
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{uri.Scheme}://{uri.Authority.ToLowerInvariant()}{path}";
    }

    /// <summary>
    /// نشانی نسبی سایت → مطلق برای ایمیل. فقط مسیرهای «/…» (نه «//…») پذیرفته می‌شوند؛ نشانی مطلق فقط اگر هم‌دامنهٔ سایت باشد.
    /// مثال: («/profile/orders», «https://fratak.ir») → «https://fratak.ir/profile/orders»؛ («https://evil.com») → null.
    /// </summary>
    public static string? AbsoluteLink(string? url, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(baseUrl))
            return null;
        url = url.Trim();
        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains('\\'))
            return baseUrl + url;
        return url.StartsWith(baseUrl + "/", StringComparison.OrdinalIgnoreCase) ? url : null;
    }

    /// <summary>رمزنگاری واقعی برای یک درگاه وقتی «خودکار» انتخاب شده. مثال: (Auto, 465) → SslOnConnect؛ (Auto, 587) → StartTls.</summary>
    public static EmailSecurity Effective(EmailSecurity security, int port) => security != EmailSecurity.Auto
        ? security
        : port switch { 465 => EmailSecurity.SslOnConnect, 587 => EmailSecurity.StartTls, _ => EmailSecurity.Auto };

    /// <summary>سرور محلی/داخلی؟ (اتصال بی‌رمزنگاری فقط برای این‌ها قابل قبول است.)</summary>
    public static bool IsLocalHost(string? host) =>
        host is not null && (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.StartsWith("127.", StringComparison.Ordinal)
            || host.StartsWith("10.", StringComparison.Ordinal) || host.StartsWith("192.168.", StringComparison.Ordinal));

    /// <summary>
    /// نشانی را برای لاگ و پنل نیمه‌پنهان می‌کند. مثال: «ali.rezaei@gmail.com» → «al***@gmail.com».
    /// </summary>
    public static string Mask(string? address)
    {
        if (string.IsNullOrEmpty(address))
            return "-";
        var at = address.IndexOf('@');
        if (at <= 0)
            return "***";
        var keep = Math.Min(2, at);
        return address[..keep] + "***" + address[at..];
    }

    [GeneratedRegex(@"^(?=.{1,200}$)([A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)(\.[A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)*$")]
    private static partial Regex HostRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex SpacesRegex();
}

/// <summary>
/// خواندن و ذخیرهٔ تنظیمات سرویس ایمیل (جدول EmailProviderConfigs، یک ردیف).
/// استفاده: صفحهٔ پنل «فنی ← سرویس ایمیل» (Get/Save)، صف ایمیل در لایهٔ وب و گزارش‌های خودکار (GetRuntime).
/// </summary>
public interface IEmailProviderService
{
    Task<EmailProviderView> GetAsync(CancellationToken cancellationToken = default);

    /// <param name="requestBaseUrl">نشانی‌ای که مدیر با آن وارد پنل شده؛ اگر «نشانی سایت» خالی بماند همین ذخیره می‌شود. مثال: https://fratak.ir</param>
    Task<OperationResult> SaveAsync(EmailProviderInput input, string? requestBaseUrl, CancellationToken cancellationToken = default);

    /// <summary>تنظیمات با رمز رمزگشایی‌شده؛ یک دقیقه در حافظه می‌ماند و با ذخیره فوراً تازه می‌شود.</summary>
    Task<EmailRuntimeConfig> GetRuntimeAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class EmailProviderService(FratakShopDbContext context, ISecretProtector secrets, IMemoryCache cache, IEmailServerSecret serverSecret)
    : IEmailProviderService
{
    public const string CacheKey = "email:provider:runtime";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(1);

    public async Task<EmailProviderView> GetAsync(CancellationToken cancellationToken = default)
    {
        var c = await LoadAsync(tracking: false, cancellationToken);
        var d = EmailRuntimeConfig.Default;
        return c is null
            ? new EmailProviderView(d.Mode, null, null, d.Port, d.Security, null, false, null, null, null, null, d.MaxPerHour)
            : new EmailProviderView(c.Mode, c.Preset, c.Host, c.Port, c.Security, c.UserName, !string.IsNullOrEmpty(c.PasswordProtected),
                c.FromAddress, c.FromName, c.ReplyTo, c.SiteBaseUrl, c.MaxPerHour);
    }

    public async Task<OperationResult> SaveAsync(EmailProviderInput input, string? requestBaseUrl, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(input.Mode) || !Enum.IsDefined(input.Security))
            return OperationResult.Error("حالت سرویس یا نوع رمزنگاری معتبر نیست.");

        var smtp = input.Mode == EmailMode.Smtp;
        var preset = EmailPresets.Find(input.Preset)?.Key;

        var host = input.Host?.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(host))
            host = null;
        if (host is not null && !EmailRules.IsValidHost(host))
            return OperationResult.Error("نشانی سرور SMTP معتبر نیست؛ فقط نام سرور، بدون https:// و مسیر. مثال: smtp.gmail.com");
        if (smtp && host is null)
            return OperationResult.Error("برای ارسال با SMTP، نشانی سرور لازم است. مثال: smtp.gmail.com یا mail.دامنهٔ‌شما.ir");

        if (!SmsProviderService.TryParsePositive(input.Port, 1, 5, out var portValue) || portValue > 65535)
            return OperationResult.Error("درگاه باید عددی بین ۱ تا ۶۵۵۳۵ باشد (معمولاً ۵۸۷ یا ۴۶۵).");
        var port = (int)(portValue ?? 587);

        var userName = EmailRules.OneLine(input.UserName, 201);
        if (userName.Length > 200)
            return OperationResult.Error("نام کاربری SMTP حداکثر ۲۰۰ نویسه است.");

        var password = input.Password;
        if (!string.IsNullOrEmpty(password))
        {
            // App Password جیمیل با فاصله نمایش داده می‌شود («abcd efgh ijkl mnop»)؛ فاصله‌ها جزء رمز نیستند.
            if (host == "smtp.gmail.com")
                password = password.Replace(" ", "", StringComparison.Ordinal);
            if (password.Length > 500 || password.Any(char.IsControl))
                return OperationResult.Error("رمز SMTP معتبر نیست (حداکثر ۵۰۰ نویسه و بدون خط جدید).");
        }

        var from = string.IsNullOrWhiteSpace(input.FromAddress) ? null : input.FromAddress.Trim();
        if (from is not null && !EmailRules.IsValidAddress(from))
            return OperationResult.Error("نشانی فرستنده معتبر نیست. مثال: no-reply@fratak.ir");
        if (smtp && from is null)
            return OperationResult.Error("برای ارسال با SMTP، نشانی فرستنده لازم است.");

        var fromName = EmailRules.OneLine(input.FromName, 100).Replace("\"", "", StringComparison.Ordinal).Replace("<", "", StringComparison.Ordinal)
            .Replace(">", "", StringComparison.Ordinal).Trim();
        var replyTo = string.IsNullOrWhiteSpace(input.ReplyTo) ? null : input.ReplyTo.Trim();
        if (replyTo is not null && !EmailRules.IsValidAddress(replyTo))
            return OperationResult.Error("نشانی پاسخ (Reply-To) معتبر نیست. مثال: support@fratak.ir");

        string? baseUrl = null;
        if (!string.IsNullOrWhiteSpace(input.SiteBaseUrl))
        {
            baseUrl = EmailRules.NormalizeBaseUrl(input.SiteBaseUrl);
            if (baseUrl is null)
                return OperationResult.Error("نشانی سایت باید کامل و با https:// باشد، بدون ? و #. مثال: https://fratak.ir");
        }
        baseUrl ??= EmailRules.NormalizeBaseUrl(requestBaseUrl);

        if (!SmsProviderService.TryParsePositive(input.MaxPerHour, 1, 5, out var capValue) && input.MaxPerHour?.Trim() is not ("0" or "۰"))
            return OperationResult.Error($"سقف ساعتی باید عددی بین ۰ تا {EmailRules.MaxHourlyCap:#,0} باشد (۰ = بدون سقف).");
        var cap = input.MaxPerHour?.Trim() is "0" or "۰" ? 0 : (int)(capValue ?? Round12DefaultsSeeder.DefaultEmailMaxPerHour);
        if (cap > EmailRules.MaxHourlyCap)
            return OperationResult.Error($"سقف ساعتی حداکثر {EmailRules.MaxHourlyCap:#,0} است.");

        var config = await LoadAsync(tracking: true, cancellationToken);
        if (config is null)
        {
            config = new EmailProviderConfig();
            context.EmailProviderConfigs.Add(config);
        }

        config.Mode = input.Mode;
        config.Preset = preset;
        config.Host = host;
        config.Port = port;
        config.Security = input.Security;
        config.UserName = userName.Length == 0 ? null : userName;
        config.FromAddress = from;
        config.FromName = fromName.Length == 0 ? null : fromName;
        config.ReplyTo = replyTo;
        config.SiteBaseUrl = baseUrl;
        config.MaxPerHour = cap;
        if (input.ClearPassword)
            config.PasswordProtected = null;
        else if (!string.IsNullOrEmpty(password))
            config.PasswordProtected = secrets.Protect(password);

        await context.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey);

        var warnings = new List<string>();
        if (smtp && config.UserName is not null && string.IsNullOrEmpty(config.PasswordProtected) && string.IsNullOrEmpty(serverSecret.Password))
            warnings.Add("رمز SMTP ثبت نشده است؛ بیشتر سرورها بدون رمز ایمیل نمی‌پذیرند.");
        if (smtp && host == "smtp.gmail.com" && config.UserName is not null && !string.Equals(config.UserName, from, StringComparison.OrdinalIgnoreCase))
            warnings.Add("در جیمیل نشانی فرستنده باید همان حساب ورود باشد؛ وگرنه گوگل فرستنده را به حساب ورود برمی‌گرداند.");
        if (smtp && EmailRules.Effective(input.Security, port) == EmailSecurity.None && !EmailRules.IsLocalHost(host))
            warnings.Add("اتصال بدون رمزنگاری انتخاب شده؛ رمز و متن ایمیل‌ها بی‌حفاظت جابه‌جا می‌شوند.");
        if (config.SiteBaseUrl is null)
            warnings.Add("نشانی سایت مشخص نیست؛ لوگو و دکمه‌های داخل ایمیل نمایش داده نمی‌شوند.");
        if (smtp)
            warnings.Add("حالا «ایمیل آزمایشی» بفرستید تا از اتصال مطمئن شوید.");
        return OperationResult.Success("تنظیمات سرویس ایمیل ذخیره شد." + (warnings.Count == 0 ? "" : " " + string.Join(" ", warnings)));
    }

    public async Task<EmailRuntimeConfig> GetRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<EmailRuntimeConfig>(CacheKey, out var cached) && cached is not null)
            return cached;

        var c = await LoadAsync(tracking: false, cancellationToken);
        EmailRuntimeConfig runtime;
        if (c is null)
        {
            runtime = EmailRuntimeConfig.Default;
        }
        else
        {
            var stored = Unprotect(c.PasswordProtected);
            var server = serverSecret.Password;
            var (password, source) = !string.IsNullOrEmpty(stored) ? (stored, "پنل مدیریت")
                : !string.IsNullOrEmpty(server) ? (server, "تنظیمات سرور")
                : ((string?)null, (string?)null);
            runtime = new EmailRuntimeConfig(c.Mode, c.Host, c.Port, c.Security, c.UserName, password, source, c.FromAddress, c.FromName, c.ReplyTo,
                c.SiteBaseUrl, c.MaxPerHour);
        }
        cache.Set(CacheKey, runtime, CacheLifetime);
        return runtime;
    }

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        try
        {
            return secrets.Unprotect(value);
        }
        catch (Exception)
        {
            // کلید Data Protection عوض شده (مثلاً سرور جدید)؛ مدیر باید رمز را دوباره وارد کند. پنل «رمز ثبت نشده» نشان می‌دهد.
            return null;
        }
    }

    private Task<EmailProviderConfig?> LoadAsync(bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? context.EmailProviderConfigs.AsTracking() : context.EmailProviderConfigs.AsNoTracking();
        return query.OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
    }
}
