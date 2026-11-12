using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Settings;

/// <summary>
/// حالت سرویس ایمیل (مرحلهٔ دوازدهم).
/// مثال: روی سرور توسعه «فقط لاگ» تا ایمیل واقعی برای مشتری نرود؛ روی سرور اصلی «SMTP».
/// </summary>
public enum EmailMode
{
    /// <summary>هیچ ایمیلی ساخته یا فرستاده نمی‌شود.</summary>
    Disabled = 0,

    /// <summary>ایمیل ساخته می‌شود ولی فقط در لاگ سرور می‌نشیند (برای توسعه و آزمون). در Production چیزی لاگ نمی‌شود.</summary>
    LogOnly = 1,

    /// <summary>ارسال واقعی با SMTP (جیمیل، زوهو، ایمیل هاست سی‌پنل/دایرکت‌ادمین، Brevo، Mailgun و …).</summary>
    Smtp = 2,
}

/// <summary>
/// نوع رمزنگاری اتصال SMTP.
/// مثال: درگاه ۴۶۵ → SslOnConnect؛ درگاه ۵۸۷ → StartTls؛ «خودکار» از روی شمارهٔ درگاه تصمیم می‌گیرد.
/// </summary>
public enum EmailSecurity
{
    /// <summary>از روی درگاه: ۴۶۵ = SSL از ابتدا، ۵۸۷ = STARTTLS اجباری، بقیه = STARTTLS اگر سرور پشتیبانی کند.</summary>
    Auto = 0,

    /// <summary>SSL/TLS از لحظهٔ اتصال (معمولاً درگاه ۴۶۵).</summary>
    SslOnConnect = 1,

    /// <summary>اتصال ساده و سپس STARTTLS اجباری (معمولاً درگاه ۵۸۷).</summary>
    StartTls = 2,

    /// <summary>بدون رمزنگاری (فقط برای سرور داخلی؛ رمز عبور بی‌حفاظت جابه‌جا می‌شود).</summary>
    None = 3,
}

/// <summary>
/// تنظیمات سرویس ایمیل فروشگاه (فقط یک ردیف؛ صفحهٔ «فنی ← سرویس ایمیل» در پنل مدیریت).
/// <para>
/// رمز SMTP با ISecretProtector رمزنگاری ذخیره می‌شود و هرگز در فرم یا لاگ نمایش داده نمی‌شود (فقط «ثبت شده/نشده»).
/// اگر رمزی در پایگاه داده نباشد، مقدار <c>Email:Password</c> از تنظیمات سرور (متغیر محیطی Email__Password) استفاده می‌شود.
/// </para>
/// مثال (جیمیل): Host = smtp.gmail.com، Port = 587، Security = StartTls، UserName = shop@gmail.com،
/// رمز = «App Password» شانزده‌حرفی، FromAddress = shop@gmail.com، FromName = «فراتک شاپ».
/// </summary>
public class EmailProviderConfig : BaseEntity
{
    public EmailMode Mode { get; set; } = EmailMode.LogOnly;

    /// <summary>کلید سرویس آماده‌ای که مدیر انتخاب کرده (فقط برای نمایش دوباره در فرم). مثال: gmail، zoho، cpanel.</summary>
    [MaxLength(40)]
    public string? Preset { get; set; }

    /// <summary>نشانی سرور SMTP. مثال: smtp.gmail.com یا mail.example.ir.</summary>
    [MaxLength(200)]
    public string? Host { get; set; }

    /// <summary>درگاه SMTP. مثال: ۵۸۷ (STARTTLS) یا ۴۶۵ (SSL).</summary>
    public int Port { get; set; } = 587;

    public EmailSecurity Security { get; set; } = EmailSecurity.Auto;

    /// <summary>نام کاربری ورود به SMTP (معمولاً همان نشانی ایمیل). خالی = بدون ورود (سرور داخلی).</summary>
    [MaxLength(200)]
    public string? UserName { get; set; }

    [MaxLength(2000)]
    public string? PasswordProtected { get; set; }

    /// <summary>نشانی فرستنده. مثال: no-reply@fratak.ir. بیشتر سرویس‌ها فقط نشانی تأییدشدهٔ خودشان را می‌پذیرند.</summary>
    [MaxLength(200)]
    public string? FromAddress { get; set; }

    /// <summary>نام نمایشی فرستنده. خالی = نام فروشگاه. مثال: «فراتک شاپ».</summary>
    [MaxLength(100)]
    public string? FromName { get; set; }

    /// <summary>نشانی پاسخ (Reply-To). مثال: support@fratak.ir تا پاسخ مشتری به پشتیبانی برسد نه no-reply.</summary>
    [MaxLength(200)]
    public string? ReplyTo { get; set; }

    /// <summary>
    /// نشانی کامل سایت برای لینک‌ها و لوگوی داخل ایمیل (ایمیل نشانی نسبی نمی‌فهمد). مثال: https://fratak.ir.
    /// خالی = نشانی‌ای که مدیر هنگام ذخیرهٔ تنظیمات با آن وارد پنل بوده است.
    /// </summary>
    [MaxLength(200)]
    public string? SiteBaseUrl { get; set; }

    /// <summary>
    /// سقف ایمیل در هر ساعت برای محافظت از حساب (جیمیل رایگان روزانه حدود ۵۰۰ ایمیل اجازه می‌دهد).
    /// ایمیل‌های بیشتر در صف می‌مانند و ساعت بعد فرستاده می‌شوند. ۰ = بدون سقف.
    /// </summary>
    public int MaxPerHour { get; set; } = 100;
}
