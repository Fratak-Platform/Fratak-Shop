using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Data.Entities.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Fratak_Shop.Web.Infrastructure.Email;

/// <summary>
/// اتصال و ارسال SMTP با MailKit (مرحلهٔ دوازدهم) – یک جا برای سرویس ایمیل سراسری و گزارش‌های خودکار.
/// <para>
/// هیچ‌وقت خطا پرتاب نمی‌کند؛ همیشه <see cref="DeliveryAttempt"/> با پیام فارسی قابل فهم برای مدیر برمی‌گرداند
/// (رمز یا متن ایمیل هرگز در پیام یا لاگ نمی‌آید). مهلت هر اتصال ۲۰ ثانیه.
/// </para>
/// مثال: SendAsync(smtp, message, logger, ct) → (true, «ارسال شد») یا (false, «نام کاربری یا رمز SMTP اشتباه است…»).
/// </summary>
public static class SmtpTransport
{
    /// <summary>مهلت هر عملیات SMTP (میلی‌ثانیه).</summary>
    public const int TimeoutMs = 20_000;

    /// <summary>
    /// رمزنگاری MailKit از روی تنظیم مدیر. «خودکار»: ۴۶۵ = SSL از ابتدا، ۵۸۷ = STARTTLS اجباری، بقیه = STARTTLS اگر سرور داشت.
    /// مثال: (Auto, 465) → SslOnConnect؛ (None, 25) → None.
    /// </summary>
    public static SecureSocketOptions SocketOptions(EmailSecurity security, int port) => security switch
    {
        EmailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        EmailSecurity.StartTls => SecureSocketOptions.StartTls,
        EmailSecurity.None => SecureSocketOptions.None,
        _ => port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.StartTlsWhenAvailable,
        },
    };

    /// <summary>ارسال یک پیام آماده. فرستنده/Reply-To از <paramref name="smtp"/> روی پیام گذاشته می‌شود اگر خالی باشد.</summary>
    public static async Task<DeliveryAttempt> SendAsync(SmtpSettings smtp, MimeMessage message, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            if (message.From.Count == 0)
                message.From.Add(new MailboxAddress(smtp.FromName ?? "", smtp.FromAddress));
            if (message.ReplyTo.Count == 0 && !string.IsNullOrWhiteSpace(smtp.ReplyTo))
                message.ReplyTo.Add(MailboxAddress.Parse(smtp.ReplyTo));

            using var client = new SmtpClient { Timeout = TimeoutMs };
            await ConnectAsync(client, smtp, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return new DeliveryAttempt(true, "ارسال شد");
        }
        catch (Exception ex) when (Describe(ex, cancellationToken) is { } reason)
        {
            logger.LogWarning("SMTP send via {Host}:{Port} failed: {Type} {Reason}", smtp.Host, smtp.Port, ex.GetType().Name, reason);
            return new DeliveryAttempt(false, reason);
        }
    }

    /// <summary>
    /// «بررسی اتصال» پنل: فقط وصل شدن و ورود، بدون ارسال ایمیل. مثال خروجی موفق: «اتصال و ورود موفق بود (STARTTLS، درگاه ۵۸۷)».
    /// </summary>
    public static async Task<DeliveryAttempt> CheckAsync(SmtpSettings smtp, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new SmtpClient { Timeout = TimeoutMs };
            await ConnectAsync(client, smtp, cancellationToken);
            var secure = client.IsSecure ? "ارتباط رمزنگاری‌شده" : "بدون رمزنگاری";
            var auth = client.IsAuthenticated ? "ورود موفق" : "بدون ورود";
            await client.DisconnectAsync(true, cancellationToken);
            return new DeliveryAttempt(true, $"اتصال برقرار است ({auth}، {secure}، درگاه {smtp.Port}).");
        }
        catch (Exception ex) when (Describe(ex, cancellationToken) is { } reason)
        {
            logger.LogWarning("SMTP check {Host}:{Port} failed: {Type} {Reason}", smtp.Host, smtp.Port, ex.GetType().Name, reason);
            return new DeliveryAttempt(false, reason);
        }
    }

    private static async Task ConnectAsync(SmtpClient client, SmtpSettings smtp, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(smtp.Host, smtp.Port, SocketOptions(smtp.Security, smtp.Port), cancellationToken);
        if (!string.IsNullOrEmpty(smtp.UserName))
            await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? "", cancellationToken);
    }

    /// <summary>
    /// خطای MailKit/شبکه → پیام فارسی با راهنمای رفع. null یعنی لغو واقعی درخواست (باید بالا برود).
    /// مثال: AuthenticationException → «نام کاربری یا رمز اشتباه است (برای جیمیل App Password لازم است)».
    /// </summary>
    public static string? Describe(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        AuthenticationException => "نام کاربری یا رمز SMTP اشتباه است. برای جیمیل، یاهو و یاندکس باید «App Password» بسازید؛ رمز معمولی حساب پذیرفته نمی‌شود.",
        SslHandshakeException => "خطای SSL/TLS: نوع رمزنگاری با درگاه نمی‌خواند (۴۶۵ = SSL، ۵۸۷ = STARTTLS) یا گواهی سرور معتبر نیست.",
        SmtpCommandException { ErrorCode: SmtpErrorCode.SenderNotAccepted } => "سرور نشانی فرستنده را نپذیرفت؛ فرستنده باید همان حساب ورود یا نشانی تأییدشده در سرویس باشد.",
        SmtpCommandException { ErrorCode: SmtpErrorCode.RecipientNotAccepted } => "سرور نشانی گیرنده را نپذیرفت.",
        SmtpCommandException { ErrorCode: SmtpErrorCode.MessageNotAccepted } sce => "سرور پیام را نپذیرفت: " + Clip(sce.Message),
        SmtpCommandException sce => $"خطای سرور SMTP ({(int)sce.StatusCode}): {Clip(sce.Message)}",
        SmtpProtocolException => "سرور پاسخ نامعتبر داد؛ نشانی سرور یا درگاه را بررسی کنید.",
        System.Net.Sockets.SocketException => "اتصال به سرور SMTP برقرار نشد؛ نشانی و درگاه را بررسی کنید (ممکن است هاست درگاه‌های ایمیل را بسته باشد).",
        OperationCanceledException or TimeoutException => "مهلت اتصال به سرور SMTP تمام شد (احتمالاً درگاه بسته است یا سرور از این هاست در دسترس نیست).",
        IOException => "ارتباط با سرور SMTP قطع شد: " + Clip(ex.Message),
        ParseException or FormatException or ArgumentException => "نشانی ایمیل فرستنده/گیرنده معتبر نیست.",
        InvalidOperationException => "ارسال ممکن نشد: " + Clip(ex.Message),
        _ => "خطای پیش‌بینی‌نشده در ارسال ایمیل (" + ex.GetType().Name + ")",
    };

    private static string Clip(string text) => text.Length > 150 ? text[..150] : text;
}
