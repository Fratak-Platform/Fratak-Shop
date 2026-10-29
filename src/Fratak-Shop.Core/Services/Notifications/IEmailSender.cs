namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>
/// یک ایمیل برای یک نفر (مرحلهٔ دوازدهم). متن ساده است؛ قالب برند‌دار (لوگو، دکمه، پانویس) را لایهٔ وب می‌سازد.
/// مثال: new EmailMessage("ali@example.com", "سفارش شما ارسال شد", "سفارش FR-1024 تحویل پست شد.") { ActionUrl = "/profile/orders/…", ActionText = "پیگیری سفارش" }.
/// </summary>
/// <param name="To">نشانی گیرنده (پیش از صف با <see cref="EmailRules.IsValidAddress"/> بررسی می‌شود).</param>
/// <param name="Subject">موضوع (یک خط؛ خط جدید حذف می‌شود).</param>
/// <param name="Body">متن پیام؛ خط جدید = پاراگراف جدا. HTML نیست و هنگام ساخت قالب Encode می‌شود.</param>
public sealed record EmailMessage(string To, string Subject, string Body)
{
    /// <summary>نشانی دکمهٔ پایین پیام؛ نسبی («/profile/orders») با نشانی سایت کامل می‌شود. null = بدون دکمه.</summary>
    public string? ActionUrl { get; init; }

    /// <summary>متن دکمه. مثال: «مشاهدهٔ سفارش». خالی = «مشاهده در سایت».</summary>
    public string? ActionText { get; init; }

    /// <summary>دسته برای لاگ و آمار. مثال: «OrderShipped»، «Test».</summary>
    public string? Category { get; init; }
}

/// <summary>
/// تحویل ایمیل به صف ارسال پس‌زمینه (پیاده‌سازی: Web/Infrastructure/Email/EmailQueue).
/// هرگز منتظر SMTP نمی‌ماند و خطا پرتاب نمی‌کند؛ کندی سرور ایمیل نباید ثبت سفارش را کند کند.
/// محل استفاده: NotificationService (کانال ایمیل اعلان‌ها) و هر جای دیگری که باید به کاربر ایمیل بزند.
/// </summary>
public interface IEmailSender
{
    /// <summary>true = در صف گذاشته شد؛ false = نشانی نامعتبر یا صف پر.</summary>
    bool Enqueue(EmailMessage message);
}

/// <summary>برای آزمون‌ها و ابزارها که لایهٔ وب ندارند: هیچ کاری نمی‌کند.</summary>
public sealed class NullEmailSender : IEmailSender
{
    public bool Enqueue(EmailMessage message) => false;
}

/// <summary>
/// رمز SMTP از تنظیمات سرور (متغیر محیطی <c>Email__Password</c>) وقتی مدیر رمز را در پنل ثبت نکرده.
/// پیاده‌سازی واقعی در لایهٔ وب (IConfiguration)؛ پیش‌فرض Core = هیچ.
/// </summary>
public interface IEmailServerSecret
{
    string? Password { get; }
}

/// <inheritdoc />
public sealed class NoEmailServerSecret : IEmailServerSecret
{
    public string? Password => null;
}
